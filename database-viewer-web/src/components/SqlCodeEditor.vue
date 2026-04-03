<script setup lang="ts">
import ace from 'ace-builds';
import 'ace-builds/src-noconflict/ext-language_tools';
import 'ace-builds/src-noconflict/mode-sql';
import 'ace-builds/src-noconflict/theme-textmate';
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import type { Ace } from 'ace-builds';
import type { ProviderType, SqlContextTable } from '../types/explorer';
import { getSqlCompletions } from '../data/sql-completions';
import { tryParseForCompletion, extractAnalysis, type AstAnalysis } from '../lib/sql-ast';

const props = defineProps<{
  modelValue: string
  provider: ProviderType
  database: string | null
  databases: string[]
  tables: SqlContextTable[]
  height: number
}>();

const emit = defineEmits<{
  'update:modelValue': [value: string]
  execute: []
}>();

const hostRef = ref<HTMLDivElement | null>(null);
let editor: Ace.Editor | null = null;
let suppressModelSync = false;

type AceCompletionItem = {
  caption: string
  value: string
  meta: string
  score: number
}

type AceCompleter = {
  getCompletions: (
    editor: Ace.Editor,
    session: Ace.EditSession,
    pos: Ace.Point,
    prefix: string,
    callback: (error: unknown, results: AceCompletionItem[]) => void,
  ) => void
}

const languageTools = ace.require('ace/ext/language_tools') as {
  keyWordCompleter?: AceCompleter
  snippetCompleter?: AceCompleter
  textCompleter?: AceCompleter
};

const providerCompletions = computed(() => getSqlCompletions(props.provider));

// ── AST 分析缓存 ──
/** 上次 AST 分析结果 */
let cachedAstAnalysis: AstAnalysis | null = null;
/** 上次分析的 SQL 文本（用于跳过重复分析） */
let cachedAstSql = '';
/** AST 分析是否正在进行中 */
let astAnalysisPending = false;

/**
 * 异步触发 AST 分析，结果缓存到 cachedAstAnalysis。
 * 不阻塞补全流程——如果 AST 尚未就绪，fallback 到 regex。
 */
function triggerAstAnalysis(fullSql: string, sqlBeforeCursor: string): void {
  if (fullSql === cachedAstSql || astAnalysisPending) {
    return;
  }

  cachedAstSql = fullSql;
  astAnalysisPending = true;

  tryParseForCompletion(fullSql, sqlBeforeCursor, props.provider)
    .then((astList) => {
      if (astList) {
        cachedAstAnalysis = extractAnalysis(astList);
      }
    })
    .catch(() => { /* 解析失败，保留上次缓存 */ })
    .finally(() => { astAnalysisPending = false; });
}

function inferContext(sqlTextBeforeCursor: string) {
  if (/\buse\s+[A-Za-z_\d$]*$/i.test(sqlTextBeforeCursor)) {
    return 'database';
  }

  if (/\b(?:from|join|update|into|table|truncate\s+table|delete\s+from|describe|desc)\s+[A-Za-z_\d$.]*$/i.test(sqlTextBeforeCursor)) {
    return 'table';
  }

  if (/([A-Za-z_][\w$]*(?:\.[A-Za-z_][\w$]*)*)\.[A-Za-z_\d$]*$/i.test(sqlTextBeforeCursor)) {
    return 'qualified-column';
  }

  if (/\b(?:select|where|and|or|on|having|group\s+by|order\s+by|set|values|by)\s+[A-Za-z_\d$]*$/i.test(sqlTextBeforeCursor)) {
    return 'column';
  }

  return 'general';
}

/**
 * 提取别名映射和引用的表集合。
 * 优先使用 AST 分析结果，fallback 到 regex。
 */
function extractAliases(sqlText: string) {
  const aliases = new Map<string, string>();
  const referencedTables = new Set<string>();
  const cteNames: string[] = [];

  // 如果 AST 分析已就绪就使用 AST 数据
  if (cachedAstAnalysis) {
    for (const tableRef of cachedAstAnalysis.tables) {
      const qualifiedName = tableRef.schema
        ? `${tableRef.schema}.${tableRef.table}`
        : tableRef.table;

      referencedTables.add(qualifiedName.toLowerCase());

      if (tableRef.alias) {
        aliases.set(tableRef.alias.toLowerCase(), qualifiedName);
      }
    }

    cteNames.push(...cachedAstAnalysis.cteNames);
  } else {
    // Fallback: 用 regex 提取
    const regex = /\b(?:from|join|update|into)\s+([A-Za-z_][\w$]*(?:\.[A-Za-z_][\w$]*){0,2})(?:\s+(?:as\s+)?([A-Za-z_][\w$]*))?/gi;
    let match: RegExpExecArray | null;
    while ((match = regex.exec(sqlText)) !== null) {
      const tableRef = match[1];
      const alias = match[2];
      if (!tableRef) {
        continue;
      }

      referencedTables.add(tableRef.toLowerCase());
      if (alias) {
        aliases.set(alias.toLowerCase(), tableRef);
      }
    }
  }

  return { aliases, referencedTables, cteNames };
}

function resolveTableReference(reference: string, aliases: Map<string, string>) {
  const aliasTarget = aliases.get(reference.toLowerCase());
  const target = (aliasTarget ?? reference).toLowerCase();
  return props.tables.find((table) => table.qualifiedName.toLowerCase() === target
    || table.name.toLowerCase() === target
    || `${table.schema ?? ''}.${table.name}`.replace(/^\./, '').toLowerCase() === target) ?? null;
}

function toCompletionItems(items: Array<{ caption: string; value?: string; meta: string; score: number }>) {
  return items.map((item) => ({
    caption: item.caption,
    value: item.value ?? item.caption,
    meta: item.meta,
    score: item.score,
  }));
}

function completionInsertValue(caption: string, qualifiedPrefix?: string) {
  if (!qualifiedPrefix) {
    return caption;
  }

  const prefixWithDot = `${qualifiedPrefix}.`;
  return caption.toLowerCase().startsWith(prefixWithDot.toLowerCase())
    ? caption.slice(prefixWithDot.length)
    : caption;
}

/** 将表列表拆为独立的 schema 和 table name 补全项（不输出 schema.table 合体） */
function buildTableCompletionItems(qualifier?: string, baseScore = 1050, extraCteNames?: string[]) {
  const schemaSet = new Set<string>();
  const items: Array<{ caption: string; value?: string; meta: string; score: number }> = [];

  for (const table of props.tables) {
    if (table.schema) {
      schemaSet.add(table.schema);
    }

    items.push({
      caption: table.name,
      value: completionInsertValue(table.name, qualifier),
      meta: table.schema ? `${table.schema} · table` : 'table',
      score: baseScore,
    });
  }

  for (const schema of schemaSet) {
    items.push({
      caption: schema,
      meta: 'schema',
      score: baseScore - 10,
    });
  }

  // CTE 名称也当作可引用的"表"
  if (extraCteNames) {
    for (const cteName of extraCteNames) {
      items.push({
        caption: cteName,
        meta: 'CTE',
        score: baseScore + 20,
      });
    }
  }

  return items;
}

function buildSqlCompleter(): AceCompleter {
  return {
    getCompletions: (_editor, session, pos, prefix, callback) => {
      // 检查光标是否在字符串或注释内，如果是则不触发补全
      const tokenAtCursor = session.getTokenAt(pos.row, pos.column);
      if (tokenAtCursor && /\bstring\b|\bcomment\b/.test(tokenAtCursor.type)) {
        callback(null, []);
        return;
      }

      const lines = session.getDocument().getAllLines();
      const currentLine = lines[pos.row] ?? '';
      const beforeText = `${lines.slice(0, pos.row).join('\n')}${pos.row > 0 ? '\n' : ''}${currentLine.slice(0, pos.column)}`;
      const sqlText = session.getValue();

      // 异步触发 AST 分析（不阻塞当前补全）
      triggerAstAnalysis(sqlText, beforeText);

      const { aliases, referencedTables, cteNames } = extractAliases(sqlText);
      const context = inferContext(beforeText);
      const qualifierMatch = beforeText.match(/([A-Za-z_][\w$]*(?:\.[A-Za-z_][\w$]*)*)\.[A-Za-z_\d$]*$/i);
      const qualifier = qualifierMatch?.[1];

      // 检测 @ 或 @@ 全局变量前缀
      const atMatch = beforeText.match(/@(@?\w*)$/);
      const isAtContext = !!atMatch;

      // eslint-disable-next-line no-useless-assignment
      let items: AceCompletionItem[] = [];

      if (isAtContext) {
        // @ 或 @@ 全局变量模式
        const completions = providerCompletions.value;
        const typedAt = atMatch[1] ?? ''; // @@ 后输入的部分，如 @@VER → @VER 或 @V → V
        const hasDoubleAt = typedAt.startsWith('@');
        // 已输入 @@ 时，value 去掉 @@ 前缀；只输入 @ 时，去掉第一个 @
        const sliceLen = hasDoubleAt ? 2 : 1;
        items = toCompletionItems(completions.globalVariables.map((v) => ({
          caption: v,
          value: v.slice(sliceLen),
          meta: 'variable',
          score: 1200,
        })));
      }
      else if (context === 'database') {
        items = toCompletionItems(props.databases.map((database) => ({
          caption: database,
          meta: 'database',
          score: 1100,
        })));
      }
      else if (context === 'table') {
        const completions = providerCompletions.value;
        items = [
          ...toCompletionItems(buildTableCompletionItems(qualifier, 1050, cteNames)),
          ...toCompletionItems(completions.systemObjects.map((obj) => ({
            caption: obj,
            meta: 'system',
            score: 1000,
          }))),
        ];
      }
      else if (context === 'qualified-column' && qualifier) {
        const table = resolveTableReference(qualifier, aliases);
        if (table) {
          items = toCompletionItems(table.columns.map((column) => ({
            caption: column,
            meta: table.qualifiedName,
            score: 1200,
          })));
        }
        else {
          // qualifier 不是表别名时，作为 schema 前缀匹配系统对象（如 sys. → objects, tables ...）
          const completions = providerCompletions.value;
          const qualLower = qualifier.toLowerCase();
          items = [
            ...toCompletionItems(buildTableCompletionItems(qualifier, 1100, cteNames)),
            ...toCompletionItems(completions.systemObjects
              .filter((obj) => obj.toLowerCase() !== qualLower) // 排除 schema 自身
              .map((obj) => ({
                caption: obj,
                meta: `${qualifier} · system`,
                score: 1050,
              }))),
          ];
        }
      }
      else if (context === 'column') {
        const referenced = props.tables.filter((table) => referencedTables.has(table.qualifiedName.toLowerCase()) || referencedTables.has(table.name.toLowerCase()));
        const sourceTables = referenced.length ? referenced : props.tables.slice(0, 20);
        const completions = providerCompletions.value;

        // 收集别名作为点前缀补全项（输入别名后加 . 可触发列补全）
        const aliasItems = [...aliases.keys()].map((alias) => ({
          caption: alias,
          meta: 'alias',
          score: 1180,
        }));

        // CTE 名称也可在列上下文中引用
        const cteItems = cteNames.map((cteName) => ({
          caption: cteName,
          meta: 'CTE',
          score: 1170,
        }));

        items = [
          ...toCompletionItems(sourceTables.flatMap((table) => table.columns.map((column) => ({
            caption: column,
            meta: table.qualifiedName,
            score: 1150,
          })))),
          ...toCompletionItems(aliasItems),
          ...toCompletionItems(cteItems),
          ...toCompletionItems(completions.functions.map((fn) => ({
            caption: fn,
            meta: 'function',
            score: 950,
          }))),
          ...toCompletionItems(completions.keywords.map((keyword) => ({
            caption: keyword,
            meta: 'keyword',
            score: 900,
          }))),
        ];
      }
      else {
        const completions = providerCompletions.value;
        items = [
          ...toCompletionItems(completions.keywords.map((keyword) => ({
            caption: keyword,
            meta: 'keyword',
            score: 900,
          }))),
          ...toCompletionItems(completions.functions.map((fn) => ({
            caption: fn,
            meta: 'function',
            score: 880,
          }))),
          ...toCompletionItems(completions.globalVariables.map((v) => ({
            caption: v,
            meta: 'variable',
            score: 870,
          }))),
          ...toCompletionItems(completions.systemObjects.map((obj) => ({
            caption: obj,
            meta: 'system',
            score: 860,
          }))),
          ...toCompletionItems(buildTableCompletionItems(qualifier, 850, cteNames)),
          ...toCompletionItems(props.databases.map((database) => ({
            caption: database,
            meta: 'database',
            score: 800,
          }))),
        ];
      }

      const deduped = new Map<string, AceCompletionItem>();
      for (const item of items) {
        const key = `${item.meta}:${item.caption.toLowerCase()}`;
        if (!deduped.has(key)) {
          deduped.set(key, item);
        }
      }

      // 前缀匹配优先排序：完全前缀匹配的 score +200，包含匹配的不加分
      const atContent = isAtContext ? (atMatch[1] ?? '').replace(/^@/, '') : '';
      const effectivePrefix = isAtContext ? atContent.toLowerCase() : prefix.toLowerCase();
      const results = [...deduped.values()]
        .filter((item) => !effectivePrefix || item.caption.toLowerCase().includes(effectivePrefix))
        .map((item) => {
          if (effectivePrefix && item.caption.toLowerCase().startsWith(effectivePrefix)) {
            return { ...item, score: item.score + 200 };
          }

          return item;
        });

      callback(null, results);
    },
  };
}

function applyCompleters(nextEditor: Ace.Editor) {
  nextEditor.completers = [
    buildSqlCompleter(),
    languageTools.textCompleter,
    languageTools.snippetCompleter,
  ].filter(Boolean) as AceCompleter[];
}

function duplicateSelectionOrLine(nextEditor: Ace.Editor) {
  if (nextEditor.selection.isEmpty()) {
    nextEditor.copyLinesDown();
    return;
  }

  nextEditor.duplicateSelection();
}

/**
 * 注册自定义 SQL 高亮模式到 Ace，支持 SSMS 风格着色：
 * - @@VARIABLE → variable.language（紫色）
 * - 内置函数 → support.function（紫色）
 * - GO → keyword（蓝色）
 * - 关键字 → keyword（蓝色）
 * - 数据类型 → storage.type
 */
function registerCustomSqlMode(): void {
  const completions = providerCompletions.value;

  // 构建查找集合（小写）
  const keywordSet = new Set(completions.keywords.map((keyword) => keyword.toLowerCase()));
  const functionSet = new Set(completions.functions.map((fn) => fn.toLowerCase()));
  const globalVarSet = new Set(completions.globalVariables.map((v) => v.toLowerCase()));

  const dataTypeSet = new Set([
    'int', 'integer', 'bigint', 'smallint', 'tinyint', 'decimal', 'numeric',
    'float', 'real', 'double', 'char', 'varchar', 'nchar', 'nvarchar', 'text',
    'ntext', 'date', 'time', 'datetime', 'datetime2', 'datetimeoffset',
    'smalldatetime', 'timestamp', 'bit', 'binary', 'varbinary', 'image',
    'money', 'smallmoney', 'uniqueidentifier', 'xml', 'sql_variant',
    'boolean', 'bool', 'serial', 'bigserial', 'blob', 'clob', 'json', 'jsonb',
    'uuid', 'bytea', 'interval', 'cidr', 'inet', 'macaddr', 'rowversion',
    'hierarchyid', 'geometry', 'geography',
  ]);

  const boolNullSet = new Set(['true', 'false', 'null']);

  /** 根据标识符文本返回 token 类型（大小写不敏感） */
  const classifyIdentifier = (value: string): string => {
    const lower = value.toLowerCase();

    // @@VARIABLE（全局变量）
    if (lower.startsWith('@@')) {
      return globalVarSet.has(lower) ? 'variable.language' : 'invalid.illegal';
    }

    // @variable（用户变量）— 普通颜色
    if (lower.startsWith('@')) {
      return 'variable';
    }

    if (boolNullSet.has(lower)) {
      return 'constant.language';
    }

    if (dataTypeSet.has(lower)) {
      return 'storage.type';
    }

    // 函数优先于关键字（关键字兼函数的保留为关键字）
    if (functionSet.has(lower) && !keywordSet.has(lower)) {
      return 'support.function';
    }

    if (keywordSet.has(lower)) {
      return 'keyword';
    }

    return 'identifier';
  };

  const oop = ace.require('ace/lib/oop');
  const TextHighlightRules = ace.require('ace/mode/text_highlight_rules').TextHighlightRules;
  const TextMode = ace.require('ace/mode/text').Mode;

  // 定义高亮规则
  const CustomSqlHighlightRules = function (this: { $rules: Record<string, unknown[]>; normalizeRules: () => void }) {
    this.$rules = {
      start: [
        // 行注释
        { token: 'comment', regex: '--.*$' },
        // 块注释
        { token: 'comment', regex: '/\\*', next: 'comment' },
        // N'...' Unicode 字符串前缀（SQL Server）
        { token: 'string', regex: "N'", next: 'qstring' },
        // 单引号字符串
        { token: 'string', regex: "'", next: 'qstring' },
        // 双引号标识符
        { token: 'string', regex: '"', next: 'qqstring' },
        // [bracket] 标识符（SQL Server）
        { token: 'string', regex: '\\[', next: 'bstring' },
        // 反引号标识符（MySQL）
        { token: 'string', regex: '`', next: 'btstring' },
        // 数字
        { token: 'constant.numeric', regex: '[+-]?\\d+(?:\\.\\d+)?(?:[eE][+-]?\\d+)?\\b' },
        // @@VARIABLE / @variable / 普通标识符 — 使用函数动态分类
        { token: classifyIdentifier, regex: '@@\\w+|@\\w+|[A-Za-z_$][A-Za-z0-9_$#]*' },
        // 运算符
        { token: 'keyword.operator', regex: '[<>=!]=?|[+\\-*/%&|^~]|\\|\\|' },
        // 括号
        { token: 'paren.lparen', regex: '[(]' },
        { token: 'paren.rparen', regex: '[)]' },
        // 标点
        { token: 'punctuation', regex: '[,;.]' },
        // 空白
        { token: 'text', regex: '\\s+' },
      ],
      comment: [
        { token: 'comment', regex: '\\*/', next: 'start' },
        { token: 'comment', regex: '.+' },
      ],
      qstring: [
        { token: 'string', regex: "''", merge: true },
        { token: 'string', regex: "'", next: 'start' },
        { token: 'string', regex: "[^']+", merge: true },
      ],
      qqstring: [
        { token: 'string', regex: '""', merge: true },
        { token: 'string', regex: '"', next: 'start' },
        { token: 'string', regex: '[^"]+', merge: true },
      ],
      bstring: [
        { token: 'string', regex: '\\]\\]', merge: true },
        { token: 'string', regex: '\\]', next: 'start' },
        { token: 'string', regex: '[^\\]]+', merge: true },
      ],
      btstring: [
        { token: 'string', regex: '``', merge: true },
        { token: 'string', regex: '`', next: 'start' },
        { token: 'string', regex: '[^`]+', merge: true },
      ],
    };

    this.normalizeRules();
  };

  oop.inherits(CustomSqlHighlightRules, TextHighlightRules);

  // 定义自定义 mode
  const CustomSqlMode = function (this: { HighlightRules: unknown; $behaviour: unknown }) {
    this.HighlightRules = CustomSqlHighlightRules;
  };

  oop.inherits(CustomSqlMode, TextMode);

  // 注册到 Ace
  (ace as unknown as { define: (name: string, deps: string[], factory: (req: unknown, exp: Record<string, unknown>) => void) => void })
    .define('ace/mode/custom_sql', [], (_require: unknown, exports: Record<string, unknown>) => {
      exports.Mode = CustomSqlMode;
    });
}

// ── @variable 声明检查 ──

/** 活跃的错误标记 ID 列表 */
let activeVarMarkerIds: number[] = [];
/** 防抖定时器 */
let varValidationTimer: ReturnType<typeof setTimeout> | null = null;

/**
 * 从 SQL 文本中提取所有 DECLARE @variable 声明的变量名（小写）。
 * 支持：DECLARE @a INT, @b VARCHAR(50) = 'hello'
 */
function extractDeclaredVariables(sql: string): Set<string> {
  const vars = new Set<string>();

  // 匹配 DECLARE 后到分号/语句结束之间的所有 @varname
  const declareBlockRegex = /\bDECLARE\b\s+([\s\S]*?)(?=\b(?:SELECT|INSERT|UPDATE|DELETE|EXEC|EXECUTE|IF|WHILE|BEGIN|PRINT|RETURN|SET)\b|;|$)/gi;
  let blockMatch: RegExpExecArray | null;
  while ((blockMatch = declareBlockRegex.exec(sql)) !== null) {
    const body = blockMatch[1];
    if (!body) {
      continue;
    }

    const varRegex = /@(\w+)/g;
    let varMatch: RegExpExecArray | null;
    while ((varMatch = varRegex.exec(body)) !== null) {
      vars.add(`@${varMatch[1]!.toLowerCase()}`);
    }
  }

  // 也提取 SET @varname = ... 中的变量名（隐式声明）
  const setRegex = /\bSET\s+(@\w+)\s*=/gi;
  let setMatch: RegExpExecArray | null;
  while ((setMatch = setRegex.exec(sql)) !== null) {
    if (setMatch[1]) {
      vars.add(setMatch[1].toLowerCase());
    }
  }

  // 存储过程参数 @paramName
  const procParamRegex = /\bCREATE\s+(?:PROC|PROCEDURE)\b\s+\S+\s+([\s\S]*?)\bAS\b/gi;
  let procMatch: RegExpExecArray | null;
  while ((procMatch = procParamRegex.exec(sql)) !== null) {
    const paramBody = procMatch[1];
    if (!paramBody) {
      continue;
    }

    const paramVarRegex = /@(\w+)/g;
    let paramVarMatch: RegExpExecArray | null;
    while ((paramVarMatch = paramVarRegex.exec(paramBody)) !== null) {
      vars.add(`@${paramVarMatch[1]!.toLowerCase()}`);
    }
  }

  return vars;
}

/**
 * 验证 SQL 中的 @variable 引用是否已声明，未声明的添加红色波浪线标记。
 * 优先使用 AST 分析结果，fallback 到正则表达式。
 * 仅对 SQL Server (sqlserver) provider 生效。
 */
function validateVariableDeclarations(nextEditor: Ace.Editor): void {
  const session = nextEditor.session;

  // 清除旧标记
  for (const markerId of activeVarMarkerIds) {
    session.removeMarker(markerId);
  }

  activeVarMarkerIds = [];

  // 仅 SQL Server 有 @variable 语法
  if (props.provider !== 'sqlserver') {
    return;
  }

  const sql = session.getValue();
  const Range = ace.require('ace/range').Range as new (
    startRow: number, startCol: number, endRow: number, endCol: number,
  ) => Ace.Range;

  // 优先使用 AST 分析结果
  if (cachedAstAnalysis && cachedAstAnalysis.declaredVariables.length >= 0) {
    const declaredSet = new Set(cachedAstAnalysis.declaredVariables);

    // AST 有位置信息时，直接从引用列表中检查
    if (cachedAstAnalysis.referencedVariables.length > 0) {
      for (const ref of cachedAstAnalysis.referencedVariables) {
        if (!declaredSet.has(ref.name) && !ref.name.startsWith('@@')) {
          // AST 位置可能为 0（无位置信息），fallback 到行扫描
          if (ref.line >= 0 && ref.column >= 0) {
            const range = new Range(ref.line, ref.column, ref.line, ref.column + ref.name.length);
            const markerId = session.addMarker(range, 'sql-var-undeclared', 'text', false);
            activeVarMarkerIds.push(markerId);
          }
        }
      }

      return;
    }

    // AST 无位置信息时，用 regex 扫描行并与声明集合比较
    markUndeclaredByLineScan(session, declaredSet, Range);
    return;
  }

  // Fallback: 纯 regex 分析
  const declaredVars = extractDeclaredVariables(sql);
  markUndeclaredByLineScan(session, declaredVars, Range);
}

/** 逐行扫描 @variable 并标记未声明的 */
function markUndeclaredByLineScan(
  session: Ace.EditSession,
  declaredVars: Set<string>,
  Range: new (sr: number, sc: number, er: number, ec: number) => Ace.Range,
): void {
  const lines = session.getDocument().getAllLines();

  for (let row = 0; row < lines.length; row++) {
    const line = lines[row]!;
    const varRegex = /(?<!@)@(?!@)\w+/g;
    let match: RegExpExecArray | null;
    while ((match = varRegex.exec(line)) !== null) {
      const fullVar = match[0];
      const col = match.index;

      if (!declaredVars.has(fullVar.toLowerCase())) {
        const range = new Range(row, col, row, col + fullVar.length);
        const markerId = session.addMarker(range, 'sql-var-undeclared', 'text', false);
        activeVarMarkerIds.push(markerId);
      }
    }
  }
}

/** 防抖调度变量验证（300ms） */
function scheduleVariableValidation(nextEditor: Ace.Editor): void {
  if (varValidationTimer) {
    clearTimeout(varValidationTimer);
  }

  varValidationTimer = setTimeout(() => {
    validateVariableDeclarations(nextEditor);
  }, 300);
}

function configureEditor(nextEditor: Ace.Editor) {
  // 注册并使用自定义 SQL 高亮模式
  registerCustomSqlMode();
  nextEditor.session.setMode(new (ace.require('ace/mode/custom_sql').Mode as new () => Ace.SyntaxMode)());
  nextEditor.setTheme('ace/theme/textmate');

  nextEditor.setShowPrintMargin(false);
  nextEditor.setHighlightActiveLine(true);
  nextEditor.setHighlightGutterLine(true);
  nextEditor.setOption('fontSize', '12px');
  nextEditor.setOption('fontFamily', "'Cascadia Code', 'JetBrains Mono', 'Cascadia Mono', 'Consolas', monospace");
  nextEditor.setOption('wrap', false);
  nextEditor.setOption('useSoftTabs', true);
  nextEditor.setOption('tabSize', 2);
  nextEditor.setOption('animatedScroll', false);
  nextEditor.setOption('showLineNumbers', true);
  nextEditor.setOption('showGutter', true);
  nextEditor.setOption('displayIndentGuides', false);
  nextEditor.setOption('scrollPastEnd', 0.08);
  nextEditor.setOption('enableBasicAutocompletion', true);
  nextEditor.setOption('enableLiveAutocompletion', true);
  nextEditor.setOption('enableSnippets', false);
  applyCompleters(nextEditor);

  nextEditor.commands.addCommand({
    name: 'executeSqlF5',
    bindKey: { win: 'F5', mac: 'F5' },
    exec: () => emit('execute'),
  });

  nextEditor.commands.addCommand({
    name: 'executeSqlCtrlEnter',
    bindKey: { win: 'Ctrl-Enter', mac: 'Command-Enter' },
    exec: () => emit('execute'),
  });

  nextEditor.commands.addCommand({
    name: 'duplicateLikeVisualStudio',
    bindKey: { win: 'Ctrl-D', mac: 'Command-D' },
    exec: (instance) => {
      duplicateSelectionOrLine(instance);
    },
    readOnly: false,
  });

  // Ctrl+X：无选区时剪切当前行，有选区时正常剪切
  nextEditor.commands.addCommand({
    name: 'cutLineOrSelection',
    bindKey: { win: 'Ctrl-X', mac: 'Command-X' },
    exec: (instance) => {
      if (instance.selection.isEmpty()) {
        const row = instance.getCursorPosition().row;
        const line = instance.session.getLine(row);
        const lineText = line + (row < instance.session.getLength() - 1 ? '\n' : '');
        navigator.clipboard.writeText(lineText);
        instance.session.remove({
          start: { row, column: 0 },
          end: { row: row + 1, column: 0 },
        } as Ace.Range);
      } else {
        const selectedText = instance.getSelectedText();
        navigator.clipboard.writeText(selectedText);
        instance.session.remove(instance.getSelectionRange());
      }
    },
    readOnly: false,
  });

  nextEditor.session.on('change', () => {
    if (suppressModelSync) {
      return;
    }

    emit('update:modelValue', nextEditor.getValue());

    // 延迟验证 @variable 声明
    scheduleVariableValidation(nextEditor);
  });

  // 输入 . 或 @ 时自动触发补全列表
  nextEditor.commands.on('afterExec', ((event: { command: { name: string } }) => {
    if (event.command.name === 'insertstring') {
      const cursor = nextEditor.getCursorPosition();
      const line = nextEditor.session.getLine(cursor.row);
      const charBefore = line[cursor.column - 1];

      if (charBefore === '.' || charBefore === '@') {
        nextEditor.execCommand('startAutocomplete');
      }

      // 每次击键后强制补全列表滚动到第一项（最高分项）
      requestAnimationFrame(() => {
        const comp = (nextEditor as unknown as Record<string, unknown>).completer as
          { popup?: { isOpen?: boolean; setRow?: (row: number) => void } } | undefined;

        if (comp?.popup?.isOpen) {
          comp.popup.setRow?.(0);
        }
      });
    }
  }) as Parameters<typeof nextEditor.commands.on>[1]);

  // 阻止编辑器区域滚轮事件关闭补全列表 + Ctrl+滚轮缩放编辑器字号
  nextEditor.container.addEventListener('wheel', (event) => {
    // Ctrl+滚轮：缩放编辑器字号（阻止页面缩放）
    if (event.ctrlKey) {
      event.preventDefault();
      event.stopPropagation();
      const currentSize = parseInt(nextEditor.getOption('fontSize') as string, 10) || 12;
      const delta = event.deltaY < 0 ? 1 : -1;
      const newSize = Math.max(8, Math.min(36, currentSize + delta));
      nextEditor.setOption('fontSize', `${newSize}px`);
      return;
    }

    const completer = (nextEditor as unknown as Record<string, unknown>).completer as
      { popup?: { container?: HTMLElement } } | undefined;
    const popupEl = completer?.popup?.container;
    const isPopupVisible = popupEl != null && popupEl.style.display !== 'none';

    if (isPopupVisible && popupEl.contains(event.target as Node)) {
      return; // 滚轮在补全列表内，不阻止
    }

    if (isPopupVisible) {
      event.stopPropagation(); // 阻止向上冒泡，防止 Ace 的 blurListener 关闭弹窗
    }
  }, { capture: true });
}

onMounted(() => {
  if (!hostRef.value) {
    return;
  }

  editor = ace.edit(hostRef.value);
  configureEditor(editor);
  suppressModelSync = true;
  editor.setValue(props.modelValue, -1);
  suppressModelSync = false;
  hostRef.value.style.height = `${props.height}px`;
  editor.resize();
});

watch(() => props.modelValue, (value) => {
  if (!editor) {
    return;
  }

  const current = editor.getValue();
  if (current === value) {
    return;
  }

  const position = editor.getCursorPosition();
  suppressModelSync = true;
  editor.setValue(value, -1);
  editor.moveCursorToPosition(position);
  suppressModelSync = false;
});

watch(() => props.height, (height) => {
  if (!hostRef.value) {
    return;
  }

  hostRef.value.style.height = `${height}px`;
  editor?.resize();
}, { immediate: true });

watch(() => [props.provider, props.database, props.databases.join('|'), props.tables.map((table) => `${table.qualifiedName}:${table.columns.join(',')}`).join('|')] as const, () => {
  if (!editor) {
    return;
  }

  // provider 或数据库变化时重置 AST 缓存
  cachedAstAnalysis = null;
  cachedAstSql = '';

  // 重设 mode 并重新应用高亮规则
  registerCustomSqlMode();
  editor.session.setMode(new (ace.require('ace/mode/custom_sql').Mode as new () => Ace.SyntaxMode)());

  applyCompleters(editor);
});

onBeforeUnmount(() => {
  editor?.destroy();
  editor = null;
});
</script>

<template>
  <div ref="hostRef" class="sql-code-editor" />
</template>

<style scoped lang="scss">
.sql-code-editor {
  width: 100%;

  // Ace editor 内部 token 高亮覆盖（SSMS 风格）
  :deep(.ace_variable.ace_language) {
    color: #ff00ff; // @@VERSION 等全局变量 — 紫色
  }

  :deep(.ace_support.ace_function) {
    color: #ff00ff; // GETDATE / NEWID 等内置函数 — 紫色
  }

  :deep(.ace_invalid.ace_illegal) {
    color: #dc2626; // @@abc 等无效全局变量 — 红色
    text-decoration: wavy underline #dc2626;
    text-underline-offset: 2px;
  }

  // 未声明的 @variable — 红色波浪下划线
  :deep(.sql-var-undeclared) {
    position: absolute;
    border-bottom: 2px wavy #dc2626;
  }
}
</style>

<style scoped lang="scss">
.sql-code-editor {
  width: 100%;
  min-height: 160px;
  border: 1px solid $color-border-light;
  border-radius: $gap-2xl;
  overflow: hidden;
  background: $color-surface-white;

  :deep(.ace_editor) {
    font-family: $font-family-mono-ext;
    font-size: $font-size-md;
    line-height: 1.55;
    background: $color-surface-white;
    color: $color-text-primary;
  }

  :deep(.ace_gutter) {
    background: $color-surface-light;
    color: $color-text-secondary;
    border-right: 1px solid rgba(148, 163, 184, 0.14);
  }

  :deep(.ace_active-line),
  :deep(.ace_gutter-active-line) {
    background: rgba(219, 234, 254, 0.55);
  }

  :deep(.ace_cursor) {
    color: $color-text-primary;
  }

  :deep(.ace_marker-layer .ace_selection) {
    background: rgba(191, 219, 254, 0.7);
  }

  :deep(.ace_keyword) {
    color: $color-accent-blue-dark;
    font-weight: 700;
  }

  :deep(.ace_string) {
    color: #ff0000;
  }

  :deep(.ace_constant.ace_numeric) {
    color: $color-accent-amber;
  }

  :deep(.ace_comment) {
    color: $color-text-secondary;
    font-style: italic;
  }

  :deep(.ace_identifier) {
    color: $color-text-primary;
  }

  :deep(.ace_completion-meta) {
    color: $color-text-muted;
  }

  :deep(.ace_autocomplete) {
    font-family: $font-family-mono-ext;
    font-size: $font-size-md;
    line-height: 1.5;
    border: 1px solid rgba(148, 163, 184, 0.2);
    border-radius: $gap-xl;
    box-shadow: 0 20px 44px $shadow-medium;

    .ace_line {
      font-family: inherit;
    }

    .ace_completion-highlight {
      color: $color-accent-blue;
      font-weight: 700;
    }
  }
}
</style>
