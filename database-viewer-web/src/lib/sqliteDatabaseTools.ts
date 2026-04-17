import type { ProviderType } from '../types/explorer';

export type SqliteDatabaseToolKey =
  | 'sqlite-tool:optimize'
  | 'sqlite-tool:integrity-check'
  | 'sqlite-tool:quick-check'
  | 'sqlite-tool:foreign-key-check'
  | 'sqlite-tool:analyze'
  | 'sqlite-tool:vacuum';

export type SqliteDatabaseToolDefinition = {
  key: SqliteDatabaseToolKey
  label: string
  commandText: string
};

/**
 * 统一登记 SQLite/SQLCipher 的轻量数据库工具，
 * 右键菜单和 SQL 标签页预填内容都从这里生成，避免散落的 switch/case。
 */
const SQLITE_DATABASE_TOOLS: readonly SqliteDatabaseToolDefinition[] = [
  {
    key: 'sqlite-tool:optimize',
    label: '优化数据库 (PRAGMA optimize)',
    commandText: 'PRAGMA optimize;',
  },
  {
    key: 'sqlite-tool:integrity-check',
    label: '完整性检查 (PRAGMA integrity_check)',
    commandText: 'PRAGMA integrity_check;',
  },
  {
    key: 'sqlite-tool:quick-check',
    label: '快速完整性检查 (PRAGMA quick_check)',
    commandText: 'PRAGMA quick_check;',
  },
  {
    key: 'sqlite-tool:foreign-key-check',
    label: '外键检查 (PRAGMA foreign_key_check)',
    commandText: 'PRAGMA foreign_key_check;',
  },
  {
    key: 'sqlite-tool:analyze',
    label: '更新统计信息 (ANALYZE)',
    commandText: 'ANALYZE;',
  },
  {
    key: 'sqlite-tool:vacuum',
    label: '释放空间占用 (VACUUM)',
    commandText: 'VACUUM;',
  },
] as const;

function buildToolSqlText(database: string, tool: SqliteDatabaseToolDefinition) {
  return `-- SQLite 工具: ${tool.label}\n-- database: ${database}\n-- 手动点击“执行 SQL”运行\n\n${tool.commandText}`;
}

export function getSqliteDatabaseTools(provider: ProviderType) {
  return provider === 'sqlite' ? SQLITE_DATABASE_TOOLS : [];
}

export function findSqliteDatabaseTool(provider: ProviderType, key: string) {
  return getSqliteDatabaseTools(provider).find((tool) => tool.key === key) ?? null;
}

export function buildSqliteDatabaseToolSql(provider: ProviderType, database: string, key: string) {
  const tool = findSqliteDatabaseTool(provider, key);
  if (!tool) {
    return null;
  }

  return {
    tool,
    sqlText: buildToolSqlText(database, tool),
  };
}