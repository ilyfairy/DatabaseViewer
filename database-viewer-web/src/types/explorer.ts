export type ProviderType = 'sqlserver' | 'mysql' | 'postgresql' | 'sqlite'
export type AuthenticationMode = 'windows' | 'password'
export type CellValue = string | number | boolean | null
export type DatabaseObjectType = 'table' | 'view'
export type CatalogObjectType = 'synonym' | 'sequence' | 'rule' | 'default' | 'user-defined-type' | 'database-trigger' | 'xml-schema-collection'

export interface ConnectionInfo {
  id: string
  name: string
  provider: ProviderType
  host: string
  port?: number
  authentication: AuthenticationMode
  accent: string
  error?: string | null
  databases: DatabaseInfo[]
}

export interface CreateConnectionRequest {
  name: string
  provider: ProviderType
  authentication: AuthenticationMode
  host: string
  port?: number | null
  username?: string | null
  password?: string | null
  trustServerCertificate: boolean
  sshTunnel: SshTunnelRequest
  sqliteCipher: SqliteCipherRequest
}

export interface SqliteCipherRequest {
  enabled: boolean
  password?: string | null
  keyFormat: 'passphrase' | 'hex'
  pageSize?: number | null
  kdfIter?: number | null
  cipherCompatibility?: number | null
  plaintextHeaderSize?: number | null
  skipBytes?: number | null
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
  privateKeyPath?: string | null
}

export interface ConnectionConfig {
  id: string
  name: string
  provider: ProviderType
  authentication: AuthenticationMode
  host: string
  port?: number | null
  username?: string | null
  trustServerCertificate: boolean
  sshTunnel: SshTunnelConfig
  sqliteCipher: SqliteCipherConfig
}

export interface SqliteCipherConfig {
  enabled: boolean
  hasPassword: boolean
  keyFormat: 'passphrase' | 'hex'
  pageSize?: number | null
  kdfIter?: number | null
  cipherCompatibility?: number | null
  plaintextHeaderSize?: number | null
  skipBytes?: number | null
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

export interface CatalogObjectProperty {
  label: string
  value?: string | null
}

export interface CatalogObjectDetail {
  connectionId: string
  database: string
  provider: ProviderType
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
  provider: ProviderType
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

export type ExplorerPanel = ExplorerGridPanel | ExplorerDetailPanel

export interface TableWorkspaceTab {
  id: string
  type: 'table'
  tableKey: string
  detailPanels: ExplorerDetailPanel[]
}

export type TableDesignSection = 'columns' | 'indexes' | 'foreignKeys' | 'triggers' | 'statistics'

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
  provider: ProviderType
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

export type WorkspaceTab = TableWorkspaceTab | TableDesignWorkspaceTab | SqlWorkspaceTab | GraphWorkspaceTab | CatalogObjectWorkspaceTab | TableMockWorkspaceTab

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