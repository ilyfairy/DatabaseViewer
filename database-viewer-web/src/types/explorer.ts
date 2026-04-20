export type DatabaseProviderType = 'sqlserver' | 'mysql' | 'postgresql' | 'sqlite'
export type SqlServerAuthenticationMode = 'windows' | 'password'
export type SqliteOpenMode = 'readwrite' | 'readonly'
export type SqliteVfsKind = 'default' | 'builtInOffset' | 'named'
export type SqliteLoadableExtensionPhase = 'preOpen' | 'postOpen'
export type CellValue = string | number | boolean | null
export type DatabaseObjectType = 'table' | 'view'
export type CatalogObjectType = 'synonym' | 'sequence' | 'rule' | 'default' | 'user-defined-type' | 'database-trigger' | 'xml-schema-collection' | 'assembly'

export interface SqlServerConnectionSummary {
  authenticationMode: SqlServerAuthenticationMode
  trustServerCertificate: boolean
}

export interface MySqlConnectionSummary {
}

export interface PostgreSqlConnectionSummary {
}

export interface SqliteConnectionSummary {
  openMode: SqliteOpenMode
}

export interface ConnectionInfo {
  id: string
  name: string
  provider: DatabaseProviderType
  host: string
  port?: number
  sqlServer: SqlServerConnectionSummary
  mySql: MySqlConnectionSummary
  postgreSql: PostgreSqlConnectionSummary
  sqlite: SqliteConnectionSummary
  accent: string
  error?: string | null
  databases: DatabaseInfo[]
}

export interface ExplorerSettings {
  showTableRowCounts: boolean
  sqliteExtensions: SqliteLoadableExtensionConfig[]
}

export interface WorkspaceLayout {
  sidebarPaneSize: number
  detailPaneSize: number
}

export interface CreateConnectionRequest {
  name: string
  provider: DatabaseProviderType
  host: string
  port?: number | null
  username?: string | null
  password?: string | null
  sqlServer: SqlServerConnectionRequest
  mySql: MySqlConnectionRequest
  postgreSql: PostgreSqlConnectionRequest
  sqlite: SqliteConnectionRequest
  sshTunnel: SshTunnelRequest
}

export interface SqlServerConnectionRequest {
  authenticationMode?: SqlServerAuthenticationMode | null
  trustServerCertificate?: boolean | null
}

export interface SqlServerConnectionConfig {
  authenticationMode: SqlServerAuthenticationMode
  trustServerCertificate: boolean
}

export interface MySqlConnectionRequest {
}

export interface MySqlConnectionConfig {
}

export interface PostgreSqlConnectionRequest {
}

export interface PostgreSqlConnectionConfig {
}

export interface SqliteConnectionRequest {
  openMode?: SqliteOpenMode | null
  cipher: SqliteCipherRequest
  vfs: SqliteVfsRequest
}

export interface SqliteConnectionConfig {
  openMode: SqliteOpenMode
  cipher: SqliteCipherConfig
  vfs: SqliteVfsConfig
}

export interface SqliteVfsRequest {
  kind?: SqliteVfsKind | null
  builtInOffset: SqliteBuiltInOffsetVfsRequest
  named: SqliteNamedVfsRequest
}

export interface SqliteBuiltInOffsetVfsRequest {
  skipBytes?: number | null
}

export interface SqliteNamedVfsRequest {
  name?: string | null
}

export interface SqliteVfsConfig {
  kind: SqliteVfsKind
  builtInOffset: SqliteBuiltInOffsetVfsConfig
  named: SqliteNamedVfsConfig
}

export interface SqliteBuiltInOffsetVfsConfig {
  skipBytes?: number | null
}

export interface SqliteNamedVfsConfig {
  name?: string | null
}

export interface SqliteLoadableExtensionRequest {
  path?: string | null
  entryPoint?: string | null
  phase?: SqliteLoadableExtensionPhase | null
}

export interface SqliteLoadableExtensionConfig {
  path?: string | null
  entryPoint?: string | null
  phase: SqliteLoadableExtensionPhase
}

export interface SqliteCipherRequest {
  enabled: boolean
  password?: string | null
  keyFormat: 'passphrase' | 'hex'
  pageSize?: number | null
  kdfIter?: number | null
  cipherCompatibility?: number | null
  plaintextHeaderSize?: number | null
  useHmac?: boolean | null
  kdfAlgorithm?: 'PBKDF2_HMAC_SHA1' | 'PBKDF2_HMAC_SHA256' | 'PBKDF2_HMAC_SHA512' | null
  hmacAlgorithm?: 'HMAC_SHA1' | 'HMAC_SHA256' | 'HMAC_SHA512' | null
}

export interface SshTunnelRequest {
  enabled: boolean
  authentication: 'password' | 'publicKey'
  host?: string | null
  port?: number | null
  username?: string | null
  password?: string | null
  privateKeyPath?: string | null
  passphrase?: string | null
}

export interface SshTunnelConfig {
  enabled: boolean
  authentication: 'password' | 'publicKey'
  host?: string | null
  port?: number | null
  username?: string | null
  hasPassword: boolean
  hasPassphrase: boolean
  privateKeyPath?: string | null
}

export interface ConnectionConfig {
  id: string
  name: string
  provider: DatabaseProviderType
  host: string
  port?: number | null
  username?: string | null
  hasPassword: boolean
  sqlServer: SqlServerConnectionConfig
  mySql: MySqlConnectionConfig
  postgreSql: PostgreSqlConnectionConfig
  sqlite: SqliteConnectionConfig
  sshTunnel: SshTunnelConfig
}

export interface SqliteCipherConfig {
  enabled: boolean
  hasPassword: boolean
  keyFormat: 'passphrase' | 'hex'
  pageSize?: number | null
  kdfIter?: number | null
  cipherCompatibility?: number | null
  plaintextHeaderSize?: number | null
  useHmac?: boolean | null
  kdfAlgorithm?: 'PBKDF2_HMAC_SHA1' | 'PBKDF2_HMAC_SHA256' | 'PBKDF2_HMAC_SHA512' | null
  hmacAlgorithm?: 'HMAC_SHA1' | 'HMAC_SHA256' | 'HMAC_SHA512' | null
}

export interface TestConnectionRequest extends CreateConnectionRequest {
  connectionId?: string | null
}

export interface SqliteRekeyRequest {
  connectionId: string
  currentPassword?: string | null
  currentKeyFormat?: 'passphrase' | 'hex' | null
  newPassword: string
  newKeyFormat: 'passphrase' | 'hex'
}

export interface DatabaseInfo {
  name: string
  tables: TableSummary[]
  views: TableSummary[]
  synonyms: SynonymInfo[]
  sequences: SequenceInfo[]
  rules: RuleInfo[]
  defaults: DefaultInfo[]
  userDefinedTypes: UserDefinedTypeInfo[]
  databaseTriggers: DatabaseTriggerInfo[]
  xmlSchemaCollections: XmlSchemaCollectionInfo[]
  assemblies: AssemblyInfo[]
  routines: RoutineInfo[]
}

export interface SynonymInfo {
  database: string
  schema?: string | null
  name: string
  baseObjectName: string
}

export interface SequenceInfo {
  database: string
  schema?: string | null
  name: string
  dataType: string
  startValue: string
  incrementValue: string
}

export interface RuleInfo {
  database: string
  schema?: string | null
  name: string
  definition?: string | null
}

export interface DefaultInfo {
  database: string
  schema?: string | null
  name: string
  definition?: string | null
}

export interface UserDefinedTypeInfo {
  database: string
  schema?: string | null
  name: string
  baseTypeName: string
  isTableType: boolean
}

export interface DatabaseTriggerInfo {
  database: string
  schema?: string | null
  name: string
  timing?: string | null
  event?: string | null
}

export interface XmlSchemaCollectionInfo {
  database: string
  schema?: string | null
  name: string
  xmlNamespaceCount: number
}

export interface AssemblyInfo {
  database: string
  name: string
  clrName: string
  permissionSet: string
  isVisible: boolean
}

export interface CatalogObjectProperty {
  label: string
  value?: string | null
}

export interface CatalogObjectDetail {
  connectionId: string
  database: string
  provider: DatabaseProviderType
  objectType: CatalogObjectType
  schema?: string | null
  name: string
  title: string
  summary?: string | null
  definition?: string | null
  properties: CatalogObjectProperty[]
}

export interface RoutineInfo {
  schema?: string | null
  name: string
  routineType: string // 'Procedure' | 'ScalarFunction' | 'TableFunction' | 'AggregateFunction'
  parameters: RoutineParameter[]
}

export interface RoutineParameter {
  name: string
  dataType: string
  direction: string // 'IN' | 'OUT' | 'INOUT'
  defaultValue?: string | null
}

export interface DatabaseGraphNode {
  tableKey: string
  title: string
  rowCount?: number | null
  isJunction: boolean
  comment?: string | null
  columns: DatabaseGraphColumn[]
}

export interface DatabaseGraphColumn {
  name: string
  type: string
  displayType: string
  isNullable: boolean
  isPrimaryKey: boolean
  isForeignKey: boolean
}

export interface DatabaseGraphEdge {
  sourceTableKey: string
  targetTableKey: string
  relationType: 'one-to-one' | 'many-to-one' | 'many-to-many'
  label: string
  logical: boolean
  viaTableKey?: string | null
  sourceColumn: string
  targetColumn: string
}

export interface DatabaseGraph {
  connectionId: string
  database: string
  nodes: DatabaseGraphNode[]
  edges: DatabaseGraphEdge[]
}

export interface TableColumn {
  name: string
  type: string
  isPrimaryKey?: boolean
  isNullable?: boolean
  isAutoGenerated?: boolean
  isComputed?: boolean
  isHiddenRowId?: boolean
  maxLength?: number | null
  comment?: string | null
  numericPrecision?: number | null
  numericScale?: number | null
}

export interface ForeignKeyRef {
  sourceColumn: string
  targetTableKey: string
  targetColumn: string
  logical: boolean
}

export interface TableRow {
  rowKey: string
  [key: string]: CellValue
}

export interface TableSummary {
  key: string
  database: string
  schema?: string
  name: string
  objectType: DatabaseObjectType
  comment?: string
  rowCount?: number | null
}

export interface TableDefinition extends TableSummary {
  primaryKeys: string[]
  columns: TableColumn[]
  foreignKeys: ForeignKeyRef[]
  rows: TableRow[]
  hasMoreRows: boolean
  rowsLoaded?: boolean
}

export interface TableSearchResponse {
  tableKey: string
  query: string
  columns: string[]
  rows: TableRow[]
  totalMatches: number
  hasMoreRows: boolean
}

export interface SqlResultColumn {
  name: string
  type: string
}

export interface SqlResultRow {
  [key: string]: CellValue
}

export interface SqlResultSet {
  name: string
  columns: SqlResultColumn[]
  rows: SqlResultRow[]
  rowCount: number
  truncated: boolean
}

export interface SqlExecutionResponse {
  connectionId: string
  database: string
  executedSql: string
  affectedRows?: number | null
  elapsedMs: number
  resultSets: SqlResultSet[]
}

export interface SqlContextTable {
  name: string
  schema?: string | null
  qualifiedName: string
  columns: string[]
}

export interface SqlContext {
  connectionId: string
  database: string
  provider: DatabaseProviderType
  tables: SqlContextTable[]
}

export interface ExplorerGridPanel {
  id: string
  type: 'grid'
  tableKey: string
}

export interface ExplorerDetailPanel {
  id: string
  type: 'detail'
  tableKey: string
  rowKey: string
  depth: number
  sourceLabel: string
}

export interface DatabaseFileInfo {
  logicalName: string
  fileType: string
  fileGroup?: string | null
  sizeMB: string
  autoGrowth?: string | null
  path?: string | null
}

export interface DatabasePermissionInfo {
  userName: string
  userType: string
  defaultSchema?: string | null
  loginName?: string | null
  roles: string[]
}

export interface DatabaseProperties {
  connectionId: string
  database: string
  provider: DatabaseProviderType
  generalProperties: CatalogObjectProperty[]
  files: DatabaseFileInfo[]
  permissions: DatabasePermissionInfo[]
}

export type ExplorerPanel = ExplorerGridPanel | ExplorerDetailPanel

export interface TableWorkspaceTab {
  id: string
  type: 'table'
  tableKey: string
  detailPanels: ExplorerDetailPanel[]
}

export type TableDesignSection = 'properties' | 'columns' | 'indexes' | 'foreignKeys' | 'triggers' | 'statistics'

export interface TableIndexInfo {
  name: string
  isPrimaryKey: boolean
  isUnique: boolean
  columns: string[]
}

export interface TableTriggerInfo {
  name: string
  timing?: string | null
  event?: string | null
}

export interface TableStatisticInfo {
  name: string
  isAutoCreated: boolean
  isUserCreated: boolean
  noRecompute: boolean
  filterDefinition?: string | null
  columns: string[]
}

export interface TableDesign {
  tableKey: string
  connectionId: string
  database: string
  provider: DatabaseProviderType
  objectType: DatabaseObjectType
  schema?: string | null
  name: string
  comment?: string | null
  columns: TableColumn[]
  foreignKeys: ForeignKeyRef[]
  indexes: TableIndexInfo[]
  triggers: TableTriggerInfo[]
  statistics: TableStatisticInfo[]
}

export interface TableDesignWorkspaceTab {
  id: string
  type: 'design'
  tableKey: string
  selectedSection: TableDesignSection
  selectedEntry: string | null
  createContext?: {
    connectionId: string
    database: string
    provider: DatabaseProviderType
    schema?: string | null
    tableName: string
  } | null
}

export interface SqlWorkspaceTab {
  id: string
  type: 'sql'
  connectionId: string | null
  database: string | null
  displayName?: string | null
  sqlText: string
  savedSqlText: string
  filePath: string | null
  /** 是否由存储过程/函数源码打开（Ctrl+S 时执行 ALTER 而非保存到文件） */
  isRoutineSource?: boolean
  loading: boolean
  error: string | null
  result: SqlExecutionResponse | null
  selectedResultIndex: number
}

export interface GraphWorkspaceTab {
  id: string
  type: 'graph'
  connectionId: string
  database: string
  loading: boolean
  error: string | null
  graph: DatabaseGraph | null
}

export interface CatalogObjectWorkspaceTab {
  id: string
  type: 'catalog'
  connectionId: string
  database: string
  objectType: CatalogObjectType
  schema?: string | null
  name: string
}

export interface TableMockWorkspaceTab {
  id: string
  type: 'mock'
  connectionId: string
  database: string
  tableKey: string
}

export interface SettingsWorkspaceTab {
  id: string
  type: 'settings'
}

export interface SqlServerLoginManagerWorkspaceTab {
  id: string
  type: 'sqlserver-login-manager'
  connectionId: string
  selectedLoginName: string | null
  mode: 'browse' | 'create'
}

export interface SqlServerLoginEditorWorkspaceTab {
  id: string
  type: 'sqlserver-login-editor'
  connectionId: string
  loginName: string | null
  mode: 'browse' | 'create'
}

export interface DatabasePropertiesWorkspaceTab {
  id: string
  type: 'database-properties'
  connectionId: string
  database: string
}

export type WorkspaceTab = TableWorkspaceTab | TableDesignWorkspaceTab | SqlWorkspaceTab | GraphWorkspaceTab | CatalogObjectWorkspaceTab | TableMockWorkspaceTab | SettingsWorkspaceTab | SqlServerLoginManagerWorkspaceTab | SqlServerLoginEditorWorkspaceTab | DatabasePropertiesWorkspaceTab

export interface ReverseReferenceRow {
  rowKey: string
  values: TableRow
}

export interface ReverseReferenceGroup {
  sourceTableKey: string
  relationLabel: string
  rows: ReverseReferenceRow[]
}

export interface RecordDetail {
  tableKey: string
  rowKey: string
  row: TableRow
  primaryKeys: string[]
  columns: TableColumn[]
  foreignKeys: ForeignKeyRef[]
  reverseReferences: ReverseReferenceGroup[]
}

export interface ForeignKeyTarget {
  targetTableKey: string
  targetRowKey: string
  sourceLabel: string
}

export interface CellContentPreview {
  tableKey: string
  rowKey: string
  columnName: string
  kind: 'image' | 'text' | 'binary' | 'empty'
  mimeType: string
  suggestedFileName: string
  sizeBytes: number
  textContent?: string | null
  base64Data?: string | null
}

export interface TableCellUpdateRequest {
  tableKey: string
  rowKey: string
  columnName: string
  valueKind: 'text' | 'binary' | 'null'
  textValue?: string | null
  base64Value?: string | null
  setNull: boolean
}

export interface TableCellUpdateResponse {
  tableKey: string
  previousRowKey: string
  rowKey: string
  row: TableRow
}

export interface TableRowWriteValueRequest {
  columnName: string
  valueKind: 'text' | 'binary' | 'null'
  textValue?: string | null
  base64Value?: string | null
  setNull: boolean
}

export interface TableRowInsertRequest {
  tableKey: string
  values: TableRowWriteValueRequest[]
}

export interface TableRowInsertResponse {
  tableKey: string
  rowKey?: string | null
  row?: TableRow | null
}