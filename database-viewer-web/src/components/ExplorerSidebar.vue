<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { NAlert, NButton, NCheckbox, NEmpty, NInput, NModal, NSelect, NSpin, NTag, NText } from 'naive-ui'
import { useExplorerStore } from '../stores/explorer'
import type { AuthenticationMode, ConnectionInfo, ProviderType, TableSummary } from '../types/explorer'

const store = useExplorerStore()
const expandedConnections = ref<string[]>([])
const expandedDatabases = ref<string[]>([])
const connectionContextMenu = ref<{ x: number; y: number; connectionId: string; connectionName: string } | null>(null)
const databaseContextMenu = ref<{ x: number; y: number; connectionId: string; database: string } | null>(null)
const tableContextMenu = ref<{ x: number; y: number; connectionId: string; database: string; table: TableSummary; showScriptSubmenu: boolean } | null>(null)
const deleteConnectionConfirm = ref<{ connectionId: string; connectionName: string } | null>(null)
const createConnectionVisible = ref(false)
const createConnectionLoading = ref(false)
const createConnectionError = ref<string | null>(null)
const createConnectionForm = reactive({
  name: '',
  provider: 'sqlserver' as ProviderType,
  authentication: 'password' as AuthenticationMode,
  host: '',
  port: '1433',
  username: '',
  password: '',
  trustServerCertificate: true,
})
const closeAllContextMenus = () => {
  closeConnectionContextMenu()
  closeDatabaseContextMenu()
  closeTableContextMenu()
}

const visibleConnections = computed(() => store.visibleConnections)
const providerOptions = [
  { label: 'SQL Server', value: 'sqlserver' },
  { label: 'MySQL', value: 'mysql' },
  { label: 'PostgreSQL', value: 'postgresql' },
]
const authenticationOptions = computed(() => createConnectionForm.provider === 'sqlserver'
  ? [
      { label: '账号密码', value: 'password' },
      { label: 'Windows 身份验证', value: 'windows' },
    ]
  : [
      { label: '账号密码', value: 'password' },
    ])

function toggleConnection(connectionId: string) {
  expandedConnections.value = expandedConnections.value.includes(connectionId)
    ? expandedConnections.value.filter((id) => id !== connectionId)
    : [...expandedConnections.value, connectionId]
}

function toggleDatabase(key: string) {
  expandedDatabases.value = expandedDatabases.value.includes(key)
    ? expandedDatabases.value.filter((id) => id !== key)
    : [...expandedDatabases.value, key]
}

function providerLabel(connection: ConnectionInfo) {
  return connection.provider === 'sqlserver'
    ? 'SQL Server'
    : connection.provider === 'postgresql'
      ? 'PostgreSQL'
      : 'MySQL'
}

async function confirmDeleteConnection(connectionId: string) {
  try {
    await store.deleteConnection(connectionId)
  }
  catch (error) {
    store.refreshBootstrap()
  }
  finally {
    deleteConnectionConfirm.value = null
  }
}

function openConnectionContextMenu(event: MouseEvent, connectionId: string, connectionName: string) {
  closeDatabaseContextMenu()
  closeTableContextMenu()
  connectionContextMenu.value = {
    x: event.clientX,
    y: event.clientY,
    connectionId,
    connectionName,
  }
}

function closeConnectionContextMenu() {
  connectionContextMenu.value = null
}

function openDeleteConnectionConfirm() {
  if (!connectionContextMenu.value) {
    return
  }

  deleteConnectionConfirm.value = {
    connectionId: connectionContextMenu.value.connectionId,
    connectionName: connectionContextMenu.value.connectionName,
  }
  closeConnectionContextMenu()
}

function closeDeleteConnectionConfirm() {
  deleteConnectionConfirm.value = null
}

function openDatabaseContextMenu(event: MouseEvent, connectionId: string, database: string) {
  closeConnectionContextMenu()
  closeTableContextMenu()
  databaseContextMenu.value = {
    x: event.clientX,
    y: event.clientY,
    connectionId,
    database,
  }
}

function closeDatabaseContextMenu() {
  databaseContextMenu.value = null
}

function openTableContextMenu(event: MouseEvent, connectionId: string, database: string, table: TableSummary) {
  closeConnectionContextMenu()
  closeDatabaseContextMenu()
  tableContextMenu.value = {
    x: event.clientX,
    y: event.clientY,
    connectionId,
    database,
    table,
    showScriptSubmenu: false,
  }
}

function closeTableContextMenu() {
  tableContextMenu.value = null
}

function setTableScriptSubmenu(open: boolean) {
  if (!tableContextMenu.value) {
    return
  }

  tableContextMenu.value = {
    ...tableContextMenu.value,
    showScriptSubmenu: open,
  }
}

function providerForConnection(connectionId: string): ProviderType {
  return store.getConnectionInfo(connectionId)?.provider ?? 'sqlserver'
}

function quoteIdentifier(provider: ProviderType, value: string) {
  if (provider === 'mysql') {
    return `\`${value.replace(/`/g, '``')}\``
  }

  if (provider === 'postgresql') {
    return `"${value.replace(/"/g, '""')}"`
  }

  return `[${value.replace(/\]/g, ']]')}]`
}

function formatSchemaTable(provider: ProviderType, table: TableSummary) {
  const parts = []
  if (table.schema) {
    parts.push(quoteIdentifier(provider, table.schema))
  }
  parts.push(quoteIdentifier(provider, table.name))
  return parts.join('.')
}

function buildBatchPrefix(provider: ProviderType, database: string) {
  if (provider === 'sqlserver') {
    return `USE ${quoteIdentifier(provider, database)}\nGO\n\n`
  }

  if (provider === 'mysql') {
    return `USE ${quoteIdentifier(provider, database)};\n\n`
  }

  return `-- database: ${database}\n\n`
}

function buildBatchSuffix(provider: ProviderType) {
  return provider === 'sqlserver' ? '\nGO' : ';'
}

async function getTableColumns(connectionId: string, database: string, table: TableSummary) {
  await store.ensureSqlContextLoaded(connectionId, database)
  const sqlContextTable = store.getSqlContext(connectionId, database)?.tables.find((entry) => {
    const qualifiedName = table.schema ? `${table.schema}.${table.name}` : table.name
    return entry.qualifiedName === qualifiedName || entry.name === table.name
  })

  try {
    const loaded = await store.ensureTableLoaded(table.key)
    if (loaded?.columns?.length) {
      return loaded.columns.map((column) => ({
        name: column.name,
        type: column.type,
        isNullable: column.isNullable,
      }))
    }
  }
  catch {
    // Fall back to SQL context columns when table schema is not loaded.
  }

  return (sqlContextTable?.columns ?? []).map((column) => ({
    name: column,
    type: undefined,
    isNullable: true,
  }))
}

function buildSelectScript(provider: ProviderType, database: string, table: TableSummary, columns: Array<{ name: string }>) {
  const selectLines = columns.length
    ? columns.map((column, index) => `${index === 0 ? 'SELECT' : '      ,'} ${quoteIdentifier(provider, column.name)}`)
    : ['SELECT *']
  return `${buildBatchPrefix(provider, database)}${selectLines.join('\n')}\n  FROM ${formatSchemaTable(provider, table)}${buildBatchSuffix(provider)}`
}

function buildDeleteScript(provider: ProviderType, database: string, table: TableSummary) {
  return `${buildBatchPrefix(provider, database)}DELETE FROM ${formatSchemaTable(provider, table)}\n      WHERE <搜索条件,,>${buildBatchSuffix(provider)}`
}

function buildDropScript(provider: ProviderType, database: string, table: TableSummary) {
  return `${buildBatchPrefix(provider, database)}DROP TABLE ${formatSchemaTable(provider, table)}${buildBatchSuffix(provider)}`
}

function buildInsertScript(provider: ProviderType, database: string, table: TableSummary, columns: Array<{ name: string; type?: string }>) {
  const names = columns.length ? columns : [{ name: 'Column1', type: 'type' }]
  const columnLines = names.map((column, index) => `${index === 0 ? '           (' : '           ,'}${quoteIdentifier(provider, column.name)}`).join('\n')
  const valueLines = names.map((column, index) => `${index === 0 ? '           (' : '           ,'}<${column.name}, ${column.type ?? 'type'},>`).join('\n')
  return `${buildBatchPrefix(provider, database)}INSERT INTO ${formatSchemaTable(provider, table)}\n${columnLines})\n     VALUES\n${valueLines})${buildBatchSuffix(provider)}`
}

function buildUpdateScript(provider: ProviderType, database: string, table: TableSummary, columns: Array<{ name: string; type?: string }>) {
  const names = columns.length ? columns : [{ name: 'Column1', type: 'type' }]
  const setLines = names.map((column, index) => `${index === 0 ? '   SET' : '      ,'} ${quoteIdentifier(provider, column.name)} = <${column.name}, ${column.type ?? 'type'},>`).join('\n')
  return `${buildBatchPrefix(provider, database)}UPDATE ${formatSchemaTable(provider, table)}\n${setLines}\n WHERE <搜索条件,,>${buildBatchSuffix(provider)}`
}

function buildCreateScript(provider: ProviderType, database: string, table: TableSummary, columns: Array<{ name: string; type?: string; isNullable?: boolean }>) {
  const names = columns.length ? columns : [{ name: 'Column1', type: 'int', isNullable: false }]
  const body = names.map((column, index) => `    ${index === 0 ? '' : ','}${quoteIdentifier(provider, column.name)} ${column.type ?? 'nvarchar(255)'} ${column.isNullable === false ? 'NOT NULL' : 'NULL'}`).join('\n')
  return `${buildBatchPrefix(provider, database)}CREATE TABLE ${formatSchemaTable(provider, table)}\n(\n${body}\n)${buildBatchSuffix(provider)}`
}

async function openDatabaseSqlQuery() {
  if (!databaseContextMenu.value) {
    return
  }

  const { connectionId, database } = databaseContextMenu.value
  closeDatabaseContextMenu()
  await store.openSqlTabWithContext({ connectionId, database })
}

async function openTableScript(kind: 'create' | 'drop' | 'select' | 'insert' | 'update' | 'delete') {
  if (!tableContextMenu.value) {
    return
  }

  const { connectionId, database, table } = tableContextMenu.value
  closeTableContextMenu()
  const provider = providerForConnection(connectionId)
  const columns = await getTableColumns(connectionId, database, table)

  const sqlText = kind === 'create'
    ? buildCreateScript(provider, database, table, columns)
    : kind === 'drop'
      ? buildDropScript(provider, database, table)
      : kind === 'select'
        ? buildSelectScript(provider, database, table, columns)
        : kind === 'insert'
          ? buildInsertScript(provider, database, table, columns)
          : kind === 'update'
            ? buildUpdateScript(provider, database, table, columns)
            : buildDeleteScript(provider, database, table)

  await store.openSqlTabWithContext({
    connectionId,
    database,
    sqlText,
  })
}

async function openDatabaseGraphOverview() {
  if (!databaseContextMenu.value) {
    return
  }

  const { connectionId, database } = databaseContextMenu.value
  closeDatabaseContextMenu()
  await store.openDatabaseGraph(connectionId, database)
}

async function refreshDatabaseFromMenu() {
  if (!databaseContextMenu.value) {
    return
  }

  const { connectionId, database } = databaseContextMenu.value
  closeDatabaseContextMenu()
  await store.refreshDatabase(connectionId, database)
}

function resetCreateConnectionForm() {
  createConnectionForm.name = ''
  createConnectionForm.provider = 'sqlserver'
  createConnectionForm.authentication = 'password'
  createConnectionForm.host = ''
  createConnectionForm.port = '1433'
  createConnectionForm.username = ''
  createConnectionForm.password = ''
  createConnectionForm.trustServerCertificate = true
  createConnectionError.value = null
}

function openCreateConnectionDialog() {
  closeAllContextMenus()
  resetCreateConnectionForm()
  createConnectionVisible.value = true
}

function handleProviderChange(provider: ProviderType) {
  createConnectionForm.provider = provider
  createConnectionForm.port = provider === 'sqlserver' ? '1433' : provider === 'postgresql' ? '5432' : '3306'
  if (provider === 'mysql' && createConnectionForm.authentication === 'windows') {
    createConnectionForm.authentication = 'password'
  }

  if (provider === 'postgresql' && createConnectionForm.authentication === 'windows') {
    createConnectionForm.authentication = 'password'
  }
}

async function submitCreateConnection() {
  createConnectionLoading.value = true
  createConnectionError.value = null

  try {
    await store.createConnection({
      name: createConnectionForm.name,
      provider: createConnectionForm.provider,
      authentication: createConnectionForm.authentication,
      host: createConnectionForm.host,
      port: Number.isFinite(Number(createConnectionForm.port)) ? Number(createConnectionForm.port) : null,
      username: createConnectionForm.authentication === 'windows' ? null : createConnectionForm.username,
      password: createConnectionForm.authentication === 'windows' ? null : createConnectionForm.password,
      trustServerCertificate: createConnectionForm.trustServerCertificate,
    })
    createConnectionVisible.value = false
  }
  catch (error) {
    createConnectionError.value = error instanceof Error ? error.message : '连接创建失败'
  }
  finally {
    createConnectionLoading.value = false
  }
}

onMounted(() => {
  window.addEventListener('click', closeAllContextMenus)
})

onBeforeUnmount(() => {
  window.removeEventListener('click', closeAllContextMenus)
})
</script>

<template>
  <div class="sidebar-shell">
    <div class="sidebar-toolbar compact-panel">
      <strong>连接与表</strong>
      <div class="sidebar-toolbar-actions">
        <n-button size="tiny" tertiary type="primary" @click="openCreateConnectionDialog">新建连接</n-button>
      </div>
    </div>

    <n-input
      v-model:value="store.sidebarQuery"
      clearable
      size="large"
      placeholder="筛选连接、数据库或表名"
    />

    <div class="sidebar-list">
      <n-spin :show="store.isBootstrapping">
      <template v-if="visibleConnections.length">
        <div
          v-for="connection in visibleConnections"
          :key="connection.id"
          class="connection-card"
          :style="{ '--connection-accent': connection.accent }"
        >
          <div class="connection-header" @contextmenu.prevent.stop="openConnectionContextMenu($event, connection.id, connection.name)">
            <button class="connection-toggle" type="button" @click="toggleConnection(connection.id)">
              <div class="connection-title-block">
                <div class="connection-title-row">
                  <strong>{{ connection.name }}</strong>
                  <n-tag size="small" :bordered="false" :color="{ color: connection.accent + '18', textColor: connection.accent }">
                    {{ providerLabel(connection) }}
                  </n-tag>
                </div>
                <n-text depth="3">{{ connection.host }}<template v-if="connection.port">:{{ connection.port }}</template></n-text>
              </div>
              <span class="toggle-indicator">{{ expandedConnections.includes(connection.id) ? '−' : '+' }}</span>
            </button>
          </div>

          <n-alert v-if="connection.error" type="warning" :show-icon="false" class="connection-error">
            {{ connection.error }}
          </n-alert>

          <div v-if="expandedConnections.includes(connection.id)" class="database-list" @contextmenu.prevent.stop>
            <div
              v-for="database in store.getDatabases(connection.id)"
              :key="`${connection.id}:${database.name}`"
              class="database-block"
            >
              <button
                class="database-toggle"
                type="button"
                @click="toggleDatabase(`${connection.id}:${database.name}`)"
                @contextmenu.prevent.stop="openDatabaseContextMenu($event, connection.id, database.name)"
                :class="{ 'database-toggle-expanded': expandedDatabases.includes(`${connection.id}:${database.name}`) }"
              >
                <div class="database-title-block">
                  <span class="database-name-text">{{ database.name }}</span>
                  <span class="database-summary-text">{{ store.getTables(connection.id, database.name).length }} tables</span>
                </div>
                <div class="database-toggle-meta">
                  <span class="database-toggle-indicator">{{ expandedDatabases.includes(`${connection.id}:${database.name}`) ? '−' : '+' }}</span>
                </div>
              </button>

              <div
                v-if="expandedDatabases.includes(`${connection.id}:${database.name}`)"
                class="table-list table-list-animated"
                @contextmenu.prevent.stop
              >
                <button
                  v-for="table in store.getTables(connection.id, database.name)"
                  :key="table.key"
                  type="button"
                  class="table-tile"
                  @contextmenu.stop.prevent="openTableContextMenu($event, connection.id, database.name, table)"
                  @click="store.openTable(table.key)"
                >
                  <div class="table-title-row compact-table-title-row">
                    <strong class="table-name-text">{{ table.schema ? `${table.schema}.${table.name}` : table.name }}</strong>
                  </div>
                  <div class="table-meta compact-table-meta">
                    <span>{{ store.tableRowCountLabel(table.key) ?? '未统计' }} rows</span>
                    <span v-if="table.comment" class="table-comment-text">{{ table.comment }}</span>
                  </div>
                </button>
              </div>
            </div>
          </div>
        </div>
      </template>

      <n-empty v-else description="没有匹配的连接或表" size="large" />
      </n-spin>
    </div>

    <div
      v-if="connectionContextMenu"
      class="database-context-menu"
      :style="{ left: `${connectionContextMenu.x}px`, top: `${connectionContextMenu.y}px` }"
    >
      <button type="button" class="database-context-menu-item" @click.stop="openDeleteConnectionConfirm()">
        删除连接
      </button>
    </div>

    <div
      v-if="databaseContextMenu"
      class="database-context-menu"
      :style="{ left: `${databaseContextMenu.x}px`, top: `${databaseContextMenu.y}px` }"
    >
      <button type="button" class="database-context-menu-item" @click="openDatabaseSqlQuery()">
        新建查询
      </button>
      <button type="button" class="database-context-menu-item" @click="refreshDatabaseFromMenu">
        刷新数据库
      </button>
      <button type="button" class="database-context-menu-item" @click="openDatabaseGraphOverview">
        查看关系总览
      </button>
    </div>

    <div
      v-if="tableContextMenu"
      class="database-context-menu"
      :style="{ left: `${tableContextMenu.x}px`, top: `${tableContextMenu.y}px` }"
    >
      <button
        type="button"
        class="database-context-menu-item database-context-menu-item-arrow"
        @mouseenter="setTableScriptSubmenu(true)"
        @click="setTableScriptSubmenu(!tableContextMenu.showScriptSubmenu)"
      >
        编写脚本为
      </button>
    </div>

    <div
      v-if="tableContextMenu?.showScriptSubmenu"
      class="database-context-menu database-context-submenu"
      :style="{ left: `${tableContextMenu.x + 186}px`, top: `${tableContextMenu.y + 34}px` }"
    >
      <button type="button" class="database-context-menu-item" @click="openTableScript('create')">CREATE 到</button>
      <button type="button" class="database-context-menu-item" @click="openTableScript('drop')">DROP 到</button>
      <button type="button" class="database-context-menu-item" @click="openTableScript('select')">SELECT 到</button>
      <button type="button" class="database-context-menu-item" @click="openTableScript('insert')">INSERT 到</button>
      <button type="button" class="database-context-menu-item" @click="openTableScript('update')">UPDATE 到</button>
      <button type="button" class="database-context-menu-item" @click="openTableScript('delete')">DELETE 到</button>
    </div>

    <n-modal v-model:show="createConnectionVisible" preset="card" style="width: min(460px, 92vw)" title="新建连接">
      <div class="connection-dialog-form">
        <n-input v-model:value="createConnectionForm.name" placeholder="连接名称" />
        <n-select :value="createConnectionForm.provider" :options="providerOptions" @update:value="handleProviderChange($event)" />
        <n-select v-model:value="createConnectionForm.authentication" :options="authenticationOptions" />
        <n-input v-model:value="createConnectionForm.host" placeholder="主机，例如 .\SQLEXPRESS 或 localhost" />
        <n-input v-model:value="createConnectionForm.port" placeholder="端口" inputmode="numeric" />
        <n-input v-if="createConnectionForm.authentication !== 'windows'" v-model:value="createConnectionForm.username" placeholder="用户名" />
        <n-input v-if="createConnectionForm.authentication !== 'windows'" v-model:value="createConnectionForm.password" type="password" show-password-on="click" placeholder="密码" />
        <n-checkbox v-if="createConnectionForm.provider === 'sqlserver'" v-model:checked="createConnectionForm.trustServerCertificate">信任服务器证书</n-checkbox>
        <n-alert v-if="createConnectionError" type="warning" :show-icon="false">{{ createConnectionError }}</n-alert>
        <div class="connection-dialog-actions">
          <n-button tertiary @click="createConnectionVisible = false">取消</n-button>
          <n-button type="primary" :loading="createConnectionLoading" @click="submitCreateConnection">保存连接</n-button>
        </div>
      </div>
    </n-modal>

    <n-modal
      :show="!!deleteConnectionConfirm"
      preset="card"
      style="width: min(420px, 92vw)"
      title="删除连接"
      @update:show="(show) => { if (!show) closeDeleteConnectionConfirm() }"
    >
      <div class="connection-dialog-form">
        <n-alert type="warning" :show-icon="false">
          确认删除连接 {{ deleteConnectionConfirm?.connectionName }}？
        </n-alert>
        <div class="connection-dialog-actions">
          <n-button tertiary @click="closeDeleteConnectionConfirm()">取消</n-button>
          <n-button type="error" @click="deleteConnectionConfirm && confirmDeleteConnection(deleteConnectionConfirm.connectionId)">删除</n-button>
        </div>
      </div>
    </n-modal>
  </div>
</template>
