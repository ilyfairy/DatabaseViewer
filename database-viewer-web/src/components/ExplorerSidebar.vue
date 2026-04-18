<script setup lang="ts">
import type { Component } from 'vue';
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue';
import { IconBolt, IconChevronDown, IconChevronRight, IconColumns3, IconDatabase, IconEye, IconFunction, IconLink, IconListDetails, IconLoader2, IconPackage, IconScript, IconServer, IconTable, IconTableColumn, IconVariable } from '@tabler/icons-vue';
import { NAlert, NButton, NCheckbox, NEmpty, NIcon, NInput, NModal, NSelect, NSpin, NTabPane, NTabs, NText } from 'naive-ui';
import type { DropdownOption } from 'naive-ui';
import ContextDropdown from './ContextDropdown.vue';
import StoredMaskedPasswordInput from './StoredMaskedPasswordInput.vue';
import { buildSqliteDatabaseToolSql, findSqliteDatabaseTool, getSqliteDatabaseTools } from '../lib/sqliteDatabaseTools';
import { useExplorerStore } from '../stores/explorer';
import type { CatalogObjectType, DatabaseProviderType, RoutineInfo, SqliteOpenMode, SqlServerAuthenticationMode, SynonymInfo, TableColumn, TableDesignSection, TableSummary } from '../types/explorer';

const store = useExplorerStore();
const expandedConnections = ref<string[]>([]);
const expandedDatabases = ref<string[]>([]);
const expandedTables = ref<string[]>([]);
const expandedRoutineGroups = ref<string[]>([]);
const expandedRoutines = ref<string[]>([]);
const expandedRoutineParamGroups = ref<string[]>([]);
const expandedTableGroups = ref<string[]>([]);
const connectionContextMenu = ref<{
  x: number;
  y: number;
  connectionId: string;
  connectionName: string;
  provider: DatabaseProviderType;
  sqliteEncrypted: boolean | null;
} | null>(null);
const databaseContextMenu = ref<{ x: number; y: number; connectionId: string; database: string } | null>(null);
const tableContextMenu = ref<{ x: number; y: number; connectionId: string; database: string; table: TableSummary } | null>(null);
const routineContextMenu = ref<{ x: number; y: number; connectionId: string; database: string; provider: DatabaseProviderType; routine: RoutineInfo } | null>(null);
const designEntryContextMenu = ref<{ x: number; y: number; tableKey: string; kind: 'column' | 'index'; name: string } | null>(null);
const deleteConnectionConfirm = ref<{ connectionId: string; connectionName: string } | null>(null);
const deleteRoutineConfirm = ref<{ connectionId: string; database: string; provider: DatabaseProviderType; routine: RoutineInfo } | null>(null);
const renameTableTarget = ref<{ connectionId: string; database: string; table: TableSummary } | null>(null);
const renameTableVisible = ref(false);
const renameTableLoading = ref(false);
const renameTableError = ref<string | null>(null);
const renameTableName = ref('');
const renameRoutineTarget = ref<{ connectionId: string; database: string; provider: DatabaseProviderType; routine: RoutineInfo } | null>(null);
const renameRoutineVisible = ref(false);
const renameRoutineLoading = ref(false);
const renameRoutineError = ref<string | null>(null);
const renameRoutineName = ref('');
const editingConnectionId = ref<string | null>(null);
const createConnectionVisible = ref(false);
const createConnectionLoading = ref(false);
const testConnectionLoading = ref(false);
const createConnectionError = ref<string | null>(null);
const sqliteRekeyVisible = ref(false);
const sqliteRekeyLoading = ref(false);
const sqliteRekeyError = ref<string | null>(null);
const sqliteRekeyTarget = ref<{ connectionId: string; connectionName: string; currentEncrypted: boolean } | null>(null);
const activeConnectionSettingsTab = ref<'general' | 'ssh' | 'cipher'>('general');
const createConnectionForm = reactive({
  name: '',
  provider: 'sqlserver' as DatabaseProviderType,
  host: '',
  port: '1433',
  username: '',
  password: '',
});
const sqlServerConnectionForm = reactive({
  authenticationMode: 'password' as SqlServerAuthenticationMode,
  trustServerCertificate: true,
});
const sqliteConnectionForm = reactive({
  openMode: 'readwrite' as SqliteOpenMode,
  cipherEnabled: false,
  cipherHasStoredPassword: false,
  cipherPassword: '',
  cipherKeyFormat: 'passphrase' as 'passphrase' | 'hex',
  cipherPageSize: '',
  cipherKdfIter: '',
  cipherCompatibility: '',
  cipherPlaintextHeaderSize: '',
  cipherSkipBytes: '',
  cipherUseHmac: 'default' as 'default' | 'enabled' | 'disabled',
  cipherKdfAlgorithm: '' as '' | 'PBKDF2_HMAC_SHA1' | 'PBKDF2_HMAC_SHA256' | 'PBKDF2_HMAC_SHA512',
  cipherHmacAlgorithm: '' as '' | 'HMAC_SHA1' | 'HMAC_SHA256' | 'HMAC_SHA512',
});
const sshTunnelForm = reactive({
  enabled: false,
  authentication: 'password' as 'password' | 'publicKey',
  host: '',
  port: '22',
  username: '',
  password: '',
  privateKeyPath: '',
  passphrase: '',
});
const sqliteRekeyForm = reactive({
  currentPassword: '',
  currentKeyFormat: 'passphrase' as 'passphrase' | 'hex',
  newPassword: '',
  newKeyFormat: 'passphrase' as 'passphrase' | 'hex',
});
const sqliteCipherPasswordEdited = ref(false);
const passwordEdited = ref(false);
const sshPasswordEdited = ref(false);
const sshPassphraseEdited = ref(false);
const hasStoredPassword = ref(false);
const hasStoredSshPassword = ref(false);
const hasStoredSshPassphrase = ref(false);
const closeAllContextMenus = () => {
  closeConnectionContextMenu();
  closeDatabaseContextMenu();
  closeTableContextMenu();
  closeRoutineContextMenu();
  closeDesignEntryContextMenu();
};

const visibleConnections = computed(() => store.visibleConnections);
const showStoredSqliteCipherPasswordMask = computed(() => !!editingConnectionId.value
  && sqliteConnectionForm.cipherHasStoredPassword
  && !sqliteCipherPasswordEdited.value
  && !sqliteConnectionForm.cipherPassword);
const showStoredPasswordMask = computed(() => !!editingConnectionId.value
  && hasStoredPassword.value
  && !passwordEdited.value
  && !createConnectionForm.password);
const showStoredSshPasswordMask = computed(() => !!editingConnectionId.value
  && hasStoredSshPassword.value
  && !sshPasswordEdited.value
  && !sshTunnelForm.password);
const showStoredSshPassphraseMask = computed(() => !!editingConnectionId.value
  && hasStoredSshPassphrase.value
  && !sshPassphraseEdited.value
  && !sshTunnelForm.passphrase);
const lockExistingUnencryptedSqliteCipherSettings = computed(() => !!editingConnectionId.value
  && createConnectionForm.provider === 'sqlite'
  && !sqliteConnectionForm.cipherEnabled);
const connectionContextMenuOptions = computed<DropdownOption[]>(() => {
  if (!connectionContextMenu.value) {
    return [];
  }

  const isConnected = store.isConnectionConnected(connectionContextMenu.value.connectionId);
  const options: DropdownOption[] = [];

  if (isConnected) {
    options.push({ label: '断开连接', key: 'disconnect' });
  }
  else {
    options.push({ label: '连接', key: 'connect' });
  }

  options.push({ type: 'divider', key: 'd-conn' });
  options.push({ label: '编辑连接', key: 'edit' });

  if (connectionContextMenu.value.provider === 'sqlserver') {
    options.push({ label: '用户管理', key: 'sqlserver-login-manager' });
  }

  if (connectionContextMenu.value.provider === 'sqlite' && connectionContextMenu.value.sqliteEncrypted !== false) {
    options.push({ label: '修改加密密码', key: 'sqlite-rekey' });
  }

  options.push({ type: 'divider', key: 'd-del' });
  options.push({ label: '删除连接', key: 'delete' });
  return options;
});
const databaseContextMenuOptions = computed<DropdownOption[]>(() => {
  if (!databaseContextMenu.value) {
    return [];
  }

  const isReadOnlyConnection = store.isConnectionReadOnly(databaseContextMenu.value.connectionId);

  const options: DropdownOption[] = [
    { label: '新建查询', key: 'new-query' },
    { label: '刷新数据库', key: 'refresh-database' },
    { label: '查看关系总览', key: 'graph-overview' },
    { label: '属性...', key: 'database-properties' },
  ];

  if (!isReadOnlyConnection) {
    options.splice(1, 0, { label: '新建表...', key: 'new-table' });
  }

  const connection = store.getConnectionInfo(databaseContextMenu.value.connectionId);
  const sqliteTools = connection ? getSqliteDatabaseTools(connection.provider) : [];
  if (sqliteTools.length > 0) {
    options.splice(4, 0, {
      label: '工具',
      key: 'tools',
      children: sqliteTools.map((tool) => ({
        label: tool.label,
        key: tool.key,
      })),
    });
  }

  return options;
});
const tableContextMenuOptions = computed<DropdownOption[]>(() => {
  if (!tableContextMenu.value) {
    return [];
  }

  const isReadOnlyConnection = store.isConnectionReadOnly(tableContextMenu.value.connectionId);

  const options: DropdownOption[] = [
    { label: '表设计...', key: 'table-design' },
    { label: '生成 Mock 数据...', key: 'mock-data' },
  ];

  if (!isReadOnlyConnection) {
    options.push({ label: '重命名表...', key: 'rename-table' });
  }

  options.push({
    label: '编写脚本为',
    key: 'table-script',
    children: [
      { label: 'CREATE 到', key: 'script:create' },
      { label: 'DROP 到', key: 'script:drop' },
      { label: 'SELECT 到', key: 'script:select' },
      { label: 'INSERT 到', key: 'script:insert' },
      { label: 'UPDATE 到', key: 'script:update' },
      { label: 'DELETE 到', key: 'script:delete' },
    ],
  });

  return options;
});
const routineContextMenuOptions = computed<DropdownOption[]>(() => {
  if (!routineContextMenu.value) {
    return [];
  }

  return [
    { label: '执行...', key: 'execute-routine' },
    { label: '修改...', key: 'edit-routine' },
    { label: '重命名...', key: 'rename-routine' },
    { label: '删除', key: 'delete-routine' },
  ];
});
const designEntryContextMenuOptions = computed<DropdownOption[]>(() => {
  if (!designEntryContextMenu.value) {
    return [];
  }

  if (store.isTableConnectionReadOnly(designEntryContextMenu.value.tableKey)) {
    return [];
  }

  return [
    { label: designEntryContextMenu.value.kind === 'column' ? '删除字段' : '删除索引', key: 'delete-design-entry' },
  ];
});

function beginSqliteCipherPasswordEdit() {
  if (!showStoredSqliteCipherPasswordMask.value) {
    return;
  }

  sqliteCipherPasswordEdited.value = true;
  sqliteConnectionForm.cipherPassword = '';
}

function beginPasswordEdit() {
  if (!showStoredPasswordMask.value) {
    return;
  }

  passwordEdited.value = true;
  createConnectionForm.password = '';
}

function beginSshPasswordEdit() {
  if (!showStoredSshPasswordMask.value) {
    return;
  }

  sshPasswordEdited.value = true;
  sshTunnelForm.password = '';
}

function beginSshPassphraseEdit() {
  if (!showStoredSshPassphraseMask.value) {
    return;
  }

  sshPassphraseEdited.value = true;
  sshTunnelForm.passphrase = '';
}

const providerOptions = [
  { label: 'SQL Server', value: 'sqlserver' },
  { label: 'MySQL', value: 'mysql' },
  { label: 'PostgreSQL', value: 'postgresql' },
  { label: 'SQLite', value: 'sqlite' },
];

const sqliteOpenModeOptions = [
  { label: '读写', value: 'readwrite' },
  { label: '只读', value: 'readonly' },
];
const sshAuthenticationOptions = [
  { label: '密码', value: 'password' },
  { label: '公钥', value: 'publicKey' },
];
const sqliteCipherKeyFormatOptions = [
  { label: '口令', value: 'passphrase' },
  { label: '十六进制密钥', value: 'hex' },
];
const sqliteCipherUseHmacOptions = [
  { label: '跟随默认值', value: 'default' },
  { label: '启用 HMAC', value: 'enabled' },
  { label: '禁用 HMAC', value: 'disabled' },
];
const sqliteCipherKdfAlgorithmOptions = [
  { label: '跟随默认值', value: '' },
  { label: 'PBKDF2_HMAC_SHA1', value: 'PBKDF2_HMAC_SHA1' },
  { label: 'PBKDF2_HMAC_SHA256', value: 'PBKDF2_HMAC_SHA256' },
  { label: 'PBKDF2_HMAC_SHA512', value: 'PBKDF2_HMAC_SHA512' },
];
const sqliteCipherHmacAlgorithmOptions = [
  { label: '跟随默认值', value: '' },
  { label: 'HMAC_SHA1', value: 'HMAC_SHA1' },
  { label: 'HMAC_SHA256', value: 'HMAC_SHA256' },
  { label: 'HMAC_SHA512', value: 'HMAC_SHA512' },
];
const sqlServerAuthenticationModeOptions = computed(() => createConnectionForm.provider === 'sqlserver'
  ? [
      { label: '账号密码', value: 'password' },
      { label: 'Windows 身份验证', value: 'windows' },
    ]
  : [
      { label: createConnectionForm.provider === 'sqlite' ? '本地文件' : '账号密码', value: 'password' },
    ]);

const hostPlaceholder = computed(() => {
  switch (createConnectionForm.provider) {
    case 'sqlserver':
      return '例如 .\\sqlexpress 或 127.0.0.1';
    case 'mysql':
      return '例如 127.0.0.1 或 localhost';
    case 'postgresql':
      return '例如 127.0.0.1 或 localhost';
    default:
      return '例如 127.0.0.1';
  }
});

function toggleConnection(connectionId: string) {
  const isExpanded = expandedConnections.value.includes(connectionId);
  if (isExpanded) {
    expandedConnections.value = expandedConnections.value.filter((id) => id !== connectionId);
  }
  else if (store.isConnectionConnected(connectionId) || store.isConnectionConnecting(connectionId)) {
    expandedConnections.value = [...expandedConnections.value, connectionId];
  }
}

async function handleConnectionDoubleClick(connectionId: string) {
  if (!store.isConnectionConnected(connectionId) && !store.isConnectionConnecting(connectionId)) {
    await store.connectConnection(connectionId);
  }

  if (!expandedConnections.value.includes(connectionId)) {
    expandedConnections.value = [...expandedConnections.value, connectionId];
  }
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
    // 设计加载错误会由 store 展示；这里保留展开态，方便用户直接重试。
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
function toggleRoutineGroup(connectionId: string, database: string, group: 'tables' | 'views' | 'synonyms' | 'sequences' | 'rules' | 'defaults' | 'types' | 'table-types' | 'database-triggers' | 'xml-schema-collections' | 'assemblies' | 'procedures' | 'functions') {
  const key = `${connectionId}:${database}:${group}`;
  expandedRoutineGroups.value = expandedRoutineGroups.value.includes(key)
    ? expandedRoutineGroups.value.filter((entry) => entry !== key)
    : [...expandedRoutineGroups.value, key];
}

function isRoutineGroupExpanded(connectionId: string, database: string, group: 'tables' | 'views' | 'synonyms' | 'sequences' | 'rules' | 'defaults' | 'types' | 'table-types' | 'database-triggers' | 'xml-schema-collections' | 'assemblies' | 'procedures' | 'functions') {
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

function treeAssemblies(connectionId: string, database: string) {
  return store.getAssemblies(connectionId, database);
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
function formatRoutineName(provider: DatabaseProviderType, routine: RoutineInfo) {
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

function providerTreeIcon(provider: DatabaseProviderType): Component {
  void provider;
  return IconServer;
}

function providerTreeClass(provider: DatabaseProviderType) {
  return `tree-icon-provider-${provider}`;
}

function treeToggleIcon(expanded: boolean): Component {
  return expanded ? IconChevronDown : IconChevronRight;
}

function routineTreeIcon(routineType: RoutineInfo['routineType']): Component {
  return routineType === 'Procedure' ? IconScript : IconFunction;
}

function isTableFocused(tableKey: string) {
  return store.activeTableTab?.tableKey === tableKey || store.activeDesignTab?.tableKey === tableKey;
}

async function openDesignSection(tableKey: string, section: TableDesignSection, entry: string | null = null) {
  await store.openTableDesign(tableKey);
  store.updateDesignTabSelection(`design:${tableKey}`, section, entry);
}

/** 打开设计树节点右键菜单，并在渲染后按真实尺寸修正位置。 */
function openDesignEntryContextMenu(event: MouseEvent, kind: 'column' | 'index', tableKey: string, name: string) {
  event.preventDefault();
  event.stopPropagation();
  closeAllContextMenus();
  designEntryContextMenu.value = {
    x: event.clientX,
    y: event.clientY,
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

function openExtendedCatalogObject(connectionId: string, database: string, objectType: CatalogObjectType, schema: string | null | undefined, name: string) {
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
  const assemblyCount = treeAssemblies(connectionId, database).length;
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
  if (assemblyCount > 0) {
    parts.push(`${assemblyCount} 程序集`);
  }

  return parts.join(' · ');
}

function objectRowMeta(object: TableSummary) {
  return object.objectType === 'view'
    ? '视图'
    : store.showTableRowCounts
      ? `${store.tableRowCountLabel(object.key) ?? '未统计'} rows`
      : null;
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

/** 打开连接右键菜单，并在渲染后按真实菜单尺寸回夹到视口内。 */
async function openConnectionContextMenu(event: MouseEvent, connectionId: string, connectionName: string, provider: DatabaseProviderType) {
  closeDatabaseContextMenu();
  closeTableContextMenu();
  const clickX = event.clientX;
  const clickY = event.clientY;

  let sqliteEncrypted: boolean | null = null;
  if (provider === 'sqlite') {
    try {
      const config = await store.getConnectionConfig(connectionId);
      sqliteEncrypted = config.sqlite.cipher.enabled;
    }
    catch {
      sqliteEncrypted = null;
    }
  }

  connectionContextMenu.value = {
    x: clickX,
    y: clickY,
    connectionId,
    connectionName,
    provider,
    sqliteEncrypted,
  };
}

function closeConnectionContextMenu() {
  connectionContextMenu.value = null;
}

function openSqlServerLoginManager() {
  if (!connectionContextMenu.value || connectionContextMenu.value.provider !== 'sqlserver') {
    return;
  }

  store.openSqlServerLoginManager(connectionContextMenu.value.connectionId);
  closeConnectionContextMenu();
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
    sqlServerConnectionForm.authenticationMode = config.sqlServer.authenticationMode;
    createConnectionForm.host = config.host;
    createConnectionForm.port = config.port ? String(config.port) : '';
    createConnectionForm.username = config.username ?? '';
    createConnectionForm.password = '';
    hasStoredPassword.value = config.hasPassword;
    passwordEdited.value = false;
    sqlServerConnectionForm.trustServerCertificate = config.sqlServer.trustServerCertificate;
    sshTunnelForm.enabled = config.sshTunnel.enabled;
    sshTunnelForm.authentication = config.sshTunnel.authentication;
    sshTunnelForm.host = config.sshTunnel.host ?? '';
    sshTunnelForm.port = config.sshTunnel.port ? String(config.sshTunnel.port) : '22';
    sshTunnelForm.username = config.sshTunnel.username ?? '';
    sshTunnelForm.password = '';
    hasStoredSshPassword.value = config.sshTunnel.hasPassword;
    sshPasswordEdited.value = false;
    sshTunnelForm.privateKeyPath = config.sshTunnel.privateKeyPath ?? '';
    sshTunnelForm.passphrase = '';
    hasStoredSshPassphrase.value = config.sshTunnel.hasPassphrase;
    sshPassphraseEdited.value = false;
    sqliteConnectionForm.openMode = config.sqlite.openMode ?? 'readwrite';
    sqliteConnectionForm.cipherEnabled = config.sqlite.cipher.enabled;
    sqliteConnectionForm.cipherHasStoredPassword = config.sqlite.cipher.hasPassword;
    sqliteCipherPasswordEdited.value = false;
    sqliteConnectionForm.cipherPassword = '';
    sqliteConnectionForm.cipherKeyFormat = config.sqlite.cipher.keyFormat;
    sqliteConnectionForm.cipherPageSize = config.sqlite.cipher.pageSize ? String(config.sqlite.cipher.pageSize) : '';
    sqliteConnectionForm.cipherKdfIter = config.sqlite.cipher.kdfIter ? String(config.sqlite.cipher.kdfIter) : '';
    sqliteConnectionForm.cipherCompatibility = config.sqlite.cipher.cipherCompatibility ? String(config.sqlite.cipher.cipherCompatibility) : '';
    sqliteConnectionForm.cipherPlaintextHeaderSize = config.sqlite.cipher.plaintextHeaderSize !== null && config.sqlite.cipher.plaintextHeaderSize !== undefined
      ? String(config.sqlite.cipher.plaintextHeaderSize)
      : '';
    sqliteConnectionForm.cipherSkipBytes = config.sqlite.cipher.skipBytes !== null && config.sqlite.cipher.skipBytes !== undefined
      ? String(config.sqlite.cipher.skipBytes)
      : '';
    sqliteConnectionForm.cipherUseHmac = config.sqlite.cipher.useHmac === true
      ? 'enabled'
      : config.sqlite.cipher.useHmac === false
        ? 'disabled'
        : 'default';
    sqliteConnectionForm.cipherKdfAlgorithm = config.sqlite.cipher.kdfAlgorithm ?? '';
    sqliteConnectionForm.cipherHmacAlgorithm = config.sqlite.cipher.hmacAlgorithm ?? '';
    activeConnectionSettingsTab.value = 'general';
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

async function openSqliteRekeyDialog() {
  if (!connectionContextMenu.value || connectionContextMenu.value.provider !== 'sqlite') {
    return;
  }

  sqliteRekeyLoading.value = true;
  sqliteRekeyError.value = null;

  try {
    const config = await store.getConnectionConfig(connectionContextMenu.value.connectionId);
    if (!config.sqlite.cipher.enabled) {
      store.showNotice('warning', '当前实现仅支持修改已加密 SQLite 数据库的密码。明文库加密/解密需要导出重写，暂未实现。');
      return;
    }

    sqliteRekeyTarget.value = {
      connectionId: config.id,
      connectionName: config.name,
      currentEncrypted: config.sqlite.cipher.enabled,
    };
    sqliteRekeyForm.currentPassword = '';
    sqliteRekeyForm.currentKeyFormat = config.sqlite.cipher.keyFormat;
    sqliteRekeyForm.newPassword = '';
    sqliteRekeyForm.newKeyFormat = config.sqlite.cipher.keyFormat;
    sqliteRekeyVisible.value = true;
  }
  catch (error) {
    sqliteRekeyError.value = error instanceof Error ? error.message : 'SQLite 加密配置加载失败';
  }
  finally {
    sqliteRekeyLoading.value = false;
    closeConnectionContextMenu();
  }
}

function closeSqliteRekeyDialog() {
  sqliteRekeyVisible.value = false;
  sqliteRekeyLoading.value = false;
  sqliteRekeyError.value = null;
  sqliteRekeyTarget.value = null;
  sqliteRekeyForm.currentPassword = '';
  sqliteRekeyForm.currentKeyFormat = 'passphrase';
  sqliteRekeyForm.newPassword = '';
  sqliteRekeyForm.newKeyFormat = 'passphrase';
}

function handleSqliteRekeyVisibleChange(value: boolean) {
  if (!value) {
    closeSqliteRekeyDialog();
  }
}

async function submitSqliteRekey() {
  if (!sqliteRekeyTarget.value) {
    return;
  }

  sqliteRekeyLoading.value = true;
  sqliteRekeyError.value = null;

  try {
    await store.rekeySqliteDatabase({
      connectionId: sqliteRekeyTarget.value.connectionId,
      currentPassword: sqliteRekeyForm.currentPassword.trim() ? sqliteRekeyForm.currentPassword : null,
      currentKeyFormat: sqliteRekeyForm.currentPassword.trim() ? sqliteRekeyForm.currentKeyFormat : null,
      newPassword: sqliteRekeyForm.newPassword,
      newKeyFormat: sqliteRekeyForm.newKeyFormat,
    });
    closeSqliteRekeyDialog();
  }
  catch (error) {
    sqliteRekeyError.value = error instanceof Error ? error.message : 'SQLite 加密密钥更新失败';
  }
  finally {
    sqliteRekeyLoading.value = false;
  }
}

function closeDeleteConnectionConfirm() {
  deleteConnectionConfirm.value = null;
}

/** 打开数据库右键菜单，并在渲染后按真实菜单尺寸修正位置。 */
function openDatabaseContextMenu(event: MouseEvent, connectionId: string, database: string) {
  closeConnectionContextMenu();
  closeTableContextMenu();
  databaseContextMenu.value = {
    x: event.clientX,
    y: event.clientY,
    connectionId,
    database,
  };
}

function closeDatabaseContextMenu() {
  databaseContextMenu.value = null;
}

/** 打开表右键菜单，并在渲染后按真实菜单尺寸修正位置。 */
function openTableContextMenu(event: MouseEvent, connectionId: string, database: string, table: TableSummary) {
  closeConnectionContextMenu();
  closeDatabaseContextMenu();
  tableContextMenu.value = {
    x: event.clientX,
    y: event.clientY,
    connectionId,
    database,
    table,
  };
}

function closeTableContextMenu() {
  tableContextMenu.value = null;
}

/** 打开例程右键菜单，并在渲染后按真实菜单尺寸修正位置。 */
function openRoutineContextMenu(event: MouseEvent, connectionId: string, database: string, provider: DatabaseProviderType, routine: RoutineInfo) {
  closeAllContextMenus();
  routineContextMenu.value = {
    x: event.clientX,
    y: event.clientY,
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
function buildRenameRoutineScript(provider: DatabaseProviderType, routine: RoutineInfo, newName: string): string {
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
    // MySQL 不支持直接重命名存储过程或函数。
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

function providerForConnection(connectionId: string): DatabaseProviderType {
  return store.getConnectionInfo(connectionId)?.provider ?? 'sqlserver';
}

function openTableDesignFromContextMenu() {
  if (!tableContextMenu.value) {
    return;
  }

  store.openTableDesign(tableContextMenu.value.table.key);
  closeTableContextMenu();
}

function openRoutineSourceFromContextMenu() {
  if (!routineContextMenu.value) {
    return;
  }

  store.openRoutineSource(
    routineContextMenu.value.connectionId,
    routineContextMenu.value.database,
    routineContextMenu.value.routine.schema ?? null,
    routineContextMenu.value.routine.name,
    routineContextMenu.value.routine.routineType,
  );
  closeRoutineContextMenu();
}

/** 打开执行存储过程/函数对话框，无参数时直接打开 SQL Tab。 */
/** 执行存储过程/函数：无参数直接打开 SQL Tab 执行，有参数弹对话框填写。 */
function openExecuteRoutineDialog() {
  if (!routineContextMenu.value) {
    return;
  }

  const ctx = routineContextMenu.value;
  closeRoutineContextMenu();

  const inputParams = ctx.routine.parameters.filter((p) => p.direction !== 'RETURN_VALUE');

  if (inputParams.length === 0) {
    // 无参数，直接创建 SQL Tab 并执行
    const qualifiedName = buildRoutineQualifiedName(ctx.provider, ctx.routine);
    const sql = buildNoParamExecuteSql(ctx.provider, ctx.routine, qualifiedName);
    void store.openSqlTabWithContext({
      connectionId: ctx.connectionId,
      database: ctx.database,
      sqlText: sql,
      savedSqlText: sql,
      execute: true,
      skipRoutineCheck: true,
    });
    return;
  }

  // 有参数，直接弹对话框（不创建 SQL Tab）
  store.pendingRoutineExec = {
    tabId: null,
    connectionId: ctx.connectionId,
    database: ctx.database,
    provider: ctx.provider,
    routine: ctx.routine,
  };
}

function buildRoutineQualifiedName(provider: DatabaseProviderType, routine: RoutineInfo): string {
  if (!routine.schema || provider === 'mysql') {
    return quoteIdentifier(provider, routine.name);
  }

  return `${quoteIdentifier(provider, routine.schema)}.${quoteIdentifier(provider, routine.name)}`;
}

function buildNoParamExecuteSql(provider: DatabaseProviderType, routine: RoutineInfo, qualifiedName: string): string {
  if (routine.routineType === 'Procedure') {
    if (provider === 'sqlserver') {
      return `EXEC ${qualifiedName};`;
    }

    return `CALL ${qualifiedName}();`;
  }

  if (provider === 'postgresql') {
    return `SELECT * FROM ${qualifiedName}();`;
  }

  return `SELECT ${qualifiedName}();`;
}

function openDeleteRoutineConfirm() {
  if (!routineContextMenu.value) {
    return;
  }

  deleteRoutineConfirm.value = { ...routineContextMenu.value };
  closeRoutineContextMenu();
}

function handleConnectionContextMenuShow(value: boolean) {
  if (!value) {
    closeConnectionContextMenu();
  }
}

function handleDatabaseContextMenuShow(value: boolean) {
  if (!value) {
    closeDatabaseContextMenu();
  }
}

function handleTableContextMenuShow(value: boolean) {
  if (!value) {
    closeTableContextMenu();
  }
}

function handleRoutineContextMenuShow(value: boolean) {
  if (!value) {
    closeRoutineContextMenu();
  }
}

function handleDesignEntryContextMenuShow(value: boolean) {
  if (!value) {
    closeDesignEntryContextMenu();
  }
}

function handleConnectionContextMenuSelect(key: string | number) {
  switch (key) {
    case 'connect':
      if (connectionContextMenu.value) {
        const connId = connectionContextMenu.value.connectionId;
        closeConnectionContextMenu();
        void store.connectConnection(connId);
        if (!expandedConnections.value.includes(connId)) {
          expandedConnections.value = [...expandedConnections.value, connId];
        }
      }
      return;
    case 'disconnect':
      if (connectionContextMenu.value) {
        const connId = connectionContextMenu.value.connectionId;
        closeConnectionContextMenu();
        store.disconnectConnection(connId);
      }
      return;
    case 'edit':
      void openEditConnectionDialog();
      return;
    case 'sqlserver-login-manager':
      openSqlServerLoginManager();
      return;
    case 'sqlite-rekey':
      void openSqliteRekeyDialog();
      return;
    case 'delete':
      openDeleteConnectionConfirm();
      return;
    default:
      return;
  }
}

function handleDatabaseContextMenuSelect(key: string | number) {
  if (typeof key === 'string' && databaseContextMenu.value) {
    const provider = store.getConnectionInfo(databaseContextMenu.value.connectionId)?.provider ?? null;
    if (provider && findSqliteDatabaseTool(provider, key)) {
      void openDatabaseToolQuery(key);
      return;
    }
  }

  switch (key) {
    case 'new-query':
      openDatabaseSqlQuery();
      return;
    case 'new-table':
      openCreateTableQuery();
      return;
    case 'refresh-database':
      refreshDatabaseFromMenu();
      return;
    case 'graph-overview':
      openDatabaseGraphOverview();
      return;
    case 'database-properties':
      openDatabaseProperties();
      return;
    default:
      return;
  }
}

function handleTableContextMenuSelect(key: string | number) {
  switch (key) {
    case 'table-design':
      openTableDesignFromContextMenu();
      return;
    case 'mock-data':
      void openTableMockDataDialog();
      return;
    case 'rename-table':
      openRenameTableQuery();
      return;
    case 'script:create':
      void openTableScript('create');
      return;
    case 'script:drop':
      void openTableScript('drop');
      return;
    case 'script:select':
      void openTableScript('select');
      return;
    case 'script:insert':
      void openTableScript('insert');
      return;
    case 'script:update':
      void openTableScript('update');
      return;
    case 'script:delete':
      void openTableScript('delete');
      return;
    default:
      return;
  }
}

function handleRoutineContextMenuSelect(key: string | number) {
  switch (key) {
    case 'execute-routine':
      openExecuteRoutineDialog();
      return;
    case 'edit-routine':
      openRoutineSourceFromContextMenu();
      return;
    case 'rename-routine':
      openRenameRoutineDialog();
      return;
    case 'delete-routine':
      openDeleteRoutineConfirm();
      return;
    default:
      return;
  }
}

function handleDesignEntryContextMenuSelect(key: string | number) {
  if (key === 'delete-design-entry') {
    void deleteDesignEntryFromSidebar();
  }
}

function quoteIdentifier(provider: DatabaseProviderType, value: string) {
  if (provider === 'mysql') {
    return `\`${value.replace(/`/g, '``')}\``;
  }

  if (provider === 'postgresql' || provider === 'sqlite') {
    return `"${value.replace(/"/g, '""')}"`;
  }

  return `[${value.replace(/\]/g, ']]')}]`;
}

function formatSchemaTable(provider: DatabaseProviderType, table: TableSummary) {
  const parts = [];
  if (table.schema) {
    parts.push(quoteIdentifier(provider, table.schema));
  }
  parts.push(quoteIdentifier(provider, table.name));
  return parts.join('.');
}

function buildBatchPrefix(provider: DatabaseProviderType, database: string) {
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

function buildBatchSuffix(provider: DatabaseProviderType) {
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
    // 表结构尚未加载时，回退到 SQL 上下文里的列信息。
  }

  return (sqlContextTable?.columns ?? []).map((column) => ({
    name: column,
    type: undefined,
    isNullable: true,
  }));
}

function buildSelectScript(provider: DatabaseProviderType, database: string, table: TableSummary, columns: Array<{ name: string }>) {
  const selectLines = columns.length
    ? columns.map((column, index) => `${index === 0 ? 'SELECT' : '      ,'} ${quoteIdentifier(provider, column.name)}`)
    : ['SELECT *'];
  return `${buildBatchPrefix(provider, database)}${selectLines.join('\n')}\n  FROM ${formatSchemaTable(provider, table)}${buildBatchSuffix(provider)}`;
}

function buildDeleteScript(provider: DatabaseProviderType, database: string, table: TableSummary) {
  return `${buildBatchPrefix(provider, database)}DELETE FROM ${formatSchemaTable(provider, table)}\n      WHERE <搜索条件,,>${buildBatchSuffix(provider)}`;
}

function buildDropScript(provider: DatabaseProviderType, database: string, table: TableSummary) {
  return `${buildBatchPrefix(provider, database)}DROP TABLE ${formatSchemaTable(provider, table)}${buildBatchSuffix(provider)}`;
}

function buildInsertScript(provider: DatabaseProviderType, database: string, table: TableSummary, columns: Array<{ name: string; type?: string }>) {
  const names = columns.length ? columns : [{ name: 'Column1', type: 'type' }];
  const columnLines = names.map((column, index) => `${index === 0 ? '           (' : '           ,'}${quoteIdentifier(provider, column.name)}`).join('\n');
  const valueLines = names.map((column, index) => `${index === 0 ? '           (' : '           ,'}<${column.name}, ${column.type ?? 'type'},>`).join('\n');
  return `${buildBatchPrefix(provider, database)}INSERT INTO ${formatSchemaTable(provider, table)}\n${columnLines})\n     VALUES\n${valueLines})${buildBatchSuffix(provider)}`;
}

function buildUpdateScript(provider: DatabaseProviderType, database: string, table: TableSummary, columns: Array<{ name: string; type?: string }>) {
  const names = columns.length ? columns : [{ name: 'Column1', type: 'type' }];
  const setLines = names.map((column, index) => `${index === 0 ? '   SET' : '      ,'} ${quoteIdentifier(provider, column.name)} = <${column.name}, ${column.type ?? 'type'},>`).join('\n');
  return `${buildBatchPrefix(provider, database)}UPDATE ${formatSchemaTable(provider, table)}\n${setLines}\n WHERE <搜索条件,,>${buildBatchSuffix(provider)}`;
}

function buildCreateScript(provider: DatabaseProviderType, database: string, table: TableSummary, columns: Array<{ name: string; type?: string; isNullable?: boolean }>) {
  const names = columns.length ? columns : [{ name: 'Column1', type: 'int', isNullable: false }];
  const body = names.map((column, index) => `    ${index === 0 ? '' : ','}${quoteIdentifier(provider, column.name)} ${column.type ?? 'nvarchar(255)'} ${column.isNullable === false ? 'NOT NULL' : 'NULL'}`).join('\n');
  return `${buildBatchPrefix(provider, database)}CREATE TABLE ${formatSchemaTable(provider, table)}\n(\n${body}\n)${buildBatchSuffix(provider)}`;
}

function buildRenameTableScript(provider: DatabaseProviderType, database: string, table: TableSummary, targetName: string) {
  const currentName = formatSchemaTable(provider, table);

  if (provider === 'sqlserver') {
    return `${buildBatchPrefix(provider, database)}EXEC sp_rename '${currentName.replace(/'/g, "''")}', '${targetName.replace(/'/g, "''")}'${buildBatchSuffix(provider)}`;
  }

  if (provider === 'postgresql' || provider === 'sqlite') {
    return `${buildBatchPrefix(provider, database)}ALTER TABLE ${currentName}\n  RENAME TO ${quoteIdentifier(provider, targetName)}${buildBatchSuffix(provider)}`;
  }

  return `${buildBatchPrefix(provider, database)}RENAME TABLE ${currentName}\n         TO ${table.schema ? `${quoteIdentifier(provider, table.schema)}.` : ''}${quoteIdentifier(provider, targetName)}${buildBatchSuffix(provider)}`;
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
  store.openCreateTableDesign(connectionId, database);
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

function openDatabaseProperties() {
  if (!databaseContextMenu.value) {
    return;
  }

  const { connectionId, database } = databaseContextMenu.value;
  closeDatabaseContextMenu();
  store.openDatabaseProperties(connectionId, database);
}

/**
 * 打开 SQLite 数据库工具对应的 SQL 标签页。
 * 当前阶段只预填 SQL，不自动执行，保持工具行为一致且可审阅。
 */
async function openDatabaseToolQuery(toolKey: string) {
  if (!databaseContextMenu.value) {
    return;
  }

  const { connectionId, database } = databaseContextMenu.value;
  const provider = store.getConnectionInfo(connectionId)?.provider ?? null;
  closeDatabaseContextMenu();

  if (!provider) {
    return;
  }

  const toolSql = buildSqliteDatabaseToolSql(provider, database, toolKey);
  if (!toolSql) {
    return;
  }

  await store.openSqlTabWithContext({
    connectionId,
    database,
    displayName: toolSql.tool.label,
    sqlText: toolSql.sqlText,
    savedSqlText: toolSql.sqlText,
  });
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
  sqlServerConnectionForm.authenticationMode = 'password';
  createConnectionForm.host = '';
  createConnectionForm.port = '1433';
  createConnectionForm.username = '';
  createConnectionForm.password = '';
  sqlServerConnectionForm.trustServerCertificate = true;
  hasStoredPassword.value = false;
  passwordEdited.value = false;
  sshTunnelForm.enabled = false;
  sshTunnelForm.authentication = 'password';
  sshTunnelForm.host = '';
  sshTunnelForm.port = '22';
  sshTunnelForm.username = '';
  sshTunnelForm.password = '';
  sshTunnelForm.privateKeyPath = '';
  sshTunnelForm.passphrase = '';
  sqliteConnectionForm.openMode = 'readwrite';
  hasStoredSshPassword.value = false;
  sshPasswordEdited.value = false;
  hasStoredSshPassphrase.value = false;
  sshPassphraseEdited.value = false;
  sqliteConnectionForm.cipherEnabled = false;
  sqliteConnectionForm.cipherHasStoredPassword = false;
  sqliteCipherPasswordEdited.value = false;
  sqliteConnectionForm.cipherPassword = '';
  sqliteConnectionForm.cipherKeyFormat = 'passphrase';
  sqliteConnectionForm.cipherPageSize = '';
  sqliteConnectionForm.cipherKdfIter = '';
  sqliteConnectionForm.cipherCompatibility = '';
  sqliteConnectionForm.cipherPlaintextHeaderSize = '';
  sqliteConnectionForm.cipherSkipBytes = '';
  sqliteConnectionForm.cipherUseHmac = 'default';
  sqliteConnectionForm.cipherKdfAlgorithm = '';
  sqliteConnectionForm.cipherHmacAlgorithm = '';
  activeConnectionSettingsTab.value = 'general';
  createConnectionError.value = null;
}

function openCreateConnectionDialog() {
  closeAllContextMenus();
  resetCreateConnectionForm();
  createConnectionVisible.value = true;
}

function handleProviderChange(provider: DatabaseProviderType) {
  createConnectionForm.provider = provider;
  createConnectionForm.port = provider === 'sqlserver' ? '1433' : provider === 'postgresql' ? '5432' : provider === 'mysql' ? '3306' : '';
  if (provider === 'mysql' && sqlServerConnectionForm.authenticationMode === 'windows') {
    sqlServerConnectionForm.authenticationMode = 'password';
  }

  if (provider === 'postgresql' && sqlServerConnectionForm.authenticationMode === 'windows') {
    sqlServerConnectionForm.authenticationMode = 'password';
  }

  if (provider === 'sqlite') {
    sqlServerConnectionForm.authenticationMode = 'password';
    createConnectionForm.username = '';
    createConnectionForm.password = '';
    sshTunnelForm.enabled = false;
    activeConnectionSettingsTab.value = 'general';
  }

  if (provider !== 'sqlite' && activeConnectionSettingsTab.value === 'cipher') {
    activeConnectionSettingsTab.value = 'general';
  }
}

function toNullableNumber(value: string) {
  const normalizedValue = value.trim();
  if (!normalizedValue) {
    return null;
  }

  const parsedValue = Number(normalizedValue);
  return Number.isFinite(parsedValue) ? parsedValue : null;
}

function toNullableSqliteCipherUseHmac(value: 'default' | 'enabled' | 'disabled') {
  if (value === 'enabled') {
    return true;
  }

  if (value === 'disabled') {
    return false;
  }

  return null;
}

function buildSqliteCipherPayload() {
  if (createConnectionForm.provider !== 'sqlite') {
    return {
      enabled: false,
      password: null,
      keyFormat: 'passphrase' as const,
      pageSize: null,
      kdfIter: null,
      cipherCompatibility: null,
      plaintextHeaderSize: null,
      skipBytes: null,
      useHmac: null,
      kdfAlgorithm: null,
      hmacAlgorithm: null,
    };
  }

  return {
    enabled: sqliteConnectionForm.cipherEnabled,
    password: sqliteConnectionForm.cipherEnabled && sqliteConnectionForm.cipherPassword.trim()
      ? sqliteConnectionForm.cipherPassword
      : null,
    keyFormat: sqliteConnectionForm.cipherKeyFormat,
    pageSize: sqliteConnectionForm.cipherEnabled ? toNullableNumber(sqliteConnectionForm.cipherPageSize) : null,
    kdfIter: sqliteConnectionForm.cipherEnabled ? toNullableNumber(sqliteConnectionForm.cipherKdfIter) : null,
    cipherCompatibility: sqliteConnectionForm.cipherEnabled ? toNullableNumber(sqliteConnectionForm.cipherCompatibility) : null,
    plaintextHeaderSize: sqliteConnectionForm.cipherEnabled ? toNullableNumber(sqliteConnectionForm.cipherPlaintextHeaderSize) : null,
    skipBytes: sqliteConnectionForm.cipherEnabled ? toNullableNumber(sqliteConnectionForm.cipherSkipBytes) : null,
    useHmac: sqliteConnectionForm.cipherEnabled ? toNullableSqliteCipherUseHmac(sqliteConnectionForm.cipherUseHmac) : null,
    kdfAlgorithm: sqliteConnectionForm.cipherEnabled && sqliteConnectionForm.cipherKdfAlgorithm
      ? sqliteConnectionForm.cipherKdfAlgorithm
      : null,
    hmacAlgorithm: sqliteConnectionForm.cipherEnabled && sqliteConnectionForm.cipherHmacAlgorithm
      ? sqliteConnectionForm.cipherHmacAlgorithm
      : null,
  };
}

async function browseSshPrivateKeyFile() {
  createConnectionError.value = null;

  try {
    const filePath = await store.pickSshPrivateKeyFile(sshTunnelForm.privateKeyPath || null);
    if (filePath) {
      sshTunnelForm.privateKeyPath = filePath;
    }
  }
  catch (error) {
    createConnectionError.value = error instanceof Error ? error.message : 'SSH 私钥选择失败';
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
      host: createConnectionForm.host,
      port: createConnectionForm.provider === 'sqlite' || !Number.isFinite(Number(createConnectionForm.port)) ? null : Number(createConnectionForm.port),
      username: createConnectionForm.provider === 'sqlite' || sqlServerConnectionForm.authenticationMode === 'windows' ? null : createConnectionForm.username,
      password: createConnectionForm.provider === 'sqlite' || sqlServerConnectionForm.authenticationMode === 'windows' ? null : createConnectionForm.password,
      sqlServer: {
        authenticationMode: sqlServerConnectionForm.authenticationMode,
        trustServerCertificate: sqlServerConnectionForm.trustServerCertificate,
      },
      mySql: {},
      postgreSql: {},
      sqlite: {
        openMode: createConnectionForm.provider === 'sqlite' ? sqliteConnectionForm.openMode : null,
        cipher: buildSqliteCipherPayload(),
      },
      sshTunnel: {
        enabled: createConnectionForm.provider !== 'sqlite' && sshTunnelForm.enabled,
        authentication: sshTunnelForm.authentication,
        host: createConnectionForm.provider === 'sqlite' || !sshTunnelForm.enabled ? null : sshTunnelForm.host,
        port: createConnectionForm.provider === 'sqlite' || !sshTunnelForm.enabled || !Number.isFinite(Number(sshTunnelForm.port)) ? null : Number(sshTunnelForm.port),
        username: createConnectionForm.provider === 'sqlite' || !sshTunnelForm.enabled ? null : sshTunnelForm.username,
        password: createConnectionForm.provider === 'sqlite' || !sshTunnelForm.enabled || sshTunnelForm.authentication === 'publicKey' ? null : sshTunnelForm.password,
        privateKeyPath: createConnectionForm.provider === 'sqlite' || !sshTunnelForm.enabled || sshTunnelForm.authentication !== 'publicKey' ? null : (sshTunnelForm.privateKeyPath || null),
        passphrase: createConnectionForm.provider === 'sqlite' || !sshTunnelForm.enabled || sshTunnelForm.authentication !== 'publicKey' ? null : (sshTunnelForm.passphrase || null),
      },
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
      host: createConnectionForm.host,
      port: createConnectionForm.provider === 'sqlite' || !Number.isFinite(Number(createConnectionForm.port)) ? null : Number(createConnectionForm.port),
      username: createConnectionForm.provider === 'sqlite' || sqlServerConnectionForm.authenticationMode === 'windows' ? null : createConnectionForm.username,
      password: createConnectionForm.provider === 'sqlite' || sqlServerConnectionForm.authenticationMode === 'windows' ? null : (createConnectionForm.password || null),
      sqlServer: {
        authenticationMode: sqlServerConnectionForm.authenticationMode,
        trustServerCertificate: sqlServerConnectionForm.trustServerCertificate,
      },
      mySql: {},
      postgreSql: {},
      sqlite: {
        openMode: createConnectionForm.provider === 'sqlite' ? sqliteConnectionForm.openMode : null,
        cipher: buildSqliteCipherPayload(),
      },
      sshTunnel: {
        enabled: createConnectionForm.provider !== 'sqlite' && sshTunnelForm.enabled,
        authentication: sshTunnelForm.authentication,
        host: createConnectionForm.provider === 'sqlite' || !sshTunnelForm.enabled ? null : sshTunnelForm.host,
        port: createConnectionForm.provider === 'sqlite' || !sshTunnelForm.enabled || !Number.isFinite(Number(sshTunnelForm.port)) ? null : Number(sshTunnelForm.port),
        username: createConnectionForm.provider === 'sqlite' || !sshTunnelForm.enabled ? null : sshTunnelForm.username,
        password: createConnectionForm.provider === 'sqlite' || !sshTunnelForm.enabled || sshTunnelForm.authentication === 'publicKey' ? null : (sshTunnelForm.password || null),
        privateKeyPath: createConnectionForm.provider === 'sqlite' || !sshTunnelForm.enabled || sshTunnelForm.authentication !== 'publicKey' ? null : (sshTunnelForm.privateKeyPath || null),
        passphrase: createConnectionForm.provider === 'sqlite' || !sshTunnelForm.enabled || sshTunnelForm.authentication !== 'publicKey' ? null : (sshTunnelForm.passphrase || null),
      },
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

// 断开连接后折叠对应的树节点
watch(() => store.connectedConnectionIds, (newIds) => {
  expandedConnections.value = expandedConnections.value.filter((id) => newIds.has(id));
});
</script>

<template>
  <div class="sidebar-shell">
    <div class="sidebar-toolbar compact-panel">
      <strong>连接与表</strong>
      <div class="sidebar-toolbar-actions">
        <NButton size="small" tertiary type="primary" @click="openCreateConnectionDialog">新建连接</NButton>
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
          <div class="tree-row tree-row-connection" @contextmenu.prevent.stop="openConnectionContextMenu($event, connection.id, connection.name, connection.provider)">
            <button class="tree-toggle" type="button" @click="toggleConnection(connection.id)">
              <NIcon size="12"><component :is="treeToggleIcon(expandedConnections.includes(connection.id))" /></NIcon>
            </button>
            <button class="tree-node-main tree-node-main-connection" type="button" @click="toggleConnection(connection.id)" @dblclick.stop="handleConnectionDoubleClick(connection.id)">
              <NIcon class="tree-icon tree-icon-provider" :class="[store.isConnectionConnecting(connection.id) ? 'tree-icon-connecting' : providerTreeClass(connection.provider), { 'tree-icon-disconnected': !store.isConnectionConnected(connection.id) && !store.isConnectionConnecting(connection.id) }]" size="12"><component :is="store.isConnectionConnecting(connection.id) ? IconLoader2 : providerTreeIcon(connection.provider)" /></NIcon>
              <span class="tree-label-stack">
                <span class="tree-label-main">{{ connection.name }}</span>
                <span class="tree-label-meta">{{ connection.host }}<template v-if="connection.port">:{{ connection.port }}</template></span>
              </span>
            </button>
          </div>

          <div v-if="expandedConnections.includes(connection.id)" class="tree-children tree-children-animated" @contextmenu.prevent.stop>
            <template v-if="store.isConnectionConnecting(connection.id)">
              <div class="tree-row tree-row-empty">
                <span class="tree-label-meta">正在连接…</span>
              </div>
            </template>
            <template v-else-if="connection.error">
              <div class="tree-row tree-row-empty tree-row-error">
                <span class="tree-label-meta">{{ connection.error }}</span>
              </div>
            </template>
            <template v-else-if="!store.isConnectionConnected(connection.id)">
              <div class="tree-row tree-row-empty">
                <span class="tree-label-meta">双击连接或右键选择"连接"</span>
              </div>
            </template>
            <template v-else>
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
                            <span v-if="objectRowMeta(table)" class="tree-label-meta">{{ objectRowMeta(table) }}</span>
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
                            <span v-if="objectRowMeta(view)" class="tree-label-meta">{{ objectRowMeta(view) }}</span>
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

                <div v-if="treeAssemblies(connection.id, database.name).length > 0" class="tree-node">
                  <div class="tree-row tree-row-group">
                    <button class="tree-toggle" type="button" @click="toggleRoutineGroup(connection.id, database.name, 'assemblies')">
                      <NIcon size="12"><component :is="treeToggleIcon(isRoutineGroupExpanded(connection.id, database.name, 'assemblies'))" /></NIcon>
                    </button>
                    <button type="button" class="tree-node-main" @click="toggleRoutineGroup(connection.id, database.name, 'assemblies')">
                      <NIcon class="tree-icon tree-icon-function" size="12"><IconPackage /></NIcon>
                      <span class="tree-label-main">程序集</span>
                      <span class="tree-row-count">{{ treeAssemblies(connection.id, database.name).length }}</span>
                    </button>
                  </div>
                  <div v-if="isRoutineGroupExpanded(connection.id, database.name, 'assemblies')" class="tree-children tree-children-animated">
                    <button v-for="item in treeAssemblies(connection.id, database.name)" :key="item.name" type="button" class="tree-leaf-row tree-leaf-row-catalog" :title="item.clrName" @dblclick.stop.prevent="openExtendedCatalogObject(connection.id, database.name, 'assembly', null, item.name)">
                      <NIcon class="tree-icon tree-icon-leaf" size="12"><IconPackage /></NIcon>
                      <span class="tree-leaf-stack">
                        <span class="tree-leaf-name">{{ item.name }}</span>
                        <span class="tree-leaf-type">{{ item.permissionSet }}</span>
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
            </template>
          </div>
        </div>
      </template>

      <NEmpty v-else description="没有匹配的连接或表" size="large" />
      </NSpin>
    </div>

    <ContextDropdown
      :show="!!connectionContextMenu"
      :x="connectionContextMenu?.x ?? 0"
      :y="connectionContextMenu?.y ?? 0"
      :options="connectionContextMenuOptions"
      @update:show="handleConnectionContextMenuShow"
      @select="handleConnectionContextMenuSelect"
    />

    <ContextDropdown
      :show="!!databaseContextMenu"
      :x="databaseContextMenu?.x ?? 0"
      :y="databaseContextMenu?.y ?? 0"
      :options="databaseContextMenuOptions"
      @update:show="handleDatabaseContextMenuShow"
      @select="handleDatabaseContextMenuSelect"
    />

    <ContextDropdown
      :show="!!tableContextMenu"
      :x="tableContextMenu?.x ?? 0"
      :y="tableContextMenu?.y ?? 0"
      :options="tableContextMenuOptions"
      @update:show="handleTableContextMenuShow"
      @select="handleTableContextMenuSelect"
    />

    <NModal v-model:show="createConnectionVisible" preset="card" style="width: min(500px, 94vw)" :title="editingConnectionId ? '编辑连接' : '新建连接'">
      <div class="connection-dialog-form">
        <NTabs v-model:value="activeConnectionSettingsTab" type="line" animated>
          <NTabPane name="general" tab="常规">
            <div class="connection-dialog-form connection-dialog-form-section">
              <div class="connection-parameter-grid">
                <div class="connection-parameter-row">
                  <div class="connection-parameter-meta" title="连接显示名称。">
                    <span class="connection-parameter-label">连接名称</span>
                    <span class="connection-parameter-key">name</span>
                  </div>
                  <NInput v-model:value="createConnectionForm.name" class="connection-parameter-control" placeholder="输入连接名称" />
                </div>

                <div class="connection-parameter-row">
                  <div class="connection-parameter-meta" title="数据库类型。">
                    <span class="connection-parameter-label">数据库类型</span>
                    <span class="connection-parameter-key">provider</span>
                  </div>
                  <NSelect :value="createConnectionForm.provider" :options="providerOptions" class="connection-parameter-control" @update:value="handleProviderChange($event)" />
                </div>

                <div v-if="createConnectionForm.provider !== 'sqlite'" class="connection-parameter-row">
                  <div class="connection-parameter-meta" title="认证方式。SQL Server 支持 Windows 身份验证。">
                    <span class="connection-parameter-label">认证方式</span>
                    <span class="connection-parameter-key">sql_server_authentication_mode</span>
                  </div>
                  <NSelect v-model:value="sqlServerConnectionForm.authenticationMode" :options="sqlServerAuthenticationModeOptions" class="connection-parameter-control" />
                </div>

                <div class="connection-parameter-row">
                  <div class="connection-parameter-meta" :title="createConnectionForm.provider === 'sqlite' ? 'SQLite 数据库文件路径。文件不存在时会在首次连接时创建。' : '数据库主机、IP 或数据源地址。'">
                    <span class="connection-parameter-label">{{ createConnectionForm.provider === 'sqlite' ? '数据库文件' : '主机 / 数据源' }}</span>
                    <span class="connection-parameter-key">{{ createConnectionForm.provider === 'sqlite' ? 'data_source' : 'host' }}</span>
                  </div>
                  <div v-if="createConnectionForm.provider === 'sqlite'" class="connection-dialog-inline-row connection-parameter-control">
                    <NInput v-model:value="createConnectionForm.host" placeholder="例如 C:\data\app.db" />
                    <NButton tertiary type="primary" @click="browseSqliteDatabaseFile">选择...</NButton>
                  </div>
                  <NInput v-else v-model:value="createConnectionForm.host" class="connection-parameter-control" :placeholder="hostPlaceholder" />
                </div>

                <div v-if="createConnectionForm.provider === 'sqlite'" class="connection-parameter-row">
                  <div class="connection-parameter-meta" title="控制 SQLite 默认以读写还是只读方式打开。右键菜单里的“只读连接”会在当前会话里临时覆盖这里的默认值。">
                    <span class="connection-parameter-label">打开方式</span>
                    <span class="connection-parameter-key">open_mode</span>
                  </div>
                  <NSelect v-model:value="sqliteConnectionForm.openMode" :options="sqliteOpenModeOptions" class="connection-parameter-control" />
                </div>

                <div v-if="createConnectionForm.provider !== 'sqlite'" class="connection-parameter-row">
                  <div class="connection-parameter-meta" title="数据库端口。">
                    <span class="connection-parameter-label">端口</span>
                    <span class="connection-parameter-key">port</span>
                  </div>
                  <NInput v-model:value="createConnectionForm.port" class="connection-parameter-control" placeholder="输入端口" inputmode="numeric" />
                </div>

                <div v-if="createConnectionForm.provider !== 'sqlite' && sqlServerConnectionForm.authenticationMode !== 'windows'" class="connection-parameter-row">
                  <div class="connection-parameter-meta" title="登录用户名。">
                    <span class="connection-parameter-label">用户名</span>
                    <span class="connection-parameter-key">username</span>
                  </div>
                  <NInput v-model:value="createConnectionForm.username" class="connection-parameter-control" placeholder="输入用户名" />
                </div>

                <div v-if="createConnectionForm.provider !== 'sqlite' && sqlServerConnectionForm.authenticationMode !== 'windows'" class="connection-parameter-row">
                  <div class="connection-parameter-meta" :title="editingConnectionId ? '编辑已有连接时留空会保持原密码。' : '数据库登录密码。'">
                    <span class="connection-parameter-label">密码</span>
                    <span class="connection-parameter-key">password</span>
                  </div>
                  <StoredMaskedPasswordInput
                    :model-value="createConnectionForm.password"
                    :has-stored-value="showStoredPasswordMask"
                    class="connection-parameter-control"
                    :placeholder="editingConnectionId && hasStoredPassword ? '留空则保持原密码' : '输入密码'"
                    @begin-edit="beginPasswordEdit"
                    @update:model-value="(value: string) => { createConnectionForm.password = value }"
                  />
                </div>

                <div v-if="createConnectionForm.provider === 'sqlserver'" class="connection-parameter-row connection-parameter-row-toggle">
                  <div class="connection-parameter-meta" title="是否跳过 SQL Server 服务器证书验证。">
                    <span class="connection-parameter-label">信任服务器证书</span>
                    <span class="connection-parameter-key">trust_server_certificate</span>
                  </div>
                  <NCheckbox v-model:checked="sqlServerConnectionForm.trustServerCertificate">启用</NCheckbox>
                </div>
              </div>
              <NText v-if="createConnectionForm.provider === 'sqlite'" depth="3">SQLite 文件会在首次连接时自动创建。</NText>
            </div>
          </NTabPane>
          <NTabPane v-if="createConnectionForm.provider === 'sqlite'" name="cipher" tab="加密">
            <div class="connection-dialog-form connection-dialog-form-section">
              <NAlert v-if="lockExistingUnencryptedSqliteCipherSettings" type="info" :show-icon="false">
                当前 SQLite 数据库尚未加密。SQLCipher 对明文库加密/解密需要导出并重写数据库文件，当前应用暂未提供这条流程，因此这里只允许编辑已经加密好的 SQLite 参数。
              </NAlert>
              <div class="connection-parameter-row connection-parameter-row-toggle">
                <div class="connection-parameter-meta" title="开启后按下面的参数使用 SQLCipher 打开或创建数据库。">
                  <span class="connection-parameter-label">启用加密</span>
                  <span class="connection-parameter-key">enabled</span>
                </div>
                <NCheckbox v-model:checked="sqliteConnectionForm.cipherEnabled" :disabled="lockExistingUnencryptedSqliteCipherSettings">启用 SQLCipher 加密</NCheckbox>
              </div>
              <NText depth="3">留空表示跟随默认值。</NText>
              <template v-if="sqliteConnectionForm.cipherEnabled && !lockExistingUnencryptedSqliteCipherSettings">
                <div class="connection-parameter-grid">
                  <div class="connection-parameter-row">
                    <div class="connection-parameter-meta" title="选择口令或十六进制原始密钥格式。">
                      <span class="connection-parameter-label">密钥格式</span>
                      <span class="connection-parameter-key">key_format</span>
                    </div>
                    <NSelect v-model:value="sqliteConnectionForm.cipherKeyFormat" :options="sqliteCipherKeyFormatOptions" class="connection-parameter-control" />
                  </div>

                  <div class="connection-parameter-row">
                    <div class="connection-parameter-meta" :title="editingConnectionId && sqliteConnectionForm.cipherHasStoredPassword ? '编辑连接时留空会保留当前保存的密钥。' : sqliteConnectionForm.cipherKeyFormat === 'hex' ? '请输入偶数长度的 HEX 字符串。' : '数据库加密口令。'">
                      <span class="connection-parameter-label">密钥</span>
                      <span class="connection-parameter-key">key</span>
                    </div>
                    <StoredMaskedPasswordInput
                      :model-value="sqliteConnectionForm.cipherPassword"
                      :has-stored-value="showStoredSqliteCipherPasswordMask"
                      class="connection-parameter-control"
                      :placeholder="editingConnectionId && sqliteConnectionForm.cipherHasStoredPassword ? '留空则保持当前密钥' : sqliteConnectionForm.cipherKeyFormat === 'hex' ? '例如 0011AABB' : '输入 SQLCipher 密码'"
                      @begin-edit="beginSqliteCipherPasswordEdit"
                      @update:model-value="(value) => { sqliteConnectionForm.cipherPassword = value }"
                    />
                  </div>

                  <div class="connection-parameter-row">
                    <div class="connection-parameter-meta" title="页面大小。常见值为 4096。">
                      <span class="connection-parameter-label">页面大小</span>
                      <span class="connection-parameter-key">cipher_page_size</span>
                    </div>
                    <NInput v-model:value="sqliteConnectionForm.cipherPageSize" class="connection-parameter-control" placeholder="例如 4096" inputmode="numeric" />
                  </div>

                  <div class="connection-parameter-row">
                    <div class="connection-parameter-meta" title="KDF 迭代次数。常见值如 256000。">
                      <span class="connection-parameter-label">KDF 迭代</span>
                      <span class="connection-parameter-key">kdf_iter</span>
                    </div>
                    <NInput v-model:value="sqliteConnectionForm.cipherKdfIter" class="connection-parameter-control" placeholder="例如 256000" inputmode="numeric" />
                  </div>

                  <div class="connection-parameter-row">
                    <div class="connection-parameter-meta" title="兼容 SQLCipher 版本。常见值为 3 或 4。">
                      <span class="connection-parameter-label">兼容版本</span>
                      <span class="connection-parameter-key">cipher_compatibility</span>
                    </div>
                    <NInput v-model:value="sqliteConnectionForm.cipherCompatibility" class="connection-parameter-control" placeholder="例如 4" inputmode="numeric" />
                  </div>

                  <div class="connection-parameter-row">
                    <div class="connection-parameter-meta" title="明文头大小。通常保留默认即可。">
                      <span class="connection-parameter-label">纯文本头</span>
                      <span class="connection-parameter-key">cipher_plaintext_header_size</span>
                    </div>
                    <NInput v-model:value="sqliteConnectionForm.cipherPlaintextHeaderSize" class="connection-parameter-control" placeholder="例如 0 或 32" inputmode="numeric" />
                  </div>

                  <div class="connection-parameter-row">
                    <div class="connection-parameter-meta" title="读取数据库前跳过的头部字节数。普通 SQLite 留空即可。">
                      <span class="connection-parameter-label">跳过字节</span>
                      <span class="connection-parameter-key">skip_bytes</span>
                    </div>
                    <NInput v-model:value="sqliteConnectionForm.cipherSkipBytes" class="connection-parameter-control" placeholder="例如 1024" inputmode="numeric" />
                  </div>

                  <div class="connection-parameter-row">
                    <div class="connection-parameter-meta" title="是否启用页级 HMAC 校验。默认通常跟随 SQLCipher 版本。">
                      <span class="connection-parameter-label">HMAC 校验</span>
                      <span class="connection-parameter-key">cipher_use_hmac</span>
                    </div>
                    <NSelect v-model:value="sqliteConnectionForm.cipherUseHmac" :options="sqliteCipherUseHmacOptions" class="connection-parameter-control" />
                  </div>

                  <div class="connection-parameter-row">
                    <div class="connection-parameter-meta" title="派生密钥时使用的 KDF 算法。">
                      <span class="connection-parameter-label">KDF 算法</span>
                      <span class="connection-parameter-key">cipher_kdf_algorithm</span>
                    </div>
                    <NSelect v-model:value="sqliteConnectionForm.cipherKdfAlgorithm" :options="sqliteCipherKdfAlgorithmOptions" class="connection-parameter-control" />
                  </div>

                  <div class="connection-parameter-row">
                    <div class="connection-parameter-meta" title="页完整性校验使用的 HMAC 算法。">
                      <span class="connection-parameter-label">HMAC 算法</span>
                      <span class="connection-parameter-key">cipher_hmac_algorithm</span>
                    </div>
                    <NSelect v-model:value="sqliteConnectionForm.cipherHmacAlgorithm" :options="sqliteCipherHmacAlgorithmOptions" class="connection-parameter-control" />
                  </div>
                </div>
              </template>
            </div>
          </NTabPane>
          <NTabPane v-if="createConnectionForm.provider !== 'sqlite'" name="ssh" tab="SSH">
            <div class="connection-dialog-form connection-dialog-form-section">
              <div class="connection-parameter-row connection-parameter-row-toggle">
                <div class="connection-parameter-meta" title="开启后通过 SSH 跳板连接数据库。">
                  <span class="connection-parameter-label">SSH 隧道</span>
                  <span class="connection-parameter-key">ssh.enabled</span>
                </div>
                <NCheckbox v-model:checked="sshTunnelForm.enabled">启用</NCheckbox>
              </div>
              <template v-if="sshTunnelForm.enabled">
                <div class="connection-parameter-grid">
                  <div class="connection-parameter-row">
                    <div class="connection-parameter-meta" title="SSH 跳板主机地址。">
                      <span class="connection-parameter-label">SSH 主机</span>
                      <span class="connection-parameter-key">ssh.host</span>
                    </div>
                    <NInput v-model:value="sshTunnelForm.host" class="connection-parameter-control" placeholder="例如 jump.example.com" />
                  </div>

                  <div class="connection-parameter-row">
                    <div class="connection-parameter-meta" title="SSH 监听端口，默认 22。">
                      <span class="connection-parameter-label">SSH 端口</span>
                      <span class="connection-parameter-key">ssh.port</span>
                    </div>
                    <NInput v-model:value="sshTunnelForm.port" class="connection-parameter-control" placeholder="输入端口" inputmode="numeric" />
                  </div>

                  <div class="connection-parameter-row">
                    <div class="connection-parameter-meta" title="SSH 登录用户名。">
                      <span class="connection-parameter-label">SSH 用户名</span>
                      <span class="connection-parameter-key">ssh.username</span>
                    </div>
                    <NInput v-model:value="sshTunnelForm.username" class="connection-parameter-control" placeholder="输入用户名" />
                  </div>

                  <div class="connection-parameter-row">
                    <div class="connection-parameter-meta" title="SSH 认证方式，可选密码或公钥。">
                      <span class="connection-parameter-label">SSH 认证</span>
                      <span class="connection-parameter-key">ssh.authentication</span>
                    </div>
                    <NSelect v-model:value="sshTunnelForm.authentication" :options="sshAuthenticationOptions" class="connection-parameter-control" />
                  </div>

                  <div v-if="sshTunnelForm.authentication === 'password'" class="connection-parameter-row">
                    <div class="connection-parameter-meta" :title="editingConnectionId ? '编辑已有连接时留空会保持原 SSH 密码。' : 'SSH 登录密码。'">
                      <span class="connection-parameter-label">SSH 密码</span>
                      <span class="connection-parameter-key">ssh.password</span>
                    </div>
                    <StoredMaskedPasswordInput
                      :model-value="sshTunnelForm.password"
                      :has-stored-value="showStoredSshPasswordMask"
                      class="connection-parameter-control"
                      :placeholder="editingConnectionId && hasStoredSshPassword ? '留空则保持原密码' : '输入 SSH 密码'"
                      @begin-edit="beginSshPasswordEdit"
                      @update:model-value="(value: string) => { sshTunnelForm.password = value }"
                    />
                  </div>

                  <template v-else>
                    <div class="connection-parameter-row">
                      <div class="connection-parameter-meta" title="私钥文件路径；留空时会自动尝试用户目录下的默认密钥。">
                        <span class="connection-parameter-label">私钥路径</span>
                        <span class="connection-parameter-key">ssh.private_key_path</span>
                      </div>
                      <div class="connection-dialog-inline-row connection-parameter-control">
                        <NInput v-model:value="sshTunnelForm.privateKeyPath" placeholder="留空自动尝试默认私钥" />
                        <NButton tertiary @click="browseSshPrivateKeyFile">选择...</NButton>
                      </div>
                    </div>

                    <div class="connection-parameter-row">
                      <div class="connection-parameter-meta" :title="editingConnectionId ? '编辑已有连接时留空会保持原通行短语。' : '私钥通行短语，可选。'">
                        <span class="connection-parameter-label">通行短语</span>
                        <span class="connection-parameter-key">ssh.passphrase</span>
                      </div>
                      <StoredMaskedPasswordInput
                        :model-value="sshTunnelForm.passphrase"
                        :has-stored-value="showStoredSshPassphraseMask"
                        class="connection-parameter-control"
                        :placeholder="editingConnectionId && hasStoredSshPassphrase ? '留空则保持原通行短语' : '可选'"
                        @begin-edit="beginSshPassphraseEdit"
                        @update:model-value="(value: string) => { sshTunnelForm.passphrase = value }"
                      />
                    </div>
                  </template>
                </div>
                <NText depth="3">数据库主机和端口会作为 SSH 目标地址使用。</NText>
              </template>
            </div>
          </NTabPane>
        </NTabs>
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

    <NModal v-model:show="sqliteRekeyVisible" preset="card" style="width: min(460px, 92vw)" title="修改 SQLite 加密密码" @update:show="handleSqliteRekeyVisibleChange">
      <div class="connection-dialog-form connection-dialog-form-section">
        <NText depth="3">留空当前密码时会使用连接里已保存的密钥。</NText>
        <div class="connection-parameter-grid">
          <div class="connection-parameter-row">
            <div class="connection-parameter-meta" title="当前数据库使用的密钥格式。">
              <span class="connection-parameter-label">当前密钥格式</span>
              <span class="connection-parameter-key">current.key_format</span>
            </div>
            <NSelect v-model:value="sqliteRekeyForm.currentKeyFormat" :options="sqliteCipherKeyFormatOptions" class="connection-parameter-control" />
          </div>

          <div class="connection-parameter-row">
            <div class="connection-parameter-meta" title="当前数据库密码；留空时使用连接里已保存的密钥。">
              <span class="connection-parameter-label">当前密码</span>
              <span class="connection-parameter-key">current.key</span>
            </div>
            <NInput
              v-model:value="sqliteRekeyForm.currentPassword"
              class="connection-parameter-control"
              type="password"
              show-password-on="click"
              placeholder="留空则使用已保存密钥"
            />
          </div>
        </div>
        <div class="connection-parameter-grid">
          <div class="connection-parameter-row">
            <div class="connection-parameter-meta" title="新的密钥格式。">
              <span class="connection-parameter-label">新密钥格式</span>
              <span class="connection-parameter-key">new.key_format</span>
            </div>
            <NSelect v-model:value="sqliteRekeyForm.newKeyFormat" :options="sqliteCipherKeyFormatOptions" class="connection-parameter-control" />
          </div>

          <div class="connection-parameter-row">
            <div class="connection-parameter-meta" title="新的 SQLCipher 密钥或密码。">
              <span class="connection-parameter-label">新密码</span>
              <span class="connection-parameter-key">new.key</span>
            </div>
            <NInput
              v-model:value="sqliteRekeyForm.newPassword"
              class="connection-parameter-control"
              type="password"
              show-password-on="click"
              :placeholder="sqliteRekeyForm.newKeyFormat === 'hex' ? '例如 0011AABB' : '输入新的 SQLCipher 密码'"
            />
          </div>
        </div>
        <NAlert v-if="sqliteRekeyError" type="warning" :show-icon="false">{{ sqliteRekeyError }}</NAlert>
        <div class="connection-dialog-actions">
          <NButton tertiary @click="closeSqliteRekeyDialog">取消</NButton>
          <NButton type="primary" :loading="sqliteRekeyLoading" @click="submitSqliteRekey">更新密钥</NButton>
        </div>
      </div>
    </NModal>

    <ContextDropdown
      :show="!!routineContextMenu"
      :x="routineContextMenu?.x ?? 0"
      :y="routineContextMenu?.y ?? 0"
      :options="routineContextMenuOptions"
      @update:show="handleRoutineContextMenuShow"
      @select="handleRoutineContextMenuSelect"
    />

    <ContextDropdown
      :show="!!designEntryContextMenu"
      :x="designEntryContextMenu?.x ?? 0"
      :y="designEntryContextMenu?.y ?? 0"
      :options="designEntryContextMenuOptions"
      @update:show="handleDesignEntryContextMenuShow"
      @select="handleDesignEntryContextMenuSelect"
    />

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
// ── 侧边栏外壳 ──
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

// ── 连接卡片 ──
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

// ── 树结构 ──
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

@keyframes spin-icon {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
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
    background: rgba(219, 234, 254, 0.35);
    border-radius: var(--radius-sm);
  }

  &:hover {
    background: rgba(219, 234, 254, 0.35);

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

.tree-row-empty {
  padding: 4px 8px 4px 30px;
  font-size: $font-size-xs;
  color: $color-text-muted;
}

.tree-row-error {
  color: $color-accent-red;
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
    background: rgba(219, 234, 254, 0.35);
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

// ── 树节点图标 ──
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

  &-disconnected {
    background: rgba(148, 163, 184, 0.16);
    color: $color-text-muted;
    opacity: 0.6;
  }

  &-connecting {
    background: transparent;
    color: $color-accent-indigo;
    animation: spin-icon 0.8s linear infinite;
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

// ── 树节点文本 ──
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

.connection-dialog-form-section {
  padding-top: 8px;
}

.connection-dialog-inline-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 6px;
  align-items: center;
}

.connection-parameter-grid {
  display: grid;
  gap: 6px;
}

.connection-parameter-row {
  display: grid;
  grid-template-columns: minmax(96px, 118px) minmax(0, 1fr);
  gap: 6px;
  align-items: center;
}

.connection-parameter-row-toggle {
  align-items: center;
}

.connection-parameter-meta {
  display: grid;
  gap: 2px;
  min-width: 0;
}

.connection-parameter-label {
  font-size: 14px;
  line-height: 1.25;
  font-weight: 700;
  color: rgba(15, 23, 42, 0.9);
  white-space: nowrap;
}

.connection-parameter-key {
  display: none;
  font-size: 12px;
  line-height: 1.2;
  font-weight: 500;
  color: rgba(100, 116, 139, 0.95);
}

.connection-parameter-control {
  min-width: 0;
}

.connection-dialog-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 8px;
}

@media (max-width: 720px) {
  .connection-parameter-row {
    grid-template-columns: minmax(0, 1fr);
  }

  .connection-dialog-actions {
    justify-content: stretch;
    flex-wrap: wrap;
  }
}
</style>
