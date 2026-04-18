'use strict';

import type { DatabaseProviderType } from '../types/explorer';

/**
 * SQL 自动补全数据 — 关键字、内置函数、系统变量、数据类型。
 * 按 provider 分组，所有条目均为大写便于不区分大小写匹配。
 */

/* ---------- 通用 SQL 关键字（所有 provider 共享） ---------- */
const COMMON_KEYWORDS = [
  'SELECT', 'FROM', 'WHERE', 'JOIN', 'LEFT', 'RIGHT', 'INNER', 'OUTER', 'CROSS',
  'ON', 'GROUP', 'BY', 'ORDER', 'HAVING', 'DISTINCT', 'AS', 'CASE', 'WHEN', 'THEN',
  'ELSE', 'END', 'INSERT', 'INTO', 'VALUES', 'UPDATE', 'SET', 'DELETE', 'CREATE',
  'ALTER', 'DROP', 'TABLE', 'VIEW', 'INDEX', 'UNION', 'ALL', 'AND', 'OR', 'NOT',
  'NULL', 'IS', 'IN', 'EXISTS', 'BETWEEN', 'LIKE', 'WITH', 'ASC', 'DESC', 'CAST',
  'CONVERT', 'COALESCE', 'NULLIF', 'IF', 'ELSE', 'BEGIN', 'END', 'DECLARE',
  'EXEC', 'EXECUTE', 'PRINT', 'RETURN', 'GOTO', 'WHILE', 'BREAK', 'CONTINUE',
  'TRY', 'CATCH', 'THROW', 'RAISERROR', 'TRANSACTION', 'COMMIT', 'ROLLBACK',
  'SAVEPOINT', 'CONSTRAINT', 'PRIMARY', 'KEY', 'FOREIGN', 'REFERENCES', 'UNIQUE',
  'CHECK', 'DEFAULT', 'IDENTITY', 'NOT', 'NULL', 'AUTO_INCREMENT',
  'TRUNCATE', 'GRANT', 'REVOKE', 'DENY', 'SCHEMA', 'DATABASE', 'PROCEDURE',
  'FUNCTION', 'TRIGGER', 'CURSOR', 'FETCH', 'OPEN', 'CLOSE', 'DEALLOCATE',
  'TOP', 'LIMIT', 'OFFSET', 'EXCEPT', 'INTERSECT', 'PIVOT', 'UNPIVOT',
  'OVER', 'PARTITION', 'ROWS', 'RANGE', 'UNBOUNDED', 'PRECEDING', 'FOLLOWING',
  'CURRENT', 'ROW', 'FIRST', 'LAST', 'NEXT', 'PRIOR', 'ABSOLUTE', 'RELATIVE',
  'SOME', 'ANY', 'ESCAPE', 'COLLATE', 'CASCADE', 'RESTRICT', 'NO', 'ACTION',
  'TEMPORARY', 'TEMP', 'REPLACE', 'RENAME', 'TO', 'ADD', 'COLUMN', 'MODIFY',
  'ENABLE', 'DISABLE',
];

/* ---------- 聚合函数（通用） ---------- */
const COMMON_AGGREGATE_FUNCTIONS = [
  'COUNT', 'SUM', 'AVG', 'MIN', 'MAX',
  'COUNT_BIG', 'STDEV', 'STDEVP', 'VAR', 'VARP',
  'GROUPING', 'GROUPING_ID', 'STRING_AGG',
];

/* ---------- 数学函数（通用） ---------- */
const COMMON_MATH_FUNCTIONS = [
  'ABS', 'ACOS', 'ASIN', 'ATAN', 'ATAN2', 'ATN2',
  'CEILING', 'COS', 'COT', 'DEGREES', 'EXP', 'FLOOR',
  'LOG', 'LOG10', 'LOG2', 'PI', 'POWER', 'RADIANS', 'RAND',
  'ROUND', 'SIGN', 'SIN', 'SQRT', 'SQUARE', 'TAN',
  'TRUNCATE', 'MOD',
];

/* ---------- 字符串函数（通用） ---------- */
const COMMON_STRING_FUNCTIONS = [
  'ASCII', 'CHAR', 'CHARINDEX', 'CONCAT', 'CONCAT_WS',
  'DIFFERENCE', 'FORMAT', 'LEFT', 'LEN', 'LENGTH', 'LOWER',
  'LTRIM', 'NCHAR', 'PATINDEX', 'QUOTENAME', 'REPLACE',
  'REPLICATE', 'REVERSE', 'RIGHT', 'RTRIM', 'SOUNDEX',
  'SPACE', 'STR', 'STRING_ESCAPE', 'STRING_SPLIT', 'STUFF',
  'SUBSTR', 'SUBSTRING', 'TRANSLATE', 'TRIM', 'UNICODE', 'UPPER',
];

/* ---------- 日期/时间函数（通用） ---------- */
const COMMON_DATE_FUNCTIONS = [
  'CURRENT_TIMESTAMP', 'CURRENT_DATE', 'CURRENT_TIME',
  'DATEADD', 'DATEDIFF', 'DATEDIFF_BIG', 'DATENAME', 'DATEPART',
  'DATEFROMPARTS', 'DATETIME2FROMPARTS', 'DATETIMEFROMPARTS',
  'DATETIMEOFFSETFROMPARTS', 'SMALLDATETIMEFROMPARTS', 'TIMEFROMPARTS',
  'DAY', 'MONTH', 'YEAR', 'GETDATE', 'GETUTCDATE', 'SYSDATETIME',
  'SYSUTCDATETIME', 'SYSDATETIMEOFFSET',
  'ISDATE', 'EOMONTH', 'SWITCHOFFSET', 'TODATETIMEOFFSET',
  'NOW', 'DATE', 'TIME', 'EXTRACT', 'DATE_FORMAT', 'DATE_ADD',
  'DATE_SUB', 'TIMESTAMPDIFF', 'TIMESTAMPADD',
];

/* ---------- 系统/元数据函数（通用） ---------- */
const COMMON_SYSTEM_FUNCTIONS = [
  'ISNULL', 'IIF', 'CHOOSE', 'OBJECT_ID', 'OBJECT_NAME',
  'DB_ID', 'DB_NAME', 'SCHEMA_ID', 'SCHEMA_NAME',
  'COL_NAME', 'COL_LENGTH', 'COLUMNPROPERTY',
  'TYPE_ID', 'TYPE_NAME', 'TYPEPROPERTY',
  'DATALENGTH', 'IDENT_CURRENT', 'IDENT_INCR', 'IDENT_SEED',
  'SCOPE_IDENTITY', 'NEWID', 'NEWSEQUENTIALID',
  'ROWCOUNT_BIG', 'ERROR_MESSAGE', 'ERROR_NUMBER',
  'ERROR_SEVERITY', 'ERROR_STATE', 'ERROR_LINE', 'ERROR_PROCEDURE',
  'HOST_NAME', 'APP_NAME', 'SUSER_NAME', 'SUSER_SNAME',
  'USER_NAME', 'SYSTEM_USER', 'SESSION_USER', 'CURRENT_USER',
  'ORIGINAL_LOGIN', 'SERVERPROPERTY', 'CONNECTIONPROPERTY',
  'HAS_DBACCESS', 'HAS_PERMS_BY_NAME',
  'TRY_CAST', 'TRY_CONVERT', 'TRY_PARSE', 'PARSE',
  'JSON_VALUE', 'JSON_QUERY', 'JSON_MODIFY', 'ISJSON',
  'OPENJSON', 'FOR', 'JSON', 'PATH', 'AUTO',
];

/* ---------- 窗口函数 ---------- */
const COMMON_WINDOW_FUNCTIONS = [
  'ROW_NUMBER', 'RANK', 'DENSE_RANK', 'NTILE',
  'LAG', 'LEAD', 'FIRST_VALUE', 'LAST_VALUE', 'NTH_VALUE',
  'PERCENT_RANK', 'CUME_DIST', 'PERCENTILE_CONT', 'PERCENTILE_DISC',
];

/* ---------- 类型转换关键字 ---------- */
const COMMON_TYPE_KEYWORDS = [
  'INT', 'INTEGER', 'BIGINT', 'SMALLINT', 'TINYINT',
  'DECIMAL', 'NUMERIC', 'FLOAT', 'REAL', 'DOUBLE',
  'CHAR', 'VARCHAR', 'NCHAR', 'NVARCHAR', 'TEXT', 'NTEXT',
  'DATE', 'TIME', 'DATETIME', 'DATETIME2', 'DATETIMEOFFSET',
  'SMALLDATETIME', 'TIMESTAMP', 'ROWVERSION',
  'BIT', 'BINARY', 'VARBINARY', 'IMAGE',
  'MONEY', 'SMALLMONEY', 'UNIQUEIDENTIFIER', 'XML',
  'SQL_VARIANT', 'HIERARCHYID', 'GEOMETRY', 'GEOGRAPHY',
  'BOOLEAN', 'BOOL', 'SERIAL', 'BIGSERIAL',
  'BLOB', 'CLOB', 'JSON', 'JSONB', 'UUID',
  'BYTEA', 'INTERVAL', 'CIDR', 'INET', 'MACADDR',
];

/* ========== SQL Server 专有 ========== */

/** SQL Server 系统全局变量（@@xxx） */
export const MSSQL_GLOBAL_VARIABLES = [
  '@@CONNECTIONS', '@@CPU_BUSY', '@@CURSOR_ROWS', '@@DATEFIRST',
  '@@DBTS', '@@DEFAULT_LANGID', '@@ERROR', '@@FETCH_STATUS',
  '@@IDENTITY', '@@IDLE', '@@IO_BUSY', '@@LANGID', '@@LANGUAGE',
  '@@LOCK_TIMEOUT', '@@MAX_CONNECTIONS', '@@MAX_PRECISION',
  '@@NESTLEVEL', '@@OPTIONS', '@@PACK_RECEIVED', '@@PACK_SENT',
  '@@PACKET_ERRORS', '@@PROCID', '@@REMSERVER', '@@ROWCOUNT',
  '@@SERVERNAME', '@@SERVICENAME', '@@SPID', '@@TEXTSIZE',
  '@@TIMETICKS', '@@TOTAL_ERRORS', '@@TOTAL_READ', '@@TOTAL_WRITE',
  '@@TRANCOUNT', '@@VERSION',
];

/** SQL Server 专有关键字 */
const MSSQL_EXTRA_KEYWORDS = [
  'GO', 'MERGE', 'OUTPUT', 'INSERTED', 'DELETED', 'APPLY',
  'CROSS', 'OUTER', 'APPLY', 'TABLESAMPLE', 'READPAST',
  'NOLOCK', 'HOLDLOCK', 'UPDLOCK', 'ROWLOCK', 'PAGLOCK', 'TABLOCK',
  'TABLOCKX', 'XLOCK', 'SERIALIZABLE', 'SNAPSHOT', 'READ',
  'COMMITTED', 'UNCOMMITTED', 'REPEATABLE',
  'BULK', 'OPENROWSET', 'OPENQUERY', 'OPENDATASOURCE',
  'WAITFOR', 'DELAY', 'RECONFIGURE', 'SHUTDOWN',
  'DBCC', 'BACKUP', 'RESTORE', 'ATTACH', 'DETACH',
  'STATISTICS', 'RECOMPILE', 'OPTION', 'MAXRECURSION',
  'NONCLUSTERED', 'CLUSTERED', 'INCLUDE', 'FILLFACTOR',
  'SPARSE', 'FILESTREAM', 'ROWGUIDCOL',
  'COMPUTE', 'CONTAINS', 'FREETEXT', 'FREETEXTTABLE',
  'CONTAINSTABLE', 'SEMANTIC', 'FULLTEXT',
  'XML', 'FOR', 'PATH', 'RAW', 'AUTO', 'EXPLICIT', 'ELEMENTS',
  'TRY_CONVERT', 'TRY_CAST', 'TRY_PARSE',
  'FORMAT', 'IIF', 'CHOOSE', 'CONCAT', 'CONCAT_WS',
  'STRING_AGG', 'STRING_SPLIT', 'STRING_ESCAPE', 'TRANSLATE',
  'TRIM', 'GREATEST', 'LEAST',
  'APPROX_COUNT_DISTINCT', 'APPROX_PERCENTILE_CONT', 'APPROX_PERCENTILE_DISC',
  'GENERATE_SERIES', 'DATE_BUCKET', 'DATETRUNC',
];

/** SQL Server 专有系统函数 */
const MSSQL_EXTRA_FUNCTIONS = [
  'OBJECT_DEFINITION', 'OBJECT_SCHEMA_NAME',
  'INDEX_COL', 'INDEXKEY_PROPERTY', 'INDEXPROPERTY',
  'FILE_ID', 'FILE_IDEX', 'FILE_NAME', 'FILEGROUP_ID', 'FILEGROUP_NAME',
  'FULLTEXTCATALOGPROPERTY', 'FULLTEXTSERVICEPROPERTY',
  'ASSEMBLYPROPERTY', 'CERT_ID', 'LOGINPROPERTY',
  'SUSER_ID', 'USER_ID', 'IS_MEMBER', 'IS_ROLEMEMBER', 'IS_SRVROLEMEMBER',
  'PERMISSIONS', 'PWDCOMPARE', 'PWDENCRYPT',
  'HASHBYTES', 'CHECKSUM', 'BINARY_CHECKSUM',
  'COMPRESS', 'DECOMPRESS', 'CERTENCODED', 'CERTPRIVATEKEY',
  'ENCRYPTBYKEY', 'DECRYPTBYKEY', 'ENCRYPTBYPASSPHRASE', 'DECRYPTBYPASSPHRASE',
  'KEY_ID', 'KEY_NAME', 'KEY_GUID',
  'SIGNBYASYMKEY', 'SIGNBYCERT', 'VERIFYSIGNEDBYCERT', 'VERIFYSIGNEDBYASYMKEY',
  'XACT_STATE', 'SESSION_CONTEXT', 'CONTEXT_INFO',
  'FORMATMESSAGE', 'STUFF', 'PATINDEX', 'CHARINDEX',
  'DIFFERENCE', 'SOUNDEX', 'SPACE', 'REPLICATE',
  'QUOTENAME', 'PARSENAME',
  'sp_rename', 'sp_help', 'sp_helptext', 'sp_helpindex', 'sp_helpdb',
  'sp_who', 'sp_who2', 'sp_lock', 'sp_spaceused',
  'sp_columns', 'sp_tables', 'sp_pkeys', 'sp_fkeys',
  'sp_addextendedproperty', 'sp_updateextendedproperty', 'sp_dropextendedproperty',
  'sp_executesql', 'sp_configure', 'sp_recompile',
  'sp_adduser', 'sp_dropuser', 'sp_addrole', 'sp_droprole',
  'sp_addrolemember', 'sp_droprolemember',
  'sp_addlinkedserver', 'sp_linkedservers',
];

/** SQL Server 系统表/视图/DMV/DMF（不带 sys. 前缀，单独提示 schema 和对象名） */
const MSSQL_SYSTEM_OBJECTS = [
  // 顶级 schema
  'sys', 'INFORMATION_SCHEMA',

  // 核心目录视图
  'objects', 'all_objects', 'system_objects',
  'tables', 'views', 'columns', 'all_columns', 'system_columns',
  'indexes', 'index_columns', 'types', 'schemas', 'databases',
  'server_principals', 'database_principals', 'procedures', 'all_parameters',
  'parameters', 'sql_modules', 'all_sql_modules',
  'triggers', 'server_triggers', 'all_views',
  'foreign_keys', 'foreign_key_columns',
  'key_constraints', 'default_constraints', 'check_constraints',
  'computed_columns', 'identity_columns',
  'extended_properties', 'partitions', 'allocation_units',
  'stats', 'stats_columns',
  'sequences', 'synonyms', 'numbered_procedures',
  'xml_schema_collections', 'xml_schema_namespaces', 'xml_schema_elements',
  'service_queues', 'services', 'conversation_endpoints',
  'routes', 'remote_service_bindings', 'transmission_queue',
  'change_tracking_tables', 'change_tracking_databases',
  'filetables', 'column_store_segments', 'column_store_dictionaries',
  'sensitivity_classifications', 'ledger_table_history',

  // 安全/权限
  'server_permissions', 'database_permissions',
  'login_token', 'user_token',
  'server_role_members', 'database_role_members',
  'credentials', 'server_audits', 'server_audit_specifications',
  'database_audit_specifications', 'certificates', 'asymmetric_keys',
  'symmetric_keys', 'column_encryption_keys', 'column_master_keys',
  'database_scoped_credentials',

  // 配置/元数据
  'configurations', 'messages', 'server_events', 'event_notifications',
  'server_event_sessions', 'database_event_sessions',
  'fulltext_catalogs', 'fulltext_indexes', 'fulltext_index_columns',
  'fulltext_languages', 'fulltext_stoplists', 'fulltext_stopwords',
  'registered_search_property_lists', 'registered_search_properties',
  'assemblies', 'assembly_modules', 'assembly_types', 'assembly_files',
  'data_spaces', 'filegroups', 'database_files', 'master_files',
  'backup_devices',

  // dm_exec_* — 执行相关 DMV/DMF
  'dm_exec_requests', 'dm_exec_sessions', 'dm_exec_connections',
  'dm_exec_query_stats', 'dm_exec_cached_plans',
  'dm_exec_sql_text', 'dm_exec_query_plan',
  'dm_exec_procedure_stats', 'dm_exec_trigger_stats',
  'dm_exec_function_stats', 'dm_exec_text_query_plan',
  'dm_exec_query_plan_stats', 'dm_exec_session_wait_stats',
  'dm_exec_input_buffer', 'dm_exec_describe_first_result_set',
  'dm_exec_describe_first_result_set_for_object',
  'dm_exec_plan_attributes', 'dm_exec_valid_use_hints',
  'dm_exec_compute_node_status', 'dm_exec_distributed_requests',
  'dm_exec_external_work', 'dm_exec_dms_workers', 'dm_exec_dms_services',

  // dm_os_* — 操作系统相关 DMV
  'dm_os_wait_stats', 'dm_os_waiting_tasks',
  'dm_os_performance_counters', 'dm_os_buffer_descriptors',
  'dm_os_memory_clerks', 'dm_os_memory_cache_counters',
  'dm_os_memory_cache_entries', 'dm_os_memory_cache_hash_tables',
  'dm_os_memory_objects', 'dm_os_memory_pools', 'dm_os_memory_nodes',
  'dm_os_nodes', 'dm_os_schedulers', 'dm_os_threads', 'dm_os_workers',
  'dm_os_tasks', 'dm_os_spinlock_stats', 'dm_os_latch_stats',
  'dm_os_ring_buffers', 'dm_os_sys_info', 'dm_os_sys_memory',
  'dm_os_process_memory', 'dm_os_loaded_modules',
  'dm_os_cluster_nodes', 'dm_os_cluster_properties',
  'dm_os_dispatcher_pool_stats', 'dm_os_host_info',
  'dm_os_enumerate_fixed_drives', 'dm_os_virtual_address_dump',
  'dm_os_stacks', 'dm_os_volume_stats',

  // dm_db_* — 数据库级 DMV
  'dm_db_index_usage_stats', 'dm_db_index_physical_stats',
  'dm_db_index_operational_stats', 'dm_db_missing_index_details',
  'dm_db_missing_index_groups', 'dm_db_missing_index_group_stats',
  'dm_db_missing_index_columns',
  'dm_db_partition_stats', 'dm_db_file_space_usage',
  'dm_db_session_space_usage', 'dm_db_task_space_usage',
  'dm_db_persisted_sku_features', 'dm_db_uncontained_entities',
  'dm_db_log_space_usage', 'dm_db_log_info', 'dm_db_log_stats',
  'dm_db_xtp_table_memory_stats', 'dm_db_xtp_index_stats',
  'dm_db_column_store_row_group_physical_stats',
  'dm_db_column_store_row_group_operational_stats',
  'dm_db_stats_properties', 'dm_db_incremental_stats_properties',
  'dm_db_stats_histogram',

  // dm_tran_* — 事务相关 DMV
  'dm_tran_locks', 'dm_tran_active_transactions',
  'dm_tran_session_transactions', 'dm_tran_database_transactions',
  'dm_tran_current_transaction', 'dm_tran_current_snapshot',
  'dm_tran_active_snapshot_database_transactions',
  'dm_tran_version_store', 'dm_tran_top_version_generators',
  'dm_tran_commit_table',

  // dm_io_* — IO 相关 DMV
  'dm_io_virtual_file_stats', 'dm_io_pending_io_requests',
  'dm_io_cluster_valid_path_names', 'dm_io_cluster_shared_drives',

  // dm_clr_*, dm_fts_*, dm_qn_*, dm_repl_*, dm_broker_*
  'dm_clr_loaded_assemblies', 'dm_clr_tasks', 'dm_clr_appdomains',
  'dm_clr_properties',
  'dm_fts_active_catalogs', 'dm_fts_index_population',
  'dm_fts_memory_buffers', 'dm_fts_memory_pools',
  'dm_fts_outstanding_batches', 'dm_fts_population_ranges',
  'dm_fts_semantic_similarity_population',
  'dm_qn_subscriptions',
  'dm_repl_articles', 'dm_repl_schemas', 'dm_repl_tranhash',
  'dm_broker_connections', 'dm_broker_forwarded_messages',
  'dm_broker_queue_monitors', 'dm_broker_activated_tasks',

  // dm_hadr_* — Always On 相关
  'dm_hadr_availability_group_states', 'dm_hadr_availability_replica_states',
  'dm_hadr_database_replica_states', 'dm_hadr_database_replica_cluster_states',
  'dm_hadr_cluster', 'dm_hadr_cluster_members', 'dm_hadr_cluster_networks',
  'dm_hadr_instance_node_map', 'dm_hadr_name_id_map',

  // dm_xe_* — Extended Events
  'dm_xe_sessions', 'dm_xe_session_events', 'dm_xe_session_targets',
  'dm_xe_session_event_actions', 'dm_xe_objects', 'dm_xe_object_columns',
  'dm_xe_packages', 'dm_xe_map_values',

  // dm_resource_*, dm_server_*, dm_tcp_*
  'dm_resource_governor_configuration', 'dm_resource_governor_resource_pools',
  'dm_resource_governor_workload_groups', 'dm_resource_governor_external_resource_pools',
  'dm_server_services', 'dm_server_registry', 'dm_server_memory_dumps',
  'dm_server_audit_status',
  'dm_tcp_listener_states',

  // fn_* — 系统函数（表值/标量）
  'fn_get_sql', 'fn_virtualfilestats', 'fn_trace_getinfo',
  'fn_trace_geteventinfo', 'fn_trace_getfilterinfo', 'fn_trace_gettable',
  'fn_dblog', 'fn_dump_dblog', 'fn_xe_file_target_read_file',
  'fn_get_audit_file', 'fn_builtin_permissions',
  'fn_my_permissions', 'fn_check_object_signatures',
  'fn_listextendedproperty', 'fn_servershareddrives',
  'fn_virtualservernodes', 'fn_helpcollations',
  'fn_PhysLocFormatter', 'fn_PageResCracker',

  // 旧式兼容视图
  'sysprocesses', 'syscolumns', 'sysobjects', 'sysindexes',
  'sysusers', 'sysdatabases', 'syslogins', 'sysmembers',
  'syspermissions', 'sysprotects', 'sysreferences',
  'syscomments', 'sysconstraints', 'sysdepends',
  'sysfilegroups', 'sysfiles', 'sysforeignkeys',
  'sysfulltextcatalogs', 'sysindexkeys', 'syslockinfo',
  'sysmessages', 'sysoledbusers', 'sysopentapes',
  'sysperfinfo', 'sysservers', 'systypes',

  // INFORMATION_SCHEMA 视图
  'TABLES', 'COLUMNS', 'VIEWS', 'ROUTINES',
  'KEY_COLUMN_USAGE', 'TABLE_CONSTRAINTS',
  'REFERENTIAL_CONSTRAINTS', 'SCHEMATA',
  'CHECK_CONSTRAINTS', 'COLUMN_DOMAIN_USAGE', 'COLUMN_PRIVILEGES',
  'CONSTRAINT_COLUMN_USAGE', 'CONSTRAINT_TABLE_USAGE',
  'DOMAIN_CONSTRAINTS', 'DOMAINS', 'PARAMETERS',
  'ROUTINE_COLUMNS', 'TABLE_PRIVILEGES', 'VIEW_COLUMN_USAGE',
  'VIEW_TABLE_USAGE',

  // master 下的特殊系统表
  'spt_fallback_db', 'spt_fallback_dev', 'spt_fallback_usg',
  'spt_values', 'spt_monitor',
];

/* ========== MySQL 专有 ========== */

const MYSQL_EXTRA_KEYWORDS = [
  'DESCRIBE', 'SHOW', 'EXPLAIN', 'USE', 'DATABASES', 'TABLES',
  'COLUMNS', 'PROCESSLIST', 'STATUS', 'VARIABLES', 'WARNINGS',
  'ERRORS', 'ENGINE', 'ENGINES', 'CHARSET', 'CHARACTER',
  'COLLATION', 'GRANTS', 'PRIVILEGES', 'FLUSH', 'RESET',
  'PURGE', 'CHANGE', 'MASTER', 'SLAVE', 'START', 'STOP',
  'HANDLER', 'LOAD', 'DATA', 'INFILE', 'OUTFILE',
  'REPLACE', 'IGNORE', 'LOCK', 'UNLOCK', 'FORCE', 'STRAIGHT_JOIN',
  'SQL_CALC_FOUND_ROWS', 'FOUND_ROWS', 'HIGH_PRIORITY', 'LOW_PRIORITY',
  'DELAYED', 'DUPLICATE', 'REGEXP', 'RLIKE', 'BINARY',
  'ENUM', 'SET', 'YEAR', 'UNSIGNED', 'ZEROFILL', 'AUTO_INCREMENT',
  'ON', 'UPDATE', 'CURRENT_TIMESTAMP',
  'IF', 'ELSEIF', 'LOOP', 'LEAVE', 'ITERATE', 'REPEAT', 'UNTIL',
];

const MYSQL_EXTRA_FUNCTIONS = [
  'IFNULL', 'NULLIF', 'IF', 'ELT', 'FIELD', 'FIND_IN_SET',
  'MAKE_SET', 'EXPORT_SET', 'BIN', 'OCT', 'HEX', 'UNHEX',
  'CONV', 'INET_ATON', 'INET_NTOA', 'INET6_ATON', 'INET6_NTOA',
  'IS_IPV4', 'IS_IPV6',
  'BENCHMARK', 'CHARSET', 'COERCIBILITY', 'COLLATION',
  'CONNECTION_ID', 'DATABASE', 'LAST_INSERT_ID', 'ROW_COUNT',
  'SCHEMA', 'USER', 'VERSION', 'FOUND_ROWS',
  'UUID', 'UUID_SHORT', 'UUID_TO_BIN', 'BIN_TO_UUID',
  'SLEEP', 'GET_LOCK', 'RELEASE_LOCK', 'IS_FREE_LOCK', 'IS_USED_LOCK',
  'SHA1', 'SHA2', 'MD5', 'AES_ENCRYPT', 'AES_DECRYPT',
  'COMPRESS', 'UNCOMPRESS', 'UNCOMPRESSED_LENGTH',
  'LPAD', 'RPAD', 'INSERT', 'LOCATE', 'INSTR', 'MID',
  'REPEAT', 'CHAR_LENGTH', 'CHARACTER_LENGTH', 'OCTET_LENGTH', 'BIT_LENGTH',
  'POSITION', 'SUBSTRING_INDEX', 'MAKE_SET', 'EXPORT_SET',
  'DATE_FORMAT', 'TIME_FORMAT', 'STR_TO_DATE', 'DATE_ADD', 'DATE_SUB',
  'ADDDATE', 'SUBDATE', 'ADDTIME', 'SUBTIME', 'DATEDIFF', 'TIMEDIFF',
  'PERIOD_ADD', 'PERIOD_DIFF', 'TIMESTAMP', 'TIMESTAMPADD', 'TIMESTAMPDIFF',
  'DAYOFWEEK', 'DAYOFYEAR', 'DAYOFMONTH', 'DAYNAME', 'MONTHNAME',
  'WEEKDAY', 'WEEKOFYEAR', 'WEEK', 'QUARTER', 'HOUR', 'MINUTE', 'SECOND',
  'MICROSECOND', 'TO_DAYS', 'TO_SECONDS', 'FROM_DAYS', 'FROM_UNIXTIME',
  'UNIX_TIMESTAMP', 'UTC_DATE', 'UTC_TIME', 'UTC_TIMESTAMP',
  'NOW', 'CURDATE', 'CURTIME', 'LOCALTIME', 'LOCALTIMESTAMP', 'SYSDATE',
  'JSON_EXTRACT', 'JSON_UNQUOTE', 'JSON_SET', 'JSON_INSERT', 'JSON_REPLACE',
  'JSON_REMOVE', 'JSON_CONTAINS', 'JSON_CONTAINS_PATH',
  'JSON_TYPE', 'JSON_VALID', 'JSON_KEYS', 'JSON_LENGTH',
  'JSON_DEPTH', 'JSON_SEARCH', 'JSON_ARRAY', 'JSON_OBJECT',
  'JSON_ARRAYAGG', 'JSON_OBJECTAGG', 'JSON_PRETTY',
  'JSON_TABLE', 'JSON_VALUE', 'JSON_MERGE_PATCH', 'JSON_MERGE_PRESERVE',
  'GROUP_CONCAT', 'BIT_AND', 'BIT_OR', 'BIT_XOR',
  'ANY_VALUE', 'STD', 'STDDEV', 'STDDEV_POP', 'STDDEV_SAMP',
  'VARIANCE', 'VAR_POP', 'VAR_SAMP',
];

const MYSQL_SYSTEM_OBJECTS = [
  // 顶级 schema
  'information_schema', 'mysql', 'performance_schema', 'sys',

  // information_schema 视图
  'TABLES', 'COLUMNS', 'VIEWS', 'ROUTINES', 'SCHEMATA',
  'KEY_COLUMN_USAGE', 'TABLE_CONSTRAINTS', 'STATISTICS',
  'REFERENTIAL_CONSTRAINTS', 'PROCESSLIST', 'ENGINES',
  'CHARACTER_SETS', 'COLLATIONS', 'COLLATION_CHARACTER_SET_APPLICABILITY',
  'TRIGGERS', 'EVENTS', 'PARTITIONS', 'FILES',
  'INNODB_BUFFER_PAGE', 'INNODB_BUFFER_POOL_STATS',
  'INNODB_CMP', 'INNODB_CMP_RESET', 'INNODB_CMPMEM', 'INNODB_CMPMEM_RESET',
  'INNODB_LOCK_WAITS', 'INNODB_LOCKS', 'INNODB_TRX',
  'INNODB_METRICS', 'INNODB_SYS_TABLES', 'INNODB_SYS_COLUMNS',
  'INNODB_SYS_INDEXES', 'INNODB_SYS_FIELDS',
  'INNODB_SYS_TABLESPACES', 'INNODB_SYS_DATAFILES',
  'INNODB_SYS_FOREIGN', 'INNODB_SYS_FOREIGN_COLS',
  'INNODB_FT_DEFAULT_STOPWORD', 'INNODB_FT_CONFIG',
  'INNODB_FT_INDEX_TABLE', 'INNODB_FT_INDEX_CACHE',
  'OPTIMIZER_TRACE', 'PARAMETERS', 'PROFILING',
  'COLUMN_PRIVILEGES', 'TABLE_PRIVILEGES', 'SCHEMA_PRIVILEGES',
  'USER_PRIVILEGES', 'GLOBAL_STATUS', 'GLOBAL_VARIABLES',
  'SESSION_STATUS', 'SESSION_VARIABLES',

  // mysql schema 系统表
  'user', 'db', 'tables_priv', 'columns_priv', 'procs_priv',
  'proc', 'event', 'general_log', 'slow_log',
  'plugin', 'func', 'help_topic', 'help_category', 'help_keyword',
  'time_zone', 'time_zone_name', 'time_zone_transition',
  'servers', 'innodb_table_stats', 'innodb_index_stats',
  'slave_master_info', 'slave_relay_log_info', 'slave_worker_info',
  'gtid_executed',

  // performance_schema 表
  'threads', 'events_statements_summary_by_digest',
  'events_statements_current', 'events_statements_history',
  'events_waits_current', 'events_waits_history', 'events_waits_summary_global_by_event_name',
  'events_stages_current', 'events_stages_history',
  'file_instances', 'file_summary_by_instance',
  'mutex_instances', 'rwlock_instances', 'socket_instances',
  'table_io_waits_summary_by_table', 'table_lock_waits_summary_by_table',
  'memory_summary_global_by_event_name', 'memory_summary_by_thread_by_event_name',
  'session_variables', 'global_variables', 'status_by_thread',
  'replication_connection_status', 'replication_applier_status',
  'setup_instruments', 'setup_consumers', 'setup_actors', 'setup_objects',
];

/* ========== PostgreSQL 专有 ========== */

const POSTGRESQL_EXTRA_KEYWORDS = [
  'RETURNING', 'ILIKE', 'SIMILAR', 'LATERAL', 'MATERIALIZED',
  'RECURSIVE', 'DEFERRABLE', 'INITIALLY', 'DEFERRED', 'IMMEDIATE',
  'INHERITS', 'TABLESPACE', 'EXTENSION', 'CONCURRENTLY',
  'ONLY', 'USING', 'VACUUM', 'ANALYZE', 'REINDEX', 'CLUSTER',
  'NOTIFY', 'LISTEN', 'UNLISTEN', 'COPY', 'PERFORM', 'RAISE',
  'NOTICE', 'EXCEPTION', 'FOUND', 'NEW', 'OLD',
  'DO', 'LANGUAGE', 'PLPGSQL', 'SQL',
  'GENERATED', 'ALWAYS', 'STORED', 'OVERRIDING', 'SYSTEM', 'VALUE',
  'CONFLICT', 'NOTHING', 'EXCLUDED',
  'ARRAY', 'UNNEST', 'VARIADIC',
  'WINDOW', 'FILTER',
];

const POSTGRESQL_EXTRA_FUNCTIONS = [
  'COALESCE', 'NULLIF', 'GREATEST', 'LEAST',
  'ARRAY_AGG', 'ARRAY_LENGTH', 'ARRAY_UPPER', 'ARRAY_LOWER',
  'ARRAY_REMOVE', 'ARRAY_REPLACE', 'ARRAY_CAT', 'ARRAY_APPEND', 'ARRAY_PREPEND',
  'ARRAY_TO_STRING', 'STRING_TO_ARRAY', 'UNNEST',
  'GENERATE_SERIES', 'GENERATE_SUBSCRIPTS',
  'TO_CHAR', 'TO_DATE', 'TO_TIMESTAMP', 'TO_NUMBER',
  'AGE', 'DATE_TRUNC', 'DATE_PART', 'EXTRACT', 'ISFINITE',
  'JUSTIFY_DAYS', 'JUSTIFY_HOURS', 'JUSTIFY_INTERVAL',
  'CLOCK_TIMESTAMP', 'STATEMENT_TIMESTAMP', 'TRANSACTION_TIMESTAMP',
  'TIMEOFDAY', 'NOW',
  'CURRENT_DATABASE', 'CURRENT_SCHEMA', 'CURRENT_SCHEMAS',
  'CURRENT_SETTING', 'SET_CONFIG',
  'PG_BACKEND_PID', 'PG_CANCEL_BACKEND', 'PG_TERMINATE_BACKEND',
  'PG_COLUMN_SIZE', 'PG_DATABASE_SIZE', 'PG_INDEXES_SIZE',
  'PG_RELATION_SIZE', 'PG_SIZE_PRETTY', 'PG_TABLE_SIZE', 'PG_TOTAL_RELATION_SIZE',
  'PG_TABLESPACE_SIZE', 'PG_TYPEOF',
  'OBJ_DESCRIPTION', 'COL_DESCRIPTION', 'SHOBJ_DESCRIPTION',
  'HAS_TABLE_PRIVILEGE', 'HAS_SCHEMA_PRIVILEGE', 'HAS_DATABASE_PRIVILEGE',
  'HAS_COLUMN_PRIVILEGE', 'HAS_FUNCTION_PRIVILEGE', 'HAS_SEQUENCE_PRIVILEGE',
  'PG_GET_CONSTRAINTDEF', 'PG_GET_INDEXDEF', 'PG_GET_VIEWDEF',
  'PG_GET_RULEDEF', 'PG_GET_TRIGGERDEF', 'PG_GET_FUNCTIONDEF', 'PG_GET_EXPR',
  'REGEXP_MATCH', 'REGEXP_MATCHES', 'REGEXP_REPLACE', 'REGEXP_SPLIT_TO_TABLE',
  'REGEXP_SPLIT_TO_ARRAY',
  'ENCODE', 'DECODE', 'MD5', 'SHA256', 'SHA512', 'GEN_RANDOM_UUID',
  'JSONB_BUILD_OBJECT', 'JSONB_BUILD_ARRAY', 'JSONB_AGG',
  'JSONB_OBJECT_AGG', 'JSONB_EXTRACT_PATH', 'JSONB_EXTRACT_PATH_TEXT',
  'JSONB_SET', 'JSONB_INSERT', 'JSONB_PRETTY', 'JSONB_STRIP_NULLS',
  'JSONB_EACH', 'JSONB_EACH_TEXT', 'JSONB_ARRAY_ELEMENTS',
  'JSONB_ARRAY_ELEMENTS_TEXT', 'JSONB_TYPEOF', 'JSONB_ARRAY_LENGTH',
  'JSON_BUILD_OBJECT', 'JSON_BUILD_ARRAY', 'JSON_AGG', 'JSON_OBJECT_AGG',
  'JSON_EXTRACT_PATH', 'JSON_EXTRACT_PATH_TEXT',
  'JSON_EACH', 'JSON_EACH_TEXT', 'JSON_ARRAY_ELEMENTS', 'JSON_TYPEOF',
  'BOOL_AND', 'BOOL_OR', 'BIT_AND', 'BIT_OR',
  'STRING_AGG', 'ARRAY_AGG', 'XMLAGG',
  'OVERLAY', 'POSITION', 'BTRIM', 'LPAD', 'RPAD',
  'INITCAP', 'CHR', 'REPEAT', 'LEFT', 'RIGHT',
  'SPLIT_PART', 'TRANSLATE', 'FORMAT',
];

const POSTGRESQL_SYSTEM_OBJECTS = [
  // 顶级 schema
  'pg_catalog', 'information_schema', 'pg_toast', 'pg_temp',

  // pg_catalog 目录表
  'pg_class', 'pg_namespace', 'pg_attribute', 'pg_type', 'pg_index',
  'pg_constraint', 'pg_trigger', 'pg_proc', 'pg_database',
  'pg_tablespace', 'pg_roles', 'pg_user', 'pg_group',
  'pg_auth_members', 'pg_authid', 'pg_am', 'pg_amop', 'pg_amproc',
  'pg_attrdef', 'pg_cast', 'pg_collation', 'pg_conversion',
  'pg_default_acl', 'pg_depend', 'pg_description',
  'pg_enum', 'pg_event_trigger', 'pg_extension',
  'pg_foreign_data_wrapper', 'pg_foreign_server', 'pg_foreign_table',
  'pg_inherits', 'pg_init_privs', 'pg_language',
  'pg_largeobject', 'pg_largeobject_metadata',
  'pg_opclass', 'pg_operator', 'pg_opfamily',
  'pg_partitioned_table', 'pg_policy', 'pg_publication',
  'pg_publication_rel', 'pg_range', 'pg_replication_origin',
  'pg_rewrite', 'pg_seclabel', 'pg_sequence',
  'pg_shdepend', 'pg_shdescription', 'pg_shseclabel',
  'pg_statistic', 'pg_statistic_ext', 'pg_statistic_ext_data',
  'pg_subscription', 'pg_subscription_rel',
  'pg_transform', 'pg_ts_config', 'pg_ts_config_map',
  'pg_ts_dict', 'pg_ts_parser', 'pg_ts_template',
  'pg_user_mapping',

  // 统计视图
  'pg_stat_activity', 'pg_stat_all_tables', 'pg_stat_user_tables',
  'pg_stat_sys_tables', 'pg_stat_all_indexes', 'pg_stat_user_indexes',
  'pg_stat_sys_indexes', 'pg_stat_io', 'pg_stat_bgwriter',
  'pg_stat_database', 'pg_stat_database_conflicts',
  'pg_stat_replication', 'pg_stat_wal_receiver',
  'pg_stat_subscription', 'pg_stat_ssl', 'pg_stat_gssapi',
  'pg_stat_archiver', 'pg_stat_progress_vacuum',
  'pg_stat_progress_create_index', 'pg_stat_progress_analyze',
  'pg_stat_progress_basebackup', 'pg_stat_progress_copy',
  'pg_stat_progress_cluster',
  'pg_stat_user_functions', 'pg_stat_xact_user_tables',
  'pg_stat_xact_user_functions',
  'pg_statio_all_tables', 'pg_statio_user_tables', 'pg_statio_sys_tables',
  'pg_statio_all_indexes', 'pg_statio_user_indexes', 'pg_statio_sys_indexes',
  'pg_statio_all_sequences', 'pg_statio_user_sequences', 'pg_statio_sys_sequences',

  // 锁/其他系统视图
  'pg_locks', 'pg_settings', 'pg_file_settings', 'pg_hba_file_rules',
  'pg_cursors', 'pg_prepared_statements', 'pg_prepared_xacts',
  'pg_available_extensions', 'pg_available_extension_versions',
  'pg_views', 'pg_tables', 'pg_indexes', 'pg_matviews',
  'pg_sequences', 'pg_rules', 'pg_policies',
  'pg_timezone_names', 'pg_timezone_abbrevs',
  'pg_replication_slots',
  'pg_stat_wal', 'pg_stat_slru',

  // INFORMATION_SCHEMA
  'tables', 'columns', 'views', 'routines', 'schemata',
  'key_column_usage', 'table_constraints', 'referential_constraints',
  'check_constraints', 'column_privileges', 'table_privileges',
  'domains', 'parameters', 'sequences',
];

/* ========== SQLite 专有 ========== */

const SQLITE_EXTRA_KEYWORDS = [
  'PRAGMA', 'ATTACH', 'DETACH', 'VACUUM', 'REINDEX',
  'EXPLAIN', 'QUERY', 'PLAN', 'GLOB', 'AUTOINCREMENT',
  'ROWID', 'OID', '_ROWID_', 'WITHOUT', 'ROWID',
  'STRICT', 'CONFLICT', 'ABORT', 'FAIL', 'IGNORE', 'REPLACE',
  'INSTEAD', 'OF', 'BEFORE', 'AFTER', 'EACH', 'FOR',
  'RAISE', 'IMMEDIATE', 'DEFERRED', 'EXCLUSIVE',
  'IF', 'NOT', 'EXISTS',
  'INDEXED', 'UNINDEXED', 'VIRTUAL', 'USING', 'FTS5', 'FTS4', 'FTS3',
  'CONTENT', 'TOKENIZE', 'RTREE',
];

const SQLITE_EXTRA_FUNCTIONS = [
  'TYPEOF', 'TOTAL', 'GROUP_CONCAT', 'ZEROBLOB',
  'LIKELIHOOD', 'LIKELY', 'UNLIKELY',
  'GLOB', 'INSTR', 'PRINTF', 'QUOTE',
  'RANDOMBLOB', 'HEX', 'UNHEX',
  'TOTAL_CHANGES', 'CHANGES', 'LAST_INSERT_ROWID',
  'SQLITE_VERSION', 'SQLITE_SOURCE_ID', 'SQLITE_COMPILEOPTION_GET',
  'SQLITE_COMPILEOPTION_USED', 'SQLITE_OFFSET',
  'CHAR', 'UNICODE', 'SUBSTR', 'REPLACE', 'TRIM', 'LTRIM', 'RTRIM',
  'LENGTH', 'LOWER', 'UPPER', 'TYPEOF', 'QUOTE',
  'ABS', 'MAX', 'MIN', 'RANDOM', 'ROUND',
  'DATE', 'TIME', 'DATETIME', 'JULIANDAY', 'STRFTIME', 'UNIXEPOCH',
  'JSON', 'JSON_ARRAY', 'JSON_ARRAY_LENGTH', 'JSON_EXTRACT',
  'JSON_INSERT', 'JSON_OBJECT', 'JSON_PATCH', 'JSON_REMOVE',
  'JSON_REPLACE', 'JSON_SET', 'JSON_TYPE', 'JSON_VALID', 'JSON_QUOTE',
  'JSON_GROUP_ARRAY', 'JSON_GROUP_OBJECT', 'JSON_EACH', 'JSON_TREE',
  'IIF', 'NULLIF', 'COALESCE', 'IFNULL',
];

const SQLITE_PRAGMAS = [
  'pragma_table_info', 'pragma_table_xinfo', 'pragma_table_list',
  'pragma_index_list', 'pragma_index_info', 'pragma_index_xinfo',
  'pragma_foreign_key_list', 'pragma_foreign_key_check',
  'pragma_database_list', 'pragma_collation_list',
  'pragma_compile_options', 'pragma_function_list',
  'pragma_module_list', 'pragma_integrity_check',
  'pragma_quick_check', 'pragma_page_count', 'pragma_page_size',
  'pragma_freelist_count', 'pragma_journal_mode',
  'pragma_wal_checkpoint', 'pragma_auto_vacuum',
  'pragma_cache_size', 'pragma_encoding', 'pragma_synchronous',
  'pragma_temp_store', 'pragma_mmap_size', 'pragma_busy_timeout',
];

const SQLITE_SYSTEM_OBJECTS = [
  'sqlite_master', 'sqlite_schema', 'sqlite_temp_master', 'sqlite_temp_schema',
  'sqlite_sequence', 'sqlite_stat1', 'sqlite_stat2', 'sqlite_stat3', 'sqlite_stat4',
  '_rowid_', 'rowid', 'oid',
];

/* ========== 组合导出 ========== */

/** 对条目去重（不区分大小写） */
function dedupe(items: string[]): string[] {
  const seen = new Set<string>();
  const result: string[] = [];
  for (const item of items) {
    const key = item.toUpperCase();
    if (!seen.has(key)) {
      seen.add(key);
      result.push(item);
    }
  }
  return result;
}

export interface SqlCompletionSet {
  /** SQL 关键字 */
  keywords: string[];
  /** 内置函数（标量、聚合、窗口等） */
  functions: string[];
  /** 系统全局变量（如 @@VERSION） */
  globalVariables: string[];
  /** 系统表/视图 */
  systemObjects: string[];
}

export function getSqlCompletions(provider: DatabaseProviderType): SqlCompletionSet {
  switch (provider) {
    case 'sqlserver':
      return {
        keywords: dedupe([...COMMON_KEYWORDS, ...MSSQL_EXTRA_KEYWORDS, ...COMMON_TYPE_KEYWORDS]),
        functions: dedupe([
          ...COMMON_AGGREGATE_FUNCTIONS, ...COMMON_MATH_FUNCTIONS, ...COMMON_STRING_FUNCTIONS,
          ...COMMON_DATE_FUNCTIONS, ...COMMON_SYSTEM_FUNCTIONS, ...COMMON_WINDOW_FUNCTIONS,
          ...MSSQL_EXTRA_FUNCTIONS,
        ]),
        globalVariables: [...MSSQL_GLOBAL_VARIABLES],
        systemObjects: [...MSSQL_SYSTEM_OBJECTS],
      };

    case 'mysql':
      return {
        keywords: dedupe([...COMMON_KEYWORDS, ...MYSQL_EXTRA_KEYWORDS, ...COMMON_TYPE_KEYWORDS]),
        functions: dedupe([
          ...COMMON_AGGREGATE_FUNCTIONS, ...COMMON_MATH_FUNCTIONS, ...COMMON_STRING_FUNCTIONS,
          ...COMMON_DATE_FUNCTIONS, ...COMMON_SYSTEM_FUNCTIONS, ...COMMON_WINDOW_FUNCTIONS,
          ...MYSQL_EXTRA_FUNCTIONS,
        ]),
        globalVariables: [],
        systemObjects: [...MYSQL_SYSTEM_OBJECTS],
      };

    case 'postgresql':
      return {
        keywords: dedupe([...COMMON_KEYWORDS, ...POSTGRESQL_EXTRA_KEYWORDS, ...COMMON_TYPE_KEYWORDS]),
        functions: dedupe([
          ...COMMON_AGGREGATE_FUNCTIONS, ...COMMON_MATH_FUNCTIONS, ...COMMON_STRING_FUNCTIONS,
          ...COMMON_DATE_FUNCTIONS, ...COMMON_SYSTEM_FUNCTIONS, ...COMMON_WINDOW_FUNCTIONS,
          ...POSTGRESQL_EXTRA_FUNCTIONS,
        ]),
        globalVariables: [],
        systemObjects: [...POSTGRESQL_SYSTEM_OBJECTS],
      };

    case 'sqlite':
      return {
        keywords: dedupe([...COMMON_KEYWORDS, ...SQLITE_EXTRA_KEYWORDS, ...COMMON_TYPE_KEYWORDS]),
        functions: dedupe([
          ...COMMON_AGGREGATE_FUNCTIONS, ...COMMON_MATH_FUNCTIONS, ...COMMON_STRING_FUNCTIONS,
          ...COMMON_DATE_FUNCTIONS, ...COMMON_WINDOW_FUNCTIONS,
          ...SQLITE_EXTRA_FUNCTIONS,
        ]),
        globalVariables: [],
        systemObjects: [...SQLITE_SYSTEM_OBJECTS, ...SQLITE_PRAGMAS],
      };

    default:
      return {
        keywords: dedupe([...COMMON_KEYWORDS, ...COMMON_TYPE_KEYWORDS]),
        functions: dedupe([
          ...COMMON_AGGREGATE_FUNCTIONS, ...COMMON_MATH_FUNCTIONS, ...COMMON_STRING_FUNCTIONS,
          ...COMMON_DATE_FUNCTIONS, ...COMMON_WINDOW_FUNCTIONS,
        ]),
        globalVariables: [],
        systemObjects: [],
      };
  }
}
