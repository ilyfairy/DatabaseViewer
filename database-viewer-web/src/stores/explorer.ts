import { computed, ref } from 'vue';
import { defineStore } from 'pinia';
import { useLocalStorage } from '@vueuse/core';
import dayjs from 'dayjs';
import { tryMatchRoutineCall } from '../lib/sql-ast';
import type {
  CatalogObjectDetail,
  CatalogObjectType,
  CatalogObjectWorkspaceTab,
  CellValue,
  ConnectionConfig,
  ConnectionInfo,
  CreateConnectionRequest,
  ExplorerSettings,
  GraphWorkspaceTab,
  DatabaseProviderType,
  RoutineInfo,
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
  TableDesign,
  TableDesignSection,
  TableDesignWorkspaceTab,
  TableRowInsertRequest,
  TableRowInsertResponse,
  SqlExecutionResponse,
  SqliteRekeyRequest,
  SqlWorkspaceTab,
  SettingsWorkspaceTab,
  DatabasePropertiesWorkspaceTab,
  SqlServerLoginEditorWorkspaceTab,
  SqlServerLoginManagerWorkspaceTab,
  TableDefinition,
  TableMockWorkspaceTab,
  TableWorkspaceTab,
  TableSearchResponse,
  TableRow,
  TestConnectionRequest,
  WorkspaceLayout,
  WorkspaceTab,
} from '../types/explorer';

type NoticeType = 'info' | 'warning' | 'success'
type SortDirection = 'none' | 'asc' | 'desc'
type OpenSqlTabOptions = {
  connectionId?: string | null
  database?: string | null
  displayName?: string | null
  sqlText?: string
  savedSqlText?: string
  filePath?: string | null
  isRoutineSource?: boolean
  execute?: boolean
  skipRoutineCheck?: boolean
}
type PendingSqlCloseState = {
  tabId: string
}

type PendingDesignCloseState = {
  tabId: string
}

type RefreshBootstrapOptions = {
  rehydrateConnectionIds?: Iterable<string>
}

type ConnectConnectionOptions = {
  forceReload?: boolean
  preserveExistingDatabases?: boolean
}

let noticeTimer: ReturnType<typeof setTimeout> | null = null;

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
  }).chrome?.webview;

  return host ?? null;
}

const hostResponseListeners = new Map<string, (response: HostBridgeResponse) => void>();
let hostResponseListenerAttached = false;

function attachHostResponseListener() {
  const webview = getHostWebView();
  if (!webview || hostResponseListenerAttached) {
    return;
  }

  webview.addEventListener('message', (event) => {
    const payload = event.data as HostBridgeResponse | { channel?: string };
    if (!payload || payload.channel !== 'dbv-response' || typeof (payload as HostBridgeResponse).id !== 'string') {
      return;
    }

    const response = payload as HostBridgeResponse;
    const resolver = hostResponseListeners.get(response.id);
    if (!resolver) {
      return;
    }

    hostResponseListeners.delete(response.id);
    resolver(response);
  });
  hostResponseListenerAttached = true;
}

async function requestHost<TPayload, TResult>(command: string, payload: TPayload): Promise<TResult> {
  const webview = getHostWebView();
  if (!webview) {
    throw new Error('当前运行环境不支持桌面宿主文件操作。');
  }

  attachHostResponseListener();
  const id = `dbv:${Date.now()}:${Math.random().toString(36).slice(2, 8)}`;
  const request: HostBridgeRequest = {
    channel: 'dbv-request',
    id,
    command,
    payload,
  };

  return await new Promise<TResult>((resolve, reject) => {
    hostResponseListeners.set(id, (response) => {
      if (!response.success) {
        reject(new Error(response.error ?? '宿主操作失败。'));
        return;
      }

      resolve((response.payload ?? null) as TResult);
    });
    webview.postMessage(request);
  });
}

function getSqlTabFileName(filePath: string | null) {
  if (!filePath) {
    return null;
  }

  const normalized = filePath.replace(/\\/g, '/');
  return normalized.split('/').pop() ?? filePath;
}

function normalizeCellValue(value: CellValue | undefined): CellValue {
  return value ?? null;
}

function quoteSqlIdentifier(provider: DatabaseProviderType, value: string) {
  if (provider === 'mysql') {
    return `\`${value.replace(/`/g, '``')}\``;
  }

  if (provider === 'postgresql' || provider === 'sqlite') {
    return `"${value.replace(/"/g, '""')}"`;
  }

  return `[${value.replace(/\]/g, ']]')}]`;
}

function qualifiedSqlObject(provider: DatabaseProviderType, schema: string | null | undefined, objectName: string) {
  return schema
    ? `${quoteSqlIdentifier(provider, schema)}.${quoteSqlIdentifier(provider, objectName)}`
    : quoteSqlIdentifier(provider, objectName);
}

function isBinaryColumn(columnName: string, columnType?: string) {
  const normalizedType = (columnType ?? '').toLowerCase();
  const normalizedName = columnName.toLowerCase();
  return normalizedType.includes('binary')
    || normalizedType.includes('image')
    || normalizedType.includes('blob')
    || normalizedName.endsWith('image')
    || normalizedName.endsWith('photo')
    || normalizedName.endsWith('thumbnail');
}

async function requestJson<T>(input: string, init?: RequestInit): Promise<T> {
  const response = await fetch(input, init);
  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || `${response.status} ${response.statusText}`);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  const contentType = response.headers.get('content-type') ?? '';
  if (!contentType.toLowerCase().includes('application/json')) {
    const text = await response.text();
    if (/<!doctype\s+html/i.test(text) || /<html/i.test(text)) {
      throw new Error('接口当前不可用，通常是桌面应用尚未重启到最新后端版本。请重启程序后再试。');
    }

    throw new Error(text || '接口返回了非 JSON 内容');
  }

  return await response.json() as T;
}

export const useExplorerStore = defineStore('explorer', () => {
  const defaultPageSize = 100;
  const connections = ref<ConnectionInfo[]>([]);
  const connectedConnectionIds = ref<Set<string>>(new Set());
  const connectingConnectionIds = ref<Set<string>>(new Set());
  const loadedTables = ref<Record<string, TableDefinition>>({});
  const detailCache = ref<Record<string, RecordDetail>>({});
  const activeConnectionId = useLocalStorage<string | null>('dbv-active-connection', null);
  const activeTabId = useLocalStorage<string | null>('dbv-active-tab', null);
  const explorerSettings = ref<ExplorerSettings>({ showTableRowCounts: true });
  const settingsDraft = ref<ExplorerSettings>({ showTableRowCounts: true });
  const explorerSettingsLoaded = ref(false);
  const workspaceLayout = ref<WorkspaceLayout>({ sidebarPaneSize: 22, detailPaneSize: 32 });
  const workspaceLayoutLoaded = ref(false);
  const settingsSaving = ref(false);
  const pendingSqlEditorFocusTabId = ref<string | null>(null);
  const sidebarQuery = useLocalStorage('dbv-sidebar-query', '');
  const globalSearch = useLocalStorage('dbv-global-search', '');
  const searchColumns = useLocalStorage<string[]>('dbv-table-search-columns', []);
  const workspaceTabs = useLocalStorage<WorkspaceTab[]>('dbv-workspace-tabs', []);
  const openPanels = ref<ExplorerPanel[]>([]);
  const notice = ref<{ type: NoticeType; text: string } | null>(null);
  const isBootstrapping = ref(false);
  /** bootstrap 失败时保存错误信息 */
  const bootstrapError = ref<string | null>(null);
  const tableLoadingState = ref<Record<string, boolean>>({});
  const recordLoadingState = ref<Record<string, boolean>>({});
  const tableErrorState = ref<Record<string, string>>({});
  const recordErrorState = ref<Record<string, string>>({});
  const initialized = ref(false);
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
  });
  const tableSortState = ref<{
    tableKey: string | null
    columnName: string | null
    direction: SortDirection
  }>({
    tableKey: null,
    columnName: null,
    direction: 'none',
  });
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
  });
  const sqlContextState = ref<Record<string, { loading: boolean; error: string | null; value: SqlContext | null }>>({});
  const tableDesignState = ref<Record<string, { loading: boolean; error: string | null; value: TableDesign | null }>>({});
  const catalogObjectState = ref<Record<string, { loading: boolean; error: string | null; value: CatalogObjectDetail | null }>>({});
  const pendingSqlClose = ref<PendingSqlCloseState | null>(null);
  const pendingDesignClose = ref<PendingDesignCloseState | null>(null);
  const pendingDisconnect = ref<{ connectionId: string; remainingDirtyTabIds: string[] } | null>(null);
  const pendingRoutineExec = ref<{
    tabId: string | null;
    connectionId: string;
    database: string;
    provider: DatabaseProviderType;
    routine: RoutineInfo;
  } | null>(null);

  /** 正在执行 SQL 的 Tab 对应的 AbortController，用于取消执行。 */
  const sqlAbortControllers = new Map<string, AbortController>();

  const allTables = computed(() => connections.value.flatMap((connection) => connection.databases.flatMap((database) => database.tables)));
  const allDataObjects = computed(() => connections.value.flatMap((connection) => connection.databases.flatMap((database) => [...database.tables, ...database.views])));
  const tableMetaMap = computed(() => new Map(allDataObjects.value.map((table) => [table.key, table] as const)));
  const activeTab = computed(() => workspaceTabs.value.find((tab) => tab.id === activeTabId.value));
  const activeTableTab = computed(() => activeTab.value?.type === 'table' ? activeTab.value as TableWorkspaceTab : undefined);
  const activeDesignTab = computed(() => activeTab.value?.type === 'design' ? activeTab.value as TableDesignWorkspaceTab : undefined);
  const activeSqlTab = computed(() => activeTab.value?.type === 'sql' ? activeTab.value as SqlWorkspaceTab : undefined);
  const activeGraphTab = computed(() => activeTab.value?.type === 'graph' ? activeTab.value as GraphWorkspaceTab : undefined);
  const activeCatalogTab = computed(() => activeTab.value?.type === 'catalog' ? activeTab.value as CatalogObjectWorkspaceTab : undefined);
  const activeMockTab = computed(() => activeTab.value?.type === 'mock' ? activeTab.value as TableMockWorkspaceTab : undefined);
  const activeSettingsTab = computed(() => activeTab.value?.type === 'settings' ? activeTab.value as SettingsWorkspaceTab : undefined);
  const activeSqlServerLoginManagerTab = computed(() => activeTab.value?.type === 'sqlserver-login-manager' ? activeTab.value as SqlServerLoginManagerWorkspaceTab : undefined);
  const activeSqlServerLoginEditorTab = computed(() => activeTab.value?.type === 'sqlserver-login-editor' ? activeTab.value as SqlServerLoginEditorWorkspaceTab : undefined);
  const activeDatabasePropertiesTab = computed(() => activeTab.value?.type === 'database-properties' ? activeTab.value as DatabasePropertiesWorkspaceTab : undefined);
  const showTableRowCounts = computed(() => explorerSettings.value.showTableRowCounts);
  const isSettingsDirty = computed(() => explorerSettingsLoaded.value && settingsDraft.value.showTableRowCounts !== explorerSettings.value.showTableRowCounts);
  const gridPanel = computed(() => openPanels.value.find((panel) => panel.type === 'grid') as ExplorerGridPanel | undefined);
  const detailPanels = computed(() => openPanels.value.filter((panel) => panel.type === 'detail') as ExplorerDetailPanel[]);
  const activeTable = computed(() => (gridPanel.value ? getTable(gridPanel.value.tableKey) : undefined));
  const connectionCount = computed(() => connections.value.length);
  const activeDetailCount = computed(() => detailPanels.value.length);
  const totalTableCount = computed(() => allTables.value.length);
  const totalRowCount = computed(() => allTables.value.reduce((sum, table) => sum + (table.rowCount ?? loadedTables.value[table.key]?.rows.length ?? 0), 0));

  function buildTableTabId(tableKey: string) {
    return `table:${tableKey}`;
  }

  function buildDesignTabId(tableKey: string) {
    return `design:${tableKey}`;
  }

  function buildCreateDesignTabId() {
    return `design-create:${Date.now()}:${Math.random().toString(36).slice(2, 8)}`;
  }

  function buildCatalogTabId(connectionId: string, database: string, objectType: CatalogObjectType, schema: string | null | undefined, name: string) {
    return `catalog:${connectionId}:${database}:${objectType}:${schema ?? ''}:${name}`;
  }

  function buildSqlTabId() {
    return `sql:${Date.now()}:${Math.random().toString(36).slice(2, 8)}`;
  }

  function buildMockTabId(tableKey: string) {
    return `mock:${tableKey}`;
  }

  function buildSettingsTabId() {
    return 'settings';
  }

  function buildSqlServerLoginManagerTabId(connectionId: string) {
    return `sqlserver-login-manager:${connectionId}`;
  }

  function buildSqlServerLoginEditorTabId(connectionId: string, loginName: string | null, mode: 'browse' | 'create') {
    return mode === 'create'
      ? `sqlserver-login-editor:${connectionId}:__new__`
      : `sqlserver-login-editor:${connectionId}:${loginName ?? '__unknown__'}`
  }

  function persistActiveTablePanels() {
    const current = activeTableTab.value;
    if (!current) {
      return;
    }

    workspaceTabs.value = workspaceTabs.value.map((tab) => tab.id === current.id
      ? {
          ...tab,
          detailPanels: detailPanels.value,
        }
      : tab);
  }

  function restorePanelsForActiveTableTab() {
    const current = activeTableTab.value;
    if (!current) {
      openPanels.value = [];
      return;
    }

    openPanels.value = [
      { id: `grid:${current.tableKey}`, type: 'grid', tableKey: current.tableKey },
      ...current.detailPanels,
    ];

    void ensureTableLoaded(current.tableKey).catch(() => undefined);
    current.detailPanels.forEach((panel) => {
      void ensureRecordLoaded(panel.id, panel.tableKey, panel.rowKey).catch(() => undefined);
    });
  }

  function setActiveTab(tabId: string | null) {
    persistActiveTablePanels();
    activeTabId.value = tabId;
    restorePanelsForActiveTableTab();
  }

  function requestSqlEditorFocus(tabId: string) {
    pendingSqlEditorFocusTabId.value = tabId;
  }

  function consumeSqlEditorFocus(tabId: string) {
    if (pendingSqlEditorFocusTabId.value === tabId) {
      pendingSqlEditorFocusTabId.value = null;
    }
  }

  function upsertWorkspaceTab(tab: WorkspaceTab) {
    const existingIndex = workspaceTabs.value.findIndex((entry) => entry.id === tab.id);
    if (existingIndex >= 0) {
      const next = [...workspaceTabs.value];
      next[existingIndex] = tab;
      workspaceTabs.value = next;
      return;
    }

    workspaceTabs.value = [...workspaceTabs.value, tab];
  }

  function updateSqlTab(tabId: string, updater: (tab: SqlWorkspaceTab) => SqlWorkspaceTab) {
    workspaceTabs.value = workspaceTabs.value.map((tab) => tab.id === tabId && tab.type === 'sql' ? updater(tab) : tab);
  }

  function ensureActiveTabSelection() {
    if (workspaceTabs.value.some((tab) => tab.id === activeTabId.value)) {
      return;
    }

    activeTabId.value = workspaceTabs.value[0]?.id ?? null;
    restorePanelsForActiveTableTab();
  }

  function reconcileWorkspaceTabs() {
    const nextWorkspaceTabs = workspaceTabs.value.filter((tab) => {
      if (tab.type === 'table') {
        return allDataObjects.value.some((table) => table.key === tab.tableKey);
      }

      if (tab.type === 'design') {
        return !!tab.createContext || allDataObjects.value.some((table) => table.key === tab.tableKey);
      }

      if (tab.type === 'mock') {
        return allTables.value.some((table) => table.key === tab.tableKey);
      }

      if (tab.type === 'sqlserver-login-manager') {
        return connections.value.some((connection) => connection.id === tab.connectionId && connection.provider === 'sqlserver');
      }

      if (tab.type === 'sqlserver-login-editor') {
        if (tab.mode === 'browse' && !tab.loginName) {
          return false
        }

        return connections.value.some((connection) => connection.id === tab.connectionId && connection.provider === 'sqlserver')
      }

      if (tab.type === 'graph') {
        return connections.value.some((connection) => connection.id === tab.connectionId);
      }

      if (tab.type === 'settings') {
        return true;
      }

      if (tab.type === 'catalog') {
        const databaseStillExists = getConnectionDatabases(tab.connectionId).some((entry) => entry.name === tab.database);
        return databaseStillExists;
      }

      return true;
    }).map((tab) => {
      if (tab.type === 'table') {
        return {
          ...tab,
          detailPanels: tab.detailPanels.filter((panel) => panel.tableKey && allDataObjects.value.some((table) => table.key === panel.tableKey)),
        };
      }

      if (tab.type === 'design') {
        return tab;
      }

      if (tab.type === 'mock') {
        const table = getTable(tab.tableKey);
        if (!table) {
          return null;
        }

        return {
          ...tab,
          connectionId: tab.connectionId ?? table.key.split(':')[0] ?? tab.connectionId,
          database: tab.database ?? table.database,
        };
      }

      if (tab.type === 'graph') {
        const databaseStillExists = getConnectionDatabases(tab.connectionId).some((entry) => entry.name === tab.database);
        return databaseStillExists ? tab : null;
      }

      if (tab.type === 'settings') {
        return tab;
      }

      if (tab.type === 'catalog') {
        return tab;
      }

      if (tab.type === 'sqlserver-login-manager') {
        const connectionStillExists = connections.value.some((connection) => connection.id === tab.connectionId && connection.provider === 'sqlserver')
        return connectionStillExists ? tab : null
      }

      if (tab.type === 'sqlserver-login-editor') {
        const connectionStillExists = connections.value.some((connection) => connection.id === tab.connectionId && connection.provider === 'sqlserver')
        return connectionStillExists ? tab : null
      }

      if (tab.type === 'database-properties') {
        const databaseStillExists = connections.value.some((connection) => connection.id === tab.connectionId && connection.databases.some((db) => db.name === tab.database));
        return databaseStillExists ? tab : null;
      }

      const connectionId = tab.connectionId && connections.value.some((connection) => connection.id === tab.connectionId)
        ? tab.connectionId
        : (connections.value[0]?.id ?? null);
      const databases = connectionId ? getConnectionDatabases(connectionId) : [];
      const database = databases.some((entry) => entry.name === tab.database)
        ? tab.database
        : (tab.database ?? databases[0]?.name ?? null);
      return {
        ...tab,
        connectionId,
        database,
        savedSqlText: tab.savedSqlText ?? tab.sqlText,
        filePath: tab.filePath ?? null,
      };
    }).filter((tab): tab is Exclude<typeof tab, null> => tab !== null);

    workspaceTabs.value = nextWorkspaceTabs;

    ensureActiveTabSelection();
  }

  const visibleConnections = computed(() => {
    const query = sidebarQuery.value.trim().toLowerCase();
    if (!query) {
      return connections.value;
    }

    return connections.value
      .map((connection) => ({
        ...connection,
        databases: connection.databases
          .map((database) => ({
            ...database,
            tables: database.tables.filter((table) => `${database.name} ${table.name} ${table.comment ?? ''}`.toLowerCase().includes(query)),
            views: database.views.filter((view) => `${database.name} ${view.name} ${view.comment ?? ''}`.toLowerCase().includes(query)),
            synonyms: database.synonyms.filter((synonym) => `${database.name} ${synonym.name} ${synonym.baseObjectName}`.toLowerCase().includes(query)),
            sequences: database.sequences.filter((sequence) => `${database.name} ${sequence.name} ${sequence.dataType}`.toLowerCase().includes(query)),
            rules: database.rules.filter((rule) => `${database.name} ${rule.name} ${rule.definition ?? ''}`.toLowerCase().includes(query)),
            defaults: database.defaults.filter((item) => `${database.name} ${item.name} ${item.definition ?? ''}`.toLowerCase().includes(query)),
            userDefinedTypes: database.userDefinedTypes.filter((item) => `${database.name} ${item.name} ${item.baseTypeName}`.toLowerCase().includes(query)),
            databaseTriggers: database.databaseTriggers.filter((item) => `${database.name} ${item.name} ${item.event ?? ''}`.toLowerCase().includes(query)),
            xmlSchemaCollections: database.xmlSchemaCollections.filter((item) => `${database.name} ${item.name}`.toLowerCase().includes(query)),
            assemblies: database.assemblies.filter((item) => `${database.name} ${item.name} ${item.clrName}`.toLowerCase().includes(query)),
          }))
          .filter((database) => database.tables.length > 0 || database.views.length > 0 || database.synonyms.length > 0 || database.sequences.length > 0 || database.rules.length > 0 || database.defaults.length > 0 || database.userDefinedTypes.length > 0 || database.databaseTriggers.length > 0 || database.xmlSchemaCollections.length > 0 || database.assemblies.length > 0 || database.name.toLowerCase().includes(query)),
      }))
      .filter((connection) => connection.name.toLowerCase().includes(query) || connection.host.toLowerCase().includes(query) || connection.databases.length > 0);
  });

  function formatValue(value: CellValue) {
    if (value === null) {
      return 'NULL';
    }

    if (typeof value === 'string' && /^\d{4}-\d{2}-\d{2}/.test(value)) {
      return dayjs(value).format('YYYY-MM-DD HH:mm:ss');
    }

    return String(value);
  }

  function formatFieldValue(columnName: string, columnType: string | undefined, value: CellValue) {
    if (value === null) {
      return 'NULL';
    }

    if (isBinaryColumn(columnName, columnType)) {
      if (typeof value === 'string') {
        return `[binary ${Math.ceil(value.length * 0.75)} B]`;
      }

      return '[binary]';
    }

    const formatted = formatValue(value);
    return formatted.length > 120 ? `${formatted.slice(0, 117)}...` : formatted;
  }

  function summarizeRowValues(row: TableRow) {
    const fragments = Object.entries(row)
      .filter(([key, value]) => key !== 'rowKey' && value !== null && value !== '')
      .filter(([key]) => !isBinaryColumn(key))
      .map(([key, value]) => `${key}=${formatFieldValue(key, undefined, normalizeCellValue(value as CellValue))}`)
      .slice(0, 3);
    return fragments.join(' · ') || '打开详情';
  }

  function showNotice(type: NoticeType, text: string) {
    if (noticeTimer) {
      clearTimeout(noticeTimer);
      noticeTimer = null;
    }

    notice.value = { type, text };

    noticeTimer = setTimeout(() => {
      notice.value = null;
      noticeTimer = null;
    }, 2600);
  }

  function dismissNotice() {
    if (noticeTimer) {
      clearTimeout(noticeTimer);
      noticeTimer = null;
    }

    notice.value = null;
  }

  async function initialize() {
    if (initialized.value) {
      return;
    }

    initialized.value = true;
    await ensureExplorerSettingsLoaded();
    await ensureWorkspaceLayoutLoaded();
    await refreshBootstrap();
  }

  async function ensureExplorerSettingsLoaded(forceReload = false) {
    if (explorerSettingsLoaded.value && !forceReload) {
      return explorerSettings.value;
    }

    const settings = await requestJson<ExplorerSettings>('/api/explorer/settings');
    explorerSettings.value = settings;
    settingsDraft.value = { ...settings };
    explorerSettingsLoaded.value = true;
    return settings;
  }

  async function ensureWorkspaceLayoutLoaded(forceReload = false) {
    if (workspaceLayoutLoaded.value && !forceReload) {
      return workspaceLayout.value;
    }

    const layout = await requestJson<WorkspaceLayout>('/api/explorer/workspace-layout');
    workspaceLayout.value = layout;
    workspaceLayoutLoaded.value = true;
    return layout;
  }

  /**
   * 用最新的 bootstrap 定义替换连接基础信息，但保留已连接连接的运行态元数据。
   * 这样在编辑连接、保存设置等场景下，不会把已经展开的数据库树瞬间清空成空壳。
   */
  function applyBootstrapSnapshot(nextConnections: ConnectionInfo[]) {
    const existingConnectionsById = new Map(connections.value.map((connection) => [connection.id, connection]));
    const nextConnectionIds = new Set(nextConnections.map((connection) => connection.id));

    connectedConnectionIds.value = new Set([...connectedConnectionIds.value].filter((connectionId) => nextConnectionIds.has(connectionId)));
    connectingConnectionIds.value = new Set([...connectingConnectionIds.value].filter((connectionId) => nextConnectionIds.has(connectionId)));

    connections.value = nextConnections.map((connection) => {
      const existingConnection = existingConnectionsById.get(connection.id);
      if (!existingConnection || !connectedConnectionIds.value.has(connection.id)) {
        return connection;
      }

      return {
        ...connection,
        databases: existingConnection.databases,
        error: existingConnection.error,
      };
    });
  }

  async function rehydrateConnections(connectionIds: Iterable<string>) {
    const uniqueConnectionIds = [...new Set(Array.from(connectionIds).filter((connectionId) => connections.value.some((connection) => connection.id === connectionId)))];
    if (uniqueConnectionIds.length === 0) {
      return;
    }

    await Promise.allSettled(uniqueConnectionIds.map(async (connectionId) => {
      await connectConnection(connectionId, {
        forceReload: true,
        preserveExistingDatabases: true,
      });
    }));
  }

  async function refreshBootstrap(options: RefreshBootstrapOptions = {}) {
    isBootstrapping.value = true;
    bootstrapError.value = null;
    try {
      const payload = await requestJson<{ connections: ConnectionInfo[] }>('/api/explorer/bootstrap');
      applyBootstrapSnapshot(payload.connections);

      for (const connection of connections.value) {
        if (connection.error) {
          showNotice('warning', `${connection.name}: ${connection.error}`);
        }
      }

      if (!activeConnectionId.value || !connections.value.some((connection) => connection.id === activeConnectionId.value)) {
        activeConnectionId.value = connections.value[0]?.id ?? null;
      }
      reconcileWorkspaceTabs();

      await rehydrateConnections(options.rehydrateConnectionIds ?? []);
    }
    catch (error) {
      bootstrapError.value = error instanceof Error ? error.message : '无法连接本地 API';
      showNotice('warning', bootstrapError.value);
    }
    finally {
      isBootstrapping.value = false;
    }
  }

  async function connectConnection(connectionId: string, options: ConnectConnectionOptions = {}) {
    const { forceReload = false, preserveExistingDatabases = false } = options;

    if ((!forceReload && connectedConnectionIds.value.has(connectionId)) || connectingConnectionIds.value.has(connectionId)) {
      return;
    }

    connectingConnectionIds.value = new Set([...connectingConnectionIds.value, connectionId]);
    try {
      const currentConnection = connections.value.find((connection) => connection.id === connectionId) ?? null;
      const node = await requestJson<ConnectionInfo>(`/api/explorer/connect/${connectionId}`, { method: 'POST' });
      connections.value = connections.value.map((c) =>
        c.id === connectionId
          ? {
              ...c,
              ...node,
              databases: node.error && preserveExistingDatabases
                ? (currentConnection?.databases ?? c.databases)
                : node.databases,
            }
          : c,
      );
      if (node.error) {
        connectedConnectionIds.value = new Set([...connectedConnectionIds.value].filter((id) => id !== connectionId));
        showNotice('warning', `${node.name}: ${node.error}`);
      }
      else {
        connectedConnectionIds.value = new Set([...connectedConnectionIds.value, connectionId]);
      }
    }
    catch (error) {
      const msg = error instanceof Error ? error.message : '连接失败';
      connections.value = connections.value.map((c) =>
        c.id === connectionId
          ? {
              ...c,
              error: msg,
              databases: preserveExistingDatabases ? c.databases : [],
            }
          : c,
      );
      showNotice('warning', msg);
    }
    finally {
      connectingConnectionIds.value = new Set([...connectingConnectionIds.value].filter((id) => id !== connectionId));
    }
  }

  /** 获取 tab 所属的 connectionId（如果有的话） */
  function getTabConnectionId(tab: WorkspaceTab): string | null {
    if (tab.type === 'table' || tab.type === 'design') {
      // tableKey 格式：connectionId::database::schema::table
      return tab.tableKey.split('::')[0] || null;
    }

    if (tab.type === 'mock') {
      return tab.connectionId;
    }

    if ('connectionId' in tab && typeof tab.connectionId === 'string') {
      return tab.connectionId;
    }

    return null;
  }

  /** 获取属于指定连接的所有 tab */
  function getConnectionTabs(connectionId: string): WorkspaceTab[] {
    return workspaceTabs.value.filter((tab) => getTabConnectionId(tab) === connectionId);
  }

  /** 获取属于指定连接的所有脏 tab id */
  function getConnectionDirtyTabIds(connectionId: string): string[] {
    return getConnectionTabs(connectionId)
      .filter((tab) => isSqlTabDirty(tab.id) || isCreateDesignTabDirty(tab.id))
      .map((tab) => tab.id);
  }

  /**
   * 断开连接：先检查是否有未保存的 tab，如果有则逐个弹出保存提示。
   * 全部处理完毕后才真正断开。
   */
  function disconnectConnection(connectionId: string) {
    const dirtyTabIds = getConnectionDirtyTabIds(connectionId);
    if (dirtyTabIds.length > 0) {
      pendingDisconnect.value = { connectionId, remainingDirtyTabIds: [...dirtyTabIds] };
      promptNextDisconnectDirtyTab();
      return;
    }

    performDisconnectConnection(connectionId);
  }

  /** 弹出下一个需要保存确认的 tab */
  function promptNextDisconnectDirtyTab() {
    if (!pendingDisconnect.value) {
      return;
    }

    const { connectionId, remainingDirtyTabIds } = pendingDisconnect.value;
    if (remainingDirtyTabIds.length === 0) {
      pendingDisconnect.value = null;
      performDisconnectConnection(connectionId);
      return;
    }

    const nextTabId = remainingDirtyTabIds[0];
    const tab = workspaceTabs.value.find((t) => t.id === nextTabId);
    if (!tab) {
      pendingDisconnect.value = { connectionId, remainingDirtyTabIds: remainingDirtyTabIds.slice(1) };
      promptNextDisconnectDirtyTab();
      return;
    }

    if (tab.type === 'sql' && isSqlTabDirty(nextTabId)) {
      pendingSqlClose.value = { tabId: nextTabId };
    }
    else if (isCreateDesignTabDirty(nextTabId)) {
      pendingDesignClose.value = { tabId: nextTabId };
    }
    else {
      pendingDisconnect.value = { connectionId, remainingDirtyTabIds: remainingDirtyTabIds.slice(1) };
      promptNextDisconnectDirtyTab();
    }
  }

  /** 真正执行断开连接：关闭所有相关 tab，清理缓存 */
  function performDisconnectConnection(connectionId: string) {
    // Close all tabs belonging to this connection
    const tabsToClose = getConnectionTabs(connectionId);
    for (const tab of tabsToClose) {
      performCloseWorkspaceTab(tab.id);
    }

    connectedConnectionIds.value = new Set([...connectedConnectionIds.value].filter((id) => id !== connectionId));
    connections.value = connections.value.map((c) =>
      c.id === connectionId ? { ...c, databases: [], error: null } : c,
    );

    // Clean up loaded tables belonging to this connection
    const newLoadedTables: Record<string, TableDefinition> = {};
    for (const [key, value] of Object.entries(loadedTables.value)) {
      if (!key.startsWith(connectionId + '::')) {
        newLoadedTables[key] = value;
      }
    }
    loadedTables.value = newLoadedTables;

    // Clean up detail cache
    const newDetailCache: Record<string, RecordDetail> = {};
    for (const [key, value] of Object.entries(detailCache.value)) {
      if (!key.startsWith(connectionId + '::')) {
        newDetailCache[key] = value;
      }
    }
    detailCache.value = newDetailCache;

    ensureActiveTabSelection();
  }

  function isConnectionConnected(connectionId: string): boolean {
    return connectedConnectionIds.value.has(connectionId);
  }

  function isConnectionConnecting(connectionId: string): boolean {
    return connectingConnectionIds.value.has(connectionId);
  }

  function getDatabases(connectionId: string) {
    return visibleConnections.value.find((connection) => connection.id === connectionId)?.databases ?? [];
  }

  function getTables(connectionId: string, database: string) {
    return getDatabases(connectionId).find((entry) => entry.name === database)?.tables ?? [];
  }

  function getViews(connectionId: string, database: string) {
    return getDatabases(connectionId).find((entry) => entry.name === database)?.views ?? [];
  }

  function getSynonyms(connectionId: string, database: string) {
    return getDatabases(connectionId).find((entry) => entry.name === database)?.synonyms ?? [];
  }

  function getSequences(connectionId: string, database: string) {
    return getDatabases(connectionId).find((entry) => entry.name === database)?.sequences ?? [];
  }

  function getRules(connectionId: string, database: string) {
    return getDatabases(connectionId).find((entry) => entry.name === database)?.rules ?? [];
  }

  function getDefaults(connectionId: string, database: string) {
    return getDatabases(connectionId).find((entry) => entry.name === database)?.defaults ?? [];
  }

  function getUserDefinedTypes(connectionId: string, database: string) {
    return getDatabases(connectionId).find((entry) => entry.name === database)?.userDefinedTypes ?? [];
  }

  function getDatabaseTriggers(connectionId: string, database: string) {
    return getDatabases(connectionId).find((entry) => entry.name === database)?.databaseTriggers ?? [];
  }

  function getXmlSchemaCollections(connectionId: string, database: string) {
    return getDatabases(connectionId).find((entry) => entry.name === database)?.xmlSchemaCollections ?? [];
  }

  function getAssemblies(connectionId: string, database: string) {
    return getDatabases(connectionId).find((entry) => entry.name === database)?.assemblies ?? [];
  }

  function getRoutines(connectionId: string, database: string) {
    return getDatabases(connectionId).find((entry) => entry.name === database)?.routines ?? [];
  }

  function getConnectionDatabases(connectionId: string) {
    return connections.value.find((connection) => connection.id === connectionId)?.databases ?? [];
  }

  /**
   * 按需获取指定连接的数据库名称列表（不触发左侧栏的连接状态变更）。
   * 如果连接已打开，直接使用已有数据；否则调用轻量 API 获取数据库列表。
   */
  async function fetchDatabaseNames(connectionId: string): Promise<string[]> {
    const existing = getConnectionDatabases(connectionId);
    if (existing.length > 0) {
      return existing.map((db) => db.name);
    }

    try {
      const names = await requestJson<string[]>(`/api/explorer/databases/${connectionId}`);
      // Stash minimal database info so SQL panel can access it
      connections.value = connections.value.map((c) =>
        c.id === connectionId && c.databases.length === 0
          ? { ...c, databases: names.map((name) => ({ name, tables: [], views: [], synonyms: [], sequences: [], rules: [], defaults: [], userDefinedTypes: [], databaseTriggers: [], xmlSchemaCollections: [], assemblies: [], routines: [] })) }
          : c,
      );
      return names;
    }
    catch {
      return [];
    }
  }

  function getConnectionInfo(connectionId: string) {
    return connections.value.find((connection) => connection.id === connectionId) ?? null;
  }

  function getSqlContextKey(connectionId: string, database: string) {
    return `${connectionId}:${database}`;
  }

  function getSqlContext(connectionId: string, database: string) {
    return sqlContextState.value[getSqlContextKey(connectionId, database)]?.value ?? null;
  }

  async function ensureSqlContextLoaded(connectionId: string, database: string) {
    const key = getSqlContextKey(connectionId, database);
    const current = sqlContextState.value[key];
    if (current?.loading) {
      return current.value;
    }

    if (current?.value) {
      return current.value;
    }

    sqlContextState.value[key] = {
      loading: true,
      error: null,
      value: current?.value ?? null,
    };

    try {
      const context = await requestJson<SqlContext>(`/api/explorer/sql-context?connectionId=${encodeURIComponent(connectionId)}&database=${encodeURIComponent(database)}`);
      sqlContextState.value[key] = {
        loading: false,
        error: null,
        value: context,
      };
      return context;
    }
    catch (error) {
      sqlContextState.value[key] = {
        loading: false,
        error: error instanceof Error ? error.message : 'SQL 上下文加载失败',
        value: current?.value ?? null,
      };
      return current?.value ?? null;
    }
  }

  function getTable(tableKey: string) {
    const loaded = loadedTables.value[tableKey];
    const meta = tableMetaMap.value.get(tableKey);
    return loaded ? { ...meta, ...loaded } : meta;
  }

  function getTableDesign(tableKey: string) {
    return tableDesignState.value[tableKey]?.value ?? null;
  }

  function getTableDesignStatus(tableKey: string) {
    return tableDesignState.value[tableKey] ?? {
      loading: false,
      error: null,
      value: null,
    };
  }

  function getCatalogObjectState(tabId: string) {
    return catalogObjectState.value[tabId] ?? {
      loading: false,
      error: null,
      value: null,
    };
  }

  function getCatalogObject(tabId: string) {
    return catalogObjectState.value[tabId]?.value ?? null;
  }

  function getLoadedTable(tableKey: string) {
    return loadedTables.value[tableKey];
  }

  function isSortableColumn(columnName: string, columnType?: string) {
    return !isBinaryColumn(columnName, columnType);
  }

  function getTableSortState(tableKey: string) {
    if (tableSortState.value.tableKey !== tableKey) {
      return {
        tableKey,
        columnName: null,
        direction: 'none' as SortDirection,
      };
    }

    return tableSortState.value;
  }

  function appendSortParams(params: URLSearchParams, tableKey: string) {
    const sort = getTableSortState(tableKey);
    if (sort.direction !== 'none' && sort.columnName) {
      params.set('sortColumn', sort.columnName);
      params.set('sortDirection', sort.direction);
    }
  }

  function getConnectionForTable(tableKey: string) {
    return connections.value.find((connection) => connection.databases.some((database) => [...database.tables, ...database.views].some((table) => table.key === tableKey)));
  }
  function isConnectionReadOnly(connectionId: string) {
    const connection = connections.value.find((entry) => entry.id === connectionId);
    return connection?.provider === 'sqlite' && connection.sqlite.openMode === 'readonly';
  }

  function isTableConnectionReadOnly(tableKey: string) {
    const connection = getConnectionForTable(tableKey);
    return connection?.provider === 'sqlite' && connection.sqlite.openMode === 'readonly';
  }

  function assertTableWritable(tableKey: string, actionLabel: string) {
    if (isTableConnectionReadOnly(tableKey)) {
      throw new Error(`当前 SQLite 连接以只读方式打开，不能${actionLabel}。`);
    }
  }

  async function ensureTableLoaded(tableKey: string, forceReload = false) {
    const cached = loadedTables.value[tableKey];
    if (cached?.rowsLoaded && !forceReload) {
      return cached;
    }

    tableLoadingState.value[tableKey] = true;
    try {
      const params = new URLSearchParams({
        tableKey,
        offset: '0',
        pageSize: String(defaultPageSize),
      });
      appendSortParams(params, tableKey);
      const response = await requestJson<TableDefinition>(`/api/explorer/table?${params.toString()}`);
      const meta = tableMetaMap.value.get(tableKey);
      loadedTables.value[tableKey] = {
        ...cached,
        ...meta,
        ...response,
        rowCount: meta?.rowCount ?? response.rowCount,
        rowsLoaded: true,
      };
      delete tableErrorState.value[tableKey];
      return loadedTables.value[tableKey];
    }
    catch (error) {
      const message = error instanceof Error ? error.message : '表数据加载失败';
      tableErrorState.value[tableKey] = message;
      throw error;
    }
    finally {
      tableLoadingState.value[tableKey] = false;
    }
  }

  function isTableLoading(tableKey: string) {
    return !!tableLoadingState.value[tableKey];
  }

  function getTableError(tableKey: string) {
    return tableErrorState.value[tableKey];
  }

  async function ensureRecordLoaded(panelId: string, tableKey: string, rowKey: string) {
    if (detailCache.value[panelId]) {
      return detailCache.value[panelId];
    }

    recordLoadingState.value[panelId] = true;
    try {
      const response = await requestJson<RecordDetail>(`/api/explorer/record?tableKey=${encodeURIComponent(tableKey)}&rowKey=${encodeURIComponent(rowKey)}`);
      if (!loadedTables.value[tableKey]) {
        const meta = tableMetaMap.value.get(tableKey);
        if (meta) {
          loadedTables.value[tableKey] = {
            ...meta,
            primaryKeys: response.primaryKeys,
            columns: response.columns,
            foreignKeys: response.foreignKeys,
            rows: [],
            hasMoreRows: false,
            rowsLoaded: false,
          };
        }
      }
      detailCache.value[panelId] = response;
      delete recordErrorState.value[panelId];
      return response;
    }
    catch (error) {
      const message = error instanceof Error ? error.message : '详情加载失败';
      recordErrorState.value[panelId] = message;
      throw error;
    }
    finally {
      recordLoadingState.value[panelId] = false;
    }
  }

  function isRecordLoading(panelId: string) {
    return !!recordLoadingState.value[panelId];
  }

  function getRecordError(panelId: string) {
    return recordErrorState.value[panelId];
  }

  function getPanelRecord(panel: ExplorerDetailPanel) {
    return detailCache.value[panel.id];
  }

  function getFilteredRows(tableKey: string) {
    if (isSearchActive(tableKey)) {
      return tableSearchState.value.rows;
    }

    const table = loadedTables.value[tableKey];
    if (!table) {
      return [];
    }

    return table.rows;
  }

  function getSearchableColumns(tableKey: string) {
    return (loadedTables.value[tableKey]?.columns ?? [])
      .filter((column) => !isBinaryColumn(column.name, column.type));
  }

  function isSearchActive(tableKey: string) {
    return tableSearchState.value.tableKey === tableKey && tableSearchState.value.query.trim().length > 0;
  }

  function isSearchLoading(tableKey: string) {
    return tableSearchState.value.tableKey === tableKey && tableSearchState.value.loading;
  }

  function getTableSearchState(tableKey: string) {
    return tableSearchState.value.tableKey === tableKey ? tableSearchState.value : null;
  }

  function getTableSearchError(tableKey: string) {
    return tableSearchState.value.tableKey === tableKey ? tableSearchState.value.error : null;
  }

  async function runTableSearch(tableKey: string, offset = 0) {
    const query = globalSearch.value.trim();
    if (!query) {
      clearTableSearch(tableKey);
      return null;
    }

    tableSearchState.value = {
      ...tableSearchState.value,
      tableKey,
      loading: true,
      error: null,
      query,
      columns: [...searchColumns.value],
      rows: offset === 0 ? [] : tableSearchState.value.rows,
    };

    try {
      const params = new URLSearchParams({
        tableKey,
        query,
        offset: String(offset),
        pageSize: String(defaultPageSize),
      });
      appendSortParams(params, tableKey);
      searchColumns.value.forEach((column) => params.append('columns', column));
      const response = await requestJson<TableSearchResponse>(`/api/explorer/table-search?${params.toString()}`);
      tableSearchState.value = {
        tableKey,
        loading: false,
        error: null,
        query: response.query,
        columns: response.columns,
        rows: offset === 0 ? response.rows : [...tableSearchState.value.rows, ...response.rows],
        totalMatches: response.totalMatches,
        hasMoreRows: response.hasMoreRows,
      };
      return response;
    }
    catch (error) {
      tableSearchState.value = {
        ...tableSearchState.value,
        tableKey,
        loading: false,
        error: error instanceof Error ? error.message : '搜索失败',
      };
      throw error;
    }
  }

  async function applyActiveTableSearch() {
    if (!gridPanel.value) {
      return;
    }

    try {
      await runTableSearch(gridPanel.value.tableKey, 0);
    }
    catch (error) {
      showNotice('warning', error instanceof Error ? error.message : '搜索失败');
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
      };
    }
  }

  function clearActiveTableSearch() {
    globalSearch.value = '';
    searchColumns.value = [];
    clearTableSearch(gridPanel.value?.tableKey);
  }

  async function loadMoreSearchRows(tableKey: string) {
    const state = getTableSearchState(tableKey);
    if (!state || !state.hasMoreRows || state.loading) {
      return;
    }

    try {
      await runTableSearch(tableKey, state.rows.length);
    }
    catch (error) {
      showNotice('warning', error instanceof Error ? error.message : '更多搜索结果加载失败');
    }
  }

  async function loadAllSearchRows(tableKey: string) {
    while (tableSearchState.value.tableKey === tableKey && tableSearchState.value.hasMoreRows && !tableSearchState.value.loading) {
      await loadMoreSearchRows(tableKey);
    }
  }

  function rowSummary(table: Pick<TableDefinition, 'columns'>, row: TableRow) {
    const visibleColumns = table.columns
      .filter((column) => !column.isPrimaryKey)
      .filter((column) => !isBinaryColumn(column.name, column.type))
      .slice(0, 3);
    const fragments = visibleColumns
      .map((column) => ({ column, value: row[column.name] }))
      .filter((entry) => entry.value !== null && entry.value !== '')
      .map((entry) => formatFieldValue(entry.column.name, entry.column.type, normalizeCellValue(entry.value as CellValue)));
    return fragments.join(' · ') || '打开详情';
  }

  function tableRowCountLabel(tableKey: string) {
    if (!showTableRowCounts.value) {
      return null;
    }

    return getTable(tableKey)?.rowCount ?? loadedTables.value[tableKey]?.rows.length ?? null;
  }

  function getForeignKey(tableKey: string, columnName: string) {
    return loadedTables.value[tableKey]?.foreignKeys.find((foreignKey) => foreignKey.sourceColumn === columnName);
  }

  function tableStats(tableKey: string) {
    const table = getTable(tableKey);
    const loaded = loadedTables.value[tableKey];
    return {
      rowCount: table?.rowCount ?? loaded?.rows.length ?? 0,
      foreignKeyCount: loaded?.foreignKeys.length ?? 0,
      reverseCount: detailPanels.value.map((panel) => detailCache.value[panel.id]).find((detail) => detail?.tableKey === tableKey)?.reverseReferences.length ?? 0,
    };
  }

  async function loadMoreTableRows(tableKey: string, pageSize = defaultPageSize) {
    const loaded = loadedTables.value[tableKey];
    if (!loaded || !loaded.rowsLoaded || !loaded.hasMoreRows || tableLoadingState.value[tableKey]) {
      return loaded;
    }

    tableLoadingState.value[tableKey] = true;
    try {
      const params = new URLSearchParams({
        tableKey,
        offset: String(loaded.rows.length),
        pageSize: String(pageSize),
      });
      appendSortParams(params, tableKey);
      const response = await requestJson<TableDefinition>(`/api/explorer/table?${params.toString()}`);
      const meta = tableMetaMap.value.get(tableKey);
      loadedTables.value[tableKey] = {
        ...loaded,
        ...response,
        ...meta,
        rows: [...loaded.rows, ...response.rows],
        rowCount: meta?.rowCount ?? loaded.rowCount ?? response.rowCount,
        rowsLoaded: true,
      };
      delete tableErrorState.value[tableKey];
      return loadedTables.value[tableKey];
    }
    catch (error) {
      const message = error instanceof Error ? error.message : '更多表数据加载失败';
      tableErrorState.value[tableKey] = message;
      throw error;
    }
    finally {
      tableLoadingState.value[tableKey] = false;
    }
  }

  async function loadAllTableRows(tableKey: string) {
    while (loadedTables.value[tableKey]?.hasMoreRows) {
      await loadMoreTableRows(tableKey, 500);
    }
  }

  async function fetchCellContent(tableKey: string, rowKey: string, columnName: string) {
    return await requestJson<CellContentPreview>(`/api/explorer/cell?tableKey=${encodeURIComponent(tableKey)}&rowKey=${encodeURIComponent(rowKey)}&columnName=${encodeURIComponent(columnName)}`);
  }

  async function reloadLoadedTable(tableKey: string) {
    const loaded = loadedTables.value[tableKey];
    if (!loaded?.rowsLoaded) {
      return await ensureTableLoaded(tableKey, true);
    }

    tableLoadingState.value[tableKey] = true;
    try {
      const params = new URLSearchParams({
        tableKey,
        offset: '0',
        pageSize: String(Math.max(defaultPageSize, loaded.rows.length || defaultPageSize)),
      });
      appendSortParams(params, tableKey);
      const response = await requestJson<TableDefinition>(`/api/explorer/table?${params.toString()}`);
      const meta = tableMetaMap.value.get(tableKey);
      loadedTables.value[tableKey] = {
        ...loaded,
        ...response,
        ...meta,
        rowCount: meta?.rowCount ?? loaded.rowCount ?? response.rowCount,
        rowsLoaded: true,
      };
      delete tableErrorState.value[tableKey];
      return loadedTables.value[tableKey];
    }
    finally {
      tableLoadingState.value[tableKey] = false;
    }
  }

  async function ensureTableDesignLoaded(tableKey: string, forceReload = false) {
    const current = tableDesignState.value[tableKey];
    if (current?.loading) {
      return current.value;
    }

    if (current?.value && !forceReload) {
      return current.value;
    }

    tableDesignState.value[tableKey] = {
      loading: true,
      error: null,
      value: current?.value ?? null,
    };

    try {
      const response = await requestJson<TableDesign>(`/api/explorer/table-design?tableKey=${encodeURIComponent(tableKey)}`);
      tableDesignState.value[tableKey] = {
        loading: false,
        error: null,
        value: response,
      };
      return response;
    }
    catch (error) {
      tableDesignState.value[tableKey] = {
        loading: false,
        error: error instanceof Error ? error.message : '表设计加载失败',
        value: current?.value ?? null,
      };
      throw error;
    }
  }

  async function ensureCatalogObjectLoaded(tab: CatalogObjectWorkspaceTab, forceReload = false) {
    const current = catalogObjectState.value[tab.id];
    if (current?.loading) {
      return current.value;
    }

    if (current?.value && !forceReload) {
      return current.value;
    }

    catalogObjectState.value[tab.id] = {
      loading: true,
      error: null,
      value: current?.value ?? null,
    };

    try {
      const params = new URLSearchParams({
        connectionId: tab.connectionId,
        database: tab.database,
        objectType: tab.objectType,
        name: tab.name,
      });
      if (tab.schema) {
        params.set('schema', tab.schema);
      }

      const response = await requestJson<CatalogObjectDetail>(`/api/explorer/catalog-object?${params.toString()}`);
      catalogObjectState.value[tab.id] = {
        loading: false,
        error: null,
        value: response,
      };
      return response;
    }
    catch (error) {
      catalogObjectState.value[tab.id] = {
        loading: false,
        error: error instanceof Error ? error.message : '对象属性加载失败',
        value: current?.value ?? null,
      };
      throw error;
    }
  }

  function updateDesignTabSelection(tabId: string, selectedSection: TableDesignSection, selectedEntry: string | null) {
    workspaceTabs.value = workspaceTabs.value.map((tab) => tab.id === tabId && tab.type === 'design'
      ? {
          ...tab,
          selectedSection,
          selectedEntry,
        }
      : tab);
  }

  async function executeTableDesignSql(tableKey: string, sql: string, successMessage = '表设计已更新') {
    assertTableWritable(tableKey, '修改表结构');
    const design = getTableDesign(tableKey);
    if (!design) {
      throw new Error('表设计尚未加载完成。');
    }

    await requestJson<SqlExecutionResponse>('/api/explorer/sql-execute', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        connectionId: design.connectionId,
        database: design.database,
        sql,
      }),
    });

    await refreshDatabase(design.connectionId, design.database);
    await ensureTableLoaded(tableKey, true);
    await ensureTableDesignLoaded(tableKey, true);
    showNotice('success', successMessage);
  }

  async function deleteDesignColumn(tableKey: string, columnName: string) {
    await ensureTableDesignLoaded(tableKey);
    const design = getTableDesign(tableKey);
    if (!design) {
      throw new Error('表设计尚未加载完成。');
    }

    if (design.objectType === 'view') {
      throw new Error('视图当前不支持删除字段。');
    }

    const column = design.columns.find((entry) => entry.name === columnName);
    if (!column) {
      throw new Error('字段不存在。');
    }

    if (column.isPrimaryKey) {
      throw new Error(`字段 ${columnName} 是主键，无法删除。`);
    }

    const sql = `ALTER TABLE ${qualifiedSqlObject(design.provider, design.schema, design.name)} DROP COLUMN ${quoteSqlIdentifier(design.provider, columnName)}`;
    await executeTableDesignSql(tableKey, sql, '字段已删除');
  }

  async function deleteDesignIndex(tableKey: string, indexName: string) {
    await ensureTableDesignLoaded(tableKey);
    const design = getTableDesign(tableKey);
    if (!design) {
      throw new Error('表设计尚未加载完成。');
    }

    const index = design.indexes.find((entry) => entry.name === indexName);
    if (!index) {
      throw new Error('索引不存在。');
    }

    if (index.isPrimaryKey) {
      throw new Error('主键索引不能直接删除。');
    }

    const sql = design.provider === 'sqlserver' || design.provider === 'mysql'
      ? `DROP INDEX ${quoteSqlIdentifier(design.provider, indexName)} ON ${qualifiedSqlObject(design.provider, design.schema, design.name)}`
      : design.provider === 'postgresql'
        ? `DROP INDEX IF EXISTS ${design.schema ? `${quoteSqlIdentifier(design.provider, design.schema)}.` : ''}${quoteSqlIdentifier(design.provider, indexName)}`
        : `DROP INDEX IF EXISTS ${quoteSqlIdentifier(design.provider, indexName)}`;
    await executeTableDesignSql(tableKey, sql, '索引已删除');
  }

  async function reloadSearchResults(tableKey: string) {
    const state = getTableSearchState(tableKey);
    if (!state || !state.query.trim()) {
      return null;
    }

    tableSearchState.value = {
      ...state,
      loading: true,
      error: null,
    };

    try {
      const params = new URLSearchParams({
        tableKey,
        query: state.query,
        offset: '0',
        pageSize: String(Math.max(defaultPageSize, state.rows.length || defaultPageSize)),
      });
      appendSortParams(params, tableKey);
      state.columns.forEach((column) => params.append('columns', column));
      const response = await requestJson<TableSearchResponse>(`/api/explorer/table-search?${params.toString()}`);
      tableSearchState.value = {
        tableKey,
        loading: false,
        error: null,
        query: response.query,
        columns: response.columns,
        rows: response.rows,
        totalMatches: response.totalMatches,
        hasMoreRows: response.hasMoreRows,
      };
      return response;
    }
    catch (error) {
      tableSearchState.value = {
        ...state,
        loading: false,
        error: error instanceof Error ? error.message : '搜索结果刷新失败',
      };
      throw error;
    }
  }

  function syncDetailPanelsAfterRowKeyChange(tableKey: string, previousRowKey: string, nextRowKey: string) {
    if (previousRowKey === nextRowKey) {
      return;
    }

    const remapPanel = (panel: ExplorerDetailPanel) => panel.tableKey === tableKey && panel.rowKey === previousRowKey
      ? { ...panel, rowKey: nextRowKey, id: `detail:${tableKey}:${nextRowKey}` }
      : panel;

    workspaceTabs.value = workspaceTabs.value.map((tab) => tab.type === 'table'
      ? {
          ...tab,
          detailPanels: tab.detailPanels.map(remapPanel),
        }
      : tab);

    openPanels.value = openPanels.value.map((panel) => panel.type === 'detail' ? remapPanel(panel) : panel);
  }

  async function reloadOpenDetailPanels(tableKey: string) {
    const activePanels = openPanels.value.filter((panel) => panel.type === 'detail' && panel.tableKey === tableKey) as ExplorerDetailPanel[];
    if (activePanels.length === 0) {
      return;
    }

    const nextCache = { ...detailCache.value };
    activePanels.forEach((panel) => {
      delete nextCache[panel.id];
    });
    detailCache.value = nextCache;

    for (const panel of activePanels) {
      try {
        await ensureRecordLoaded(panel.id, panel.tableKey, panel.rowKey);
      }
      catch {
        // Leave error state for the UI to surface if the record can no longer be loaded.
      }
    }
  }

  async function updateTableCell(request: TableCellUpdateRequest) {
    assertTableWritable(request.tableKey, '修改数据');
    try {
      const response = await requestJson<TableCellUpdateResponse>('/api/explorer/table-cell', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(request),
      });

      syncDetailPanelsAfterRowKeyChange(response.tableKey, response.previousRowKey, response.rowKey);
      await reloadLoadedTable(response.tableKey);
      if (isSearchActive(response.tableKey)) {
        await reloadSearchResults(response.tableKey);
      }
      await reloadOpenDetailPanels(response.tableKey);
      showNotice('success', '单元格已更新');
      return response;
    }
    catch (error) {
      showNotice('warning', error instanceof Error ? error.message : '单元格更新失败');
      throw error;
    }
  }

  async function insertTableRow(request: TableRowInsertRequest) {
    assertTableWritable(request.tableKey, '插入新行');
    try {
      const response = await requestJson<TableRowInsertResponse>('/api/explorer/table-row', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(request),
      });

      await reloadLoadedTable(response.tableKey);
      if (isSearchActive(response.tableKey)) {
        await reloadSearchResults(response.tableKey);
      }
      await reloadOpenDetailPanels(response.tableKey);
      showNotice('success', '新行已插入');
      return response;
    }
    catch (error) {
      showNotice('warning', error instanceof Error ? error.message : '新增数据失败');
      throw error;
    }
  }

  /** 通过主键删除一行记录 */
  async function deleteTableRow(tableKey: string, rowKey: string) {
    return await deleteTableRows(tableKey, [rowKey]);
  }

  async function deleteTableRows(tableKey: string, rowKeys: string[]) {
    if (!rowKeys.length) {
      return;
    }
    assertTableWritable(tableKey, '删除行');

    try {
      for (const rowKey of rowKeys) {
        const url = '/api/explorer/table-row?' + new URLSearchParams({ tableKey, rowKey }).toString();
        const response = await fetch(url, { method: 'DELETE' });

        if (!response.ok) {
          const message = await response.text();
          throw new Error(message || `${response.status} ${response.statusText}`);
        }
      }

      await reloadLoadedTable(tableKey);
      if (isSearchActive(tableKey)) {
        await reloadSearchResults(tableKey);
      }
      await reloadOpenDetailPanels(tableKey);
      showNotice('success', rowKeys.length > 1 ? `已删除 ${rowKeys.length} 行` : '已删除一行');
    }
    catch (error) {
      showNotice('warning', error instanceof Error ? error.message : '删除数据失败');
      throw error;
    }
  }

  async function refreshActiveTableData() {
    const tableKey = gridPanel.value?.tableKey ?? activeTableTab.value?.tableKey;
    if (!tableKey) {
      return;
    }

    try {
      await refreshBootstrap();
      await reloadLoadedTable(tableKey);
      if (isSearchActive(tableKey)) {
        await reloadSearchResults(tableKey);
      }
      await reloadOpenDetailPanels(tableKey);
      showNotice('success', '主表数据已刷新');
    }
    catch (error) {
      showNotice('warning', error instanceof Error ? error.message : '主表数据刷新失败');
      throw error;
    }
  }

  async function loadDatabaseGraphTab(tabId: string, connectionId: string, database: string) {
    try {
      const graph = await requestJson<DatabaseGraph>(`/api/explorer/database-graph?connectionId=${encodeURIComponent(connectionId)}&database=${encodeURIComponent(database)}`);
      workspaceTabs.value = workspaceTabs.value.map((tab) => tab.id === tabId && tab.type === 'graph'
        ? {
            ...tab,
            loading: false,
            error: null,
            graph,
          }
        : tab);
    }
    catch (error) {
      workspaceTabs.value = workspaceTabs.value.map((tab) => tab.id === tabId && tab.type === 'graph'
        ? {
            ...tab,
            loading: false,
            error: error instanceof Error ? error.message : '数据库关系图加载失败',
            graph: null,
          }
        : tab);
    }
  }

  async function refreshDatabase(connectionId: string, database: string) {
    const beforeObjects = getConnectionDatabases(connectionId).find((entry) => entry.name === database);
    const beforeKeys = new Set([...(beforeObjects?.tables ?? []), ...(beforeObjects?.views ?? [])].map((table) => table.key));

    try {
      // Re-fetch metadata for this connection only
      const node = await requestJson<ConnectionInfo>(`/api/explorer/connect/${connectionId}`, { method: 'POST' });
      connections.value = connections.value.map((c) =>
        c.id === connectionId ? { ...c, databases: node.databases, error: node.error } : c,
      );

      const afterObjects = getConnectionDatabases(connectionId).find((entry) => entry.name === database);
      const afterKeys = [...(afterObjects?.tables ?? []), ...(afterObjects?.views ?? [])].map((table) => table.key);
      const affectedKeys = new Set([...beforeKeys, ...afterKeys]);

      for (const tableKey of affectedKeys) {
        if (loadedTables.value[tableKey]?.rowsLoaded) {
          await reloadLoadedTable(tableKey);
        }

        if (isSearchActive(tableKey)) {
          await reloadSearchResults(tableKey);
        }

        if (detailPanels.value.some((panel) => panel.tableKey === tableKey)) {
          await reloadOpenDetailPanels(tableKey);
        }
      }

      const graphTabs = workspaceTabs.value.filter((tab) => tab.type === 'graph' && tab.connectionId === connectionId && tab.database === database) as GraphWorkspaceTab[];
      for (const graphTab of graphTabs) {
        workspaceTabs.value = workspaceTabs.value.map((tab) => tab.id === graphTab.id && tab.type === 'graph'
          ? {
              ...tab,
              loading: true,
              error: null,
            }
          : tab);
        await loadDatabaseGraphTab(graphTab.id, connectionId, database);
      }

      showNotice('success', `${database} 已刷新`);
    }
    catch (error) {
      showNotice('warning', error instanceof Error ? error.message : '数据库刷新失败');
      throw error;
    }
  }

  async function openDatabaseGraph(connectionId: string, database: string) {
    persistActiveTablePanels();
    const tabId = `graph:${connectionId}:${database}`;

    upsertWorkspaceTab({
      id: tabId,
      type: 'graph',
      connectionId,
      database,
      loading: true,
      error: null,
      graph: null,
    });
    setActiveTab(tabId);

    await loadDatabaseGraphTab(tabId, connectionId, database);
  }

  function closeDatabaseGraph() {
    if (activeGraphTab.value) {
      closeWorkspaceTab(activeGraphTab.value.id);
    }
  }

  /** 打开数据库属性 Tab 页。 */
  function openDatabaseProperties(connectionId: string, database: string) {
    persistActiveTablePanels();
    const tabId = `db-props:${connectionId}:${database}`;

    upsertWorkspaceTab({
      id: tabId,
      type: 'database-properties',
      connectionId,
      database,
    });
    setActiveTab(tabId);
  }

  function setActiveTablePanels(panels: ExplorerPanel[]) {
    openPanels.value = panels;
    persistActiveTablePanels();
  }

  function activateWorkspaceTab(tabId: string) {
    setActiveTab(tabId);
  }

  function performCloseWorkspaceTab(tabId: string) {
    persistActiveTablePanels();

    const targetIndex = workspaceTabs.value.findIndex((tab) => tab.id === tabId);
    if (targetIndex < 0) {
      return;
    }

    workspaceTabs.value = workspaceTabs.value.filter((tab) => tab.id !== tabId);
    if (activeTabId.value === tabId) {
      const fallback = workspaceTabs.value[targetIndex] ?? workspaceTabs.value[targetIndex - 1] ?? workspaceTabs.value[0] ?? null;
      activeTabId.value = fallback?.id ?? null;
    }

    ensureActiveTabSelection();
  }

  function isSqlTabDirty(tabId: string) {
    const tab = workspaceTabs.value.find((entry) => entry.id === tabId && entry.type === 'sql') as SqlWorkspaceTab | undefined;
    if (!tab) {
      return false;
    }

    return tab.sqlText !== tab.savedSqlText;
  }

  function isCreateDesignTabDirty(tabId: string) {
    const tab = workspaceTabs.value.find((entry) => entry.id === tabId && entry.type === 'design') as TableDesignWorkspaceTab | undefined;
    return !!tab?.createContext;
  }

  function closeWorkspaceTab(tabId: string) {
    const sqlTab = workspaceTabs.value.find((entry) => entry.id === tabId && entry.type === 'sql') as SqlWorkspaceTab | undefined;
    if (sqlTab && isSqlTabDirty(tabId)) {
      pendingSqlClose.value = { tabId };
      return;
    }

    if (isCreateDesignTabDirty(tabId)) {
      pendingDesignClose.value = { tabId };
      return;
    }

    performCloseWorkspaceTab(tabId);
  }

  function moveWorkspaceTab(tabId: string, targetTabId: string) {
    if (tabId === targetTabId) {
      return;
    }

    const currentIndex = workspaceTabs.value.findIndex((tab) => tab.id === tabId);
    const targetIndex = workspaceTabs.value.findIndex((tab) => tab.id === targetTabId);
    if (currentIndex < 0 || targetIndex < 0) {
      return;
    }

    const nextTabs = [...workspaceTabs.value];
    const [moved] = nextTabs.splice(currentIndex, 1);
    if (!moved) {
      return;
    }

    nextTabs.splice(targetIndex, 0, moved);
    workspaceTabs.value = nextTabs;
  }

  function getWorkspaceTabLabel(tab: WorkspaceTab) {
    if (tab.type === 'settings') {
      return '设置';
    }

    if (tab.type === 'sqlserver-login-manager') {
      const connection = connections.value.find((entry) => entry.id === tab.connectionId)
      return connection ? `${connection.name} · 用户管理` : '用户管理'
    }

    if (tab.type === 'sqlserver-login-editor') {
      const connection = connections.value.find((entry) => entry.id === tab.connectionId)
      const prefix = connection?.name ? `${connection.name} / ` : ''
      return tab.mode === 'create'
        ? `${prefix}新建登录`
        : `${prefix}${tab.loginName ?? '登录'}`
    }

    if (tab.type === 'table') {
      const table = getTable(tab.tableKey);
      return table ? (table.schema ? `${table.schema}.${table.name}` : table.name) : tab.tableKey;
    }

    if (tab.type === 'design') {
      if (tab.createContext) {
        return `${tab.createContext.database} / 新建表设计`;
      }

      const table = getTable(tab.tableKey);
      const label = table ? (table.schema ? `${table.schema}.${table.name}` : table.name) : tab.tableKey;
      return table ? `${table.database} / ${label} 设计` : `${label} 设计`;
    }

    if (tab.type === 'mock') {
      const table = getTable(tab.tableKey);
      const label = table ? (table.schema ? `${table.schema}.${table.name}` : table.name) : tab.tableKey;
      return table ? `${table.database} / ${label} Mock` : `${label} Mock`;
    }

    if (tab.type === 'graph') {
      return `${tab.database} 关系总览`;
    }

    if (tab.type === 'database-properties') {
      return `${tab.database} 属性`;
    }

    if (tab.type === 'catalog') {
      const prefix = tab.schema ? `${tab.schema}.${tab.name}` : tab.name;
      return `${tab.database} / ${prefix}`;
    }

    if (tab.filePath) {
      return getSqlTabFileName(tab.filePath) ?? 'SQL';
    }

    const connection = tab.connectionId ? connections.value.find((entry) => entry.id === tab.connectionId) : null;
    const connectionLabel = connection?.name ?? '未选连接';

    if (tab.displayName) {
      return tab.database
        ? `${tab.displayName} · ${connectionLabel} / ${tab.database}`
        : `${tab.displayName} · ${connectionLabel}`;
    }

    if (tab.database) {
      return `SQL · ${connectionLabel} / ${tab.database}`;
    }

    return `SQL · ${connectionLabel}`;
  }

  async function openSqlTabWithContext(options: OpenSqlTabOptions = {}) {
    persistActiveTablePanels();
    const preferredConnectionId = options.connectionId ?? activeConnectionId.value ?? connections.value[0]?.id?.toString() ?? null;
    const preferredDatabase = options.database ?? (preferredConnectionId ? getConnectionDatabases(preferredConnectionId)[0]?.name ?? null : null);
    const sqlText = options.sqlText ?? '';
    const savedSqlText = options.savedSqlText ?? '';
    const tabId = buildSqlTabId();
    upsertWorkspaceTab({
      id: tabId,
      type: 'sql',
      connectionId: preferredConnectionId,
      database: preferredDatabase,
      displayName: options.displayName ?? null,
      sqlText,
      savedSqlText,
      filePath: options.filePath ?? null,
      isRoutineSource: options.isRoutineSource ?? false,
      loading: false,
      error: null,
      result: null,
      selectedResultIndex: 0,
    });
    setActiveTab(tabId);
    requestSqlEditorFocus(tabId);
    if (preferredConnectionId && preferredDatabase) {
      await ensureSqlContextLoaded(preferredConnectionId, preferredDatabase);
    }
    if (options.execute && sqlText.trim()) {
      await executeSqlTab(tabId, sqlText, options.skipRoutineCheck ? { skipRoutineCheck: true } : undefined);
    }
    return tabId;
  }

  function openSqlTab() {
    void openSqlTabWithContext();
  }

  function openSettingsTab() {
    const tabId = buildSettingsTabId();
    upsertWorkspaceTab({
      id: tabId,
      type: 'settings',
    });
    setActiveTab(tabId);
  }

  function openSqlServerLoginManager(connectionId: string, selectedLoginName: string | null = null, mode: 'browse' | 'create' = 'browse') {
    const tabId = buildSqlServerLoginManagerTabId(connectionId)
    upsertWorkspaceTab({
      id: tabId,
      type: 'sqlserver-login-manager',
      connectionId,
      selectedLoginName,
      mode,
    })
    setActiveTab(tabId)
  }

  function openSqlServerLoginEditor(connectionId: string, loginName: string | null, mode: 'browse' | 'create' = loginName ? 'browse' : 'create') {
    const tabId = buildSqlServerLoginEditorTabId(connectionId, loginName, mode)
    upsertWorkspaceTab({
      id: tabId,
      type: 'sqlserver-login-editor',
      connectionId,
      loginName,
      mode,
    })
    setActiveTab(tabId)
  }

  function updateSqlServerLoginManagerState(tabId: string, selectedLoginName: string | null, mode: 'browse' | 'create') {
    const currentTab = workspaceTabs.value.find((tab): tab is SqlServerLoginManagerWorkspaceTab => tab.id === tabId && tab.type === 'sqlserver-login-manager')
    if (currentTab && currentTab.selectedLoginName === selectedLoginName && currentTab.mode === mode) {
      return
    }

    workspaceTabs.value = workspaceTabs.value.map((tab) => tab.id === tabId && tab.type === 'sqlserver-login-manager'
      ? {
          ...tab,
          selectedLoginName,
          mode,
        }
      : tab)
  }

  function updateSqlServerLoginEditorState(tabId: string, loginName: string | null, mode: 'browse' | 'create') {
    const currentTab = workspaceTabs.value.find((tab): tab is SqlServerLoginEditorWorkspaceTab => tab.id === tabId && tab.type === 'sqlserver-login-editor')
    if (currentTab && currentTab.loginName === loginName && currentTab.mode === mode) {
      return
    }

    workspaceTabs.value = workspaceTabs.value.map((tab) => tab.id === tabId && tab.type === 'sqlserver-login-editor'
      ? {
          ...tab,
          loginName,
          mode,
        }
      : tab)
  }

  function resetExplorerSettingsDraft() {
    settingsDraft.value = { ...explorerSettings.value };
  }

  async function saveWorkspaceLayout(nextLayout: WorkspaceLayout) {
    const saved = await requestJson<WorkspaceLayout>('/api/explorer/workspace-layout', {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(nextLayout),
    });
    workspaceLayout.value = saved;
    workspaceLayoutLoaded.value = true;
  }

  async function saveExplorerSettings(nextSettings: ExplorerSettings = settingsDraft.value) {
    settingsSaving.value = true;
    try {
      const saved = await requestJson<ExplorerSettings>('/api/explorer/settings', {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(nextSettings),
      });
      explorerSettings.value = saved;
      settingsDraft.value = { ...saved };
      explorerSettingsLoaded.value = true;
      await refreshBootstrap({ rehydrateConnectionIds: connectedConnectionIds.value });
    }
    finally {
      settingsSaving.value = false;
    }
  }

  async function openCatalogObject(connectionId: string, database: string, objectType: CatalogObjectType, schema: string | null, name: string) {
    const tabId = buildCatalogTabId(connectionId, database, objectType, schema, name);
    const existing = workspaceTabs.value.find((tab) => tab.id === tabId && tab.type === 'catalog') as CatalogObjectWorkspaceTab | undefined;
    if (!existing) {
      upsertWorkspaceTab({
        id: tabId,
        type: 'catalog',
        connectionId,
        database,
        objectType,
        schema,
        name,
      });
    }

    setActiveTab(tabId);
    const tab = (workspaceTabs.value.find((entry) => entry.id === tabId && entry.type === 'catalog') as CatalogObjectWorkspaceTab | undefined)
      ?? {
        id: tabId,
        type: 'catalog',
        connectionId,
        database,
        objectType,
        schema,
        name,
      };
    await ensureCatalogObjectLoaded(tab, true);
  }

  async function openTableMockTab(tableKey: string) {
    const table = getTable(tableKey);
    if (!table) {
      showNotice('warning', '目标表不存在，无法打开 Mock 数据生成器');
      return;
    }

    if (table.objectType === 'view') {
      showNotice('warning', '视图当前不支持生成 Mock 数据。');
      return;
    }

    await ensureTableDesignLoaded(tableKey);

    const tabId = buildMockTabId(tableKey);
    upsertWorkspaceTab({
      id: tabId,
      type: 'mock',
      connectionId: tableKey.split(':')[0] ?? '',
      database: table.database,
      tableKey,
    });
    setActiveTab(tabId);
  }

  /**
   * 打开存储过程或函数的源代码（在新 SQL 标签页中显示）。
   */
  async function openRoutineSource(connectionId: string, database: string, schema: string | null, name: string, routineType: string) {
    const response = await requestJson<{ source: string | null }>('/api/explorer/routine-source', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ connectionId, database, schema, name, routineType }),
    });
    const source = response.source ?? `-- 无法获取 ${name} 的源代码`;
    const displayName = schema ? `${schema}.${name}` : name;
    const existingTab = workspaceTabs.value.find((tab) => tab.type === 'sql'
      && tab.isRoutineSource
      && tab.connectionId === connectionId
      && tab.database === database
      && (tab.displayName ?? null) === displayName);

    if (existingTab?.type === 'sql') {
      updateSqlTab(existingTab.id, (tab) => ({
        ...tab,
        displayName,
        sqlText: source,
        savedSqlText: source,
        error: null,
      }));
      setActiveTab(existingTab.id);
      requestSqlEditorFocus(existingTab.id);
      return;
    }

    await openSqlTabWithContext({
      connectionId,
      database,
      displayName,
      sqlText: source,
      savedSqlText: source,
      isRoutineSource: true,
    });
  }

  function updateSqlTabConnection(tabId: string, connectionId: string | null) {
    const databases = connectionId ? getConnectionDatabases(connectionId) : [];
    const nextDatabase = databases[0]?.name ?? null;
    updateSqlTab(tabId, (tab) => ({
      ...tab,
      connectionId,
      database: nextDatabase,
      error: null,
      selectedResultIndex: tab.selectedResultIndex,
    }));
    if (connectionId && nextDatabase) {
      void ensureSqlContextLoaded(connectionId, nextDatabase);
    }
  }

  function updateSqlTabDatabase(tabId: string, database: string | null) {
    const currentTab = workspaceTabs.value.find((entry) => entry.id === tabId && entry.type === 'sql') as SqlWorkspaceTab | undefined;
    updateSqlTab(tabId, (tab) => ({
      ...tab,
      database,
      error: null,
      selectedResultIndex: tab.selectedResultIndex,
    }));
    if (currentTab?.connectionId && database) {
      void ensureSqlContextLoaded(currentTab.connectionId, database);
    }
  }

  function updateSqlTabText(tabId: string, sqlText: string) {
    updateSqlTab(tabId, (tab) => ({
      ...tab,
      sqlText,
    }));
  }

  /** 清除 SQL 标签页的错误信息 */
  function clearSqlTabError(tabId: string) {
    updateSqlTab(tabId, (tab) => ({
      ...tab,
      error: null,
    }));
  }

  function markSqlTabSaved(tabId: string, filePath: string | null, sqlText: string) {
    updateSqlTab(tabId, (tab) => ({
      ...tab,
      filePath,
      sqlText,
      savedSqlText: sqlText,
    }));
  }

  async function saveSqlTab(tabId: string, saveAs = false) {
    const tab = workspaceTabs.value.find((entry) => entry.id === tabId && entry.type === 'sql') as SqlWorkspaceTab | undefined;
    if (!tab) {
      return false;
    }

    try {
      const suggestedFileName = getSqlTabFileName(tab.filePath) ?? `${tab.database ?? 'query'}.sql`;
      const host = getHostWebView();
      if (!host) {
        const blob = new Blob([tab.sqlText], { type: 'application/sql;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = suggestedFileName;
        anchor.click();
        URL.revokeObjectURL(url);
        markSqlTabSaved(tabId, tab.filePath, tab.sqlText);
        showNotice('success', 'SQL 已导出');
        return true;
      }

      const result = await requestHost<{ filePath: string | null; suggestedFileName: string; content: string; saveAs: boolean }, { canceled: boolean; filePath: string | null }>('save-sql-file', {
        filePath: tab.filePath,
        suggestedFileName,
        content: tab.sqlText,
        saveAs,
      });

      if (result.canceled) {
        return false;
      }

      markSqlTabSaved(tabId, result.filePath, tab.sqlText);
      showNotice('success', 'SQL 已保存');
      return true;
    }
    catch (error) {
      showNotice('warning', error instanceof Error ? error.message : 'SQL 保存失败');
      return false;
    }
  }

  async function saveTextFile(suggestedFileName: string, content: string) {
    return await requestHost<{ filePath: string | null; suggestedFileName: string; content: string; saveAs: boolean }, { canceled: boolean; filePath: string | null }>('save-sql-file', {
      filePath: null,
      suggestedFileName,
      content,
      saveAs: true,
    });
  }

  async function savePendingSqlClose(saveAs = false) {
    if (!pendingSqlClose.value) {
      return;
    }

    const tabId = pendingSqlClose.value.tabId;
    const saved = await saveSqlTab(tabId, saveAs);
    if (!saved) {
      return;
    }

    pendingSqlClose.value = null;
    performCloseWorkspaceTab(tabId);
    advanceDisconnectFlow(tabId);
  }

  function discardPendingSqlClose() {
    if (!pendingSqlClose.value) {
      return;
    }

    const tabId = pendingSqlClose.value.tabId;
    pendingSqlClose.value = null;
    performCloseWorkspaceTab(tabId);
    advanceDisconnectFlow(tabId);
  }

  function cancelPendingSqlClose() {
    pendingSqlClose.value = null;
    cancelDisconnectFlow();
  }

  function discardPendingDesignClose() {
    if (!pendingDesignClose.value) {
      return;
    }

    const tabId = pendingDesignClose.value.tabId;
    pendingDesignClose.value = null;
    performCloseWorkspaceTab(tabId);
    advanceDisconnectFlow(tabId);
  }

  function cancelPendingDesignClose() {
    pendingDesignClose.value = null;
    cancelDisconnectFlow();
  }

  /** 当一个脏 tab 被保存/丢弃后，继续处理下一个 */
  function advanceDisconnectFlow(resolvedTabId: string) {
    if (!pendingDisconnect.value) {
      return;
    }

    pendingDisconnect.value = {
      ...pendingDisconnect.value,
      remainingDirtyTabIds: pendingDisconnect.value.remainingDirtyTabIds.filter((id) => id !== resolvedTabId),
    };
    promptNextDisconnectDirtyTab();
  }

  /** 用户取消了保存提示，中止整个断开流程 */
  function cancelDisconnectFlow() {
    pendingDisconnect.value = null;
  }

  async function openSqlFileTab(filePath: string, content: string) {
    await openSqlTabWithContext({
      filePath,
      sqlText: content,
      savedSqlText: content,
    });
  }

  async function pickSqliteDatabaseFile(filePath: string | null, suggestedFileName: string) {
    const result = await requestHost<{ filePath: string | null; suggestedFileName: string }, HostFilePickerResult>('pick-sqlite-database', {
      filePath,
      suggestedFileName,
    });

    return result.canceled ? null : result.filePath;
  }

  async function pickSshPrivateKeyFile(filePath: string | null) {
    const result = await requestHost<{ filePath: string | null }, HostFilePickerResult>('pick-ssh-private-key', {
      filePath,
    });

    return result.canceled ? null : result.filePath;
  }

  function selectSqlResultSet(tabId: string, selectedResultIndex: number) {
    updateSqlTab(tabId, (tab) => ({
      ...tab,
      selectedResultIndex,
    }));
  }

  async function executeSqlTab(tabId: string, sqlTextOverride?: string, options?: { silent?: boolean; skipRoutineCheck?: boolean; preserveText?: boolean }) {
    const tab = workspaceTabs.value.find((entry) => entry.id === tabId && entry.type === 'sql') as SqlWorkspaceTab | undefined;
    const sqlText = sqlTextOverride ?? tab?.sqlText ?? '';
    if (!tab?.connectionId || !tab.database || !sqlText.trim()) {
      return;
    }

    // 检测是否为存储过程/函数调用，且该例程有参数需要填写
    if (!options?.silent && !options?.skipRoutineCheck) {
      const connection = getConnectionInfo(tab.connectionId);
      if (connection) {
        // 例程源码 Tab（右键"修改..."打开的）：点击"执行 SQL"应弹参数对话框来测试执行
        if (tab.isRoutineSource && tab.displayName) {
          const routine = findRoutineByDisplayName(tab.connectionId, tab.database, tab.displayName);
          if (routine && routine.parameters.filter((p) => p.direction !== 'RETURN_VALUE').length > 0) {
            pendingRoutineExec.value = {
              tabId,
              connectionId: tab.connectionId,
              database: tab.database,
              provider: connection.provider,
              routine,
            };
            return;
          }
        }

        // 普通 SQL Tab：检测 EXEC / CALL / SELECT func() 等调用语句
        const callInfo = await tryMatchRoutineCall(sqlText.trim(), connection.provider);
        if (callInfo?.hasNoArgs) {
          const routine = findRoutineByCall(tab.connectionId, tab.database, callInfo.name, callInfo.schema);
          if (routine && routine.parameters.filter((p) => p.direction !== 'RETURN_VALUE').length > 0) {
            pendingRoutineExec.value = {
              tabId,
              connectionId: tab.connectionId,
              database: tab.database,
              provider: connection.provider,
              routine,
            };
            return;
          }
        }
      }
    }

    updateSqlTab(tabId, (current) => ({
      ...current,
      ...(options?.preserveText ? {} : { sqlText }),
      loading: true,
      error: null,
    }));

    const abortController = new AbortController();
    sqlAbortControllers.set(tabId, abortController);

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
        signal: abortController.signal,
      });

      if (options?.silent) {
        // 静默模式：不显示结果面板，仅更新保存状态
        updateSqlTab(tabId, (current) => ({
          ...current,
          loading: false,
          error: null,
          savedSqlText: sqlText,
        }));
        showNotice('success', '已保存到数据库');
      }
      else {
        updateSqlTab(tabId, (current) => ({
          ...current,
          loading: false,
          error: null,
          result,
          selectedResultIndex: 0,
        }));
        showNotice('success', 'SQL 执行完成');
      }
    }
    catch (error) {
      if (abortController.signal.aborted) {
        updateSqlTab(tabId, (current) => ({
          ...current,
          loading: false,
          error: null,
        }));
        showNotice('warning', 'SQL 执行已取消');
        return;
      }

      updateSqlTab(tabId, (current) => ({
        ...current,
        loading: false,
        error: error instanceof Error ? error.message : 'SQL 执行失败',
      }));
      showNotice('warning', error instanceof Error ? error.message : 'SQL 执行失败');
    }
    finally {
      sqlAbortControllers.delete(tabId);
    }
  }

  /** 取消正在执行的 SQL 请求。 */
  function cancelSqlExecution(tabId: string) {
    const controller = sqlAbortControllers.get(tabId);
    if (controller) {
      controller.abort();
    }
  }

  /** 根据 AST 解析出的名称在已知例程中查找匹配项。 */
  function findRoutineByCall(connectionId: string, database: string, name: string, schema: string | null): RoutineInfo | null {
    const routines = getRoutines(connectionId, database);
    if (routines.length === 0) {
      return null;
    }

    const lowerName = name.toLowerCase();
    const lowerSchema = schema?.toLowerCase() ?? null;

    return routines.find((r) => {
      if (r.name.toLowerCase() !== lowerName) {
        return false;
      }

      if (lowerSchema === null) {
        return true;
      }

      return (r.schema?.toLowerCase() ?? null) === lowerSchema;
    }) ?? null;
  }

  /** 根据 Tab displayName（格式 "schema.name" 或 "name"）查找例程。 */
  function findRoutineByDisplayName(connectionId: string, database: string, displayName: string): RoutineInfo | null {
    const dotIndex = displayName.indexOf('.');
    const schema = dotIndex >= 0 ? displayName.slice(0, dotIndex) : null;
    const name = dotIndex >= 0 ? displayName.slice(dotIndex + 1) : displayName;
    return findRoutineByCall(connectionId, database, name, schema);
  }

  /** 用户在参数对话框点击执行后，将生成的 SQL 执行。 */
  async function confirmRoutineExec(generatedSql: string) {
    const pending = pendingRoutineExec.value;
    if (!pending) {
      return;
    }

    pendingRoutineExec.value = null;

    if (pending.tabId) {
      // 已有 SQL Tab：保留编辑器内容不变，后台执行生成的 SQL
      await executeSqlTab(pending.tabId, generatedSql, { skipRoutineCheck: true, preserveText: true });
    } else {
      // 从右键菜单直接触发：先获取源码显示，再后台执行生成的 SQL
      const routine = pending.routine;
      const displayName = routine.schema ? `${routine.schema}.${routine.name}` : routine.name;
      const tabId = await openSqlTabWithContext({
        connectionId: pending.connectionId,
        database: pending.database,
        displayName,
        isRoutineSource: true,
      });
      // 获取源码填充到 Tab
      try {
        const response = await requestJson<{ source: string | null }>('/api/explorer/routine-source', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            connectionId: pending.connectionId,
            database: pending.database,
            schema: routine.schema ?? null,
            name: routine.name,
            routineType: routine.routineType,
          }),
        });
        const source = response.source ?? `-- 无法获取 ${routine.name} 的源代码`;
        updateSqlTab(tabId, (tab) => ({
          ...tab,
          sqlText: source,
          savedSqlText: source,
        }));
      } catch {
        // 源码获取失败不阻塞执行
      }
      await executeSqlTab(tabId, generatedSql, { skipRoutineCheck: true, preserveText: true });
    }
  }

  function cancelRoutineExec() {
    pendingRoutineExec.value = null;
  }

  function closeDetailPanel(panelId: string) {
    const targetIndex = openPanels.value.findIndex((panel) => panel.id === panelId);
    if (targetIndex <= 0) {
      return;
    }

    setActiveTablePanels(openPanels.value.slice(0, targetIndex));
  }

  async function openTable(tableKey: string) {
    try {
      await ensureTableLoaded(tableKey);
    }
    catch (error) {
      showNotice('warning', error instanceof Error ? error.message : '表数据加载失败');
      return;
    }

    const connection = getConnectionForTable(tableKey);
    activeConnectionId.value = connection?.id ?? activeConnectionId.value;
    const tabId = buildTableTabId(tableKey);
    const existing = workspaceTabs.value.find((tab) => tab.id === tabId && tab.type === 'table') as TableWorkspaceTab | undefined;
    if (!existing) {
      upsertWorkspaceTab({
        id: tabId,
        type: 'table',
        tableKey,
        detailPanels: [],
      });
    }
    setActiveTab(tabId);
    setActiveTablePanels([{ id: `grid:${tableKey}`, type: 'grid', tableKey }]);
    globalSearch.value = '';
    searchColumns.value = [];
    tableSortState.value = {
      tableKey,
      columnName: null,
      direction: 'none',
    };
    clearTableSearch();
  }

  async function openTableDesign(tableKey: string) {
    try {
      await ensureTableDesignLoaded(tableKey);
    }
    catch (error) {
      showNotice('warning', error instanceof Error ? error.message : '表设计加载失败');
      return;
    }

    const connection = getConnectionForTable(tableKey);
    activeConnectionId.value = connection?.id ?? activeConnectionId.value;
    const tabId = buildDesignTabId(tableKey);
    const existing = workspaceTabs.value.find((tab) => tab.id === tabId && tab.type === 'design') as TableDesignWorkspaceTab | undefined;
    if (!existing) {
      upsertWorkspaceTab({
        id: tabId,
        type: 'design',
        tableKey,
        selectedSection: 'columns',
        selectedEntry: null,
      });
    }

    setActiveTab(tabId);
  }

  function openCreateTableDesign(connectionId: string, database: string) {
    const connection = getConnectionInfo(connectionId);
    if (!connection) {
      showNotice('warning', '连接不存在，无法打开建表设计器');
      return;
    }

    const tabId = buildCreateDesignTabId();
    upsertWorkspaceTab({
      id: tabId,
      type: 'design',
      tableKey: `__create__:${tabId}`,
      selectedSection: 'properties',
      selectedEntry: null,
      createContext: {
        connectionId,
        database,
        provider: connection.provider,
        schema: connection.provider === 'sqlserver'
          ? 'dbo'
          : connection.provider === 'postgresql'
            ? 'public'
            : null,
        tableName: 'new_table',
      },
    });
    activeConnectionId.value = connectionId;
    setActiveTab(tabId);
  }

  async function toggleTableSort(tableKey: string, columnName: string, columnType?: string) {
    if (!isSortableColumn(columnName, columnType)) {
      return;
    }

    const current = getTableSortState(tableKey);
    let nextDirection: SortDirection = 'asc';
    if (current.columnName === columnName) {
      nextDirection = current.direction === 'none'
        ? 'asc'
        : current.direction === 'asc'
          ? 'desc'
          : 'none';
    }

    tableSortState.value = {
      tableKey,
      columnName: nextDirection === 'none' ? null : columnName,
      direction: nextDirection,
    };

    if (isSearchActive(tableKey)) {
      await applyActiveTableSearch();
      return;
    }

    await ensureTableLoaded(tableKey, true);
  }

  async function openRowFromGrid(tableKey: string, rowKey: string) {
    const panelId = `detail:${tableKey}:${rowKey}`;
    const nextPanels: ExplorerPanel[] = [
      { id: `grid:${tableKey}`, type: 'grid', tableKey },
      { id: panelId, type: 'detail', tableKey, rowKey, depth: 1, sourceLabel: '主表记录' },
    ];
    setActiveTablePanels(nextPanels);

    try {
      await ensureRecordLoaded(panelId, tableKey, rowKey);
    }
    catch (error) {
      setActiveTablePanels(nextPanels.filter((panel) => panel.id !== panelId));
      showNotice('warning', error instanceof Error ? error.message : '详情加载失败');
    }
  }

  async function inspectRecord(tableKey: string, rowKey: string, sourceLabel: string) {
    try {
      await ensureTableLoaded(tableKey);
    }
    catch (error) {
      showNotice('warning', error instanceof Error ? error.message : '表数据加载失败');
      return;
    }

    const panelId = `detail:${tableKey}:${rowKey}`;
    const tabId = buildTableTabId(tableKey);
    if (!workspaceTabs.value.some((tab) => tab.id === tabId)) {
      upsertWorkspaceTab({
        id: tabId,
        type: 'table',
        tableKey,
        detailPanels: [],
      });
    }
    setActiveTab(tabId);
    setActiveTablePanels([
      { id: `grid:${tableKey}`, type: 'grid', tableKey },
      { id: panelId, type: 'detail', tableKey, rowKey, depth: 1, sourceLabel },
    ]);

    try {
      await ensureRecordLoaded(panelId, tableKey, rowKey);
    }
    catch (error) {
      setActiveTablePanels([{ id: `grid:${tableKey}`, type: 'grid', tableKey }]);
      showNotice('warning', error instanceof Error ? error.message : '详情加载失败');
    }
  }

  async function resolveForeignKey(tableKey: string, rowKey: string, columnName: string) {
    return await requestJson<ForeignKeyTarget>(`/api/explorer/foreign-key?tableKey=${encodeURIComponent(tableKey)}&rowKey=${encodeURIComponent(rowKey)}&columnName=${encodeURIComponent(columnName)}`);
  }

  async function navigateToResolvedTarget(target: ForeignKeyTarget, parentPanelId?: string) {
    const nextIdentity = `${target.targetTableKey}::${target.targetRowKey}`;
    const visibleDetails = parentPanelId
      ? detailPanels.value.slice(0, detailPanels.value.findIndex((panel) => panel.id === parentPanelId) + 1)
      : detailPanels.value;

    const existingPanel = visibleDetails.find((panel) => `${panel.tableKey}::${panel.rowKey}` === nextIdentity);
    if (existingPanel) {
      focusDetailPanel(existingPanel.id);
      showNotice('warning', '触发循环引用，该数据已在前方卡片中展开');
      return;
    }

    const nextPanels = parentPanelId
      ? openPanels.value.slice(0, openPanels.value.findIndex((panel) => panel.id === parentPanelId) + 1)
      : openPanels.value.slice(0, 1);
    const panelId = `detail:${target.targetTableKey}:${target.targetRowKey}`;
    nextPanels.push({
      id: panelId,
      type: 'detail',
      tableKey: target.targetTableKey,
      rowKey: target.targetRowKey,
      depth: nextPanels.filter((panel) => panel.type === 'detail').length + 1,
      sourceLabel: target.sourceLabel,
    });
    setActiveTablePanels(nextPanels);

    try {
      await ensureRecordLoaded(panelId, target.targetTableKey, target.targetRowKey);
    }
    catch (error) {
      setActiveTablePanels(nextPanels.filter((panel) => panel.id !== panelId));
      showNotice('warning', error instanceof Error ? error.message : '详情加载失败');
    }
  }

  async function navigateForeignKeyFromGrid(tableKey: string, rowKey: string, fk: ForeignKeyRef) {
    const target = await resolveForeignKey(tableKey, rowKey, fk.sourceColumn);
    await navigateToResolvedTarget(target);
  }

  async function navigateForeignKeyFromDetail(parentPanelId: string, tableKey: string, rowKey: string, columnName: string) {
    const target = await resolveForeignKey(tableKey, rowKey, columnName);
    await navigateToResolvedTarget(target, parentPanelId);
  }

  async function navigateReverseReference(parentPanelId: string, sourceTableKey: string, rowKey: string, sourceLabel: string) {
    await navigateToResolvedTarget({ targetTableKey: sourceTableKey, targetRowKey: rowKey, sourceLabel }, parentPanelId);
  }

  function focusDetailPanel(panelId: string) {
    const targetIndex = openPanels.value.findIndex((panel) => panel.id === panelId);
    if (targetIndex <= 0) {
      return;
    }

    setActiveTablePanels(openPanels.value.slice(0, targetIndex + 1));
  }

  async function navigateFirstReverseReference(panelId: string, showEmptyNotice = true) {
    const panel = detailPanels.value.find((entry) => entry.id === panelId);
    if (!panel) {
      return;
    }

    const groups = getReverseReferences(panel);
    const firstGroup = groups[0];
    const firstRow = firstGroup?.rows[0];
    if (!firstGroup || !firstRow) {
      if (showEmptyNotice) {
        showNotice('info', '当前记录没有下一层反向引用');
      }
      return;
    }

    await navigateReverseReference(panelId, firstGroup.sourceTableKey, firstRow.rowKey, firstGroup.relationLabel);
  }

  async function inspectForeignKeyFromDetail(tableKey: string, rowKey: string, columnName: string) {
    const target = await resolveForeignKey(tableKey, rowKey, columnName);
    await inspectRecord(target.targetTableKey, target.targetRowKey, target.sourceLabel);
  }

  async function inspectReverseReference(sourceTableKey: string, rowKey: string, sourceLabel: string) {
    await inspectRecord(sourceTableKey, rowKey, sourceLabel);
  }

  function closeDetails() {
    if (!gridPanel.value) {
      setActiveTablePanels([]);
      return;
    }

    setActiveTablePanels([gridPanel.value]);
  }

  async function deleteConnection(connectionId: string) {
    const response = await fetch(`/api/explorer/connections/${connectionId}`, { method: 'DELETE' });
    if (!response.ok) {
      throw new Error(await response.text());
    }

    connectedConnectionIds.value = new Set([...connectedConnectionIds.value].filter((id) => id !== connectionId));
    loadedTables.value = Object.fromEntries(Object.entries(loadedTables.value).filter(([tableKey]) => !tableKey.startsWith(connectionId)));
    detailCache.value = Object.fromEntries(Object.entries(detailCache.value).filter(([panelId]) => !panelId.includes(connectionId)));
    workspaceTabs.value = workspaceTabs.value.filter((tab) => {
      if (tab.type === 'table' || tab.type === 'design' || tab.type === 'mock') {
        return !tab.tableKey.startsWith(connectionId);
      }

      return tab.type === 'graph' || tab.type === 'sql'
        ? tab.connectionId !== connectionId
        : true;
    });
    ensureActiveTabSelection();

    await refreshBootstrap();
    showNotice('success', '连接已删除');
  }

  async function createConnection(request: CreateConnectionRequest) {
    const response = await fetch('/api/explorer/connections', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
    });

    if (!response.ok) {
      throw new Error(await response.text());
    }

    await refreshBootstrap();
    showNotice('success', '连接已创建');
  }

  async function getConnectionConfig(connectionId: string) {
    return await requestJson<ConnectionConfig>(`/api/explorer/connections/${encodeURIComponent(connectionId)}`);
  }

  async function testConnection(request: TestConnectionRequest) {
    const response = await fetch('/api/explorer/connections/test', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
    });

    if (!response.ok) {
      throw new Error(await response.text());
    }

    showNotice('success', '连接测试成功');
  }

  async function updateConnection(connectionId: string, request: CreateConnectionRequest) {
    const wasConnected = connectedConnectionIds.value.has(connectionId);

    const response = await fetch(`/api/explorer/connections/${encodeURIComponent(connectionId)}`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
    });

    if (!response.ok) {
      throw new Error(await response.text());
    }

    await refreshBootstrap({ rehydrateConnectionIds: wasConnected ? [connectionId] : [] });
    showNotice('success', '连接已更新');
  }

  async function rekeySqliteDatabase(request: SqliteRekeyRequest) {
    const response = await fetch('/api/explorer/connections/rekey', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
    });

    if (!response.ok) {
      throw new Error(await response.text());
    }

    await refreshBootstrap({ rehydrateConnectionIds: connectedConnectionIds.value.has(request.connectionId) ? [request.connectionId] : [] });
    showNotice('success', 'SQLite 加密密钥已更新');
  }

  function getReverseReferences(panel: ExplorerDetailPanel): ReverseReferenceGroup[] {
    return detailCache.value[panel.id]?.reverseReferences ?? [];
  }

  initialize().catch((error) => {
    showNotice('warning', error instanceof Error ? error.message : 'Initialization failed');
  });

  return {
    connections,
    workspaceTabs,
    activeTabId,
    pendingSqlEditorFocusTabId,
    activeTab,
    activeTableTab,
    activeDesignTab,
    activeSqlTab,
    activeGraphTab,
    activeCatalogTab,
    activeMockTab,
    activeSettingsTab,
    activeSqlServerLoginManagerTab,
    activeSqlServerLoginEditorTab,
    explorerSettings,
    settingsDraft,
    explorerSettingsLoaded,
    workspaceLayout,
    workspaceLayoutLoaded,
    isSettingsDirty,
    settingsSaving,
    showTableRowCounts,
    sidebarQuery,
    globalSearch,
    searchColumns,
    tableSortState,
    pendingSqlClose,
    pendingDesignClose,
    pendingRoutineExec,
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
    bootstrapError,
    getDatabases,
    getConnectionDatabases,
    fetchDatabaseNames,
    getConnectionInfo,
    getSqlContext,
    getTables,
    getViews,
    getSynonyms,
    getSequences,
    getRules,
    getDefaults,
    getUserDefinedTypes,
    getDatabaseTriggers,
    getXmlSchemaCollections,
    getAssemblies,
    getRoutines,
    getWorkspaceTabLabel,
    getTable,
    getTableDesign,
    getTableDesignStatus,
    getCatalogObject,
    getCatalogObjectState,
    getLoadedTable,
    isConnectionReadOnly,
    isTableConnectionReadOnly,
    ensureTableLoaded,
    ensureTableDesignLoaded,
    ensureCatalogObjectLoaded,
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
    requestSqlEditorFocus,
    consumeSqlEditorFocus,
    closeWorkspaceTab,
    moveWorkspaceTab,
    openTable,
    openTableDesign,
    openCreateTableDesign,
    openCatalogObject,
    openSqlServerLoginManager,
    openSqlServerLoginEditor,
    openTableMockTab,
    openSqlTab,
    openSettingsTab,
    updateSqlServerLoginManagerState,
    updateSqlServerLoginEditorState,
    openSqlTabWithContext,
    openSqlFileTab,
    openRoutineSource,
    pickSqliteDatabaseFile,
    pickSshPrivateKeyFile,
    updateSqlTabConnection,
    updateSqlTabDatabase,
    updateSqlTabText,
    clearSqlTabError,
    markSqlTabSaved,
    isSqlTabDirty,
    isCreateDesignTabDirty,
    saveSqlTab,
    saveTextFile,
    savePendingSqlClose,
    discardPendingSqlClose,
    cancelPendingSqlClose,
    discardPendingDesignClose,
    cancelPendingDesignClose,
    selectSqlResultSet,
    executeSqlTab,
    cancelSqlExecution,
    confirmRoutineExec,
    cancelRoutineExec,
    updateDesignTabSelection,
    executeTableDesignSql,
    deleteDesignColumn,
    deleteDesignIndex,
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
    deleteTableRow,
    deleteTableRows,
    refreshActiveTableData,
    refreshDatabase,
    openDatabaseGraph,
    closeDatabaseGraph,
    openDatabaseProperties,
    activeDatabasePropertiesTab,
    defaultPageSize,
    createConnection,
    getConnectionConfig,
    testConnection,
    updateConnection,
    rekeySqliteDatabase,
    ensureExplorerSettingsLoaded,
    ensureWorkspaceLayoutLoaded,
    resetExplorerSettingsDraft,
    saveWorkspaceLayout,
    saveExplorerSettings,
    deleteConnection,
    showNotice,
    dismissNotice,
    refreshBootstrap,
    connectConnection,
    disconnectConnection,
    isConnectionConnected,
    isConnectionConnecting,
    connectedConnectionIds,
    connectingConnectionIds,
  };
});