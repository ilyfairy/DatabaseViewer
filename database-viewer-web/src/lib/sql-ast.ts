'use strict';

/**
 * SQL AST 辅助模块
 *
 * 基于 node-sql-parser，按需懒加载各数据库方言的 parser，
 * 提供上下文感知的智能补全信息。
 */

import type { DatabaseProviderType } from '../types/explorer';
import type { Parser, AST, Select, From, BaseFrom, Join } from 'node-sql-parser';

/** 从 AST 中提取的表引用信息 */
export interface AstTableRef {
  /** 数据库/schema 名 */
  schema: string | null;
  /** 表名 */
  table: string;
  /** 别名（AS name） */
  alias: string | null;
}

/** 从 AST 中提取的 CTE 名称列表 */
export interface AstAnalysis {
  /** 所有表引用（FROM / JOIN / UPDATE / DELETE） */
  tables: AstTableRef[];
  /** CTE 名称列表（WITH ... AS） */
  cteNames: string[];
  /** 子查询别名 */
  subqueryAliases: string[];
  /** 已声明的 @variable 名称（小写，含 @ 前缀） */
  declaredVariables: string[];
  /** 引用的 @variable 名称及位置 */
  referencedVariables: Array<{ name: string; line: number; column: number }>;
}

// ── Parser 缓存（按 provider 懒加载） ──

const parserCache = new Map<DatabaseProviderType, Parser>();

/** 将项目的 ProviderType 映射为 node-sql-parser 的 database 名 */
function toParserDatabase(provider: DatabaseProviderType): string {
  const mapping: Record<DatabaseProviderType, string> = {
    sqlserver: 'TransactSQL',
    postgresql: 'PostgresQL',
    mysql: 'MySQL',
    sqlite: 'Sqlite',
  };

  return mapping[provider];
}

/**
 * 按需加载指定 provider 的 parser 实例（动态导入，仅加载对应方言的 ~200KB parser）
 */
async function getParser(provider: DatabaseProviderType): Promise<Parser> {
  const cached = parserCache.get(provider);
  if (cached) {
    return cached;
  }

  // 动态导入对应方言的 parser（Vite 会自动做 code splitting）
  let module: { Parser: new () => Parser };

  switch (provider) {
    case 'sqlserver':
      module = await import('node-sql-parser/build/transactsql');
      break;
    case 'postgresql':
      module = await import('node-sql-parser/build/postgresql');
      break;
    case 'mysql':
      module = await import('node-sql-parser/build/mysql');
      break;
    case 'sqlite':
      module = await import('node-sql-parser/build/sqlite');
      break;
    default:
      module = await import('node-sql-parser/build/mysql');
  }

  const parser = new module.Parser();
  parserCache.set(provider, parser);
  return parser;
}

/**
 * 尝试解析 SQL 文本为 AST。
 * 如果 SQL 不完整或有语法错误，返回 null（不抛异常）。
 */
export async function tryParse(
  sql: string,
  provider: DatabaseProviderType,
): Promise<AST[] | null> {
  try {
    const parser = await getParser(provider);
    const database = toParserDatabase(provider);
    const result = parser.astify(sql, {
      database,
      parseOptions: { includeLocations: true },
    });
    return Array.isArray(result) ? result : [result];
  } catch {
    return null;
  }
}

/**
 * 智能解析策略：先尝试完整 SQL，失败后尝试截断到光标位置的前一个完整子句
 */
export async function tryParseForCompletion(
  fullSql: string,
  sqlBeforeCursor: string,
  provider: DatabaseProviderType,
): Promise<AST[] | null> {
  // 策略 1: 先尝试解析完整 SQL
  const fullAst = await tryParse(fullSql, provider);
  if (fullAst) {
    return fullAst;
  }

  // 策略 2: 截断到光标前最后一个分号，解析前面的完整语句
  const lastSemicolon = sqlBeforeCursor.lastIndexOf(';');
  if (lastSemicolon > 0) {
    const completePart = sqlBeforeCursor.slice(0, lastSemicolon);
    return tryParse(completePart, provider);
  }

  // 策略 3: 尝试给不完整 SQL 加上 dummy 补全使其可解析
  // 例如 "SELECT * FROM " → "SELECT * FROM __dummy__"
  const trimmed = sqlBeforeCursor.trimEnd();

  // FROM/JOIN 后面缺表名
  if (/\b(?:from|join|into|update|table)\s*$/i.test(trimmed)) {
    const patched = `${trimmed} __dummy__`;
    return tryParse(patched, provider);
  }

  // SELECT 后面缺列名
  if (/\bselect\s*$/i.test(trimmed)) {
    const patched = `${trimmed} * FROM __dummy__`;
    return tryParse(patched, provider);
  }

  // WHERE/AND/OR/ON 后面缺条件
  if (/\b(?:where|and|or|on|having)\s*$/i.test(trimmed)) {
    const patched = `${trimmed} 1=1`;
    return tryParse(patched, provider);
  }

  return null;
}

/**
 * 从 AST 数组中提取所有表引用、CTE 名称、子查询别名和变量声明
 */
export function extractAnalysis(astList: AST[]): AstAnalysis {
  const tables: AstTableRef[] = [];
  const cteNames: string[] = [];
  const subqueryAliases: string[] = [];
  const declaredVariables: string[] = [];
  const referencedVariables: Array<{ name: string; line: number; column: number }> = [];

  for (const ast of astList) {
    // DECLARE 语句：提取声明的变量
    const astRecord = ast as unknown as Record<string, unknown>;
    if (astRecord.type === 'declare' && Array.isArray(astRecord.declare)) {
      for (const decl of astRecord.declare) {
        const declObj = decl as { at?: string; name?: string };
        if (declObj.at && declObj.name) {
          declaredVariables.push(`${declObj.at}${declObj.name}`.toLowerCase());
        }
      }

      continue;
    }

    collectFromAst(ast, tables, cteNames, subqueryAliases);

    // 收集 SELECT / WHERE 等子句中引用的 @variable
    collectVariableRefs(astRecord, referencedVariables);
  }

  return { tables, cteNames, subqueryAliases, declaredVariables, referencedVariables };
}

/** 递归收集 AST 中所有 column_ref 引用的 @variable */
function collectVariableRefs(
  node: unknown,
  refs: Array<{ name: string; line: number; column: number }>,
): void {
  if (!node || typeof node !== 'object') {
    return;
  }

  const obj = node as Record<string, unknown>;

  // column_ref 类型：column 以 @ 开头的是变量引用
  if (obj.type === 'column_ref' && typeof obj.column === 'string' && obj.column.startsWith('@')) {
    const loc = obj.loc as { start?: { line?: number; column?: number } } | undefined;
    refs.push({
      name: obj.column.toLowerCase(),
      line: (loc?.start?.line ?? 0) - 1, // AST 是 1-indexed，Ace 是 0-indexed
      column: (loc?.start?.column ?? 0) - 1,
    });
  }

  // 递归遍历所有子属性
  for (const value of Object.values(obj)) {
    if (Array.isArray(value)) {
      for (const item of value) {
        collectVariableRefs(item, refs);
      }
    } else if (value && typeof value === 'object') {
      collectVariableRefs(value, refs);
    }
  }
}

/** 递归遍历单个 AST 节点 */
function collectFromAst(
  ast: AST,
  tables: AstTableRef[],
  cteNames: string[],
  subqueryAliases: string[],
): void {
  if (!ast || typeof ast !== 'object') {
    return;
  }

  // CTE (WITH ... AS)
  if ('with' in ast && Array.isArray(ast.with)) {
    for (const cte of ast.with) {
      if (cte?.name?.value) {
        cteNames.push(cte.name.value);
      }

      // CTE 内部的子查询也需要分析
      if (cte?.stmt?.ast) {
        collectFromAst(cte.stmt.ast as AST, tables, cteNames, subqueryAliases);
      }
    }
  }

  // FROM 子句
  if ('from' in ast && ast.from) {
    const fromList = Array.isArray(ast.from) ? ast.from : [ast.from];
    for (const fromItem of fromList) {
      collectFromItem(fromItem as From, tables, cteNames, subqueryAliases);
    }
  }

  // UPDATE 的 table 字段
  if ('type' in ast && ast.type === 'update' && 'table' in ast && Array.isArray(ast.table)) {
    for (const tableItem of ast.table) {
      collectFromItem(tableItem as From, tables, cteNames, subqueryAliases);
    }
  }

  // INSERT/REPLACE 的 table 字段
  if ('type' in ast && (ast.type === 'insert' || ast.type === 'replace') && 'table' in ast) {
    const insertTable = ast.table as BaseFrom[] | BaseFrom | null;
    if (insertTable) {
      const tableList = Array.isArray(insertTable) ? insertTable : [insertTable];
      for (const tableItem of tableList) {
        if (tableItem.table && tableItem.table !== '__dummy__') {
          tables.push({
            schema: tableItem.db || tableItem.schema || null,
            table: tableItem.table,
            alias: tableItem.as || null,
          });
        }
      }
    }
  }

  // UNION / INTERSECT / EXCEPT（_next 链）
  if ('_next' in ast && (ast as Select)._next) {
    collectFromAst((ast as Select)._next as AST, tables, cteNames, subqueryAliases);
  }

  // WHERE / HAVING 中的子查询
  if ('where' in ast && ast.where) {
    collectSubqueries(ast.where as Record<string, unknown>, tables, cteNames, subqueryAliases);
  }

  if ('having' in ast && ast.having) {
    const havingList = Array.isArray(ast.having) ? ast.having : [ast.having];
    for (const havingItem of havingList) {
      if (havingItem && typeof havingItem === 'object') {
        collectSubqueries(havingItem as Record<string, unknown>, tables, cteNames, subqueryAliases);
      }
    }
  }
}

/** 处理 FROM 子句中的单个项 */
function collectFromItem(
  fromItem: From,
  tables: AstTableRef[],
  cteNames: string[],
  subqueryAliases: string[],
): void {
  if (!fromItem || typeof fromItem !== 'object') {
    return;
  }

  // 普通表引用
  if ('table' in fromItem && typeof fromItem.table === 'string' && fromItem.table !== '__dummy__') {
    const baseFrom = fromItem as BaseFrom | Join;
    tables.push({
      schema: baseFrom.db || baseFrom.schema || null,
      table: baseFrom.table,
      alias: baseFrom.as || null,
    });
  }

  // 子查询（FROM (SELECT ...) AS alias）
  if ('expr' in fromItem && fromItem.expr && typeof fromItem.expr === 'object') {
    const tableExpr = fromItem as { expr: { ast: AST }; as?: string | null };
    if (tableExpr.as) {
      subqueryAliases.push(tableExpr.as);
    }

    if (tableExpr.expr?.ast) {
      collectFromAst(tableExpr.expr.ast, tables, cteNames, subqueryAliases);
    }
  }
}

/** 递归扫描表达式中的子查询 */
function collectSubqueries(
  expr: Record<string, unknown>,
  tables: AstTableRef[],
  cteNames: string[],
  subqueryAliases: string[],
): void {
  if (!expr || typeof expr !== 'object') {
    return;
  }

  // 子查询直接在表达式中
  if (expr.type === 'select') {
    collectFromAst(expr as unknown as AST, tables, cteNames, subqueryAliases);
    return;
  }

  // 递归左右子表达式
  if (expr.left && typeof expr.left === 'object') {
    collectSubqueries(expr.left as Record<string, unknown>, tables, cteNames, subqueryAliases);
  }

  if (expr.right && typeof expr.right === 'object') {
    collectSubqueries(expr.right as Record<string, unknown>, tables, cteNames, subqueryAliases);
  }
}

// ── 存储过程/函数调用检测 ──

/** 从 AST 中提取的例程调用信息 */
export interface AstRoutineCall {
  schema: string | null;
  name: string;
  /** true = 调用语句中没有提供任何实参（即括号为空或 EXEC 后无参数） */
  hasNoArgs: boolean;
}

/**
 * 尝试从 SQL 文本中解析出存储过程/函数调用。
 * 支持 EXEC/EXECUTE (SQL Server)、CALL (MySQL/PostgreSQL)、SELECT func() 等模式。
 */
export async function tryMatchRoutineCall(
  sql: string,
  provider: DatabaseProviderType,
): Promise<AstRoutineCall | null> {
  const trimmed = sql.replace(/;+\s*$/, '').trim();
  if (!trimmed) {
    return null;
  }

  const astList = await tryParse(trimmed, provider);
  if (!astList || astList.length !== 1) {
    return null;
  }

  const ast = astList[0] as unknown as Record<string, unknown>;

  // SQL Server: EXEC / EXECUTE → type: "exec"
  if (ast.type === 'exec') {
    return extractExecAst(ast);
  }

  // MySQL / PostgreSQL: CALL → type: "call"
  if (ast.type === 'call') {
    return extractCallAst(ast);
  }

  // SELECT func() — 函数调用出现在 columns 或 from 中
  if (ast.type === 'select') {
    return extractSelectFuncCall(ast);
  }

  return null;
}

/**
 * SQL Server EXEC 节点提取。
 * AST 结构: { type: "exec", module: { db: "dbo", table: "ProcName" }, parameters: null | [...] }
 */
function extractExecAst(ast: Record<string, unknown>): AstRoutineCall | null {
  const mod = ast.module as Record<string, unknown> | null;
  if (!mod || typeof mod.table !== 'string') {
    return null;
  }

  const params = ast.parameters as unknown[] | null;
  return {
    schema: typeof mod.db === 'string' ? mod.db : null,
    name: mod.table,
    hasNoArgs: !params || params.length === 0,
  };
}

/**
 * MySQL / PostgreSQL CALL 节点提取。
 * AST 结构: { type: "call", expr: { type: "function", name: { name: [{ value }], schema?: { value } }, args: { value: null | [...] } } }
 */
function extractCallAst(ast: Record<string, unknown>): AstRoutineCall | null {
  const expr = ast.expr as Record<string, unknown> | null;
  if (!expr) {
    return null;
  }

  return extractFuncFromExpr(expr);
}

/** 从 SELECT 语句中检测函数调用（如 SELECT dbo.func() 或 SELECT * FROM func()） */
function extractSelectFuncCall(ast: Record<string, unknown>): AstRoutineCall | null {
  // SELECT func(...) — 函数调用在 columns 列表
  const columns = ast.columns as unknown[] | string | null;
  if (Array.isArray(columns) && columns.length === 1) {
    const col = columns[0] as Record<string, unknown>;
    const expr = col.expr as Record<string, unknown> | null;
    if (expr?.type === 'function' || expr?.type === 'aggr_func') {
      return extractFuncFromExpr(expr);
    }
  }

  // SELECT * FROM func(...) — 函数调用在 from 列表
  const from = ast.from as unknown[] | null;
  if (Array.isArray(from) && from.length === 1) {
    const fromItem = from[0] as Record<string, unknown>;
    if (fromItem.type === 'function' || fromItem.type === 'aggr_func') {
      return extractFuncFromExpr(fromItem);
    }

    const fromExpr = fromItem.expr as Record<string, unknown> | null;
    if (fromExpr?.type === 'function' || fromExpr?.type === 'aggr_func') {
      return extractFuncFromExpr(fromExpr);
    }
  }

  return null;
}

/**
 * 从 function 类型的 AST 节点中提取名称和参数信息。
 * AST 结构: { type: "function", name: { name: [{ value: "func_name" }], schema?: { value: "schema_name" } }, args: { value: null | [...] } }
 */
function extractFuncFromExpr(expr: Record<string, unknown>): AstRoutineCall | null {
  const nameObj = expr.name as Record<string, unknown> | null;
  if (!nameObj) {
    return null;
  }

  // name.name 是一个数组，取第一个元素的 value
  const nameArr = nameObj.name as Array<{ type: string; value: string }> | null;
  if (!Array.isArray(nameArr) || nameArr.length === 0) {
    return null;
  }

  const name = nameArr[0].value;
  if (!name) {
    return null;
  }

  // schema 是可选的对象 { type, value }
  const schemaObj = nameObj.schema as { value: string } | null | undefined;
  const schema = schemaObj?.value ?? null;

  // args.value 为 null 或空数组表示无实参
  const args = expr.args as { type: string; value: unknown[] | null } | null;
  const argList = args?.value ?? [];
  const hasNoArgs = !argList || argList.length === 0;

  return { schema, name, hasNoArgs };
}
