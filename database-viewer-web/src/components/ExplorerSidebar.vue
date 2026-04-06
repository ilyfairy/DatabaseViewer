<script setup lang="ts">
import type { Component } from 'vue';
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue';
import { IconBolt, IconChevronDown, IconChevronRight, IconColumns3, IconDatabase, IconEye, IconFunction, IconLink, IconListDetails, IconScript, IconServer, IconTable, IconTableColumn, IconVariable } from '@tabler/icons-vue';
import { NAlert, NButton, NCheckbox, NEmpty, NIcon, NInput, NModal, NSelect, NSpin, NText } from 'naive-ui';
import { useExplorerStore } from '../stores/explorer';
import type { AuthenticationMode, ProviderType, RoutineInfo, SynonymInfo, TableColumn, TableDesignSection, TableSummary } from '../types/explorer';

const store = useExplorerStore();
const expandedConnections = ref<string[]>([]);
const expandedDatabases = ref<string[]>([]);
const expandedTables = ref<string[]>([]);
const expandedRoutineGroups = ref<string[]>([]);
const expandedRoutines = ref<string[]>([]);
const expandedRoutineParamGroups = ref<string[]>([]);
const expandedTableGroups = ref<string[]>([]);
const connectionContextMenu = ref<{ x: number; y: number; connectionId: string; connectionName: string } | null>(null);
const databaseContextMenu = ref<{ x: number; y: number; connectionId: string; database: string } | null>(null);
const tableContextMenu = ref<{ x: number; y: number; connectionId: string; database: string; table: TableSummary; showScriptSubmenu: boolean } | null>(null);
const routineContextMenu = ref<{ x: number; y: number; connectionId: string; database: string; provider: ProviderType; routine: RoutineInfo } | null>(null);
const designEntryContextMenu = ref<{ x: number; y: number; tableKey: string; kind: 'column' | 'index'; name: string } | null>(null);
const deleteConnectionConfirm = ref<{ connectionId: string; connectionName: string } | null>(null);
const deleteRoutineConfirm = ref<{ connectionId: string; database: string; provider: ProviderType; routine: RoutineInfo } | null>(null);
const renameTableTarget = ref<{ connectionId: string; database: string; table: TableSummary } | null>(null);
const renameTableVisible = ref(false);
const renameTableLoading = ref(false);
const renameTableError = ref<string | null>(null);
const renameTableName = ref('');
const renameRoutineTarget = ref<{ connectionId: string; database: string; provider: ProviderType; routine: RoutineInfo } | null>(null);
const renameRoutineVisible = ref(false);
const renameRoutineLoading = ref(false);
const renameRoutineError = ref<string | null>(null);
const renameRoutineName = ref('');
const editingConnectionId = ref<string | null>(null);
const createConnectionVisible = ref(false);
const createConnectionLoading = ref(false);
const testConnectionLoading = ref(false);
const createConnectionError = ref<string | null>(null);
const createConnectionForm = reactive({
  name: '',
  provider: 'sqlserver' as ProviderType,
  authentication: 'password' as AuthenticationMode,
  host: '',
  port: '1433',
  username: '',
  password: '',
  trustServerCertificate: true,
});
const closeAllContextMenus = () => {
  closeConnectionContextMenu();
  closeDatabaseContextMenu();
  closeTableContextMenu();
  closeRoutineContextMenu();
  closeDesignEntryContextMenu();
};

const visibleConnections = computed(() => store.visibleConnections);
const providerOptions = [
  { label: 'SQL Server', value: 'sqlserver' },
  { label: 'MySQL', value: 'mysql' },
  { label: 'PostgreSQL', value: 'postgresql' },
  { label: 'SQLite', value: 'sqlite' },
];
const authenticationOptions = computed(() => createConnectionForm.provider === 'sqlserver'
  ? [
      { label: '账号密码', value: 'password' },
      { label: 'Windows 身份验证', value: 'windows' },
    ]
  : [
      { label: createConnectionForm.provider === 'sqlite' ? '本地文件' : '账号密码', value: 'password' },
    ]);

function toggleConnection(connectionId: string) {
  expandedConnections.value = expandedConnections.value.includes(connectionId)
    ? expandedConnections.value.filter((id) => id !== connectionId)
    : [...expandedConnections.value, connectionId];
}

function toggleDatabase(key: string) {
  expandedDatabases.value = expandedDatabases.value.includes(key)
    ? expandedDatabases.value.filter((id) => id !== key)
    : [...expandedDatabases.value, key];
}

async function toggleTableNode(tableKey: string) {
  const expanded = expandedTables.value.includes(tableKey);
  if (expanded) {
    expandedTables.value = expandedTables.value.filter((key) => key !== tableKey);
    expandedTableGroups.value = expandedTableGroups.value.filter((key) => !key.startsWith(`${tableKey}:`));
    return;
  }

  try {
    await store.ensureTableDesignLoaded(tableKey);
    expandedTables.value = [...expandedTables.value, tableKey];
  }
  catch {
    expandedTables.value = [...expandedTables.value, tableKey];
    // Design status is surfaced by the store; tree stays expanded so the user can retry.
  }
}

function toggleTableGroup(tableKey: string, section: 'columns' | 'indexes' | 'foreignKeys' | 'triggers' | 'statistics') {
  const key = `${tableKey}:${section}`;
  expandedTableGroups.value = expandedTableGroups.value.includes(key)
    ? expandedTableGroups.value.filter((entry) => entry !== key)
    : [...expandedTableGroups.value, key];
}

function isTableExpanded(tableKey: string) {
  return expandedTables.value.includes(tableKey);
}

function isTableGroupExpanded(tableKey: string, section: 'columns' | 'indexes' | 'foreignKeys' | 'triggers' | 'statistics') {
  return expandedTableGroups.value.includes(`${tableKey}:${section}`);
}

function tableDesignForTree(tableKey: string) {
  return store.getTableDesign(tableKey);
}

function tableDesignStatusForTree(tableKey: string) {
  return store.getTableDesignStatus(tableKey);
}

function treeColumns(tableKey: string) {
  return tableDesignForTree(tableKey)?.columns ?? [];
}

function treeIndexes(tableKey: string) {
  return tableDesignForTree(tableKey)?.indexes ?? [];
}

function treeForeignKeys(tableKey: string) {
  return tableDesignForTree(tableKey)?.foreignKeys ?? [];
}

function treeTriggers(tableKey: string) {
  return tableDesignForTree(tableKey)?.triggers ?? [];
}

function treeStatistics(tableKey: string) {
  return tableDesignForTree(tableKey)?.statistics ?? [];
}

/** 切换存储过程/函数分组的展开状态 */
function toggleRoutineGroup(connectionId: string, database: string, group: 'tables' | 'views' | 'synonyms' | 'sequences' | 'rules' | 'defaults' | 'types' | 'table-types' | 'database-triggers' | 'xml-schema-collections' | 'procedures' | 'functions') {
  const key = `${connectionId}:${database}:${group}`;
  expandedRoutineGroups.value = expandedRoutineGroups.value.includes(key)
    ? expandedRoutineGroups.value.filter((entry) => entry !== key)
    : [...expandedRoutineGroups.value, key];
}

function isRoutineGroupExpanded(connectionId: string, database: string, group: 'tables' | 'views' | 'synonyms' | 'sequences' | 'rules' | 'defaults' | 'types' | 'table-types' | 'database-triggers' | 'xml-schema-collections' | 'procedures' | 'functions') {
  return expandedRoutineGroups.value.includes(`${connectionId}:${database}:${group}`);
}

function treeTables(connectionId: string, database: string): TableSummary[] {
  return store.getTables(connectionId, database);
}

function treeViews(connectionId: string, database: string): TableSummary[] {
  return store.getViews(connectionId, database);
}

function treeSynonyms(connectionId: string, database: string): SynonymInfo[] {
  return store.getSynonyms(connectionId, database);
}

function treeSequences(connectionId: string, database: string) {
  return store.getSequences(connectionId, database);
}

function treeRules(connectionId: string, database: string) {
  return store.getRules(connectionId, database);
}

function treeDefaults(connectionId: string, database: string) {
  return store.getDefaults(connectionId, database);
}

function treeUserDefinedTypes(connectionId: string, database: string) {
  return store.getUserDefinedTypes(connectionId, database);
}

function treeScalarTypes(connectionId: string, database: string) {
  return treeUserDefinedTypes(connectionId, database).filter((item) => !item.isTableType);
}

function treeTableTypes(connectionId: string, database: string) {
  return treeUserDefinedTypes(connectionId, database).filter((item) => item.isTableType);
}

function treeDatabaseTriggers(connectionId: string, database: string) {
  return store.getDatabaseTriggers(connectionId, database);
}

function treeXmlSchemaCollections(connectionId: string, database: string) {
  return store.getXmlSchemaCollections(connectionId, database);
}

/** 获取指定数据库下的存储过程列表 */
function treeProcedures(connectionId: string, database: string): RoutineInfo[] {
  return store.getRoutines(connectionId, database).filter((r) => r.routineType === 'Procedure');
}

/** 获取指定数据库下的函数列表 */
function treeFunctions(connectionId: string, database: string): RoutineInfo[] {
  return store.getRoutines(connectionId, database).filter((r) => r.routineType !== 'Procedure');
}

/** 格式化例程显示名（含 schema） */
function formatRoutineName(provider: ProviderType, routine: RoutineInfo) {
  if (routine.schema && provider !== 'mysql') {
    return `${routine.schema}.${routine.name}`;
  }
  return routine.name;
}

/** 函数子类型显示标签 */
function routineSubType(routine: RoutineInfo) {
  switch (routine.routineType) {
    case 'ScalarFunction': return '标量';
    case 'TableFunction': return '表值';
    case 'AggregateFunction': return '聚合';
    default: return '';
  }
}

/** 构造例程唯一 key */
function routineKey(connectionId: string, database: string, routine: RoutineInfo): string {
  return `${connectionId}:${database}:${routine.schema ?? ''}:${routine.name}`;
}

/** 切换单个例程的展开状态 */
function toggleRoutine(connectionId: string, database: string, routine: RoutineInfo) {
  const key = routineKey(connectionId, database, routine);
  expandedRoutines.value = expandedRoutines.value.includes(key)
    ? expandedRoutines.value.filter((entry) => entry !== key)
    : [...expandedRoutines.value, key];
}

/** 例程是否已展开 */
function isRoutineExpanded(connectionId: string, database: string, routine: RoutineInfo): boolean {
  return expandedRoutines.value.includes(routineKey(connectionId, database, routine));
}

/** 切换例程参数分组的展开状态 */
function toggleRoutineParamGroup(connectionId: string, database: string, routine: RoutineInfo) {
  const key = `${routineKey(connectionId, database, routine)}:params`;
  expandedRoutineParamGroups.value = expandedRoutineParamGroups.value.includes(key)
    ? expandedRoutineParamGroups.value.filter((entry) => entry !== key)
    : [...expandedRoutineParamGroups.value, key];
}

/** 例程参数分组是否已展开 */
function isRoutineParamGroupExpanded(connectionId: string, database: string, routine: RoutineInfo): boolean {
  return expandedRoutineParamGroups.value.includes(`${routineKey(connectionId, database, routine)}:params`);
}

/** 格式化参数方向标签 */
function formatParamDirection(direction: string): string {
  switch (direction) {
    case 'OUT': return '输出';
    case 'INOUT': return '输入/输出';
    default: return '输入';
  }
}

function formatTreeColumnType(column: TableColumn) {
  if (column.maxLength !== null && column.maxLength !== undefined) {
    return `${column.type}(${column.maxLength < 0 ? 'max' : column.maxLength})`;
  }

  if (column.numericPrecision !== null && column.numericPrecision !== undefined) {
    return column.numericScale !== null && column.numericScale !== undefined
      ? `${column.type}(${column.numericPrecision},${column.numericScale})`
      : `${column.type}(${column.numericPrecision})`;
  }

  return column.type;
}

function providerTreeIcon(provider: ProviderType): Component {
  void provider;
  return IconServer;
}

function providerTreeClass(provider: ProviderType) {
  return `tree-icon-provider-${provider}`;
}

function treeToggleIcon(expanded: boolean): Component {
  return expanded ? IconChevronDown : IconChevronRight;
}

function routineTreeIcon(routineType: RoutineInfo['routineType']): Component {
  return routineType === 'Procedure' ? IconScript : IconFunction;
}

function getViewportSafeMenuPosition(event: MouseEvent, menuWidth: number, menuHeight: number) {
  const viewportPadding = 12;
  return {
    x: Math.max(viewportPadding, Math.min(event.clientX, window.innerWidth - menuWidth - viewportPadding)),
    y: Math.max(viewportPadding, Math.min(event.clientY, window.innerHeight - menuHeight - viewportPadding)),
  };
}

function isTableFocused(tableKey: string) {
  return store.activeTableTab?.tableKey === tableKey || store.activeDesignTab?.tableKey === tableKey;
}

async function openDesignSection(tableKey: string, section: TableDesignSection, entry: string | null = null) {
  await store.openTableDesign(tableKey);
  store.updateDesignTabSelection(`design:${tableKey}`, section, entry);
}

function openDesignEntryContextMenu(event: MouseEvent, kind: 'column' | 'index', tableKey: string, name: string) {
  event.preventDefault();
  event.stopPropagation();
  closeAllContextMenus();
  designEntryContextMenu.value = {
    x: Math.max(8, Math.min(event.clientX, window.innerWidth - 168)),
    y: Math.max(8, Math.min(event.clientY, window.innerHeight - 72)),
    tableKey,
    kind,
    name,
  };
}

function closeDesignEntryContextMenu() {
  designEntryContextMenu.value = null;
}

async function deleteDesignEntryFromSidebar() {
  const target = designEntryContextMenu.value;
  if (!target) {
    return;
  }

  closeDesignEntryContextMenu();
  try {
    if (target.kind === 'column') {
      await store.deleteDesignColumn(target.tableKey, target.name);
      return;
    }

    await store.deleteDesignIndex(target.tableKey, target.name);
  }
  catch (error) {
    store.showNotice('warning', error instanceof Error ? error.message : target.kind === 'column' ? '字段删除失败' : '索引删除失败');
  }
}

function openCatalogObject(connectionId: string, database: string, objectType: 'synonym' | 'sequence' | 'rule' | 'default' | 'user-defined-type', schema: string | null | undefined, name: string) {
  void store.openCatalogObject(connectionId, database, objectType, schema ?? null, name);
}

function openExtendedCatalogObject(connectionId: string, database: string, objectType: 'synonym' | 'sequence' | 'rule' | 'default' | 'user-defined-type' | 'database-trigger' | 'xml-schema-collection', schema: string | null | undefined, name: string) {
  void store.openCatalogObject(connectionId, database, objectType, schema ?? null, name);
}

function catalogObjectFullName(schema: string | null | undefined, name: string) {
  return schema ? `${schema}.${name}` : name;
}

function catalogObjectTitle(schema: string | null | undefined, name: string, summary: string | null | undefined) {
  const fullName = catalogObjectFullName(schema, name);
  return summary ? `${fullName}\n${summary}` : fullName;
}

function databaseObjectSummary(connectionId: string, database: string) {
  const tableCount = treeTables(connectionId, database).length;
  const viewCount = treeViews(connectionId, database).length;
  const synonymCount = treeSynonyms(connectionId, database).length;
  const sequenceCount = treeSequences(connectionId, database).length;
  const ruleCount = treeRules(connectionId, database).length;
  const defaultCount = treeDefaults(connectionId, database).length;
  const typeCount = treeUserDefinedTypes(connectionId, database).length;
  const triggerCount = treeDatabaseTriggers(connectionId, database).length;
  const xmlSchemaCount = treeXmlSchemaCollections(connectionId, database).length;
  const parts = [`${tableCount} 表`];
  if (viewCount > 0) {
    parts.push(`${viewCount} 视图`);
  }
  if (synonymCount > 0) {
    parts.push(`${synonymCount} 同义词`);
  }
  if (sequenceCount > 0) {
    parts.push(`${sequenceCount} 序列`);
  }
  if (ruleCount > 0) {
    parts.push(`${ruleCount} 规则`);
  }
  if (defaultCount > 0) {
    parts.push(`${defaultCount} 默认值`);
  }
  if (typeCount > 0) {
    parts.push(`${typeCount} 类型`);
  }
  if (triggerCount > 0) {
    parts.push(`${triggerCount} 数据库触发器`);
  }
  if (xmlSchemaCount > 0) {
    parts.push(`${xmlSchemaCount} XML 架构`);
  }

  return parts.join(' · ');
}

function objectRowMeta(object: TableSummary) {
  return object.objectType === 'view'
    ? '视图'
    : `${store.tableRowCountLabel(object.key) ?? '未统计'} rows`;
}

function showForeignKeyGroup(object: TableSummary) {
  return object.objectType === 'table';
}

function objectTreeIcon(object: TableSummary): Component {
  return object.objectType === 'view' ? IconEye : IconTable;
}

async function confirmDeleteConnection(connectionId: string) {
  try {
    await store.deleteConnection(connectionId);
  }
  catch (_error) {
    store.refreshBootstrap();
  }
  finally {
    deleteConnectionConfirm.value = null;
  }
}

function openConnectionContextMenu(event: MouseEvent, connectionId: string, connectionName: string) {
  closeDatabaseContextMenu();
  closeTableContextMenu();
  const menuPosition = getViewportSafeMenuPosition(event, 220, 120);
  connectionContextMenu.value = {
    x: menuPosition.x,
    y: menuPosition.y,
    connectionId,
    connectionName,
  };
}

function closeConnectionContextMenu() {
  connectionContextMenu.value = null;
}

function openDeleteConnectionConfirm() {
  if (!connectionContextMenu.value) {
    return;
  }

  deleteConnectionConfirm.value = {
    connectionId: connectionContextMenu.value.connectionId,
    connectionName: connectionContextMenu.value.connectionName,
  };
  closeConnectionContextMenu();
}

async function openEditConnectionDialog() {
  if (!connectionContextMenu.value) {
    return;
  }

  createConnectionLoading.value = true;
  createConnectionError.value = null;
  try {
    const config = await store.getConnectionConfig(connectionContextMenu.value.connectionId);
    editingConnectionId.value = config.id;
    createConnectionForm.name = config.name;
    createConnectionForm.provider = config.provider;
    createConnectionForm.authentication = config.authentication;
    createConnectionForm.host = config.host;
    createConnectionForm.port = config.port ? String(config.port) : '';
    createConnectionForm.username = config.username ?? '';
    createConnectionForm.password = '';
    createConnectionForm.trustServerCertificate = config.trustServerCertificate;
    createConnectionVisible.value = true;
  }
  catch (error) {
    createConnectionError.value = error instanceof Error ? error.message : '连接配置加载失败';
  }
  finally {
    createConnectionLoading.value = false;
    closeConnectionContextMenu();
  }
}

function closeDeleteConnectionConfirm() {
  deleteConnectionConfirm.value = null;
}

function openDatabaseContextMenu(event: MouseEvent, connectionId: string, database: string) {
  closeConnectionContextMenu();
  closeTableContextMenu();
  const menuPosition = getViewportSafeMenuPosition(event, 240, 180);
  databaseContextMenu.value = {
    x: menuPosition.x,
    y: menuPosition.y,
    connectionId,
    database,
  };
}

function closeDatabaseContextMenu() {
  databaseContextMenu.value = null;
}

function openTableContextMenu(event: MouseEvent, connectionId: string, database: string, table: TableSummary) {
  closeConnectionContextMenu();
  closeDatabaseContextMenu();
  const menuPosition = getViewportSafeMenuPosition(event, 250, 220);
  tableContextMenu.value = {
    x: menuPosition.x,
    y: menuPosition.y,
    connectionId,
    database,
    table,
    showScriptSubmenu: false,
  };
}

function closeTableContextMenu() {
  tableContextMenu.value = null;
}

function openRoutineContextMenu(event: MouseEvent, connectionId: string, database: string, provider: ProviderType, routine: RoutineInfo) {
  closeAllContextMenus();
  const menuPosition = getViewportSafeMenuPosition(event, 220, 150);
  routineContextMenu.value = {
    x: menuPosition.x,
    y: menuPosition.y,
    connectionId,
    database,
    provider,
    routine,
  };
}

function closeRoutineContextMenu() {
  routineContextMenu.value = null;
}

/** 删除存储过程或函数 */
async function deleteRoutine() {
  const ctx = deleteRoutineConfirm.value;
  if (!ctx) return;

  const isProcedure = ctx.routine.routineType === 'Procedure';
  const objectType = isProcedure ? 'PROCEDURE' : 'FUNCTION';
  const qualifiedName = ctx.routine.schema && ctx.provider !== 'mysql'
    ? `[${ctx.routine.schema}].[${ctx.routine.name}]`
    : `[${ctx.routine.name}]`;
  const dropSql = `DROP ${objectType} ${qualifiedName};`;

  try {
    await store.openSqlTabWithContext({
      connectionId: ctx.connectionId,
      database: ctx.database,
      sqlText: dropSql,
      execute: true,
    });
  }
  finally {
    deleteRoutineConfirm.value = null;
  }
}

/** 打开重命名例程对话框 */
function openRenameRoutineDialog() {
  if (!routineContextMenu.value) {
    return;
  }

  const { connectionId, database, provider, routine } = routineContextMenu.value;
  closeRoutineContextMenu();
  renameRoutineTarget.value = { connectionId, database, provider, routine };
  renameRoutineName.value = routine.name;
  renameRoutineError.value = null;
  renameRoutineVisible.value = true;
}

/** 关闭重命名例程对话框 */
function closeRenameRoutineDialog() {
  renameRoutineVisible.value = false;
  renameRoutineLoading.value = false;
  renameRoutineError.value = null;
  renameRoutineTarget.value = null;
  renameRoutineName.value = '';
}

/** 生成重命名例程 SQL */
function buildRenameRoutineScript(provider: ProviderType, routine: RoutineInfo, newName: string): string {
  const isProcedure = routine.routineType === 'Procedure';
  const objectType = isProcedure ? 'PROCEDURE' : 'FUNCTION';

  if (provider === 'sqlserver') {
    const oldQualified = routine.schema
      ? `${quoteIdentifier(provider, routine.schema)}.${quoteIdentifier(provider, routine.name)}`
      : quoteIdentifier(provider, routine.name);
    return `EXEC sp_rename '${oldQualified.replace(/'/g, "''")}', '${newName.replace(/'/g, "''")}';`;
  }

  if (provider === 'postgresql') {
    const oldQualified = routine.schema
      ? `${quoteIdentifier(provider, routine.schema)}.${quoteIdentifier(provider, routine.name)}`
      : quoteIdentifier(provider, routine.name);
    return `ALTER ${objectType} ${oldQualified} RENAME TO ${quoteIdentifier(provider, newName)};`;
  }

  if (provider === 'mysql') {
    // MySQL 不支持直接重命名存储过程/函数
    return '';
  }

  return '';
}

/** 提交重命名例程 */
async function submitRenameRoutine() {
  if (!renameRoutineTarget.value) {
    return;
  }

  const nextName = renameRoutineName.value.trim();
  if (!nextName) {
    renameRoutineError.value = '名称不能为空';
    return;
  }

  if (nextName === renameRoutineTarget.value.routine.name) {
    closeRenameRoutineDialog();
    return;
  }

  const sql = buildRenameRoutineScript(
    renameRoutineTarget.value.provider,
    renameRoutineTarget.value.routine,
    nextName,
  );

  if (!sql) {
    renameRoutineError.value = '该数据库类型不支持重命名存储过程/函数';
    return;
  }

  renameRoutineLoading.value = true;
  renameRoutineError.value = null;

  try {
    const response = await fetch('/api/explorer/sql-execute', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        connectionId: renameRoutineTarget.value.connectionId,
        database: renameRoutineTarget.value.database,
        sql,
      }),
    });

    if (!response.ok) {
      throw new Error(await response.text());
    }

    await store.refreshDatabase(renameRoutineTarget.value.connectionId, renameRoutineTarget.value.database);
    closeRenameRoutineDialog();
  }
  catch (error) {
    renameRoutineError.value = error instanceof Error ? error.message : '重命名失败';
    renameRoutineLoading.value = false;
  }
}

function setTableScriptSubmenu(open: boolean) {
  if (!tableContextMenu.value) {
    return;
  }

  tableContextMenu.value = {
    ...tableContextMenu.value,
    showScriptSubmenu: open,
  };
}

function providerForConnection(connectionId: string): ProviderType {
  return store.getConnectionInfo(connectionId)?.provider ?? 'sqlserver';
}

function quoteIdentifier(provider: ProviderType, value: string) {
  if (provider === 'mysql') {
    return `\`${value.replace(/`/g, '``')}\``;
  }

  if (provider === 'postgresql' || provider === 'sqlite') {
    return `"${value.replace(/"/g, '""')}"`;
  }

  return `[${value.replace(/\]/g, ']]')}]`;
}

function formatSchemaTable(provider: ProviderType, table: TableSummary) {
  const parts = [];
  if (table.schema) {
    parts.push(quoteIdentifier(provider, table.schema));
  }
  parts.push(quoteIdentifier(provider, table.name));
  return parts.join('.');
}

function buildBatchPrefix(provider: ProviderType, database: string) {
  if (provider === 'sqlserver') {
    return `USE ${quoteIdentifier(provider, database)}\nGO\n\n`;
  }

  if (provider === 'mysql') {
    return `USE ${quoteIdentifier(provider, database)};\n\n`;
  }

  if (provider === 'sqlite') {
    return `-- sqlite database: ${database}\n\n`;
  }

  return `-- database: ${database}\n\n`;
}

function buildBatchSuffix(provider: ProviderType) {
  return provider === 'sqlserver' ? '\nGO' : ';';
}

async function getTableColumns(connectionId: string, database: string, table: TableSummary) {
  await store.ensureSqlContextLoaded(connectionId, database);
  const sqlContextTable = store.getSqlContext(connectionId, database)?.tables.find((entry) => {
    const qualifiedName = table.schema ? `${table.schema}.${table.name}` : table.name;
    return entry.qualifiedName === qualifiedName || entry.name === table.name;
  });

  try {
    const loaded = await store.ensureTableLoaded(table.key);
    if (loaded?.columns?.length) {
      return loaded.columns.map((column) => ({
        name: column.name,
        type: column.type,
        isNullable: column.isNullable,
      }));
    }
  }
  catch {
    // Fall back to SQL context columns when table schema is not loaded.
  }

  return (sqlContextTable?.columns ?? []).map((column) => ({
    name: column,
    type: undefined,
    isNullable: true,
  }));
}

function buildSelectScript(provider: ProviderType, database: string, table: TableSummary, columns: Array<{ name: string }>) {
  const selectLines = columns.length
    ? columns.map((column, index) => `${index === 0 ? 'SELECT' : '      ,'} ${quoteIdentifier(provider, column.name)}`)
    : ['SELECT *'];
  return `${buildBatchPrefix(provider, database)}${selectLines.join('\n')}\n  FROM ${formatSchemaTable(provider, table)}${buildBatchSuffix(provider)}`;
}

function buildDeleteScript(provider: ProviderType, database: string, table: TableSummary) {
  return `${buildBatchPrefix(provider, database)}DELETE FROM ${formatSchemaTable(provider, table)}\n      WHERE <搜索条件,,>${buildBatchSuffix(provider)}`;
}

function buildDropScript(provider: ProviderType, database: string, table: TableSummary) {
  return `${buildBatchPrefix(provider, database)}DROP TABLE ${formatSchemaTable(provider, table)}${buildBatchSuffix(provider)}`;
}

function buildInsertScript(provider: ProviderType, database: string, table: TableSummary, columns: Array<{ name: string; type?: string }>) {
  const names = columns.length ? columns : [{ name: 'Column1', type: 'type' }];
  const columnLines = names.map((column, index) => `${index === 0 ? '           (' : '           ,'}${quoteIdentifier(provider, column.name)}`).join('\n');
  const valueLines = names.map((column, index) => `${index === 0 ? '           (' : '           ,'}<${column.name}, ${column.type ?? 'type'},>`).join('\n');
  return `${buildBatchPrefix(provider, database)}INSERT INTO ${formatSchemaTable(provider, table)}\n${columnLines})\n     VALUES\n${valueLines})${buildBatchSuffix(provider)}`;
}

function buildUpdateScript(provider: ProviderType, database: string, table: TableSummary, columns: Array<{ name: string; type?: string }>) {
  const names = columns.length ? columns : [{ name: 'Column1', type: 'type' }];
  const setLines = names.map((column, index) => `${index === 0 ? '   SET' : '      ,'} ${quoteIdentifier(provider, column.name)} = <${column.name}, ${column.type ?? 'type'},>`).join('\n');
  return `${buildBatchPrefix(provider, database)}UPDATE ${formatSchemaTable(provider, table)}\n${setLines}\n WHERE <搜索条件,,>${buildBatchSuffix(provider)}`;
}

function buildCreateScript(provider: ProviderType, database: string, table: TableSummary, columns: Array<{ name: string; type?: string; isNullable?: boolean }>) {
  const names = columns.length ? columns : [{ name: 'Column1', type: 'int', isNullable: false }];
  const body = names.map((column, index) => `    ${index === 0 ? '' : ','}${quoteIdentifier(provider, column.name)} ${column.type ?? 'nvarchar(255)'} ${column.isNullable === false ? 'NOT NULL' : 'NULL'}`).join('\n');
  return `${buildBatchPrefix(provider, database)}CREATE TABLE ${formatSchemaTable(provider, table)}\n(\n${body}\n)${buildBatchSuffix(provider)}`;
}

function buildRenameTableScript(provider: ProviderType, database: string, table: TableSummary, targetName: string) {
  const currentName = formatSchemaTable(provider, table);

  if (provider === 'sqlserver') {
    return `${buildBatchPrefix(provider, database)}EXEC sp_rename '${currentName.replace(/'/g, "''")}', '${targetName.replace(/'/g, "''")}'${buildBatchSuffix(provider)}`;
  }

  if (provider === 'postgresql' || provider === 'sqlite') {
    return `${buildBatchPrefix(provider, database)}ALTER TABLE ${currentName}\n  RENAME TO ${quoteIdentifier(provider, targetName)}${buildBatchSuffix(provider)}`;
  }

  return `${buildBatchPrefix(provider, database)}RENAME TABLE ${currentName}\n         TO ${table.schema ? `${quoteIdentifier(provider, table.schema)}.` : ''}${quoteIdentifier(provider, targetName)}${buildBatchSuffix(provider)}`;
}

function buildCreateTableTemplate(provider: ProviderType, database: string) {
  const idColumn = provider === 'sqlite'
    ? `${quoteIdentifier(provider, 'id')} INTEGER PRIMARY KEY AUTOINCREMENT`
    : provider === 'postgresql'
      ? `${quoteIdentifier(provider, 'id')} bigint generated by default as identity primary key`
      : provider === 'mysql'
        ? `${quoteIdentifier(provider, 'id')} bigint not null auto_increment primary key`
        : `${quoteIdentifier(provider, 'id')} bigint identity(1,1) not null primary key`;
  const nameColumn = provider === 'sqlite'
    ? `${quoteIdentifier(provider, 'name')} TEXT NOT NULL`
    : `${quoteIdentifier(provider, 'name')} varchar(255) NOT NULL`;
  const createdAtColumn = provider === 'sqlite'
    ? `${quoteIdentifier(provider, 'created_at')} TEXT NULL DEFAULT CURRENT_TIMESTAMP`
    : provider === 'postgresql'
      ? `${quoteIdentifier(provider, 'created_at')} timestamp NULL DEFAULT CURRENT_TIMESTAMP`
      : provider === 'mysql'
        ? `${quoteIdentifier(provider, 'created_at')} datetime NULL DEFAULT CURRENT_TIMESTAMP`
        : `${quoteIdentifier(provider, 'created_at')} datetime2 NULL DEFAULT sysdatetime()`;

  return `${buildBatchPrefix(provider, database)}CREATE TABLE ${quoteIdentifier(provider, 'new_table')}\n(\n    ${idColumn}\n   ,${nameColumn}\n   ,${createdAtColumn}\n)${buildBatchSuffix(provider)}`;
}

async function openDatabaseSqlQuery() {
  if (!databaseContextMenu.value) {
    return;
  }

  const { connectionId, database } = databaseContextMenu.value;
  closeDatabaseContextMenu();
  await store.openSqlTabWithContext({ connectionId, database });
}

async function openCreateTableQuery() {
  if (!databaseContextMenu.value) {
    return;
  }

  const { connectionId, database } = databaseContextMenu.value;
  closeDatabaseContextMenu();
  const provider = providerForConnection(connectionId);
  await store.openSqlTabWithContext({
    connectionId,
    database,
    sqlText: buildCreateTableTemplate(provider, database),
  });
}

async function openTableScript(kind: 'create' | 'drop' | 'select' | 'insert' | 'update' | 'delete') {
  if (!tableContextMenu.value) {
    return;
  }

  const { connectionId, database, table } = tableContextMenu.value;
  closeTableContextMenu();
  const provider = providerForConnection(connectionId);
  const columns = await getTableColumns(connectionId, database, table);

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
            : buildDeleteScript(provider, database, table);

  await store.openSqlTabWithContext({
    connectionId,
    database,
    sqlText,
  });
}

async function openDatabaseGraphOverview() {
  if (!databaseContextMenu.value) {
    return;
  }

  const { connectionId, database } = databaseContextMenu.value;
  closeDatabaseContextMenu();
  await store.openDatabaseGraph(connectionId, database);
}

async function openRenameTableQuery() {
  if (!tableContextMenu.value) {
    return;
  }

  const { connectionId, database, table } = tableContextMenu.value;
  closeTableContextMenu();
  renameTableTarget.value = { connectionId, database, table };
  renameTableName.value = table.name;
  renameTableError.value = null;
  renameTableVisible.value = true;
}

function closeRenameTableDialog() {
  renameTableVisible.value = false;
  renameTableLoading.value = false;
  renameTableError.value = null;
  renameTableTarget.value = null;
  renameTableName.value = '';
}

function openTableMockDataDialog() {
  if (!tableContextMenu.value) {
    return;
  }

  void store.openTableMockTab(tableContextMenu.value.table.key);
  closeTableContextMenu();
}

async function submitRenameTable() {
  if (!renameTableTarget.value) {
    return;
  }

  const nextName = renameTableName.value.trim();
  if (!nextName) {
    renameTableError.value = '表名不能为空';
    return;
  }

  if (nextName === renameTableTarget.value.table.name) {
    closeRenameTableDialog();
    return;
  }

  renameTableLoading.value = true;
  renameTableError.value = null;
  try {
    const provider = providerForConnection(renameTableTarget.value.connectionId);
    const sql = buildRenameTableScript(provider, renameTableTarget.value.database, renameTableTarget.value.table, nextName);

    const response = await fetch('/api/explorer/sql-execute', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        connectionId: renameTableTarget.value.connectionId,
        database: renameTableTarget.value.database,
        sql,
      }),
    });

    if (!response.ok) {
      throw new Error(await response.text());
    }

    await store.refreshDatabase(renameTableTarget.value.connectionId, renameTableTarget.value.database);
    closeRenameTableDialog();
  }
  catch (error) {
    renameTableError.value = error instanceof Error ? error.message : '表重命名失败';
    renameTableLoading.value = false;
  }
}

async function refreshDatabaseFromMenu() {
  if (!databaseContextMenu.value) {
    return;
  }

  const { connectionId, database } = databaseContextMenu.value;
  closeDatabaseContextMenu();
  await store.refreshDatabase(connectionId, database);
}

function resetCreateConnectionForm() {
  editingConnectionId.value = null;
  createConnectionForm.name = '';
  createConnectionForm.provider = 'sqlserver';
  createConnectionForm.authentication = 'password';
  createConnectionForm.host = '';
  createConnectionForm.port = '1433';
  createConnectionForm.username = '';
  createConnectionForm.password = '';
  createConnectionForm.trustServerCertificate = true;
  createConnectionError.value = null;
}

function openCreateConnectionDialog() {
  closeAllContextMenus();
  resetCreateConnectionForm();
  createConnectionVisible.value = true;
}

function handleProviderChange(provider: ProviderType) {
  createConnectionForm.provider = provider;
  createConnectionForm.port = provider === 'sqlserver' ? '1433' : provider === 'postgresql' ? '5432' : provider === 'mysql' ? '3306' : '';
  if (provider === 'mysql' && createConnectionForm.authentication === 'windows') {
    createConnectionForm.authentication = 'password';
  }

  if (provider === 'postgresql' && createConnectionForm.authentication === 'windows') {
    createConnectionForm.authentication = 'password';
  }

  if (provider === 'sqlite') {
    createConnectionForm.authentication = 'password';
    createConnectionForm.username = '';
    createConnectionForm.password = '';
  }
}

async function browseSqliteDatabaseFile() {
  createConnectionError.value = null;

  try {
    const baseName = (createConnectionForm.name.trim() || 'database').replace(/[\\/:*?"<>|]/g, '_');
    const suggestedFileName = /\.(db|sqlite|sqlite3)$/i.test(baseName) ? baseName : `${baseName}.db`;
    const filePath = await store.pickSqliteDatabaseFile(createConnectionForm.host || null, suggestedFileName);
    if (filePath) {
      createConnectionForm.host = filePath;
    }
  }
  catch (error) {
    createConnectionError.value = error instanceof Error ? error.message : 'SQLite 文件选择失败';
  }
}

async function submitCreateConnection() {
  createConnectionLoading.value = true;
  createConnectionError.value = null;

  try {
    const payload = {
      name: createConnectionForm.name,
      provider: createConnectionForm.provider,
      authentication: createConnectionForm.authentication,
      host: createConnectionForm.host,
      port: createConnectionForm.provider === 'sqlite' || !Number.isFinite(Number(createConnectionForm.port)) ? null : Number(createConnectionForm.port),
      username: createConnectionForm.provider === 'sqlite' || createConnectionForm.authentication === 'windows' ? null : createConnectionForm.username,
      password: createConnectionForm.provider === 'sqlite' || createConnectionForm.authentication === 'windows' ? null : createConnectionForm.password,
      trustServerCertificate: createConnectionForm.trustServerCertificate,
    };

    if (editingConnectionId.value) {
      await store.updateConnection(editingConnectionId.value, payload);
    }
    else {
      await store.createConnection(payload);
    }
    createConnectionVisible.value = false;
  }
  catch (error) {
    createConnectionError.value = error instanceof Error ? error.message : '连接创建失败';
  }
  finally {
    createConnectionLoading.value = false;
  }
}

async function testCurrentConnection() {
  testConnectionLoading.value = true;
  createConnectionError.value = null;
  try {
    await store.testConnection({
      connectionId: editingConnectionId.value,
      name: createConnectionForm.name,
      provider: createConnectionForm.provider,
      authentication: createConnectionForm.authentication,
      host: createConnectionForm.host,
      port: createConnectionForm.provider === 'sqlite' || !Number.isFinite(Number(createConnectionForm.port)) ? null : Number(createConnectionForm.port),
      username: createConnectionForm.provider === 'sqlite' || createConnectionForm.authentication === 'windows' ? null : createConnectionForm.username,
      password: createConnectionForm.provider === 'sqlite' || createConnectionForm.authentication === 'windows' ? null : (createConnectionForm.password || null),
      trustServerCertificate: createConnectionForm.trustServerCertificate,
    });
  }
  catch (error) {
    createConnectionError.value = error instanceof Error ? error.message : '连接测试失败';
  }
  finally {
    testConnectionLoading.value = false;
  }
}

onMounted(() => {
  window.addEventListener('click', closeAllContextMenus);
  window.addEventListener('contextmenu', closeAllContextMenus, true);
  window.addEventListener('blur', closeAllContextMenus);
});

onBeforeUnmount(() => {
  window.removeEventListener('click', closeAllContextMenus);
  window.removeEventListener('contextmenu', closeAllContextMenus, true);
  window.removeEventListener('blur', closeAllContextMenus);
});
</script>

<template>
  <div class="sidebar-shell">
    <div class="sidebar-toolbar compact-panel">
      <strong>连接与表</strong>
      <div class="sidebar-toolbar-actions">
        <NButton size="tiny" tertiary type="primary" @click="openCreateConnectionDialog">新建连接</NButton>
      </div>
    </div>

    <div class="sidebar-list">
      <NInput
        v-model:value="store.sidebarQuery"
        clearable
        size="small"
        class="sidebar-filter"
        placeholder="筛选连接、数据库或表名"
      />

      <NSpin :show="store.isBootstrapping">
      <template v-if="visibleConnections.length">
        <div
          v-for="connection in visibleConnections"
          :key="connection.id"
          class="tree-root connection-card"
          :style="{ '--connection-accent': connection.accent }"
        >
          <div class="tree-row tree-row-connection" @contextmenu.prevent.stop="openConnectionContextMenu($event, connection.id, connection.name)">
            <button class="tree-toggle" type="button" @click="toggleConnection(connection.id)">
              <NIcon size="12"><component :is="treeToggleIcon(expandedConnections.includes(connection.id))" /></NIcon>
            </button>
            <button class="tree-node-main tree-node-main-connection" type="button" @click="toggleConnection(connection.id)">
              <NIcon class="tree-icon tree-icon-provider" :class="providerTreeClass(connection.provider)" size="12"><component :is="providerTreeIcon(connection.provider)" /></NIcon>
              <span class="tree-label-stack">
                <span class="tree-label-main">{{ connection.name }}</span>
                <span class="tree-label-meta">{{ connection.host }}<template v-if="connection.port">:{{ connection.port }}</template></span>
              </span>
            </button>
          </div>

          <NAlert v-if="connection.error" type="warning" :show-icon="false" class="connection-error">
            {{ connection.error }}
          </NAlert>

          <div v-if="expandedConnections.includes(connection.id)" class="tree-children tree-children-animated" @contextmenu.prevent.stop>
            <div
              v-for="database in store.getDatabases(connection.id)"
              :key="`${connection.id}:${database.name}`"
              class="tree-node"
            >
              <div class="tree-row tree-row-database" @contextmenu.prevent.stop="openDatabaseContextMenu($event, connection.id, database.name)">
                <button class="tree-toggle" type="button" @click="toggleDatabase(`${connection.id}:${database.name}`)">
                  <NIcon size="12"><component :is="treeToggleIcon(expandedDatabases.includes(`${connection.id}:${database.name}`))" /></NIcon>
                </button>
                <button
                  class="tree-node-main"
                  type="button"
                  @click="toggleDatabase(`${connection.id}:${database.name}`)"
                >
                  <NIcon class="tree-icon tree-icon-database" size="12"><IconDatabase /></NIcon>
                  <span class="tree-label-stack">
                    <span class="tree-label-main">{{ database.name }}</span>
                    <span class="tree-label-meta">{{ databaseObjectSummary(connection.id, database.name) }}</span>
                  </span>
                </button>
              </div>

              <div
                v-if="expandedDatabases.includes(`${connection.id}:${database.name}`)"
                class="tree-children tree-children-animated"
                @contextmenu.prevent.stop
              >
                <div v-if="treeTables(connection.id, database.name).length > 0" class="tree-node">
                  <div class="tree-row tree-row-group">
                    <button class="tree-toggle" type="button" @click="toggleRoutineGroup(connection.id, database.name, 'tables')">
                      <NIcon size="12"><component :is="treeToggleIcon(isRoutineGroupExpanded(connection.id, database.name, 'tables'))" /></NIcon>
                    </button>
                    <button type="button" class="tree-node-main" @click="toggleRoutineGroup(connection.id, database.name, 'tables')">
                      <NIcon class="tree-icon tree-icon-table" size="12"><IconTable /></NIcon>
                      <span class="tree-label-main">表</span>
                      <span class="tree-row-count">{{ treeTables(connection.id, database.name).length }}</span>
                    </button>
                  </div>
                  <div v-if="isRoutineGroupExpanded(connection.id, database.name, 'tables')" class="tree-children tree-children-animated">
                    <div
                      v-for="table in treeTables(connection.id, database.name)"
                      :key="table.key"
                      class="tree-node"
                    >
                      <div class="tree-row tree-row-table" :class="{ 'tree-row-active': isTableFocused(table.key) }" @contextmenu.stop.prevent="openTableContextMenu($event, connection.id, database.name, table)">
                        <button class="tree-toggle" type="button" @click="toggleTableNode(table.key)">
                          <NIcon size="12"><component :is="treeToggleIcon(isTableExpanded(table.key))" /></NIcon>
                        </button>
                        <button type="button" class="tree-node-main" @click="store.openTable(table.key)">
                          <NIcon class="tree-icon tree-icon-table" size="12"><component :is="objectTreeIcon(table)" /></NIcon>
                          <span class="tree-label-stack">
                            <span class="tree-label-main">{{ table.schema ? `${table.schema}.${table.name}` : table.name }}</span>
                            <span class="tree-label-meta">{{ objectRowMeta(table) }}</span>
                          </span>
                        </button>
                      </div>

                      <div v-if="isTableExpanded(table.key)" class="tree-children tree-children-animated tree-children-table">
                        <div v-if="tableDesignStatusForTree(table.key).loading" class="tree-loading-row">正在加载对象树...</div>
                        <NAlert v-else-if="tableDesignStatusForTree(table.key).error" type="warning" :show-icon="false" class="tree-inline-alert">
                          {{ tableDesignStatusForTree(table.key).error }}
                        </NAlert>
                        <template v-else-if="tableDesignForTree(table.key)">
                          <div class="tree-node">
                            <div class="tree-row tree-row-group">
                              <button class="tree-toggle" type="button" @click="toggleTableGroup(table.key, 'columns')">
                                <NIcon size="12"><component :is="treeToggleIcon(isTableGroupExpanded(table.key, 'columns'))" /></NIcon>
                              </button>
                              <button type="button" class="tree-node-main" @click="openDesignSection(table.key, 'columns')">
                                <NIcon class="tree-icon tree-icon-column" size="12"><IconColumns3 /></NIcon>
                                <span class="tree-label-main">字段</span>
                                <span class="tree-row-count">{{ treeColumns(table.key).length }}</span>
                              </button>
                            </div>
                            <div v-if="isTableGroupExpanded(table.key, 'columns')" class="tree-children tree-children-animated">
                              <button
                                v-for="column in treeColumns(table.key)"
                                :key="column.name"
                                type="button"
                                class="tree-leaf-row"
                                @dblclick.stop.prevent="openDesignSection(table.key, 'columns', column.name)"
                                @contextmenu.stop.prevent="openDesignEntryContextMenu($event, 'column', table.key, column.name)"
                              >
                                <NIcon class="tree-icon tree-icon-leaf" size="12"><IconTableColumn /></NIcon>
                                <span class="tree-leaf-name">{{ column.name }}</span>
                                <span class="tree-leaf-type">{{ formatTreeColumnType(column) }}</span>
                              </button>
                            </div>
                          </div>

                          <div class="tree-node">
                            <div class="tree-row tree-row-group">
                              <button class="tree-toggle" type="button" @click="toggleTableGroup(table.key, 'indexes')">
                                <NIcon size="12"><component :is="treeToggleIcon(isTableGroupExpanded(table.key, 'indexes'))" /></NIcon>
                              </button>
                              <button type="button" class="tree-node-main" @click="openDesignSection(table.key, 'indexes')">
                                <NIcon class="tree-icon tree-icon-index" size="12"><IconListDetails /></NIcon>
                                <span class="tree-label-main">索引</span>
                                <span class="tree-row-count">{{ treeIndexes(table.key).length }}</span>
                              </button>
                            </div>
                            <div v-if="isTableGroupExpanded(table.key, 'indexes')" class="tree-children tree-children-animated">
                              <button v-for="index in treeIndexes(table.key)" :key="index.name" type="button" class="tree-leaf-row" @dblclick.stop.prevent="openDesignSection(table.key, 'indexes', index.name)" @contextmenu.stop.prevent="openDesignEntryContextMenu($event, 'index', table.key, index.name)">
                                <NIcon class="tree-icon tree-icon-leaf" size="12"><IconListDetails /></NIcon>
                                <span class="tree-leaf-name">{{ index.name }}</span>
                                <span class="tree-leaf-type">{{ index.columns.join(', ') }}</span>
                              </button>
                            </div>
                          </div>

                          <div v-if="showForeignKeyGroup(table)" class="tree-node">
                            <div class="tree-row tree-row-group">
                              <button class="tree-toggle" type="button" @click="toggleTableGroup(table.key, 'foreignKeys')">
                                <NIcon size="12"><component :is="treeToggleIcon(isTableGroupExpanded(table.key, 'foreignKeys'))" /></NIcon>
                              </button>
                              <button type="button" class="tree-node-main" @click="openDesignSection(table.key, 'foreignKeys')">
                                <NIcon class="tree-icon tree-icon-foreign-key" size="12"><IconLink /></NIcon>
                                <span class="tree-label-main">外键</span>
                                <span class="tree-row-count">{{ treeForeignKeys(table.key).length }}</span>
                              </button>
                            </div>
                            <div v-if="isTableGroupExpanded(table.key, 'foreignKeys')" class="tree-children tree-children-animated">
                              <button v-for="foreignKey in treeForeignKeys(table.key)" :key="`${foreignKey.sourceColumn}:${foreignKey.targetColumn}`" type="button" class="tree-leaf-row" @dblclick.stop.prevent="openDesignSection(table.key, 'foreignKeys', foreignKey.sourceColumn)">
                                <NIcon class="tree-icon tree-icon-leaf" size="12"><IconLink /></NIcon>
                                <span class="tree-leaf-name">{{ foreignKey.sourceColumn }}</span>
                                <span class="tree-leaf-type">{{ foreignKey.targetColumn }}</span>
                              </button>
                            </div>
                          </div>

                          <div class="tree-node">
                            <div class="tree-row tree-row-group">
                              <button class="tree-toggle" type="button" @click="toggleTableGroup(table.key, 'triggers')">
                                <NIcon size="12"><component :is="treeToggleIcon(isTableGroupExpanded(table.key, 'triggers'))" /></NIcon>
                              </button>
                              <button type="button" class="tree-node-main" @click="openDesignSection(table.key, 'triggers')">
                                <NIcon class="tree-icon tree-icon-trigger" size="12"><IconBolt /></NIcon>
                                <span class="tree-label-main">触发器</span>
                                <span class="tree-row-count">{{ treeTriggers(table.key).length }}</span>
                              </button>
                            </div>
                            <div v-if="isTableGroupExpanded(table.key, 'triggers')" class="tree-children tree-children-animated">
                              <button v-for="trigger in treeTriggers(table.key)" :key="trigger.name" type="button" class="tree-leaf-row" @dblclick.stop.prevent="openDesignSection(table.key, 'triggers', trigger.name)">
                                <NIcon class="tree-icon tree-icon-leaf" size="12"><IconBolt /></NIcon>
                                <span class="tree-leaf-name">{{ trigger.name }}</span>
                                <span class="tree-leaf-type">{{ trigger.event || trigger.timing || '-' }}</span>
                              </button>
                            </div>
                          </div>

                          <div class="tree-node">
                            <div class="tree-row tree-row-group">
                              <button class="tree-toggle" type="button" @click="toggleTableGroup(table.key, 'statistics')">
                                <NIcon size="12"><component :is="treeToggleIcon(isTableGroupExpanded(table.key, 'statistics'))" /></NIcon>
                              </button>
                              <button type="button" class="tree-node-main" @click="openDesignSection(table.key, 'statistics')">
                                <NIcon class="tree-icon tree-icon-index" size="12"><IconListDetails /></NIcon>
                                <span class="tree-label-main">统计信息</span>
                                <span class="tree-row-count">{{ treeStatistics(table.key).length }}</span>
                              </button>
                            </div>
                            <div v-if="isTableGroupExpanded(table.key, 'statistics')" class="tree-children tree-children-animated">
                              <button v-for="statistic in treeStatistics(table.key)" :key="statistic.name" type="button" class="tree-leaf-row" @dblclick.stop.prevent="openDesignSection(table.key, 'statistics', statistic.name)">
                                <NIcon class="tree-icon tree-icon-leaf" size="12"><IconListDetails /></NIcon>
                                <span class="tree-leaf-name">{{ statistic.name }}</span>
                                <span class="tree-leaf-type">{{ statistic.columns.join(', ') || '-' }}</span>
                              </button>
                            </div>
                          </div>
                        </template>
                      </div>
                    </div>
                  </div>
                </div>

                <div v-if="treeViews(connection.id, database.name).length > 0" class="tree-node">
                  <div class="tree-row tree-row-group">
                    <button class="tree-toggle" type="button" @click="toggleRoutineGroup(connection.id, database.name, 'views')">
                      <NIcon size="12"><component :is="treeToggleIcon(isRoutineGroupExpanded(connection.id, database.name, 'views'))" /></NIcon>
                    </button>
                    <button type="button" class="tree-node-main" @click="toggleRoutineGroup(connection.id, database.name, 'views')">
                      <NIcon class="tree-icon tree-icon-table" size="12"><IconEye /></NIcon>
                      <span class="tree-label-main">视图</span>
                      <span class="tree-row-count">{{ treeViews(connection.id, database.name).length }}</span>
                    </button>
                  </div>
                  <div v-if="isRoutineGroupExpanded(connection.id, database.name, 'views')" class="tree-children tree-children-animated">
                    <div
                      v-for="view in treeViews(connection.id, database.name)"
                      :key="view.key"
                      class="tree-node"
                    >
                      <div class="tree-row tree-row-table" :class="{ 'tree-row-active': isTableFocused(view.key) }">
                        <button class="tree-toggle" type="button" @click="toggleTableNode(view.key)">
                          <NIcon size="12"><component :is="treeToggleIcon(isTableExpanded(view.key))" /></NIcon>
                        </button>
                        <button type="button" class="tree-node-main" @click="store.openTable(view.key)">
                          <NIcon class="tree-icon tree-icon-table" size="12"><component :is="objectTreeIcon(view)" /></NIcon>
                          <span class="tree-label-stack">
                            <span class="tree-label-main">{{ view.schema ? `${view.schema}.${view.name}` : view.name }}</span>
                            <span class="tree-label-meta">{{ objectRowMeta(view) }}</span>
                          </span>
                        </button>
                      </div>

                      <div v-if="isTableExpanded(view.key)" class="tree-children tree-children-animated tree-children-table">
                        <div v-if="tableDesignStatusForTree(view.key).loading" class="tree-loading-row">正在加载对象树...</div>
                        <NAlert v-else-if="tableDesignStatusForTree(view.key).error" type="warning" :show-icon="false" class="tree-inline-alert">
                          {{ tableDesignStatusForTree(view.key).error }}
                        </NAlert>
                        <template v-else-if="tableDesignForTree(view.key)">
                          <div class="tree-node">
                            <div class="tree-row tree-row-group">
                              <button class="tree-toggle" type="button" @click="toggleTableGroup(view.key, 'columns')">
                                <NIcon size="12"><component :is="treeToggleIcon(isTableGroupExpanded(view.key, 'columns'))" /></NIcon>
                              </button>
                              <button type="button" class="tree-node-main" @click="openDesignSection(view.key, 'columns')">
                                <NIcon class="tree-icon tree-icon-column" size="12"><IconColumns3 /></NIcon>
                                <span class="tree-label-main">字段</span>
                                <span class="tree-row-count">{{ treeColumns(view.key).length }}</span>
                              </button>
                            </div>
                            <div v-if="isTableGroupExpanded(view.key, 'columns')" class="tree-children tree-children-animated">
                              <button
                                v-for="column in treeColumns(view.key)"
                                :key="column.name"
                                type="button"
                                class="tree-leaf-row"
                                @dblclick.stop.prevent="openDesignSection(view.key, 'columns', column.name)"
                              >
                                <NIcon class="tree-icon tree-icon-leaf" size="12"><IconTableColumn /></NIcon>
                                <span class="tree-leaf-name">{{ column.name }}</span>
                                <span class="tree-leaf-type">{{ formatTreeColumnType(column) }}</span>
                              </button>
                            </div>
                          </div>

                          <div class="tree-node">
                            <div class="tree-row tree-row-group">
                              <button class="tree-toggle" type="button" @click="toggleTableGroup(view.key, 'indexes')">
                                <NIcon size="12"><component :is="treeToggleIcon(isTableGroupExpanded(view.key, 'indexes'))" /></NIcon>
                              </button>
                              <button type="button" class="tree-node-main" @click="openDesignSection(view.key, 'indexes')">
                                <NIcon class="tree-icon tree-icon-index" size="12"><IconListDetails /></NIcon>
                                <span class="tree-label-main">索引</span>
                                <span class="tree-row-count">{{ treeIndexes(view.key).length }}</span>
                              </button>
                            </div>
                            <div v-if="isTableGroupExpanded(view.key, 'indexes')" class="tree-children tree-children-animated">
                              <button v-for="index in treeIndexes(view.key)" :key="index.name" type="button" class="tree-leaf-row" @dblclick.stop.prevent="openDesignSection(view.key, 'indexes', index.name)">
                                <NIcon class="tree-icon tree-icon-leaf" size="12"><IconListDetails /></NIcon>
                                <span class="tree-leaf-name">{{ index.name }}</span>
                                <span class="tree-leaf-type">{{ index.columns.join(', ') || '-' }}</span>
                              </button>
                            </div>
                          </div>

                          <div class="tree-node">
                            <div class="tree-row tree-row-group">
                              <button class="tree-toggle" type="button" @click="toggleTableGroup(view.key, 'triggers')">
                                <NIcon size="12"><component :is="treeToggleIcon(isTableGroupExpanded(view.key, 'triggers'))" /></NIcon>
                              </button>
                              <button type="button" class="tree-node-main" @click="openDesignSection(view.key, 'triggers')">
                                <NIcon class="tree-icon tree-icon-trigger" size="12"><IconBolt /></NIcon>
                                <span class="tree-label-main">触发器</span>
                                <span class="tree-row-count">{{ treeTriggers(view.key).length }}</span>
                              </button>
                            </div>
                            <div v-if="isTableGroupExpanded(view.key, 'triggers')" class="tree-children tree-children-animated">
                              <button v-for="trigger in treeTriggers(view.key)" :key="trigger.name" type="button" class="tree-leaf-row" @dblclick.stop.prevent="openDesignSection(view.key, 'triggers', trigger.name)">
                                <NIcon class="tree-icon tree-icon-leaf" size="12"><IconBolt /></NIcon>
                                <span class="tree-leaf-name">{{ trigger.name }}</span>
                                <span class="tree-leaf-type">{{ trigger.event || trigger.timing || '-' }}</span>
                              </button>
                            </div>
                          </div>

                          <div class="tree-node">
                            <div class="tree-row tree-row-group">
                              <button class="tree-toggle" type="button" @click="toggleTableGroup(view.key, 'statistics')">
                                <NIcon size="12"><component :is="treeToggleIcon(isTableGroupExpanded(view.key, 'statistics'))" /></NIcon>
                              </button>
                              <button type="button" class="tree-node-main" @click="openDesignSection(view.key, 'statistics')">
                                <NIcon class="tree-icon tree-icon-index" size="12"><IconListDetails /></NIcon>
                                <span class="tree-label-main">统计信息</span>
                                <span class="tree-row-count">{{ treeStatistics(view.key).length }}</span>
                              </button>
                            </div>
                            <div v-if="isTableGroupExpanded(view.key, 'statistics')" class="tree-children tree-children-animated">
                              <button v-for="statistic in treeStatistics(view.key)" :key="statistic.name" type="button" class="tree-leaf-row" @dblclick.stop.prevent="openDesignSection(view.key, 'statistics', statistic.name)">
                                <NIcon class="tree-icon tree-icon-leaf" size="12"><IconListDetails /></NIcon>
                                <span class="tree-leaf-name">{{ statistic.name }}</span>
                                <span class="tree-leaf-type">{{ statistic.columns.join(', ') || '-' }}</span>
                              </button>
                            </div>
                          </div>
                        </template>
                      </div>
                    </div>
                  </div>
                </div>

                <div v-if="treeSynonyms(connection.id, database.name).length > 0" class="tree-node">
                  <div class="tree-row tree-row-group">
                    <button class="tree-toggle" type="button" @click="toggleRoutineGroup(connection.id, database.name, 'synonyms')">
                      <NIcon size="12"><component :is="treeToggleIcon(isRoutineGroupExpanded(connection.id, database.name, 'synonyms'))" /></NIcon>
                    </button>
                    <button type="button" class="tree-node-main" @click="toggleRoutineGroup(connection.id, database.name, 'synonyms')">
                      <NIcon class="tree-icon tree-icon-function" size="12"><IconLink /></NIcon>
                      <span class="tree-label-main">同义词</span>
                      <span class="tree-row-count">{{ treeSynonyms(connection.id, database.name).length }}</span>
                    </button>
                  </div>
                  <div v-if="isRoutineGroupExpanded(connection.id, database.name, 'synonyms')" class="tree-children tree-children-animated">
                    <button v-for="synonym in treeSynonyms(connection.id, database.name)" :key="`${synonym.schema ?? ''}.${synonym.name}`" type="button" class="tree-leaf-row tree-leaf-row-catalog" :title="catalogObjectTitle(synonym.schema, synonym.name, synonym.baseObjectName)" @dblclick.stop.prevent="openCatalogObject(connection.id, database.name, 'synonym', synonym.schema, synonym.name)">
                      <NIcon class="tree-icon tree-icon-leaf" size="12"><IconLink /></NIcon>
                      <span class="tree-leaf-stack">
                        <span class="tree-leaf-name">{{ synonym.schema ? `${synonym.schema}.${synonym.name}` : synonym.name }}</span>
                        <span class="tree-leaf-type">{{ synonym.baseObjectName }}</span>
                      </span>
                    </button>
                  </div>
                </div>

                <div v-if="treeSequences(connection.id, database.name).length > 0" class="tree-node">
                  <div class="tree-row tree-row-group">
                    <button class="tree-toggle" type="button" @click="toggleRoutineGroup(connection.id, database.name, 'sequences')">
                      <NIcon size="12"><component :is="treeToggleIcon(isRoutineGroupExpanded(connection.id, database.name, 'sequences'))" /></NIcon>
                    </button>
                    <button type="button" class="tree-node-main" @click="toggleRoutineGroup(connection.id, database.name, 'sequences')">
                      <NIcon class="tree-icon tree-icon-function" size="12"><IconVariable /></NIcon>
                      <span class="tree-label-main">序列</span>
                      <span class="tree-row-count">{{ treeSequences(connection.id, database.name).length }}</span>
                    </button>
                  </div>
                  <div v-if="isRoutineGroupExpanded(connection.id, database.name, 'sequences')" class="tree-children tree-children-animated">
                    <button v-for="sequence in treeSequences(connection.id, database.name)" :key="`${sequence.schema ?? ''}.${sequence.name}`" type="button" class="tree-leaf-row tree-leaf-row-catalog" :title="catalogObjectTitle(sequence.schema, sequence.name, `${sequence.dataType} · ${sequence.incrementValue}`)" @dblclick.stop.prevent="openCatalogObject(connection.id, database.name, 'sequence', sequence.schema, sequence.name)">
                      <NIcon class="tree-icon tree-icon-leaf" size="12"><IconVariable /></NIcon>
                      <span class="tree-leaf-stack">
                        <span class="tree-leaf-name">{{ sequence.schema ? `${sequence.schema}.${sequence.name}` : sequence.name }}</span>
                        <span class="tree-leaf-type">{{ `${sequence.dataType} · ${sequence.incrementValue}` }}</span>
                      </span>
                    </button>
                  </div>
                </div>

                <div v-if="treeRules(connection.id, database.name).length > 0" class="tree-node">
                  <div class="tree-row tree-row-group">
                    <button class="tree-toggle" type="button" @click="toggleRoutineGroup(connection.id, database.name, 'rules')">
                      <NIcon size="12"><component :is="treeToggleIcon(isRoutineGroupExpanded(connection.id, database.name, 'rules'))" /></NIcon>
                    </button>
                    <button type="button" class="tree-node-main" @click="toggleRoutineGroup(connection.id, database.name, 'rules')">
                      <NIcon class="tree-icon tree-icon-function" size="12"><IconListDetails /></NIcon>
                      <span class="tree-label-main">规则</span>
                      <span class="tree-row-count">{{ treeRules(connection.id, database.name).length }}</span>
                    </button>
                  </div>
                  <div v-if="isRoutineGroupExpanded(connection.id, database.name, 'rules')" class="tree-children tree-children-animated">
                    <button v-for="rule in treeRules(connection.id, database.name)" :key="`${rule.schema ?? ''}.${rule.name}`" type="button" class="tree-leaf-row tree-leaf-row-catalog" :title="catalogObjectTitle(rule.schema, rule.name, rule.definition || '规则')" @dblclick.stop.prevent="openCatalogObject(connection.id, database.name, 'rule', rule.schema, rule.name)">
                      <NIcon class="tree-icon tree-icon-leaf" size="12"><IconListDetails /></NIcon>
                      <span class="tree-leaf-stack">
                        <span class="tree-leaf-name">{{ rule.schema ? `${rule.schema}.${rule.name}` : rule.name }}</span>
                        <span class="tree-leaf-type">{{ rule.definition || '规则' }}</span>
                      </span>
                    </button>
                  </div>
                </div>

                <div v-if="treeDefaults(connection.id, database.name).length > 0" class="tree-node">
                  <div class="tree-row tree-row-group">
                    <button class="tree-toggle" type="button" @click="toggleRoutineGroup(connection.id, database.name, 'defaults')">
                      <NIcon size="12"><component :is="treeToggleIcon(isRoutineGroupExpanded(connection.id, database.name, 'defaults'))" /></NIcon>
                    </button>
                    <button type="button" class="tree-node-main" @click="toggleRoutineGroup(connection.id, database.name, 'defaults')">
                      <NIcon class="tree-icon tree-icon-function" size="12"><IconListDetails /></NIcon>
                      <span class="tree-label-main">默认值</span>
                      <span class="tree-row-count">{{ treeDefaults(connection.id, database.name).length }}</span>
                    </button>
                  </div>
                  <div v-if="isRoutineGroupExpanded(connection.id, database.name, 'defaults')" class="tree-children tree-children-animated">
                    <button v-for="item in treeDefaults(connection.id, database.name)" :key="`${item.schema ?? ''}.${item.name}`" type="button" class="tree-leaf-row tree-leaf-row-catalog" :title="catalogObjectTitle(item.schema, item.name, item.definition || '默认值')" @dblclick.stop.prevent="openCatalogObject(connection.id, database.name, 'default', item.schema, item.name)">
                      <NIcon class="tree-icon tree-icon-leaf" size="12"><IconListDetails /></NIcon>
                      <span class="tree-leaf-stack">
                        <span class="tree-leaf-name">{{ item.schema ? `${item.schema}.${item.name}` : item.name }}</span>
                        <span class="tree-leaf-type">{{ item.definition || '默认值' }}</span>
                      </span>
                    </button>
                  </div>
                </div>

                <div v-if="treeScalarTypes(connection.id, database.name).length > 0" class="tree-node">
                  <div class="tree-row tree-row-group">
                    <button class="tree-toggle" type="button" @click="toggleRoutineGroup(connection.id, database.name, 'types')">
                      <NIcon size="12"><component :is="treeToggleIcon(isRoutineGroupExpanded(connection.id, database.name, 'types'))" /></NIcon>
                    </button>
                    <button type="button" class="tree-node-main" @click="toggleRoutineGroup(connection.id, database.name, 'types')">
                      <NIcon class="tree-icon tree-icon-function" size="12"><IconTableColumn /></NIcon>
                      <span class="tree-label-main">类型</span>
                      <span class="tree-row-count">{{ treeUserDefinedTypes(connection.id, database.name).length }}</span>
                    </button>
                  </div>
                  <div v-if="isRoutineGroupExpanded(connection.id, database.name, 'types')" class="tree-children tree-children-animated">
                    <button v-for="item in treeScalarTypes(connection.id, database.name)" :key="`${item.schema ?? ''}.${item.name}`" type="button" class="tree-leaf-row tree-leaf-row-catalog" :title="catalogObjectTitle(item.schema, item.name, item.baseTypeName)" @dblclick.stop.prevent="openCatalogObject(connection.id, database.name, 'user-defined-type', item.schema, item.name)">
                      <NIcon class="tree-icon tree-icon-leaf" size="12"><IconTableColumn /></NIcon>
                      <span class="tree-leaf-stack">
                        <span class="tree-leaf-name">{{ item.schema ? `${item.schema}.${item.name}` : item.name }}</span>
                        <span class="tree-leaf-type">{{ item.baseTypeName }}</span>
                      </span>
                    </button>
                  </div>
                </div>

                <div v-if="treeTableTypes(connection.id, database.name).length > 0" class="tree-node">
                  <div class="tree-row tree-row-group">
                    <button class="tree-toggle" type="button" @click="toggleRoutineGroup(connection.id, database.name, 'table-types')">
                      <NIcon size="12"><component :is="treeToggleIcon(isRoutineGroupExpanded(connection.id, database.name, 'table-types'))" /></NIcon>
                    </button>
                    <button type="button" class="tree-node-main" @click="toggleRoutineGroup(connection.id, database.name, 'table-types')">
                      <NIcon class="tree-icon tree-icon-function" size="12"><IconTable /></NIcon>
                      <span class="tree-label-main">表类型</span>
                      <span class="tree-row-count">{{ treeTableTypes(connection.id, database.name).length }}</span>
                    </button>
                  </div>
                  <div v-if="isRoutineGroupExpanded(connection.id, database.name, 'table-types')" class="tree-children tree-children-animated">
                    <button v-for="item in treeTableTypes(connection.id, database.name)" :key="`${item.schema ?? ''}.${item.name}`" type="button" class="tree-leaf-row tree-leaf-row-catalog" :title="catalogObjectTitle(item.schema, item.name, 'table type')" @dblclick.stop.prevent="openCatalogObject(connection.id, database.name, 'user-defined-type', item.schema, item.name)">
                      <NIcon class="tree-icon tree-icon-leaf" size="12"><IconTable /></NIcon>
                      <span class="tree-leaf-stack">
                        <span class="tree-leaf-name">{{ item.schema ? `${item.schema}.${item.name}` : item.name }}</span>
                        <span class="tree-leaf-type">table type</span>
                      </span>
                    </button>
                  </div>
                </div>

                <div v-if="treeDatabaseTriggers(connection.id, database.name).length > 0" class="tree-node">
                  <div class="tree-row tree-row-group">
                    <button class="tree-toggle" type="button" @click="toggleRoutineGroup(connection.id, database.name, 'database-triggers')">
                      <NIcon size="12"><component :is="treeToggleIcon(isRoutineGroupExpanded(connection.id, database.name, 'database-triggers'))" /></NIcon>
                    </button>
                    <button type="button" class="tree-node-main" @click="toggleRoutineGroup(connection.id, database.name, 'database-triggers')">
                      <NIcon class="tree-icon tree-icon-trigger" size="12"><IconBolt /></NIcon>
                      <span class="tree-label-main">数据库触发器</span>
                      <span class="tree-row-count">{{ treeDatabaseTriggers(connection.id, database.name).length }}</span>
                    </button>
                  </div>
                  <div v-if="isRoutineGroupExpanded(connection.id, database.name, 'database-triggers')" class="tree-children tree-children-animated">
                    <button v-for="item in treeDatabaseTriggers(connection.id, database.name)" :key="`${item.schema ?? ''}.${item.name}`" type="button" class="tree-leaf-row tree-leaf-row-catalog" :title="catalogObjectTitle(item.schema, item.name, item.event || item.timing || '数据库触发器')" @dblclick.stop.prevent="openExtendedCatalogObject(connection.id, database.name, 'database-trigger', item.schema, item.name)">
                      <NIcon class="tree-icon tree-icon-leaf" size="12"><IconBolt /></NIcon>
                      <span class="tree-leaf-stack">
                        <span class="tree-leaf-name">{{ item.schema ? `${item.schema}.${item.name}` : item.name }}</span>
                        <span class="tree-leaf-type">{{ item.event || item.timing || '数据库触发器' }}</span>
                      </span>
                    </button>
                  </div>
                </div>

                <div v-if="treeXmlSchemaCollections(connection.id, database.name).length > 0" class="tree-node">
                  <div class="tree-row tree-row-group">
                    <button class="tree-toggle" type="button" @click="toggleRoutineGroup(connection.id, database.name, 'xml-schema-collections')">
                      <NIcon size="12"><component :is="treeToggleIcon(isRoutineGroupExpanded(connection.id, database.name, 'xml-schema-collections'))" /></NIcon>
                    </button>
                    <button type="button" class="tree-node-main" @click="toggleRoutineGroup(connection.id, database.name, 'xml-schema-collections')">
                      <NIcon class="tree-icon tree-icon-function" size="12"><IconListDetails /></NIcon>
                      <span class="tree-label-main">XML 架构集合</span>
                      <span class="tree-row-count">{{ treeXmlSchemaCollections(connection.id, database.name).length }}</span>
                    </button>
                  </div>
                  <div v-if="isRoutineGroupExpanded(connection.id, database.name, 'xml-schema-collections')" class="tree-children tree-children-animated">
                    <button v-for="item in treeXmlSchemaCollections(connection.id, database.name)" :key="`${item.schema ?? ''}.${item.name}`" type="button" class="tree-leaf-row tree-leaf-row-catalog" :title="catalogObjectTitle(item.schema, item.name, `${item.xmlNamespaceCount} namespaces`)" @dblclick.stop.prevent="openExtendedCatalogObject(connection.id, database.name, 'xml-schema-collection', item.schema, item.name)">
                      <NIcon class="tree-icon tree-icon-leaf" size="12"><IconListDetails /></NIcon>
                      <span class="tree-leaf-stack">
                        <span class="tree-leaf-name">{{ item.schema ? `${item.schema}.${item.name}` : item.name }}</span>
                        <span class="tree-leaf-type">{{ `${item.xmlNamespaceCount} namespaces` }}</span>
                      </span>
                    </button>
                  </div>
                </div>

                <!-- 存储过程分组 -->
                <div v-if="treeProcedures(connection.id, database.name).length > 0" class="tree-node">
                  <div class="tree-row tree-row-group">
                    <button class="tree-toggle" type="button" @click="toggleRoutineGroup(connection.id, database.name, 'procedures')">
                      <NIcon size="12"><component :is="treeToggleIcon(isRoutineGroupExpanded(connection.id, database.name, 'procedures'))" /></NIcon>
                    </button>
                    <button type="button" class="tree-node-main" @click="toggleRoutineGroup(connection.id, database.name, 'procedures')">
                      <NIcon class="tree-icon tree-icon-procedure" size="12"><IconScript /></NIcon>
                      <span class="tree-label-main">存储过程</span>
                      <span class="tree-row-count">{{ treeProcedures(connection.id, database.name).length }}</span>
                    </button>
                  </div>
                  <div v-if="isRoutineGroupExpanded(connection.id, database.name, 'procedures')" class="tree-children tree-children-animated">
                    <div v-for="routine in treeProcedures(connection.id, database.name)" :key="routine.name" class="tree-node">
                      <div class="tree-row">
                        <button class="tree-toggle" type="button" @click="toggleRoutine(connection.id, database.name, routine)">
                          <NIcon size="12"><component :is="treeToggleIcon(isRoutineExpanded(connection.id, database.name, routine))" /></NIcon>
                        </button>
                        <button type="button" class="tree-node-main" @dblclick.stop.prevent="store.openRoutineSource(connection.id, database.name, routine.schema ?? null, routine.name, routine.routineType)" @contextmenu.stop.prevent="openRoutineContextMenu($event, connection.id, database.name, connection.provider, routine)">
                          <NIcon class="tree-icon tree-icon-leaf" size="12"><component :is="routineTreeIcon(routine.routineType)" /></NIcon>
                          <span class="tree-leaf-name">{{ formatRoutineName(connection.provider, routine) }}</span>
                        </button>
                      </div>
                      <div v-if="isRoutineExpanded(connection.id, database.name, routine)" class="tree-children tree-children-animated">
                        <div class="tree-node">
                          <div class="tree-row tree-row-group">
                            <button class="tree-toggle" type="button" @click="toggleRoutineParamGroup(connection.id, database.name, routine)">
                              <NIcon size="12"><component :is="treeToggleIcon(isRoutineParamGroupExpanded(connection.id, database.name, routine))" /></NIcon>
                            </button>
                            <button type="button" class="tree-node-main" @click="toggleRoutineParamGroup(connection.id, database.name, routine)">
                              <NIcon class="tree-icon tree-icon-leaf" size="12"><IconVariable /></NIcon>
                              <span class="tree-label-main">参数</span>
                              <span class="tree-row-count">{{ routine.parameters.length }}</span>
                            </button>
                          </div>
                          <div v-if="isRoutineParamGroupExpanded(connection.id, database.name, routine)" class="tree-children tree-children-animated">
                            <button v-for="param in routine.parameters" :key="param.name" type="button" class="tree-leaf-row">
                              <NIcon class="tree-icon tree-icon-leaf" size="12"><IconVariable /></NIcon>
                              <span class="tree-leaf-name">{{ param.name }}</span>
                              <span class="tree-leaf-type">{{ param.dataType }}</span>
                              <span v-if="param.direction !== 'IN'" class="tree-leaf-type tree-param-direction">{{ formatParamDirection(param.direction) }}</span>
                            </button>
                          </div>
                        </div>
                      </div>
                    </div>
                  </div>
                </div>

                <!-- 函数分组 -->
                <div v-if="treeFunctions(connection.id, database.name).length > 0" class="tree-node">
                  <div class="tree-row tree-row-group">
                    <button class="tree-toggle" type="button" @click="toggleRoutineGroup(connection.id, database.name, 'functions')">
                      <NIcon size="12"><component :is="treeToggleIcon(isRoutineGroupExpanded(connection.id, database.name, 'functions'))" /></NIcon>
                    </button>
                    <button type="button" class="tree-node-main" @click="toggleRoutineGroup(connection.id, database.name, 'functions')">
                      <NIcon class="tree-icon tree-icon-function" size="12"><IconFunction /></NIcon>
                      <span class="tree-label-main">函数</span>
                      <span class="tree-row-count">{{ treeFunctions(connection.id, database.name).length }}</span>
                    </button>
                  </div>
                  <div v-if="isRoutineGroupExpanded(connection.id, database.name, 'functions')" class="tree-children tree-children-animated">
                    <div v-for="routine in treeFunctions(connection.id, database.name)" :key="routine.name" class="tree-node">
                      <div class="tree-row">
                        <button class="tree-toggle" type="button" @click="toggleRoutine(connection.id, database.name, routine)">
                          <NIcon size="12"><component :is="treeToggleIcon(isRoutineExpanded(connection.id, database.name, routine))" /></NIcon>
                        </button>
                        <button type="button" class="tree-node-main" @dblclick.stop.prevent="store.openRoutineSource(connection.id, database.name, routine.schema ?? null, routine.name, routine.routineType)" @contextmenu.stop.prevent="openRoutineContextMenu($event, connection.id, database.name, connection.provider, routine)">
                          <NIcon class="tree-icon tree-icon-leaf" size="12"><component :is="routineTreeIcon(routine.routineType)" /></NIcon>
                          <span class="tree-leaf-name">{{ formatRoutineName(connection.provider, routine) }}</span>
                          <span v-if="routineSubType(routine)" class="tree-leaf-type">{{ routineSubType(routine) }}</span>
                        </button>
                      </div>
                      <div v-if="isRoutineExpanded(connection.id, database.name, routine)" class="tree-children tree-children-animated">
                        <div class="tree-node">
                          <div class="tree-row tree-row-group">
                            <button class="tree-toggle" type="button" @click="toggleRoutineParamGroup(connection.id, database.name, routine)">
                              <NIcon size="12"><component :is="treeToggleIcon(isRoutineParamGroupExpanded(connection.id, database.name, routine))" /></NIcon>
                            </button>
                            <button type="button" class="tree-node-main" @click="toggleRoutineParamGroup(connection.id, database.name, routine)">
                              <NIcon class="tree-icon tree-icon-leaf" size="12"><IconVariable /></NIcon>
                              <span class="tree-label-main">参数</span>
                              <span class="tree-row-count">{{ routine.parameters.length }}</span>
                            </button>
                          </div>
                          <div v-if="isRoutineParamGroupExpanded(connection.id, database.name, routine)" class="tree-children tree-children-animated">
                            <button v-for="param in routine.parameters" :key="param.name" type="button" class="tree-leaf-row">
                              <NIcon class="tree-icon tree-icon-leaf" size="12"><IconVariable /></NIcon>
                              <span class="tree-leaf-name">{{ param.name }}</span>
                              <span class="tree-leaf-type">{{ param.dataType }}</span>
                              <span v-if="param.direction !== 'IN'" class="tree-leaf-type tree-param-direction">{{ formatParamDirection(param.direction) }}</span>
                            </button>
                          </div>
                        </div>
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </template>

      <NEmpty v-else description="没有匹配的连接或表" size="large" />
      </NSpin>
    </div>

    <div
      v-if="connectionContextMenu"
      class="database-context-menu"
      :style="{ left: `${connectionContextMenu.x}px`, top: `${connectionContextMenu.y}px` }"
      @click.stop
      @mousedown.stop
    >
      <button type="button" class="database-context-menu-item" @click.stop="openEditConnectionDialog()">
        编辑连接
      </button>
      <button type="button" class="database-context-menu-item" @click.stop="openDeleteConnectionConfirm()">
        删除连接
      </button>
    </div>

    <div
      v-if="databaseContextMenu"
      class="database-context-menu"
      :style="{ left: `${databaseContextMenu.x}px`, top: `${databaseContextMenu.y}px` }"
      @click.stop
      @mousedown.stop
    >
      <button type="button" class="database-context-menu-item" @click="openDatabaseSqlQuery()">
        新建查询
      </button>
      <button type="button" class="database-context-menu-item" @click="openCreateTableQuery">
        新建表...
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
      @click.stop
      @mousedown.stop
      @mouseleave="setTableScriptSubmenu(false)"
    >
      <button type="button" class="database-context-menu-item" @click="store.openTableDesign(tableContextMenu.table.key); closeTableContextMenu()">表设计...</button>
      <button type="button" class="database-context-menu-item" @click="openTableMockDataDialog">生成 Mock 数据...</button>
      <button type="button" class="database-context-menu-item" @click="openRenameTableQuery">重命名表...</button>
      <div class="database-context-submenu-anchor" @mouseenter="setTableScriptSubmenu(true)" @mouseleave="setTableScriptSubmenu(false)">
        <button
          type="button"
          class="database-context-menu-item database-context-menu-item-arrow"
          @click="setTableScriptSubmenu(!tableContextMenu.showScriptSubmenu)"
        >
          <span>编写脚本为</span>
          <NIcon size="12" class="database-context-menu-item-arrow-icon"><IconChevronRight /></NIcon>
        </button>

        <div v-if="tableContextMenu.showScriptSubmenu" class="database-context-menu database-context-submenu">
          <button type="button" class="database-context-menu-item" @click="openTableScript('create')">CREATE 到</button>
          <button type="button" class="database-context-menu-item" @click="openTableScript('drop')">DROP 到</button>
          <button type="button" class="database-context-menu-item" @click="openTableScript('select')">SELECT 到</button>
          <button type="button" class="database-context-menu-item" @click="openTableScript('insert')">INSERT 到</button>
          <button type="button" class="database-context-menu-item" @click="openTableScript('update')">UPDATE 到</button>
          <button type="button" class="database-context-menu-item" @click="openTableScript('delete')">DELETE 到</button>
        </div>
      </div>
    </div>

    <NModal v-model:show="createConnectionVisible" preset="card" style="width: min(460px, 92vw)" :title="editingConnectionId ? '编辑连接' : '新建连接'">
      <div class="connection-dialog-form">
        <NInput v-model:value="createConnectionForm.name" placeholder="连接名称" />
        <NSelect :value="createConnectionForm.provider" :options="providerOptions" @update:value="handleProviderChange($event)" />
        <NSelect v-if="createConnectionForm.provider !== 'sqlite'" v-model:value="createConnectionForm.authentication" :options="authenticationOptions" />
        <NInput v-model:value="createConnectionForm.host" :placeholder="createConnectionForm.provider === 'sqlite' ? '数据库文件路径，例如 C:\\data\\app.db' : '主机，例如 .\\SQLEXPRESS 或 localhost'" />
        <NButton v-if="createConnectionForm.provider === 'sqlite'" tertiary type="primary" @click="browseSqliteDatabaseFile">选择或新建 SQLite 文件</NButton>
        <NText v-if="createConnectionForm.provider === 'sqlite'" depth="3">SQLite 会把这里当作数据库文件路径；如果文件不存在，会在首次连接时自动创建。</NText>
        <NInput v-if="createConnectionForm.provider !== 'sqlite'" v-model:value="createConnectionForm.port" placeholder="端口" inputmode="numeric" />
        <NInput v-if="createConnectionForm.provider !== 'sqlite' && createConnectionForm.authentication !== 'windows'" v-model:value="createConnectionForm.username" placeholder="用户名" />
        <NInput v-if="createConnectionForm.provider !== 'sqlite' && createConnectionForm.authentication !== 'windows'" v-model:value="createConnectionForm.password" type="password" show-password-on="click" :placeholder="editingConnectionId ? '密码，留空则保持不变' : '密码'" />
        <NCheckbox v-if="createConnectionForm.provider === 'sqlserver'" v-model:checked="createConnectionForm.trustServerCertificate">信任服务器证书</NCheckbox>
        <NAlert v-if="createConnectionError" type="warning" :show-icon="false">{{ createConnectionError }}</NAlert>
        <div class="connection-dialog-actions">
          <NButton tertiary @click="createConnectionVisible = false">取消</NButton>
          <NButton tertiary :loading="testConnectionLoading" @click="testCurrentConnection">测试连接</NButton>
          <NButton type="primary" :loading="createConnectionLoading" @click="submitCreateConnection">{{ editingConnectionId ? '保存修改' : '保存连接' }}</NButton>
        </div>
      </div>
    </NModal>

    <NModal v-model:show="renameTableVisible" preset="card" style="width: min(420px, 92vw)" title="重命名表">
      <div class="connection-dialog-form">
        <NInput v-model:value="renameTableName" placeholder="新的表名" @keydown.enter.prevent="submitRenameTable" />
        <NAlert v-if="renameTableError" type="warning" :show-icon="false">{{ renameTableError }}</NAlert>
        <div class="connection-dialog-actions">
          <NButton tertiary :disabled="renameTableLoading" @click="closeRenameTableDialog">取消</NButton>
          <NButton type="primary" :loading="renameTableLoading" @click="submitRenameTable">重命名</NButton>
        </div>
      </div>
    </NModal>

    <NModal
      :show="!!deleteConnectionConfirm"
      preset="card"
      style="width: min(420px, 92vw)"
      title="删除连接"
      @update:show="(show) => { if (!show) closeDeleteConnectionConfirm() }"
    >
      <div class="connection-dialog-form">
        <NAlert type="warning" :show-icon="false">
          确认删除连接 {{ deleteConnectionConfirm?.connectionName }}？
        </NAlert>
        <div class="connection-dialog-actions">
          <NButton tertiary @click="closeDeleteConnectionConfirm()">取消</NButton>
          <NButton type="error" @click="deleteConnectionConfirm && confirmDeleteConnection(deleteConnectionConfirm.connectionId)">删除</NButton>
        </div>
      </div>
    </NModal>

    <!-- routine context menu -->
    <div
      v-if="routineContextMenu"
      class="database-context-menu"
      :style="{ left: `${routineContextMenu.x}px`, top: `${routineContextMenu.y}px` }"
      @click.stop
      @mousedown.stop
    >
      <button type="button" class="database-context-menu-item" @click="store.openRoutineSource(routineContextMenu.connectionId, routineContextMenu.database, routineContextMenu.routine.schema ?? null, routineContextMenu.routine.name, routineContextMenu.routine.routineType); closeRoutineContextMenu()">
        修改...
      </button>
      <button type="button" class="database-context-menu-item" @click="openRenameRoutineDialog()">
        重命名...
      </button>
      <button type="button" class="database-context-menu-item database-context-menu-item-danger" @click="deleteRoutineConfirm = { ...routineContextMenu }; closeRoutineContextMenu()">
        删除
      </button>
    </div>

    <div
      v-if="designEntryContextMenu"
      class="database-context-menu"
      :style="{ left: `${designEntryContextMenu.x}px`, top: `${designEntryContextMenu.y}px` }"
      @click.stop
      @mousedown.stop
    >
      <button type="button" class="database-context-menu-item database-context-menu-item-danger" @click.stop="deleteDesignEntryFromSidebar()">
        {{ designEntryContextMenu.kind === 'column' ? '删除字段' : '删除索引' }}
      </button>
    </div>

    <!-- delete routine confirm -->
    <NModal
      :show="!!deleteRoutineConfirm"
      preset="card"
      style="width: min(420px, 92vw)"
      title="删除对象"
      @update:show="(show) => { if (!show) deleteRoutineConfirm = null }"
    >
      <div class="connection-dialog-form">
        <NAlert type="warning" :show-icon="false">
          确认删除 {{ deleteRoutineConfirm?.routine.routineType === 'Procedure' ? '存储过程' : '函数' }}
          {{ deleteRoutineConfirm?.routine.schema ? `${deleteRoutineConfirm.routine.schema}.` : '' }}{{ deleteRoutineConfirm?.routine.name }}？
        </NAlert>
        <div class="connection-dialog-actions">
          <NButton tertiary @click="deleteRoutineConfirm = null">取消</NButton>
          <NButton type="error" @click="deleteRoutine()">删除</NButton>
        </div>
      </div>
    </NModal>

    <!-- rename routine dialog -->
    <NModal
      :show="renameRoutineVisible"
      preset="card"
      style="width: min(420px, 92vw)"
      title="重命名"
      @update:show="(show) => { if (!show) closeRenameRoutineDialog() }"
    >
      <div class="connection-dialog-form">
        <NInput v-model:value="renameRoutineName" placeholder="新名称" @keydown.enter="submitRenameRoutine()" />
        <NAlert v-if="renameRoutineError" type="error" :show-icon="false">{{ renameRoutineError }}</NAlert>
        <div class="connection-dialog-actions">
          <NButton tertiary @click="closeRenameRoutineDialog()">取消</NButton>
          <NButton type="primary" :loading="renameRoutineLoading" @click="submitRenameRoutine()">确定</NButton>
        </div>
      </div>
    </NModal>
  </div>
</template>

<style scoped lang="scss">
// ── Sidebar shell ──
.sidebar-shell {
  display: flex;
  flex-direction: column;
  gap: $gap-md;
}

.sidebar-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: $gap-lg;
  padding: $gap-md $gap-lg;
  background: $color-bg-panel;
  border: 1px solid $color-border-subtle;
  border-radius: var(--radius-lg);
}

.sidebar-toolbar-actions {
  display: flex;
  align-items: center;
  gap: $gap-lg;
}

.sidebar-list {
  overflow-y: auto;
  overflow-x: hidden;
  padding: 0 1px 0 0;
  flex: 1;
}

.sidebar-filter {
  position: sticky;
  top: 0;
  z-index: 1;
  margin-bottom: $gap-md;
}

// ── Connection card ──
.connection-card {
  margin-bottom: $gap-sm;
  padding: $gap-sm;
  border-radius: 10px;
  background: linear-gradient(180deg, rgba(255, 255, 255, 0.96), rgba(248, 250, 252, 0.88));
  border: 1px solid rgba(226, 232, 240, 0.9);
  box-shadow: 0 10px 22px rgba(15, 23, 42, 0.035);

  &::before {
    content: none;
  }
}

// ── Tree structure ──
.tree-root,
.tree-node,
.tree-children {
  display: grid;
  gap: $gap-xs;
  min-width: 0;
}

.tree-children {
  margin-left: $gap-lg;
  padding-left: 7px;
  border-left: 1px dashed rgba(148, 163, 184, 0.2);

  &-table {
    margin-left: 14px;
  }

  &-animated {
    transform-origin: top left;
    animation: tree-node-enter 160ms ease;
  }
}

@keyframes tree-node-enter {
  from {
    opacity: 0;
    transform: translateY(-4px) scaleY(0.96);
  }

  to {
    opacity: 1;
    transform: translateY(0) scaleY(1);
  }
}

.tree-row {
  display: flex;
  align-items: center;
  gap: 3px;
  min-width: 0;
  border-radius: var(--radius-sm);

  &-active {
    background: rgba(219, 234, 254, 0.28);
    border-radius: var(--radius-sm);
  }

  &:hover {
    background: rgba(241, 245, 249, 0.72);

    .tree-node-main,
    .tree-toggle {
      background: transparent;
    }
  }

  &-connection,
  &-database,
  &-table,
  &-group {
    .tree-node-main {
      border: 1px solid transparent;
    }
  }

  &-connection .tree-node-main {
    padding: 4px 5px;
    border-radius: 8px;
  }

  &-database .tree-node-main {
    padding: 4px 5px;
    border-radius: 8px;
    background: rgba(248, 250, 252, 0.9);
  }

  &-table .tree-node-main {
    padding: 4px 5px;
    border-radius: 8px;
    background: rgba(255, 255, 255, 0.9);
  }

  &-group .tree-node-main {
    padding: 2px 4px;
    border-radius: 7px;
  }

  &-database,
  &-table {
    .tree-label-meta {
      color: #94a3b8 !important;
      font-size: 8px !important;
    }
  }
}

.tree-row-active {
  .tree-toggle,
  .tree-node-main {
    background: transparent;
  }
}

.tree-toggle {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 14px;
  height: 14px;
  padding: 0;
  border: 0;
  background: transparent;
  color: $color-text-muted;
  font-size: $font-size-xs;
  line-height: 1;
  cursor: pointer;
  flex: 0 0 auto;
  border-radius: 4px;
}

.tree-node-main,
.tree-leaf-row {
  display: flex;
  align-items: center;
  gap: 5px;
  width: 100%;
  min-width: 0;
  padding: $gap-xs 3px;
  border: 0;
  border-radius: var(--radius-sm);
  background: transparent;
  color: $color-text-primary;
  text-align: left;
  cursor: pointer;
  transition: background 120ms ease, box-shadow 120ms ease, color 120ms ease;

  &:hover {
    background: rgba(219, 234, 254, 0.7);
  }
}

.tree-toggle:focus-visible,
.tree-node-main:focus-visible,
.tree-leaf-row:focus-visible {
  outline: none;
  box-shadow: none;
}

.tree-node-main-connection {
  padding-left: 0;
}

// ── Tree icons ──
.tree-icon {
  :deep(svg) {
    width: 12px;
    height: 12px;
  }

  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 16px;
  width: 16px;
  height: 16px;
  border-radius: 4px;
  background: rgba(226, 232, 240, 0.72);
  color: $color-text-tertiary;
  font-size: $font-size-2xs;
  font-weight: 800;
  line-height: 1;
  flex: 0 0 auto;

  &-provider-sqlserver {
    background: rgba(15, 118, 110, 0.1);
    color: $color-accent-teal;
  }

  &-provider-mysql {
    background: rgba(124, 58, 237, 0.16);
    color: #7c3aed;
  }

  &-provider-postgresql {
    background: rgba(37, 99, 235, 0.16);
    color: $color-accent-blue;
  }

  &-provider-sqlite {
    background: rgba(21, 128, 61, 0.16);
    color: $color-accent-green;
  }

  &-database {
    background: rgba(37, 99, 235, 0.13);
    color: $color-accent-blue;
  }

  &-table {
    background: rgba(245, 158, 11, 0.16);
    color: $color-accent-amber;
  }

  &-column {
    background: rgba(16, 185, 129, 0.16);
    color: $color-accent-green-cell;
  }

  &-index {
    background: rgba(99, 102, 241, 0.16);
    color: $color-accent-indigo;
  }

  &-foreign-key {
    background: rgba(14, 165, 233, 0.16);
    color: #0284c7;
  }

  &-trigger {
    background: rgba(239, 68, 68, 0.15);
    color: $color-accent-red;
  }

  &-procedure {
    background: rgba(168, 85, 247, 0.16);
    color: #9333ea;
  }

  &-function {
    background: rgba(236, 72, 153, 0.16);
    color: #db2777;
  }

  &-leaf {
    min-width: 16px;
    width: 16px;
    height: 16px;
    border-radius: 4px;
    font-size: $font-size-2xs;
    background: rgba(226, 232, 240, 0.85);
    color: $color-text-secondary;
  }
}

// ── Tree labels ──
.tree-label-stack {
  display: grid;
  gap: 1px;
  min-width: 0;
  flex: 1 1 auto;
}

.tree-leaf-stack {
  display: grid;
  gap: 1px;
  min-width: 0;
  flex: 1 1 auto;
}

.tree-label-main {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 10.5px;
  font-weight: 700;
  line-height: 1.2;
  color: $color-text-primary;
}

.tree-label-meta,
.tree-row-count,
.tree-loading-row,
.tree-inline-alert :deep(.n-alert__content),
.tree-leaf-type {
  color: #9aa8ba !important;
  font-size: 8px !important;
  font-weight: 500;
  line-height: 1.1;
}

.tree-row-count {
  flex: 0 0 auto;
  padding-left: 4px;
  color: #94a3b8 !important;
  font-size: 7px !important;
  font-weight: 600;
  letter-spacing: 0.02em;
}

.tree-leaf-row {
  padding-left: 21px;

  &:hover .tree-leaf-name {
    color: #0f172a;
  }

  &:hover .tree-leaf-type {
    color: #64748b !important;
  }
}

.tree-leaf-row-catalog {
  align-items: flex-start;
}

.tree-leaf-row-catalog .tree-leaf-name,
.tree-leaf-row-catalog .tree-leaf-type {
  text-align: left;
}

.tree-leaf-row-catalog .tree-leaf-type {
  flex: 0 0 auto;
  max-width: none;
}

.tree-leaf-name {
  min-width: 0;
  flex: 1 1 auto;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 9.5px;
  color: $color-text-heading;
}

.tree-leaf-type {
  flex: 0 1 46%;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  text-align: right;
  color: #a8b4c3 !important;
}

.tree-param-direction {
  flex: 0 0 auto;
  font-size: $font-size-xs;
  color: #e0a060 !important;
  margin-left: $gap-xs;
}

.tree-loading-row {
  padding: 2px 4px 4px 24px;
}

.tree-inline-alert {
  margin: 2px 0 4px 0;
}

// ── Database context menu ──
.database-context-menu {
  position: fixed;
  z-index: 90;
  min-width: 156px;
  padding: 4px;
  border: 1px solid $color-border-strong;
  border-radius: var(--radius-md);
  background: rgba(255, 255, 255, 0.98);
  box-shadow: 0 16px 40px $shadow-strong;
  transform-origin: top left;
  animation: context-menu-enter 140ms ease;

  .database-context-menu {
    position: absolute;
  }
}

.database-context-menu-item {
  width: 100%;
  padding: 6px 10px;
  border: 0;
  border-radius: var(--radius-sm);
  background: transparent;
  text-align: left;
  font-size: 12px;
  line-height: 1.25;
  cursor: pointer;

  &:hover {
    background: $color-bg-hover;
  }

  &-arrow {
    display: flex;
    align-items: center;
    justify-content: space-between;

    .database-context-menu-item-arrow-icon {
      color: #64748b;
    }
  }
}

.database-context-submenu-anchor {
  position: relative;
}

.database-context-submenu {
  min-width: 138px;
  position: absolute;
  top: -4px;
  left: calc(100% + 6px);
}
</style>
