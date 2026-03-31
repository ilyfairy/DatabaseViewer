import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { useLocalStorage } from '@vueuse/core'
import dayjs from 'dayjs'
import type {
  CellValue,
  ConnectionConfig,
  ConnectionInfo,
  CreateConnectionRequest,
  GraphWorkspaceTab,
  ExplorerDetailPanel,
  ExplorerGridPanel,
  ExplorerPanel,
  ForeignKeyRef,
  ForeignKeyTarget,
  CellContentPreview,
  RecordDetail,
  ReverseReferenceGroup,
  DatabaseGraph,
  SqlContext,
  TableCellUpdateRequest,
  TableCellUpdateResponse,
  TableRowInsertRequest,
  TableRowInsertResponse,
  SqlExecutionResponse,
  SqlWorkspaceTab,
  TableDefinition,
  TableWorkspaceTab,
  TableSearchResponse,
  TableRow,
  TestConnectionRequest,
  WorkspaceTab,
} from '../types/explorer'

type NoticeType = 'info' | 'warning' | 'success'
type SortDirection = 'none' | 'asc' | 'desc'
type OpenSqlTabOptions = {
  connectionId?: string | null
  database?: string | null
  sqlText?: string
  savedSqlText?: string
  filePath?: string | null
  execute?: boolean
}
type PendingSqlCloseState = {
  tabId: string
}
let noticeTimer: ReturnType<typeof setTimeout> | null = null

type HostBridgeRequest = {
  channel: 'dbv-request'
  id: string
  command: string
  payload?: unknown
}

type HostBridgeResponse = {
  channel: 'dbv-response'
  id: string
  success: boolean
  payload?: unknown
  error?: string
}

type HostFilePickerResult = {
  canceled: boolean
  filePath: string | null
}

function getHostWebView() {
  const host = (window as typeof window & {
    chrome?: {
      webview?: {
        postMessage: (message: unknown) => void
        addEventListener: (type: 'message', listener: (event: MessageEvent) => void) => void
      }
    }
  }).chrome?.webview

  return host ?? null
}

const hostResponseListeners = new Map<string, (response: HostBridgeResponse) => void>()
let hostResponseListenerAttached = false

function attachHostResponseListener() {
  const webview = getHostWebView()
  if (!webview || hostResponseListenerAttached) {
    return
  }

  webview.addEventListener('message', (event) => {
    const payload = event.data as HostBridgeResponse | { channel?: string }
    if (!payload || payload.channel !== 'dbv-response' || typeof (payload as HostBridgeResponse).id !== 'string') {
      return
    }

    const response = payload as HostBridgeResponse
    const resolver = hostResponseListeners.get(response.id)
    if (!resolver) {
      return
    }

    hostResponseListeners.delete(response.id)
    resolver(response)
  })
  hostResponseListenerAttached = true
}

async function requestHost<TPayload, TResult>(command: string, payload: TPayload): Promise<TResult> {
  const webview = getHostWebView()
  if (!webview) {
    throw new Error('当前运行环境不支持桌面宿主文件操作。')
  }

  attachHostResponseListener()
  const id = `dbv:${Date.now()}:${Math.random().toString(36).slice(2, 8)}`
  const request: HostBridgeRequest = {
    channel: 'dbv-request',
    id,
    command,
    payload,
  }

  return await new Promise<TResult>((resolve, reject) => {
    hostResponseListeners.set(id, (response) => {
      if (!response.success) {
        reject(new Error(response.error ?? '宿主操作失败。'))
        return
      }

      resolve((response.payload ?? null) as TResult)
    })
    webview.postMessage(request)
  })
}

function getSqlTabFileName(filePath: string | null) {
  if (!filePath) {
    return null
  }

  const normalized = filePath.replace(/\\/g, '/')
  return normalized.split('/').pop() ?? filePath
}

function normalizeCellValue(value: CellValue | undefined): CellValue {
  return value ?? null
}

function isBinaryColumn(columnName: string, columnType?: string) {
  const normalizedType = (columnType ?? '').toLowerCase()
  const normalizedName = columnName.toLowerCase()
  return normalizedType.includes('binary')
    || normalizedType.includes('image')
    || normalizedType.includes('blob')
    || normalizedName.endsWith('image')
    || normalizedName.endsWith('photo')
    || normalizedName.endsWith('thumbnail')
}

async function requestJson<T>(input: string, init?: RequestInit): Promise<T> {
  const response = await fetch(input, init)
  if (!response.ok) {
    const message = await response.text()
    throw new Error(message || `${response.status} ${response.statusText}`)
  }

  if (response.status === 204) {
    return undefined as T
  }

  const contentType = response.headers.get('content-type') ?? ''
  if (!contentType.toLowerCase().includes('application/json')) {
    const text = await response.text()
    if (/<!doctype\s+html/i.test(text) || /<html/i.test(text)) {
      throw new Error('接口当前不可用，通常是桌面应用尚未重启到最新后端版本。请重启程序后再试。')
    }

    throw new Error(text || '接口返回了非 JSON 内容')
  }

  return await response.json() as T
}

export const useExplorerStore = defineStore('explorer', () => {
  const defaultPageSize = 100
  const connections = ref<ConnectionInfo[]>([])
  const loadedTables = ref<Record<string, TableDefinition>>({})
  const detailCache = ref<Record<string, RecordDetail>>({})
  const activeConnectionId = useLocalStorage<string | null>('dbv-active-connection', null)
  const activeTabId = useLocalStorage<string | null>('dbv-active-tab', null)
  const sidebarQuery = useLocalStorage('dbv-sidebar-query', '')
  const globalSearch = useLocalStorage('dbv-global-search', '')
  const searchColumns = useLocalStorage<string[]>('dbv-table-search-columns', [])
  const workspaceTabs = useLocalStorage<WorkspaceTab[]>('dbv-workspace-tabs', [])
  const openPanels = ref<ExplorerPanel[]>([])
  const notice = ref<{ type: NoticeType; text: string } | null>(null)
  const isBootstrapping = ref(false)
  const tableLoadingState = ref<Record<string, boolean>>({})
  const recordLoadingState = ref<Record<string, boolean>>({})
  const tableErrorState = ref<Record<string, string>>({})
  const recordErrorState = ref<Record<string, string>>({})
  const initialized = ref(false)
  const tableSearchState = ref<{
    tableKey: string | null
    loading: boolean
    error: string | null
    query: string
    columns: string[]
    rows: TableRow[]
    totalMatches: number
    hasMoreRows: boolean
  }>({
    tableKey: null,
    loading: false,
    error: null,
    query: '',
    columns: [],
    rows: [],
    totalMatches: 0,
    hasMoreRows: false,
  })
  const tableSortState = ref<{
    tableKey: string | null
    columnName: string | null
    direction: SortDirection
  }>({
    tableKey: null,
    columnName: null,
    direction: 'none',
  })
  const databaseGraphState = ref<{
    show: boolean
    loading: boolean
    error: string | null
    graph: DatabaseGraph | null
  }>({
    show: false,
    loading: false,
    error: null,
    graph: null,
  })
  const sqlContextState = ref<Record<string, { loading: boolean; error: string | null; value: SqlContext | null }>>({})
  const pendingSqlClose = ref<PendingSqlCloseState | null>(null)

  const allTables = computed(() => connections.value.flatMap((connection) => connection.databases.flatMap((database) => database.tables)))
  const tableMetaMap = computed(() => new Map(allTables.value.map((table) => [table.key, table] as const)))
  const activeTab = computed(() => workspaceTabs.value.find((tab) => tab.id === activeTabId.value))
  const activeTableTab = computed(() => activeTab.value?.type === 'table' ? activeTab.value as TableWorkspaceTab : undefined)
  const activeSqlTab = computed(() => activeTab.value?.type === 'sql' ? activeTab.value as SqlWorkspaceTab : undefined)
  const activeGraphTab = computed(() => activeTab.value?.type === 'graph' ? activeTab.value as GraphWorkspaceTab : undefined)
  const gridPanel = computed(() => openPanels.value.find((panel) => panel.type === 'grid') as ExplorerGridPanel | undefined)
  const detailPanels = computed(() => openPanels.value.filter((panel) => panel.type === 'detail') as ExplorerDetailPanel[])
  const activeTable = computed(() => (gridPanel.value ? getTable(gridPanel.value.tableKey) : undefined))
  const connectionCount = computed(() => connections.value.length)
  const activeDetailCount = computed(() => detailPanels.value.length)
  const totalTableCount = computed(() => allTables.value.length)
  const totalRowCount = computed(() => allTables.value.reduce((sum, table) => sum + (table.rowCount ?? loadedTables.value[table.key]?.rows.length ?? 0), 0))

  function buildTableTabId(tableKey: string) {
    return `table:${tableKey}`
  }

  function buildSqlTabId() {
    return `sql:${Date.now()}:${Math.random().toString(36).slice(2, 8)}`
  }

  function persistActiveTablePanels() {
    const current = activeTableTab.value
    if (!current) {
      return
    }

    workspaceTabs.value = workspaceTabs.value.map((tab) => tab.id === current.id
      ? {
          ...tab,
          detailPanels: detailPanels.value,
        }
      : tab)
  }

  function restorePanelsForActiveTableTab() {
    const current = activeTableTab.value
    if (!current) {
      openPanels.value = []
      return
    }

    openPanels.value = [
      { id: `grid:${current.tableKey}`, type: 'grid', tableKey: current.tableKey },
      ...current.detailPanels,
    ]

    void ensureTableLoaded(current.tableKey).catch(() => undefined)
    current.detailPanels.forEach((panel) => {
      void ensureRecordLoaded(panel.id, panel.tableKey, panel.rowKey).catch(() => undefined)
    })
  }

  function setActiveTab(tabId: string | null) {
    persistActiveTablePanels()
    activeTabId.value = tabId
    restorePanelsForActiveTableTab()
  }

  function upsertWorkspaceTab(tab: WorkspaceTab) {
    const existingIndex = workspaceTabs.value.findIndex((entry) => entry.id === tab.id)
    if (existingIndex >= 0) {
      const next = [...workspaceTabs.value]
      next[existingIndex] = tab
      workspaceTabs.value = next
      return
    }

    workspaceTabs.value = [...workspaceTabs.value, tab]
  }

  function updateSqlTab(tabId: string, updater: (tab: SqlWorkspaceTab) => SqlWorkspaceTab) {
    workspaceTabs.value = workspaceTabs.value.map((tab) => tab.id === tabId && tab.type === 'sql' ? updater(tab) : tab)
  }

  function ensureActiveTabSelection() {
    if (workspaceTabs.value.some((tab) => tab.id === activeTabId.value)) {
      return
    }

    activeTabId.value = workspaceTabs.value[0]?.id ?? null
    restorePanelsForActiveTableTab()
  }

  function reconcileWorkspaceTabs() {
    workspaceTabs.value = workspaceTabs.value.filter((tab) => {
      if (tab.type === 'table') {
        return allTables.value.some((table) => table.key === tab.tableKey)
      }

      if (tab.type === 'graph') {
        return connections.value.some((connection) => connection.id === tab.connectionId)
      }

      return !!tab.connectionId && connections.value.some((connection) => connection.id === tab.connectionId)
    }).map((tab) => {
      if (tab.type === 'table') {
        return {
          ...tab,
          detailPanels: tab.detailPanels.filter((panel) => panel.tableKey && allTables.value.some((table) => table.key === panel.tableKey)),
        }
      }

      if (tab.type === 'graph') {
        const databaseStillExists = getConnectionDatabases(tab.connectionId).some((entry) => entry.name === tab.database)
        return databaseStillExists ? tab : null
      }

      const databases = tab.connectionId ? getConnectionDatabases(tab.connectionId) : []
      const database = databases.some((entry) => entry.name === tab.database) ? tab.database : (databases[0]?.name ?? null)
      return {
        ...tab,
        database,
        savedSqlText: tab.savedSqlText ?? tab.sqlText,
        filePath: tab.filePath ?? null,
      }
    }).filter((tab): tab is WorkspaceTab => !!tab)

    ensureActiveTabSelection()
  }

  const visibleConnections = computed(() => {
    const query = sidebarQuery.value.trim().toLowerCase()
    if (!query) {
      return connections.value
    }

    return connections.value
      .map((connection) => ({
        ...connection,
        databases: connection.databases
          .map((database) => ({
            ...database,
            tables: database.tables.filter((table) => `${database.name} ${table.name} ${table.comment ?? ''}`.toLowerCase().includes(query)),
          }))
          .filter((database) => database.tables.length > 0 || database.name.toLowerCase().includes(query)),
      }))
      .filter((connection) => connection.name.toLowerCase().includes(query) || connection.host.toLowerCase().includes(query) || connection.databases.length > 0)
  })

  function formatValue(value: CellValue) {
    if (value === null) {
      return 'NULL'
    }

    if (typeof value === 'string' && /^\d{4}-\d{2}-\d{2}/.test(value)) {
      return dayjs(value).format('YYYY-MM-DD HH:mm:ss')
    }

    return String(value)
  }

  function formatFieldValue(columnName: string, columnType: string | undefined, value: CellValue) {
    if (value === null) {
      return 'NULL'
    }

    if (isBinaryColumn(columnName, columnType)) {
      if (typeof value === 'string') {
        return `[binary ${Math.ceil(value.length * 0.75)} B]`
      }

      return '[binary]'
    }

    const formatted = formatValue(value)
    return formatted.length > 120 ? `${formatted.slice(0, 117)}...` : formatted
  }

  function summarizeRowValues(row: TableRow) {
    const fragments = Object.entries(row)
      .filter(([key, value]) => key !== 'rowKey' && value !== null && value !== '')
      .filter(([key]) => !isBinaryColumn(key))
      .map(([key, value]) => `${key}=${formatFieldValue(key, undefined, normalizeCellValue(value as CellValue))}`)
      .slice(0, 3)
    return fragments.join(' · ') || '打开详情'
  }

  function showNotice(type: NoticeType, text: string) {
    if (noticeTimer) {
      clearTimeout(noticeTimer)
      noticeTimer = null
    }

    notice.value = { type, text }

    noticeTimer = setTimeout(() => {
      notice.value = null
      noticeTimer = null
    }, 2600)
  }

  function dismissNotice() {
    if (noticeTimer) {
      clearTimeout(noticeTimer)
      noticeTimer = null
    }

    notice.value = null
  }

  async function initialize() {
    if (initialized.value) {
      return
    }

    initialized.value = true
    await refreshBootstrap()
  }

  async function refreshBootstrap() {
    isBootstrapping.value = true
    try {
      const payload = await requestJson<{ connections: ConnectionInfo[] }>('/api/explorer/bootstrap')
      connections.value = payload.connections
      if (!activeConnectionId.value || !connections.value.some((connection) => connection.id === activeConnectionId.value)) {
        activeConnectionId.value = connections.value[0]?.id ?? null
      }
      reconcileWorkspaceTabs()
    }
    catch (error) {
      showNotice('warning', error instanceof Error ? error.message : 'Failed to load bootstrap data')
    }
    finally {
      isBootstrapping.value = false
    }
  }

  function getDatabases(connectionId: string) {
    return visibleConnections.value.find((connection) => connection.id === connectionId)?.databases ?? []
  }

  function getTables(connectionId: string, database: string) {
    return getDatabases(connectionId).find((entry) => entry.name === database)?.tables ?? []
  }

  function getConnectionDatabases(connectionId: string) {
    return connections.value.find((connection) => connection.id === connectionId)?.databases ?? []
  }

  function getConnectionInfo(connectionId: string) {
    return connections.value.find((connection) => connection.id === connectionId) ?? null
  }

  function getSqlContextKey(connectionId: string, database: string) {
    return `${connectionId}:${database}`
  }

  function getSqlContext(connectionId: string, database: string) {
    return sqlContextState.value[getSqlContextKey(connectionId, database)]?.value ?? null
  }

  async function ensureSqlContextLoaded(connectionId: string, database: string) {
    const key = getSqlContextKey(connectionId, database)
    const current = sqlContextState.value[key]
    if (current?.loading) {
      return current.value
    }

    if (current?.value) {
      return current.value
    }

    sqlContextState.value[key] = {
      loading: true,
      error: null,
      value: current?.value ?? null,
    }

    try {
      const context = await requestJson<SqlContext>(`/api/explorer/sql-context?connectionId=${encodeURIComponent(connectionId)}&database=${encodeURIComponent(database)}`)
      sqlContextState.value[key] = {
        loading: false,
        error: null,
        value: context,
      }
      return context
    }
    catch (error) {
      sqlContextState.value[key] = {
        loading: false,
        error: error instanceof Error ? error.message : 'SQL 上下文加载失败',
        value: current?.value ?? null,
      }
      return current?.value ?? null
    }
  }

  function getTable(tableKey: string) {
    const loaded = loadedTables.value[tableKey]
    const meta = tableMetaMap.value.get(tableKey)
    return loaded ? { ...meta, ...loaded } : meta
  }

  function getLoadedTable(tableKey: string) {
    return loadedTables.value[tableKey]
  }

  function isSortableColumn(columnName: string, columnType?: string) {
    return !isBinaryColumn(columnName, columnType)
  }

  function getTableSortState(tableKey: string) {
    if (tableSortState.value.tableKey !== tableKey) {
      return {
        tableKey,
        columnName: null,
        direction: 'none' as SortDirection,
      }
    }

    return tableSortState.value
  }

  function appendSortParams(params: URLSearchParams, tableKey: string) {
    const sort = getTableSortState(tableKey)
    if (sort.direction !== 'none' && sort.columnName) {
      params.set('sortColumn', sort.columnName)
      params.set('sortDirection', sort.direction)
    }
  }

  function getConnectionForTable(tableKey: string) {
    return connections.value.find((connection) => connection.databases.some((database) => database.tables.some((table) => table.key === tableKey)))
  }

  async function ensureTableLoaded(tableKey: string, forceReload = false) {
    const cached = loadedTables.value[tableKey]
    if (cached?.rowsLoaded && !forceReload) {
      return cached
    }

    tableLoadingState.value[tableKey] = true
    try {
      const params = new URLSearchParams({
        tableKey,
        offset: '0',
        pageSize: String(defaultPageSize),
      })
      appendSortParams(params, tableKey)
      const response = await requestJson<TableDefinition>(`/api/explorer/table?${params.toString()}`)
      const meta = tableMetaMap.value.get(tableKey)
      loadedTables.value[tableKey] = {
        ...cached,
        ...meta,
        ...response,
        rowCount: meta?.rowCount ?? response.rowCount,
        rowsLoaded: true,
      }
      delete tableErrorState.value[tableKey]
      return loadedTables.value[tableKey]
    }
    catch (error) {
      const message = error instanceof Error ? error.message : '表数据加载失败'
      tableErrorState.value[tableKey] = message
      throw error
    }
    finally {
      tableLoadingState.value[tableKey] = false
    }
  }

  function isTableLoading(tableKey: string) {
    return !!tableLoadingState.value[tableKey]
  }

  function getTableError(tableKey: string) {
    return tableErrorState.value[tableKey]
  }

  async function ensureRecordLoaded(panelId: string, tableKey: string, rowKey: string) {
    if (detailCache.value[panelId]) {
      return detailCache.value[panelId]
    }

    recordLoadingState.value[panelId] = true
    try {
      const response = await requestJson<RecordDetail>(`/api/explorer/record?tableKey=${encodeURIComponent(tableKey)}&rowKey=${encodeURIComponent(rowKey)}`)
      if (!loadedTables.value[tableKey]) {
        const meta = tableMetaMap.value.get(tableKey)
        if (meta) {
          loadedTables.value[tableKey] = {
            ...meta,
            primaryKeys: response.primaryKeys,
            columns: response.columns,
            foreignKeys: response.foreignKeys,
            rows: [],
            hasMoreRows: false,
            rowsLoaded: false,
          }
        }
      }
      detailCache.value[panelId] = response
      delete recordErrorState.value[panelId]
      return response
    }
    catch (error) {
      const message = error instanceof Error ? error.message : '详情加载失败'
      recordErrorState.value[panelId] = message
      throw error
    }
    finally {
      recordLoadingState.value[panelId] = false
    }
  }

  function isRecordLoading(panelId: string) {
    return !!recordLoadingState.value[panelId]
  }

  function getRecordError(panelId: string) {
    return recordErrorState.value[panelId]
  }

  function getPanelRecord(panel: ExplorerDetailPanel) {
    return detailCache.value[panel.id]
  }

  function getFilteredRows(tableKey: string) {
    if (isSearchActive(tableKey)) {
      return tableSearchState.value.rows
    }

    const table = loadedTables.value[tableKey]
    if (!table) {
      return []
    }

    return table.rows
  }

  function getSearchableColumns(tableKey: string) {
    return (loadedTables.value[tableKey]?.columns ?? [])
      .filter((column) => !isBinaryColumn(column.name, column.type))
  }

  function isSearchActive(tableKey: string) {
    return tableSearchState.value.tableKey === tableKey && tableSearchState.value.query.trim().length > 0
  }

  function isSearchLoading(tableKey: string) {
    return tableSearchState.value.tableKey === tableKey && tableSearchState.value.loading
  }

  function getTableSearchState(tableKey: string) {
    return tableSearchState.value.tableKey === tableKey ? tableSearchState.value : null
  }

  function getTableSearchError(tableKey: string) {
    return tableSearchState.value.tableKey === tableKey ? tableSearchState.value.error : null
  }

  async function runTableSearch(tableKey: string, offset = 0) {
    const query = globalSearch.value.trim()
    if (!query) {
      clearTableSearch(tableKey)
      return null
    }

    tableSearchState.value = {
      ...tableSearchState.value,
      tableKey,
      loading: true,
      error: null,
      query,
      columns: [...searchColumns.value],
      rows: offset === 0 ? [] : tableSearchState.value.rows,
    }

    try {
      const params = new URLSearchParams({
        tableKey,
        query,
        offset: String(offset),
        pageSize: String(defaultPageSize),
      })
      appendSortParams(params, tableKey)
      searchColumns.value.forEach((column) => params.append('columns', column))
      const response = await requestJson<TableSearchResponse>(`/api/explorer/table-search?${params.toString()}`)
      tableSearchState.value = {
        tableKey,
        loading: false,
        error: null,
        query: response.query,
        columns: response.columns,
        rows: offset === 0 ? response.rows : [...tableSearchState.value.rows, ...response.rows],
        totalMatches: response.totalMatches,
        hasMoreRows: response.hasMoreRows,
      }
      return response
    }
    catch (error) {
      tableSearchState.value = {
        ...tableSearchState.value,
        tableKey,
        loading: false,
        error: error instanceof Error ? error.message : '搜索失败',
      }
      throw error
    }
  }

  async function applyActiveTableSearch() {
    if (!gridPanel.value) {
      return
    }

    try {
      await runTableSearch(gridPanel.value.tableKey, 0)
    }
    catch (error) {
      showNotice('warning', error instanceof Error ? error.message : '搜索失败')
    }
  }

  function clearTableSearch(tableKey?: string) {
    if (!tableKey || tableSearchState.value.tableKey === tableKey) {
      tableSearchState.value = {
        tableKey: tableKey ?? null,
        loading: false,
        error: null,
        query: '',
        columns: [],
        rows: [],
        totalMatches: 0,
        hasMoreRows: false,
      }
    }
  }

  function clearActiveTableSearch() {
    globalSearch.value = ''
    searchColumns.value = []
    clearTableSearch(gridPanel.value?.tableKey)
  }

  async function loadMoreSearchRows(tableKey: string) {
    const state = getTableSearchState(tableKey)
    if (!state || !state.hasMoreRows || state.loading) {
      return
    }

    try {
      await runTableSearch(tableKey, state.rows.length)
    }
    catch (error) {
      showNotice('warning', error instanceof Error ? error.message : '更多搜索结果加载失败')
    }
  }

  async function loadAllSearchRows(tableKey: string) {
    while (tableSearchState.value.tableKey === tableKey && tableSearchState.value.hasMoreRows && !tableSearchState.value.loading) {
      await loadMoreSearchRows(tableKey)
    }
  }

  function rowSummary(table: Pick<TableDefinition, 'columns'>, row: TableRow) {
    const visibleColumns = table.columns
      .filter((column) => !column.isPrimaryKey)
      .filter((column) => !isBinaryColumn(column.name, column.type))
      .slice(0, 3)
    const fragments = visibleColumns
      .map((column) => ({ column, value: row[column.name] }))
      .filter((entry) => entry.value !== null && entry.value !== '')
      .map((entry) => formatFieldValue(entry.column.name, entry.column.type, normalizeCellValue(entry.value as CellValue)))
    return fragments.join(' · ') || '打开详情'
  }

  function tableRowCountLabel(tableKey: string) {
    return getTable(tableKey)?.rowCount ?? loadedTables.value[tableKey]?.rows.length ?? null
  }

  function getForeignKey(tableKey: string, columnName: string) {
    return loadedTables.value[tableKey]?.foreignKeys.find((foreignKey) => foreignKey.sourceColumn === columnName)
  }

  function tableStats(tableKey: string) {
    const table = getTable(tableKey)
    const loaded = loadedTables.value[tableKey]
    return {
      rowCount: table?.rowCount ?? loaded?.rows.length ?? 0,
      foreignKeyCount: loaded?.foreignKeys.length ?? 0,
      reverseCount: detailPanels.value.map((panel) => detailCache.value[panel.id]).find((detail) => detail?.tableKey === tableKey)?.reverseReferences.length ?? 0,
    }
  }

  async function loadMoreTableRows(tableKey: string, pageSize = defaultPageSize) {
    const loaded = loadedTables.value[tableKey]
    if (!loaded || !loaded.rowsLoaded || !loaded.hasMoreRows || tableLoadingState.value[tableKey]) {
      return loaded
    }

    tableLoadingState.value[tableKey] = true
    try {
      const params = new URLSearchParams({
        tableKey,
        offset: String(loaded.rows.length),
        pageSize: String(pageSize),
      })
      appendSortParams(params, tableKey)
      const response = await requestJson<TableDefinition>(`/api/explorer/table?${params.toString()}`)
      const meta = tableMetaMap.value.get(tableKey)
      loadedTables.value[tableKey] = {
        ...loaded,
        ...response,
        ...meta,
        rows: [...loaded.rows, ...response.rows],
        rowCount: meta?.rowCount ?? loaded.rowCount ?? response.rowCount,
        rowsLoaded: true,
      }
      delete tableErrorState.value[tableKey]
      return loadedTables.value[tableKey]
    }
    catch (error) {
      const message = error instanceof Error ? error.message : '更多表数据加载失败'
      tableErrorState.value[tableKey] = message
      throw error
    }
    finally {
      tableLoadingState.value[tableKey] = false
    }
  }

  async function loadAllTableRows(tableKey: string) {
    while (loadedTables.value[tableKey]?.hasMoreRows) {
      await loadMoreTableRows(tableKey, 500)
    }
  }

  async function fetchCellContent(tableKey: string, rowKey: string, columnName: string) {
    return await requestJson<CellContentPreview>(`/api/explorer/cell?tableKey=${encodeURIComponent(tableKey)}&rowKey=${encodeURIComponent(rowKey)}&columnName=${encodeURIComponent(columnName)}`)
  }

  async function reloadLoadedTable(tableKey: string) {
    const loaded = loadedTables.value[tableKey]
    if (!loaded?.rowsLoaded) {
      return await ensureTableLoaded(tableKey, true)
    }

    tableLoadingState.value[tableKey] = true
    try {
      const params = new URLSearchParams({
        tableKey,
        offset: '0',
        pageSize: String(Math.max(defaultPageSize, loaded.rows.length || defaultPageSize)),
      })
      appendSortParams(params, tableKey)
      const response = await requestJson<TableDefinition>(`/api/explorer/table?${params.toString()}`)
      const meta = tableMetaMap.value.get(tableKey)
      loadedTables.value[tableKey] = {
        ...loaded,
        ...response,
        ...meta,
        rowCount: meta?.rowCount ?? loaded.rowCount ?? response.rowCount,
        rowsLoaded: true,
      }
      delete tableErrorState.value[tableKey]
      return loadedTables.value[tableKey]
    }
    finally {
      tableLoadingState.value[tableKey] = false
    }
  }

  async function reloadSearchResults(tableKey: string) {
    const state = getTableSearchState(tableKey)
    if (!state || !state.query.trim()) {
      return null
    }

    tableSearchState.value = {
      ...state,
      loading: true,
      error: null,
    }

    try {
      const params = new URLSearchParams({
        tableKey,
        query: state.query,
        offset: '0',
        pageSize: String(Math.max(defaultPageSize, state.rows.length || defaultPageSize)),
      })
      appendSortParams(params, tableKey)
      state.columns.forEach((column) => params.append('columns', column))
      const response = await requestJson<TableSearchResponse>(`/api/explorer/table-search?${params.toString()}`)
      tableSearchState.value = {
        tableKey,
        loading: false,
        error: null,
        query: response.query,
        columns: response.columns,
        rows: response.rows,
        totalMatches: response.totalMatches,
        hasMoreRows: response.hasMoreRows,
      }
      return response
    }
    catch (error) {
      tableSearchState.value = {
        ...state,
        loading: false,
        error: error instanceof Error ? error.message : '搜索结果刷新失败',
      }
      throw error
    }
  }

  function syncDetailPanelsAfterRowKeyChange(tableKey: string, previousRowKey: string, nextRowKey: string) {
    if (previousRowKey === nextRowKey) {
      return
    }

    const remapPanel = (panel: ExplorerDetailPanel) => panel.tableKey === tableKey && panel.rowKey === previousRowKey
      ? { ...panel, rowKey: nextRowKey, id: `detail:${tableKey}:${nextRowKey}` }
      : panel

    workspaceTabs.value = workspaceTabs.value.map((tab) => tab.type === 'table'
      ? {
          ...tab,
          detailPanels: tab.detailPanels.map(remapPanel),
        }
      : tab)

    openPanels.value = openPanels.value.map((panel) => panel.type === 'detail' ? remapPanel(panel) : panel)
  }

  async function reloadOpenDetailPanels(tableKey: string) {
    const activePanels = openPanels.value.filter((panel) => panel.type === 'detail' && panel.tableKey === tableKey) as ExplorerDetailPanel[]
    if (activePanels.length === 0) {
      return
    }

    const nextCache = { ...detailCache.value }
    activePanels.forEach((panel) => {
      delete nextCache[panel.id]
    })
    detailCache.value = nextCache

    for (const panel of activePanels) {
      try {
        await ensureRecordLoaded(panel.id, panel.tableKey, panel.rowKey)
      }
      catch {
        // Leave error state for the UI to surface if the record can no longer be loaded.
      }
    }
  }

  async function updateTableCell(request: TableCellUpdateRequest) {
    try {
      const response = await requestJson<TableCellUpdateResponse>('/api/explorer/table-cell', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(request),
      })

      syncDetailPanelsAfterRowKeyChange(response.tableKey, response.previousRowKey, response.rowKey)
      await reloadLoadedTable(response.tableKey)
      if (isSearchActive(response.tableKey)) {
        await reloadSearchResults(response.tableKey)
      }
      await reloadOpenDetailPanels(response.tableKey)
      showNotice('success', '单元格已更新')
      return response
    }
    catch (error) {
      showNotice('warning', error instanceof Error ? error.message : '单元格更新失败')
      throw error
    }
  }

  async function insertTableRow(request: TableRowInsertRequest) {
    try {
      const response = await requestJson<TableRowInsertResponse>('/api/explorer/table-row', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(request),
      })

      await reloadLoadedTable(response.tableKey)
      if (isSearchActive(response.tableKey)) {
        await reloadSearchResults(response.tableKey)
      }
      await reloadOpenDetailPanels(response.tableKey)
      showNotice('success', '新行已插入')
      return response
    }
    catch (error) {
      showNotice('warning', error instanceof Error ? error.message : '新增数据失败')
      throw error
    }
  }

  async function refreshActiveTableData() {
    const tableKey = gridPanel.value?.tableKey ?? activeTableTab.value?.tableKey
    if (!tableKey) {
      return
    }

    try {
      await refreshBootstrap()
      await reloadLoadedTable(tableKey)
      if (isSearchActive(tableKey)) {
        await reloadSearchResults(tableKey)
      }
      await reloadOpenDetailPanels(tableKey)
      showNotice('success', '主表数据已刷新')
    }
    catch (error) {
      showNotice('warning', error instanceof Error ? error.message : '主表数据刷新失败')
      throw error
    }
  }

  async function loadDatabaseGraphTab(tabId: string, connectionId: string, database: string) {
    try {
      const graph = await requestJson<DatabaseGraph>(`/api/explorer/database-graph?connectionId=${encodeURIComponent(connectionId)}&database=${encodeURIComponent(database)}`)
      workspaceTabs.value = workspaceTabs.value.map((tab) => tab.id === tabId && tab.type === 'graph'
        ? {
            ...tab,
            loading: false,
            error: null,
            graph,
          }
        : tab)
    }
    catch (error) {
      workspaceTabs.value = workspaceTabs.value.map((tab) => tab.id === tabId && tab.type === 'graph'
        ? {
            ...tab,
            loading: false,
            error: error instanceof Error ? error.message : '数据库关系图加载失败',
            graph: null,
          }
        : tab)
    }
  }

  async function refreshDatabase(connectionId: string, database: string) {
    const beforeKeys = new Set(getConnectionDatabases(connectionId)
      .find((entry) => entry.name === database)?.tables.map((table) => table.key) ?? [])

    try {
      await refreshBootstrap()

      const afterKeys = getConnectionDatabases(connectionId)
        .find((entry) => entry.name === database)?.tables.map((table) => table.key) ?? []
      const affectedKeys = new Set([...beforeKeys, ...afterKeys])

      for (const tableKey of affectedKeys) {
        if (loadedTables.value[tableKey]?.rowsLoaded) {
          await reloadLoadedTable(tableKey)
        }

        if (isSearchActive(tableKey)) {
          await reloadSearchResults(tableKey)
        }

        if (detailPanels.value.some((panel) => panel.tableKey === tableKey)) {
          await reloadOpenDetailPanels(tableKey)
        }
      }

      const graphTabs = workspaceTabs.value.filter((tab) => tab.type === 'graph' && tab.connectionId === connectionId && tab.database === database) as GraphWorkspaceTab[]
      for (const graphTab of graphTabs) {
        workspaceTabs.value = workspaceTabs.value.map((tab) => tab.id === graphTab.id && tab.type === 'graph'
          ? {
              ...tab,
              loading: true,
              error: null,
            }
          : tab)
        await loadDatabaseGraphTab(graphTab.id, connectionId, database)
      }

      showNotice('success', `${database} 已刷新`)
    }
    catch (error) {
      showNotice('warning', error instanceof Error ? error.message : '数据库刷新失败')
      throw error
    }
  }

  async function openDatabaseGraph(connectionId: string, database: string) {
    persistActiveTablePanels()
    const tabId = `graph:${connectionId}:${database}`

    upsertWorkspaceTab({
      id: tabId,
      type: 'graph',
      connectionId,
      database,
      loading: true,
      error: null,
      graph: null,
    })
    setActiveTab(tabId)

    await loadDatabaseGraphTab(tabId, connectionId, database)
  }

  function closeDatabaseGraph() {
    if (activeGraphTab.value) {
      closeWorkspaceTab(activeGraphTab.value.id)
    }
  }

  function setActiveTablePanels(panels: ExplorerPanel[]) {
    openPanels.value = panels
    persistActiveTablePanels()
  }

  function activateWorkspaceTab(tabId: string) {
    setActiveTab(tabId)
  }

  function performCloseWorkspaceTab(tabId: string) {
    persistActiveTablePanels()

    const targetIndex = workspaceTabs.value.findIndex((tab) => tab.id === tabId)
    if (targetIndex < 0) {
      return
    }

    workspaceTabs.value = workspaceTabs.value.filter((tab) => tab.id !== tabId)
    if (activeTabId.value === tabId) {
      const fallback = workspaceTabs.value[targetIndex] ?? workspaceTabs.value[targetIndex - 1] ?? workspaceTabs.value[0] ?? null
      activeTabId.value = fallback?.id ?? null
    }

    ensureActiveTabSelection()
  }

  function isSqlTabDirty(tabId: string) {
    const tab = workspaceTabs.value.find((entry) => entry.id === tabId && entry.type === 'sql') as SqlWorkspaceTab | undefined
    if (!tab) {
      return false
    }

    return tab.sqlText !== tab.savedSqlText
  }

  function closeWorkspaceTab(tabId: string) {
    const sqlTab = workspaceTabs.value.find((entry) => entry.id === tabId && entry.type === 'sql') as SqlWorkspaceTab | undefined
    if (sqlTab && isSqlTabDirty(tabId)) {
      pendingSqlClose.value = { tabId }
      return
    }

    performCloseWorkspaceTab(tabId)
  }

  function moveWorkspaceTab(tabId: string, targetTabId: string) {
    if (tabId === targetTabId) {
      return
    }

    const currentIndex = workspaceTabs.value.findIndex((tab) => tab.id === tabId)
    const targetIndex = workspaceTabs.value.findIndex((tab) => tab.id === targetTabId)
    if (currentIndex < 0 || targetIndex < 0) {
      return
    }

    const nextTabs = [...workspaceTabs.value]
    const [moved] = nextTabs.splice(currentIndex, 1)
    if (!moved) {
      return
    }

    nextTabs.splice(targetIndex, 0, moved)
    workspaceTabs.value = nextTabs
  }

  function getWorkspaceTabLabel(tab: WorkspaceTab) {
    if (tab.type === 'table') {
      const table = getTable(tab.tableKey)
      return table ? (table.schema ? `${table.schema}.${table.name}` : table.name) : tab.tableKey
    }

    if (tab.type === 'graph') {
      return `${tab.database} 关系总览`
    }

    if (tab.filePath) {
      return getSqlTabFileName(tab.filePath) ?? 'SQL'
    }

    const connection = tab.connectionId ? connections.value.find((entry) => entry.id === tab.connectionId) : null
    const connectionLabel = connection?.name ?? '未选连接'
    if (tab.database) {
      return `SQL · ${connectionLabel} / ${tab.database}`
    }

    return `SQL · ${connectionLabel}`
  }

  async function openSqlTabWithContext(options: OpenSqlTabOptions = {}) {
    persistActiveTablePanels()
    const preferredConnectionId = options.connectionId ?? activeConnectionId.value ?? connections.value[0]?.id?.toString() ?? null
    const preferredDatabase = options.database ?? (preferredConnectionId ? getConnectionDatabases(preferredConnectionId)[0]?.name ?? null : null)
    const sqlText = options.sqlText ?? ''
    const savedSqlText = options.savedSqlText ?? ''
    const tabId = buildSqlTabId()
    upsertWorkspaceTab({
      id: tabId,
      type: 'sql',
      connectionId: preferredConnectionId,
      database: preferredDatabase,
      sqlText,
      savedSqlText,
      filePath: options.filePath ?? null,
      loading: false,
      error: null,
      result: null,
      selectedResultIndex: 0,
    })
    setActiveTab(tabId)
    if (preferredConnectionId && preferredDatabase) {
      await ensureSqlContextLoaded(preferredConnectionId, preferredDatabase)
    }
    if (options.execute && sqlText.trim()) {
      await executeSqlTab(tabId, sqlText)
    }
    return tabId
  }

  function openSqlTab() {
    void openSqlTabWithContext()
  }

  function updateSqlTabConnection(tabId: string, connectionId: string | null) {
    const databases = connectionId ? getConnectionDatabases(connectionId) : []
    const nextDatabase = databases[0]?.name ?? null
    updateSqlTab(tabId, (tab) => ({
      ...tab,
      connectionId,
      database: nextDatabase,
      error: null,
      selectedResultIndex: tab.selectedResultIndex,
    }))
    if (connectionId && nextDatabase) {
      void ensureSqlContextLoaded(connectionId, nextDatabase)
    }
  }

  function updateSqlTabDatabase(tabId: string, database: string | null) {
    const currentTab = workspaceTabs.value.find((entry) => entry.id === tabId && entry.type === 'sql') as SqlWorkspaceTab | undefined
    updateSqlTab(tabId, (tab) => ({
      ...tab,
      database,
      error: null,
      selectedResultIndex: tab.selectedResultIndex,
    }))
    if (currentTab?.connectionId && database) {
      void ensureSqlContextLoaded(currentTab.connectionId, database)
    }
  }

  function updateSqlTabText(tabId: string, sqlText: string) {
    updateSqlTab(tabId, (tab) => ({
      ...tab,
      sqlText,
    }))
  }

  function markSqlTabSaved(tabId: string, filePath: string | null, sqlText: string) {
    updateSqlTab(tabId, (tab) => ({
      ...tab,
      filePath,
      sqlText,
      savedSqlText: sqlText,
    }))
  }

  async function saveSqlTab(tabId: string, saveAs = false) {
    const tab = workspaceTabs.value.find((entry) => entry.id === tabId && entry.type === 'sql') as SqlWorkspaceTab | undefined
    if (!tab) {
      return false
    }

    try {
      const suggestedFileName = getSqlTabFileName(tab.filePath) ?? `${tab.database ?? 'query'}.sql`
      const host = getHostWebView()
      if (!host) {
        const blob = new Blob([tab.sqlText], { type: 'application/sql;charset=utf-8' })
        const url = URL.createObjectURL(blob)
        const anchor = document.createElement('a')
        anchor.href = url
        anchor.download = suggestedFileName
        anchor.click()
        URL.revokeObjectURL(url)
        markSqlTabSaved(tabId, tab.filePath, tab.sqlText)
        showNotice('success', 'SQL 已导出')
        return true
      }

      const result = await requestHost<{ filePath: string | null; suggestedFileName: string; content: string; saveAs: boolean }, { canceled: boolean; filePath: string | null }>('save-sql-file', {
        filePath: tab.filePath,
        suggestedFileName,
        content: tab.sqlText,
        saveAs,
      })

      if (result.canceled) {
        return false
      }

      markSqlTabSaved(tabId, result.filePath, tab.sqlText)
      showNotice('success', 'SQL 已保存')
      return true
    }
    catch (error) {
      showNotice('warning', error instanceof Error ? error.message : 'SQL 保存失败')
      return false
    }
  }

  async function savePendingSqlClose(saveAs = false) {
    if (!pendingSqlClose.value) {
      return
    }

    const tabId = pendingSqlClose.value.tabId
    const saved = await saveSqlTab(tabId, saveAs)
    if (!saved) {
      return
    }

    pendingSqlClose.value = null
    performCloseWorkspaceTab(tabId)
  }

  function discardPendingSqlClose() {
    if (!pendingSqlClose.value) {
      return
    }

    const tabId = pendingSqlClose.value.tabId
    pendingSqlClose.value = null
    performCloseWorkspaceTab(tabId)
  }

  function cancelPendingSqlClose() {
    pendingSqlClose.value = null
  }

  async function openSqlFileTab(filePath: string, content: string) {
    await openSqlTabWithContext({
      filePath,
      sqlText: content,
      savedSqlText: content,
    })
  }

  async function pickSqliteDatabaseFile(filePath: string | null, suggestedFileName: string) {
    const result = await requestHost<{ filePath: string | null; suggestedFileName: string }, HostFilePickerResult>('pick-sqlite-database', {
      filePath,
      suggestedFileName,
    })

    return result.canceled ? null : result.filePath
  }

  function selectSqlResultSet(tabId: string, selectedResultIndex: number) {
    updateSqlTab(tabId, (tab) => ({
      ...tab,
      selectedResultIndex,
    }))
  }

  async function executeSqlTab(tabId: string, sqlTextOverride?: string) {
    const tab = workspaceTabs.value.find((entry) => entry.id === tabId && entry.type === 'sql') as SqlWorkspaceTab | undefined
    const sqlText = sqlTextOverride ?? tab?.sqlText ?? ''
    if (!tab?.connectionId || !tab.database || !sqlText.trim()) {
      return
    }

    updateSqlTab(tabId, (current) => ({
      ...current,
      sqlText,
      loading: true,
      error: null,
    }))

    try {
      const result = await requestJson<SqlExecutionResponse>('/api/explorer/sql-execute', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          connectionId: tab.connectionId,
          database: tab.database,
          sql: sqlText,
        }),
      })

      updateSqlTab(tabId, (current) => ({
        ...current,
        loading: false,
        error: null,
        result,
        selectedResultIndex: 0,
      }))
      showNotice('success', 'SQL 执行完成')
    }
    catch (error) {
      updateSqlTab(tabId, (current) => ({
        ...current,
        loading: false,
        error: error instanceof Error ? error.message : 'SQL 执行失败',
      }))
      showNotice('warning', error instanceof Error ? error.message : 'SQL 执行失败')
    }
  }

  function closeDetailPanel(panelId: string) {
    const targetIndex = openPanels.value.findIndex((panel) => panel.id === panelId)
    if (targetIndex <= 0) {
      return
    }

    setActiveTablePanels(openPanels.value.slice(0, targetIndex))
  }

  async function openTable(tableKey: string) {
    try {
      await ensureTableLoaded(tableKey)
    }
    catch (error) {
      showNotice('warning', error instanceof Error ? error.message : '表数据加载失败')
      return
    }

    const connection = getConnectionForTable(tableKey)
    activeConnectionId.value = connection?.id ?? activeConnectionId.value
    const tabId = buildTableTabId(tableKey)
    const existing = workspaceTabs.value.find((tab) => tab.id === tabId && tab.type === 'table') as TableWorkspaceTab | undefined
    if (!existing) {
      upsertWorkspaceTab({
        id: tabId,
        type: 'table',
        tableKey,
        detailPanels: [],
      })
    }
    setActiveTab(tabId)
    setActiveTablePanels([{ id: `grid:${tableKey}`, type: 'grid', tableKey }])
    globalSearch.value = ''
    searchColumns.value = []
    tableSortState.value = {
      tableKey,
      columnName: null,
      direction: 'none',
    }
    clearTableSearch()
  }

  async function toggleTableSort(tableKey: string, columnName: string, columnType?: string) {
    if (!isSortableColumn(columnName, columnType)) {
      return
    }

    const current = getTableSortState(tableKey)
    let nextDirection: SortDirection = 'asc'
    if (current.columnName === columnName) {
      nextDirection = current.direction === 'none'
        ? 'asc'
        : current.direction === 'asc'
          ? 'desc'
          : 'none'
    }

    tableSortState.value = {
      tableKey,
      columnName: nextDirection === 'none' ? null : columnName,
      direction: nextDirection,
    }

    if (isSearchActive(tableKey)) {
      await applyActiveTableSearch()
      return
    }

    await ensureTableLoaded(tableKey, true)
  }

  async function openRowFromGrid(tableKey: string, rowKey: string) {
    const panelId = `detail:${tableKey}:${rowKey}`
    const nextPanels: ExplorerPanel[] = [
      { id: `grid:${tableKey}`, type: 'grid', tableKey },
      { id: panelId, type: 'detail', tableKey, rowKey, depth: 1, sourceLabel: '主表记录' },
    ]
    setActiveTablePanels(nextPanels)

    try {
      await ensureRecordLoaded(panelId, tableKey, rowKey)
    }
    catch (error) {
      setActiveTablePanels(nextPanels.filter((panel) => panel.id !== panelId))
      showNotice('warning', error instanceof Error ? error.message : '详情加载失败')
    }
  }

  async function inspectRecord(tableKey: string, rowKey: string, sourceLabel: string) {
    try {
      await ensureTableLoaded(tableKey)
    }
    catch (error) {
      showNotice('warning', error instanceof Error ? error.message : '表数据加载失败')
      return
    }

    const panelId = `detail:${tableKey}:${rowKey}`
    const tabId = buildTableTabId(tableKey)
    if (!workspaceTabs.value.some((tab) => tab.id === tabId)) {
      upsertWorkspaceTab({
        id: tabId,
        type: 'table',
        tableKey,
        detailPanels: [],
      })
    }
    setActiveTab(tabId)
    setActiveTablePanels([
      { id: `grid:${tableKey}`, type: 'grid', tableKey },
      { id: panelId, type: 'detail', tableKey, rowKey, depth: 1, sourceLabel },
    ])

    try {
      await ensureRecordLoaded(panelId, tableKey, rowKey)
    }
    catch (error) {
      setActiveTablePanels([{ id: `grid:${tableKey}`, type: 'grid', tableKey }])
      showNotice('warning', error instanceof Error ? error.message : '详情加载失败')
    }
  }

  async function resolveForeignKey(tableKey: string, rowKey: string, columnName: string) {
    return await requestJson<ForeignKeyTarget>(`/api/explorer/foreign-key?tableKey=${encodeURIComponent(tableKey)}&rowKey=${encodeURIComponent(rowKey)}&columnName=${encodeURIComponent(columnName)}`)
  }

  async function navigateToResolvedTarget(target: ForeignKeyTarget, parentPanelId?: string) {
    const nextIdentity = `${target.targetTableKey}::${target.targetRowKey}`
    const visibleDetails = parentPanelId
      ? detailPanels.value.slice(0, detailPanels.value.findIndex((panel) => panel.id === parentPanelId) + 1)
      : detailPanels.value

    const existingPanel = visibleDetails.find((panel) => `${panel.tableKey}::${panel.rowKey}` === nextIdentity)
    if (existingPanel) {
      focusDetailPanel(existingPanel.id)
      showNotice('warning', '触发循环引用，该数据已在前方卡片中展开')
      return
    }

    const nextPanels = parentPanelId
      ? openPanels.value.slice(0, openPanels.value.findIndex((panel) => panel.id === parentPanelId) + 1)
      : openPanels.value.slice(0, 1)
    const panelId = `detail:${target.targetTableKey}:${target.targetRowKey}`
    nextPanels.push({
      id: panelId,
      type: 'detail',
      tableKey: target.targetTableKey,
      rowKey: target.targetRowKey,
      depth: nextPanels.filter((panel) => panel.type === 'detail').length + 1,
      sourceLabel: target.sourceLabel,
    })
    setActiveTablePanels(nextPanels)

    try {
      await ensureRecordLoaded(panelId, target.targetTableKey, target.targetRowKey)
    }
    catch (error) {
      setActiveTablePanels(nextPanels.filter((panel) => panel.id !== panelId))
      showNotice('warning', error instanceof Error ? error.message : '详情加载失败')
    }
  }

  async function navigateForeignKeyFromGrid(tableKey: string, rowKey: string, fk: ForeignKeyRef) {
    const target = await resolveForeignKey(tableKey, rowKey, fk.sourceColumn)
    await navigateToResolvedTarget(target)
  }

  async function navigateForeignKeyFromDetail(parentPanelId: string, tableKey: string, rowKey: string, columnName: string) {
    const target = await resolveForeignKey(tableKey, rowKey, columnName)
    await navigateToResolvedTarget(target, parentPanelId)
  }

  async function navigateReverseReference(parentPanelId: string, sourceTableKey: string, rowKey: string, sourceLabel: string) {
    await navigateToResolvedTarget({ targetTableKey: sourceTableKey, targetRowKey: rowKey, sourceLabel }, parentPanelId)
  }

  function focusDetailPanel(panelId: string) {
    const targetIndex = openPanels.value.findIndex((panel) => panel.id === panelId)
    if (targetIndex <= 0) {
      return
    }

    setActiveTablePanels(openPanels.value.slice(0, targetIndex + 1))
  }

  async function navigateFirstReverseReference(panelId: string, showEmptyNotice = true) {
    const panel = detailPanels.value.find((entry) => entry.id === panelId)
    if (!panel) {
      return
    }

    const groups = getReverseReferences(panel)
    const firstGroup = groups[0]
    const firstRow = firstGroup?.rows[0]
    if (!firstGroup || !firstRow) {
      if (showEmptyNotice) {
        showNotice('info', '当前记录没有下一层反向引用')
      }
      return
    }

    await navigateReverseReference(panelId, firstGroup.sourceTableKey, firstRow.rowKey, firstGroup.relationLabel)
  }

  async function inspectForeignKeyFromDetail(tableKey: string, rowKey: string, columnName: string) {
    const target = await resolveForeignKey(tableKey, rowKey, columnName)
    await inspectRecord(target.targetTableKey, target.targetRowKey, target.sourceLabel)
  }

  async function inspectReverseReference(sourceTableKey: string, rowKey: string, sourceLabel: string) {
    await inspectRecord(sourceTableKey, rowKey, sourceLabel)
  }

  function closeDetails() {
    if (!gridPanel.value) {
      setActiveTablePanels([])
      return
    }

    setActiveTablePanels([gridPanel.value])
  }

  async function deleteConnection(connectionId: string) {
    const response = await fetch(`/api/explorer/connections/${connectionId}`, { method: 'DELETE' })
    if (!response.ok) {
      throw new Error(await response.text())
    }

    loadedTables.value = Object.fromEntries(Object.entries(loadedTables.value).filter(([tableKey]) => !tableKey.startsWith(connectionId)))
    detailCache.value = Object.fromEntries(Object.entries(detailCache.value).filter(([panelId]) => !panelId.includes(connectionId)))
    workspaceTabs.value = workspaceTabs.value.filter((tab) => {
      if (tab.type === 'table') {
        return !tab.tableKey.startsWith(connectionId)
      }

      return tab.connectionId !== connectionId
    })
    ensureActiveTabSelection()

    await refreshBootstrap()
    showNotice('success', '连接已删除')
  }

  async function createConnection(request: CreateConnectionRequest) {
    const response = await fetch('/api/explorer/connections', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
    })

    if (!response.ok) {
      throw new Error(await response.text())
    }

    await refreshBootstrap()
    showNotice('success', '连接已创建')
  }

  async function getConnectionConfig(connectionId: string) {
    return await requestJson<ConnectionConfig>(`/api/explorer/connections/${encodeURIComponent(connectionId)}`)
  }

  async function testConnection(request: TestConnectionRequest) {
    const response = await fetch('/api/explorer/connections/test', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
    })

    if (!response.ok) {
      throw new Error(await response.text())
    }

    showNotice('success', '连接测试成功')
  }

  async function updateConnection(connectionId: string, request: CreateConnectionRequest) {
    const response = await fetch(`/api/explorer/connections/${encodeURIComponent(connectionId)}`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
    })

    if (!response.ok) {
      throw new Error(await response.text())
    }

    await refreshBootstrap()
    showNotice('success', '连接已更新')
  }

  function getReverseReferences(panel: ExplorerDetailPanel): ReverseReferenceGroup[] {
    return detailCache.value[panel.id]?.reverseReferences ?? []
  }

  initialize().catch((error) => {
    showNotice('warning', error instanceof Error ? error.message : 'Initialization failed')
  })

  return {
    connections,
    workspaceTabs,
    activeTabId,
    activeTab,
    activeTableTab,
    activeSqlTab,
    activeGraphTab,
    sidebarQuery,
    globalSearch,
    searchColumns,
    tableSortState,
    pendingSqlClose,
    visibleConnections,
    activeConnectionId,
    openPanels,
    gridPanel,
    detailPanels,
    activeTable,
    connectionCount,
    activeDetailCount,
    totalTableCount,
    totalRowCount,
    tableSearchState,
    databaseGraphState,
    notice,
    isBootstrapping,
    getDatabases,
    getConnectionDatabases,
    getConnectionInfo,
    getSqlContext,
    getTables,
    getWorkspaceTabLabel,
    getTable,
    getLoadedTable,
    ensureTableLoaded,
    getTableSortState,
    getPanelRecord,
    getFilteredRows,
    getSearchableColumns,
    isSortableColumn,
    isSearchActive,
    isSearchLoading,
    getTableSearchState,
    getTableSearchError,
    getForeignKey,
    getReverseReferences,
    formatValue,
    formatFieldValue,
    summarizeRowValues,
    rowSummary,
    tableRowCountLabel,
    tableStats,
    isTableLoading,
    isRecordLoading,
    getTableError,
    getRecordError,
    activateWorkspaceTab,
    closeWorkspaceTab,
    moveWorkspaceTab,
    openTable,
    openSqlTab,
    openSqlTabWithContext,
    openSqlFileTab,
    pickSqliteDatabaseFile,
    updateSqlTabConnection,
    updateSqlTabDatabase,
    updateSqlTabText,
    markSqlTabSaved,
    isSqlTabDirty,
    saveSqlTab,
    savePendingSqlClose,
    discardPendingSqlClose,
    cancelPendingSqlClose,
    selectSqlResultSet,
    executeSqlTab,
    ensureSqlContextLoaded,
    applyActiveTableSearch,
    clearActiveTableSearch,
    toggleTableSort,
    openRowFromGrid,
    inspectRecord,
    inspectForeignKeyFromDetail,
    inspectReverseReference,
    navigateFirstReverseReference,
    focusDetailPanel,
    navigateForeignKeyFromGrid,
    navigateForeignKeyFromDetail,
    navigateReverseReference,
    closeDetails,
    closeDetailPanel,
    loadMoreTableRows,
    loadAllTableRows,
    loadMoreSearchRows,
    loadAllSearchRows,
    fetchCellContent,
    updateTableCell,
    insertTableRow,
    refreshActiveTableData,
    refreshDatabase,
    openDatabaseGraph,
    closeDatabaseGraph,
    defaultPageSize,
    createConnection,
    getConnectionConfig,
    testConnection,
    updateConnection,
    deleteConnection,
    dismissNotice,
    refreshBootstrap,
  }
})