<script setup lang="ts">
import * as monaco from 'monaco-editor/esm/vs/editor/editor.api.js';
import 'monaco-editor/esm/vs/editor/contrib/dnd/browser/dnd.js';
import 'monaco-editor/esm/vs/editor/contrib/hover/browser/hoverContribution.js';
import 'monaco-editor/esm/vs/editor/contrib/linesOperations/browser/linesOperations.js';
import 'monaco-editor/esm/vs/editor/contrib/snippet/browser/snippetController2.js';
import 'monaco-editor/esm/vs/editor/contrib/suggest/browser/suggestController.js';
import 'monaco-editor/esm/vs/editor/contrib/suggest/browser/suggestInlineCompletions.js';
import 'monaco-editor/esm/vs/editor/contrib/wordOperations/browser/wordOperations.js';
import editorWorker from 'monaco-editor/esm/vs/editor/editor.worker?worker';
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { useLocalStorage } from '@vueuse/core';
import type { ProviderType, RoutineInfo, SqlContextTable } from '../types/explorer';
import { getSqlCompletions } from '../data/sql-completions';
import { tryParseForCompletion, extractAnalysis, type AstAnalysis } from '../lib/sql-ast';

const props = defineProps<{
  modelValue: string
  provider: ProviderType
  database: string | null
  databases: string[]
  tables: SqlContextTable[]
  routines: RoutineInfo[]
  height: number
  fill?: boolean
}>();

const emit = defineEmits<{
  'update:modelValue': [value: string]
  execute: []
}>();

const SQL_LANGUAGE_ID = 'database-viewer-sql';
const SQL_THEME_ID = 'database-viewer-sql-theme';
const MONACO_OWNER = 'database-viewer-sql';

const hostRef = ref<HTMLDivElement | null>(null);
const editorReady = ref(false);
const editorFontSize = useLocalStorage('dbv-monaco-font-size-v1', 12);

let editor: monaco.editor.IStandaloneCodeEditor | null = null;
let model: monaco.editor.ITextModel | null = null;
let suppressModelSync = false;
let validationTimer: ReturnType<typeof setTimeout> | null = null;
let modelCounter = 0;
let monacoRegistered = false;
let completionProviderDisposable: monaco.IDisposable | null = null;
let invalidGlobalDecorationIds: string[] = [];
let selectionClickState: {
  selection: monaco.Selection
  position: monaco.Position
  moved: boolean
} | null = null;

function focusAtStart(): void {
  if (!editor) {
    return;
  }

  const position = { lineNumber: 1, column: 1 };
  editor.focus();
  editor.setPosition(position);
  editor.setSelection(new monaco.Selection(1, 1, 1, 1));
  editor.revealPositionInCenterIfOutsideViewport(position);
}

defineExpose<{
  focusAtStart: () => void
}>({
  focusAtStart,
});

const globalScope = globalThis as typeof globalThis & {
  MonacoEnvironment?: {
    getWorker?: (workerId: string, label: string) => Worker
  }
};

globalScope.MonacoEnvironment = {
  ...globalScope.MonacoEnvironment,
  getWorker(_workerId: string, _label: string): Worker {
    return new editorWorker();
  },
};

type CompletionCandidate = {
  label: string
  insertText?: string
  meta: string
  score: number
  kind: monaco.languages.CompletionItemKind
  labelDetail?: string
  labelDescription?: string
  documentation?: string
  filterText?: string
  range?: monaco.IRange
};

type ModelContext = {
  provider: ProviderType
  database: string | null
  databases: string[]
  tables: SqlContextTable[]
  routines: RoutineInfo[]
  astAnalysis: AstAnalysis | null
  astSql: string
  astPending: boolean
};

const modelContexts = new Map<string, ModelContext>();
const providerCompletions = computed(() => getSqlCompletions(props.provider));
const allCompletionSets = [
  getSqlCompletions('sqlserver'),
  getSqlCompletions('mysql'),
  getSqlCompletions('postgresql'),
  getSqlCompletions('sqlite'),
];

const highlightKeywords = [...new Set(allCompletionSets.flatMap((item) => item.keywords.map((keyword) => keyword.toLowerCase())))];
const highlightFunctions = [...new Set(allCompletionSets.flatMap((item) => item.functions.map((fn) => fn.toLowerCase())))];
const highlightGlobalVariables = [...new Set(allCompletionSets.flatMap((item) => item.globalVariables.map((variable) => variable.toLowerCase())))];
const highlightGlobalVariableTails = [...new Set(highlightGlobalVariables.map((variable) => variable.replace(/^@/, '')))];
const dataTypes = [
  'int', 'integer', 'bigint', 'smallint', 'tinyint', 'decimal', 'numeric',
  'float', 'real', 'double', 'char', 'varchar', 'nchar', 'nvarchar', 'text',
  'ntext', 'date', 'time', 'datetime', 'datetime2', 'datetimeoffset',
  'smalldatetime', 'timestamp', 'bit', 'binary', 'varbinary', 'image',
  'money', 'smallmoney', 'uniqueidentifier', 'xml', 'sql_variant',
  'boolean', 'bool', 'serial', 'bigserial', 'blob', 'clob', 'json', 'jsonb',
  'uuid', 'bytea', 'interval', 'cidr', 'inet', 'macaddr', 'rowversion',
  'hierarchyid', 'geometry', 'geography',
];
const boolNullValues = ['true', 'false', 'null'];

function ensureMonacoRegistration(): void {
  if (monacoRegistered) {
    return;
  }

  monacoRegistered = true;

  monaco.languages.register({ id: SQL_LANGUAGE_ID });
  monaco.languages.setLanguageConfiguration(SQL_LANGUAGE_ID, {
    comments: {
      lineComment: '--',
      blockComment: ['/*', '*/'],
    },
    brackets: [
      ['(', ')'],
      ['[', ']'],
    ],
    autoClosingPairs: [
      { open: '(', close: ')' },
      { open: '[', close: ']' },
      { open: '"', close: '"' },
      { open: "'", close: "'" },
      { open: '`', close: '`' },
    ],
    surroundingPairs: [
      { open: '(', close: ')' },
      { open: '[', close: ']' },
      { open: '"', close: '"' },
      { open: "'", close: "'" },
      { open: '`', close: '`' },
    ],
  });

  monaco.languages.setMonarchTokensProvider(SQL_LANGUAGE_ID, {
    ignoreCase: true,
    keywords: highlightKeywords,
    builtinFunctions: highlightFunctions,
    globalVariables: highlightGlobalVariables,
    globalVariableTails: highlightGlobalVariableTails,
    dataTypes,
    boolNullValues,
    tokenizer: {
      root: [
        [/--.*$/, 'comment'],
        [/\/\*/, 'comment', '@comment'],
        [/[Nn]'/, { token: 'string', next: '@singleQuotedString' }],
        [/'/, { token: 'string', next: '@singleQuotedString' }],
        [/"/, { token: 'string', next: '@doubleQuotedString' }],
        [/\[/, { token: 'string', next: '@bracketIdentifier' }],
        [/`/, { token: 'string', next: '@backtickIdentifier' }],
        [/[+-]?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b/, 'number'],
        [/@(?=@\w+)/, { token: 'variable.predefined', next: '@globalVariableTail' }],
        [/@\w+/, 'variable'],
        [/[A-Za-z_$][A-Za-z0-9_$#]*/, {
          cases: {
            '@boolNullValues': 'constant',
            '@dataTypes': 'type',
            '@keywords': 'keyword',
            '@default': 'identifier',
          },
        }],
        [/[<>=!]=?|[+\-*/%&|^~]|\|\|/, 'operator'],
        [/[()]/, '@brackets'],
        [/[,;.]/, 'delimiter'],
        [/\s+/, 'white'],
      ],
      comment: [
        [/\*\//, 'comment', '@pop'],
        [/./, 'comment'],
      ],
      globalVariableTail: [
        [/@\w+/, {
          cases: {
            '@globalVariableTails': 'variable.predefined',
            '@default': 'invalid',
          },
          next: '@pop',
        }],
        ['', '', '@pop'],
      ],
      singleQuotedString: [
        [/''/, 'string'],
        [/'/, 'string', '@pop'],
        [/[^']+/, 'string'],
      ],
      doubleQuotedString: [
        [/""/, 'string'],
        [/"/, 'string', '@pop'],
        [/[^\"]+/, 'string'],
      ],
      bracketIdentifier: [
        [/\]\]/, 'string'],
        [/\]/, 'string', '@pop'],
        [/[^\]]+/, 'string'],
      ],
      backtickIdentifier: [
        [/``/, 'string'],
        [/`/, 'string', '@pop'],
        [/[^`]+/, 'string'],
      ],
    },
  });

  monaco.editor.defineTheme(SQL_THEME_ID, {
    base: 'vs',
    inherit: true,
    rules: [
      { token: 'keyword', foreground: '1565c0', fontStyle: 'bold' },
      { token: 'string', foreground: 'ff0000' },
      { token: 'number', foreground: 'd97706' },
      { token: 'comment', foreground: '64748b', fontStyle: 'italic' },
      { token: 'identifier', foreground: '1f2937' },
      { token: 'variable', foreground: '1f2937' },
      { token: 'variable.predefined', foreground: 'ff00ff' },
      { token: 'predefined', foreground: 'ff00ff' },
      { token: 'type', foreground: '0f766e' },
      { token: 'constant', foreground: '475569' },
      { token: 'delimiter', foreground: '64748b' },
      { token: 'operator', foreground: '0f172a' },
      { token: 'invalid', foreground: 'dc2626' },
    ],
    colors: {
      'editor.background': '#ffffff',
      'editor.foreground': '#1f2937',
      'editor.lineHighlightBackground': '#dbeafe8c',
      'editor.selectionBackground': '#bfdbfeb3',
      'editorCursor.foreground': '#1f2937',
      'editorLineNumber.foreground': '#64748b',
      'editorLineNumber.activeForeground': '#1f2937',
      'editorGutter.background': '#f8fafc',
      'editorSuggestWidget.background': '#ffffff',
      'editorSuggestWidget.border': '#cbd5e1',
      'editorSuggestWidget.foreground': '#1f2937',
      'editorSuggestWidget.selectedBackground': '#e5e7eb',
      'editorSuggestWidget.highlightForeground': '#2563eb',
      'editorSuggestWidget.focusHighlightForeground': '#2563eb',
      'editorSuggestWidget.selectedForeground': '#0f172a',
      'editorWidget.background': '#ffffff',
      'editorWidget.border': '#cbd5e1',
      'editorHoverWidget.background': '#ffffff',
      'editorHoverWidget.border': '#cbd5e1',
      'scrollbarSlider.background': '#94a3b833',
      'scrollbarSlider.hoverBackground': '#94a3b855',
      'scrollbarSlider.activeBackground': '#94a3b877',
      'editorError.foreground': '#dc2626',
    },
  });

  completionProviderDisposable?.dispose();
  completionProviderDisposable = monaco.languages.registerCompletionItemProvider(SQL_LANGUAGE_ID, {
    triggerCharacters: ['.', '@'],
    provideCompletionItems(currentModel: monaco.editor.ITextModel, position: monaco.Position) {
      return { suggestions: buildSuggestions(currentModel, position) };
    },
  });
}

function getModelContext(currentModel: monaco.editor.ITextModel): ModelContext | null {
  return modelContexts.get(currentModel.uri.toString()) ?? null;
}

function triggerAstAnalysis(context: ModelContext, fullSql: string, sqlBeforeCursor: string): void {
  if (fullSql === context.astSql || context.astPending) {
    return;
  }

  context.astSql = fullSql;
  context.astPending = true;

  tryParseForCompletion(fullSql, sqlBeforeCursor, context.provider)
    .then((astList) => {
      if (astList) {
        context.astAnalysis = extractAnalysis(astList);
      }
    })
    .catch(() => { /* keep previous cache */ })
    .finally(() => { context.astPending = false; });
}

function inferContext(sqlTextBeforeCursor: string): 'database' | 'table' | 'qualified-column' | 'column' | 'general' {
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

function extractAliases(sqlText: string, context: ModelContext) {
  const aliases = new Map<string, string>();
  const referencedTables = new Set<string>();
  const cteNames: string[] = [];

  if (context.astAnalysis) {
    for (const tableRef of context.astAnalysis.tables) {
      const qualifiedName = tableRef.schema ? `${tableRef.schema}.${tableRef.table}` : tableRef.table;
      referencedTables.add(qualifiedName.toLowerCase());

      if (tableRef.alias) {
        aliases.set(tableRef.alias.toLowerCase(), qualifiedName);
      }
    }

    cteNames.push(...context.astAnalysis.cteNames);
  } else {
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

function resolveTableReference(reference: string, aliases: Map<string, string>, tables: SqlContextTable[]): SqlContextTable | null {
  const aliasTarget = aliases.get(reference.toLowerCase());
  const target = (aliasTarget ?? reference).toLowerCase();
  return tables.find((table) => table.qualifiedName.toLowerCase() === target
    || table.name.toLowerCase() === target
    || `${table.schema ?? ''}.${table.name}`.replace(/^\./, '').toLowerCase() === target) ?? null;
}

function completionInsertValue(caption: string, qualifiedPrefix?: string): string {
  if (!qualifiedPrefix) {
    return caption;
  }

  const prefixWithDot = `${qualifiedPrefix}.`;
  return caption.toLowerCase().startsWith(prefixWithDot.toLowerCase())
    ? caption.slice(prefixWithDot.length)
    : caption;
}

function buildTableCompletionItems(context: ModelContext, qualifier?: string, baseScore = 1050, extraCteNames?: string[]): CompletionCandidate[] {
  const schemaSet = new Set<string>();
  const items: CompletionCandidate[] = [];

  for (const table of context.tables) {
    if (table.schema) {
      schemaSet.add(table.schema);
    }

    items.push({
      label: table.name,
      insertText: completionInsertValue(table.name, qualifier),
      meta: table.schema ? `${table.schema} · table` : 'table',
      score: baseScore,
      kind: monaco.languages.CompletionItemKind.Struct,
      labelDescription: table.schema ?? undefined,
    });
  }

  for (const schema of schemaSet) {
    items.push({
      label: schema,
      meta: 'schema',
      score: baseScore - 10,
      kind: monaco.languages.CompletionItemKind.Module,
    });
  }

  if (extraCteNames) {
    for (const cteName of extraCteNames) {
      items.push({
        label: cteName,
        meta: 'CTE',
        score: baseScore + 20,
        kind: monaco.languages.CompletionItemKind.Struct,
      });
    }
  }

  return items;
}

function buildRoutineDocumentation(routine: RoutineInfo): string {
  const routineLabel = routine.routineType === 'Procedure'
    ? 'Stored Procedure'
    : routine.routineType === 'ScalarFunction'
      ? 'Scalar Function'
      : routine.routineType === 'TableFunction'
        ? 'Table Function'
        : routine.routineType === 'AggregateFunction'
          ? 'Aggregate Function'
          : routine.routineType;
  const paramRows = routine.parameters.length > 0
    ? routine.parameters.map((param) => `- ${param.name} : ${param.dataType}${param.direction !== 'IN' ? ` (${param.direction})` : ''}`).join('\n')
    : '- 无参数';

  return `**${routine.name}**\n\n${routineLabel}${routine.schema ? ` · ${routine.schema}` : ''}\n\n${paramRows}`;
}

function buildRoutineCompletionItems(routines: RoutineInfo[], baseScore = 1040): CompletionCandidate[] {
  return routines.map((routine) => ({
    label: routine.name,
    meta: routine.routineType,
    score: baseScore,
    kind: routine.routineType === 'Procedure'
      ? monaco.languages.CompletionItemKind.Function
      : monaco.languages.CompletionItemKind.Method,
    labelDetail: routine.parameters.length > 0
      ? ` (${routine.parameters.map((param) => `${param.dataType} ${param.name}`).join(', ')})`
      : undefined,
    labelDescription: routine.schema ?? undefined,
    documentation: buildRoutineDocumentation(routine),
  }));
}

function toCompletionCandidates(items: Array<{ caption: string; value?: string; meta: string; score: number; kind: monaco.languages.CompletionItemKind; labelDescription?: string; documentation?: string }>): CompletionCandidate[] {
  return items.map((item) => ({
    label: item.caption,
    insertText: item.value ?? item.caption,
    meta: item.meta,
    score: item.score,
    kind: item.kind,
    labelDescription: item.labelDescription,
    documentation: item.documentation,
  }));
}

function getTokenTypeAtPosition(sqlText: string, position: monaco.Position): string {
  const tokenLines = monaco.editor.tokenize(sqlText, SQL_LANGUAGE_ID);
  const lineTokens = tokenLines[position.lineNumber - 1] ?? [];
  const currentColumn = Math.max(0, position.column - 1);

  for (let index = 0; index < lineTokens.length; index++) {
    const token = lineTokens[index]!;
    const nextOffset = index + 1 < lineTokens.length ? lineTokens[index + 1]!.offset : Number.MAX_SAFE_INTEGER;
    if (currentColumn >= token.offset && currentColumn < nextOffset) {
      return token.type;
    }
  }

  return '';
}

function createSuggestionRange(position: monaco.Position, word: monaco.editor.IWordAtPosition | null | undefined): monaco.Range {
  const startColumn = word?.startColumn ?? position.column;
  const endColumn = word?.endColumn ?? position.column;
  return new monaco.Range(position.lineNumber, startColumn, position.lineNumber, endColumn);
}

function buildSuggestions(currentModel: monaco.editor.ITextModel, position: monaco.Position): monaco.languages.CompletionItem[] {
  const context = getModelContext(currentModel);
  if (!context) {
    return [];
  }

  const sqlText = currentModel.getValue();
  const tokenType = getTokenTypeAtPosition(sqlText, position);
  if (tokenType.includes('string') || tokenType.includes('comment')) {
    return [];
  }

  const beforeText = currentModel.getValueInRange(new monaco.Range(1, 1, position.lineNumber, position.column));
  triggerAstAnalysis(context, sqlText, beforeText);

  const { aliases, referencedTables, cteNames } = extractAliases(sqlText, context);
  const currentContextType = inferContext(beforeText);
  const qualifierMatch = beforeText.match(/([A-Za-z_][\w$]*(?:\.[A-Za-z_][\w$]*)*)\.[A-Za-z_\d$]*$/i);
  const qualifier = qualifierMatch?.[1];
  const atMatch = beforeText.match(/@(@?\w*)$/);
  const isAtContext = !!atMatch;
  const defaultRange = createSuggestionRange(position, currentModel.getWordUntilPosition(position));

  let items: CompletionCandidate[] = [];

  if (isAtContext) {
    const replaceLength = atMatch?.[0]?.length ?? 0;
    const atRange = new monaco.Range(position.lineNumber, position.column - replaceLength, position.lineNumber, position.column);
    items = providerCompletions.value.globalVariables.map((variable) => ({
      label: variable,
      insertText: variable,
      filterText: variable,
      meta: 'variable',
      score: 1200,
      kind: monaco.languages.CompletionItemKind.Variable,
      range: atRange,
    }));
  } else if (currentContextType === 'database') {
    items = context.databases.map((database) => ({
      label: database,
      meta: 'database',
      score: 1100,
      kind: monaco.languages.CompletionItemKind.Module,
    }));
  } else if (currentContextType === 'table') {
    items = [
      ...buildTableCompletionItems(context, qualifier, 1050, cteNames),
      ...toCompletionCandidates(providerCompletions.value.systemObjects.map((objectName) => ({
        caption: objectName,
        meta: 'system',
        score: 1000,
        kind: monaco.languages.CompletionItemKind.Reference,
      }))),
    ];
  } else if (currentContextType === 'qualified-column' && qualifier) {
    const table = resolveTableReference(qualifier, aliases, context.tables);
    if (table) {
      items = table.columns.map((column) => ({
        label: column,
        meta: table.qualifiedName,
        score: 1200,
        kind: monaco.languages.CompletionItemKind.Field,
      }));
    } else {
      const qualifierLower = qualifier.toLowerCase();
      items = [
        ...buildTableCompletionItems(context, qualifier, 1100, cteNames),
        ...toCompletionCandidates(providerCompletions.value.systemObjects
          .filter((objectName) => objectName.toLowerCase() !== qualifierLower)
          .map((objectName) => ({
            caption: objectName,
            meta: `${qualifier} · system`,
            score: 1050,
            kind: monaco.languages.CompletionItemKind.Reference,
          }))),
      ];
    }
  } else if (currentContextType === 'column') {
    const referenced = context.tables.filter((table) => referencedTables.has(table.qualifiedName.toLowerCase()) || referencedTables.has(table.name.toLowerCase()));
    const sourceTables = referenced.length > 0 ? referenced : context.tables.slice(0, 20);
    const aliasItems = [...aliases.keys()].map((alias) => ({
      label: alias,
      meta: 'alias',
      score: 1180,
      kind: monaco.languages.CompletionItemKind.Variable,
    }));
    const cteItems = cteNames.map((cteName) => ({
      label: cteName,
      meta: 'CTE',
      score: 1170,
      kind: monaco.languages.CompletionItemKind.Struct,
    }));

    items = [
      ...sourceTables.flatMap((table) => table.columns.map((column) => ({
        label: column,
        meta: table.qualifiedName,
        score: 1150,
        kind: monaco.languages.CompletionItemKind.Field,
      }))),
      ...aliasItems,
      ...cteItems,
      ...buildRoutineCompletionItems(context.routines, 960),
      ...toCompletionCandidates(providerCompletions.value.functions.map((fn) => ({
        caption: fn,
        meta: 'function',
        score: 950,
        kind: monaco.languages.CompletionItemKind.Function,
      }))),
      ...toCompletionCandidates(providerCompletions.value.keywords.map((keyword) => ({
        caption: keyword,
        meta: 'keyword',
        score: 900,
        kind: monaco.languages.CompletionItemKind.Keyword,
      }))),
    ];
  } else {
    items = [
      ...toCompletionCandidates(providerCompletions.value.keywords.map((keyword) => ({
        caption: keyword,
        meta: 'keyword',
        score: 900,
        kind: monaco.languages.CompletionItemKind.Keyword,
      }))),
      ...buildRoutineCompletionItems(context.routines, 890),
      ...toCompletionCandidates(providerCompletions.value.functions.map((fn) => ({
        caption: fn,
        meta: 'function',
        score: 880,
        kind: monaco.languages.CompletionItemKind.Function,
      }))),
      ...toCompletionCandidates(providerCompletions.value.globalVariables.map((variable) => ({
        caption: variable,
        meta: 'variable',
        score: 870,
        kind: monaco.languages.CompletionItemKind.Variable,
      }))),
      ...toCompletionCandidates(providerCompletions.value.systemObjects.map((objectName) => ({
        caption: objectName,
        meta: 'system',
        score: 860,
        kind: monaco.languages.CompletionItemKind.Reference,
      }))),
      ...buildTableCompletionItems(context, qualifier, 850, cteNames),
      ...context.databases.map((database) => ({
        label: database,
        meta: 'database',
        score: 800,
        kind: monaco.languages.CompletionItemKind.Module,
      })),
    ];
  }

  const deduped = new Map<string, CompletionCandidate>();
  for (const item of items) {
    const key = `${item.meta}:${item.label.toLowerCase()}`;
    if (!deduped.has(key)) {
      deduped.set(key, item);
    }
  }

  const typedPrefix = isAtContext
    ? ((atMatch?.[1] ?? '').replace(/^@/, '').toLowerCase())
    : currentModel.getWordUntilPosition(position).word.toLowerCase();

  return [...deduped.values()]
    .filter((item) => !typedPrefix || item.label.toLowerCase().includes(typedPrefix))
    .map((item) => {
      const boostedScore = typedPrefix && item.label.toLowerCase().startsWith(typedPrefix)
        ? item.score + 200
        : item.score;
      const label = item.labelDetail || item.labelDescription
        ? {
            label: item.label,
            detail: item.labelDetail,
            description: item.labelDescription,
          }
        : item.label;

      return {
        label,
        kind: item.kind,
        insertText: item.insertText ?? item.label,
        filterText: item.filterText,
        detail: item.meta,
        documentation: item.documentation ? { value: item.documentation } : undefined,
        range: item.range ?? defaultRange,
        sortText: String(999999 - boostedScore).padStart(6, '0'),
      } satisfies monaco.languages.CompletionItem;
    });
}

function extractDeclaredVariables(sql: string): Set<string> {
  const variables = new Set<string>();
  const declareBlockRegex = /\bDECLARE\b\s+([\s\S]*?)(?=\b(?:SELECT|INSERT|UPDATE|DELETE|EXEC|EXECUTE|IF|WHILE|BEGIN|PRINT|RETURN|SET)\b|;|$)/gi;
  let blockMatch: RegExpExecArray | null;
  while ((blockMatch = declareBlockRegex.exec(sql)) !== null) {
    const body = blockMatch[1];
    if (!body) {
      continue;
    }

    const variableRegex = /@(\w+)/g;
    let variableMatch: RegExpExecArray | null;
    while ((variableMatch = variableRegex.exec(body)) !== null) {
      variables.add(`@${variableMatch[1]!.toLowerCase()}`);
    }
  }

  const setRegex = /\bSET\s+(@\w+)\s*=/gi;
  let setMatch: RegExpExecArray | null;
  while ((setMatch = setRegex.exec(sql)) !== null) {
    if (setMatch[1]) {
      variables.add(setMatch[1].toLowerCase());
    }
  }

  const procParamRegex = /\b(?:CREATE|ALTER)\s+(?:PROC|PROCEDURE)\b\s+\S+\s+([\s\S]*?)\bAS\b/gi;
  let procMatch: RegExpExecArray | null;
  while ((procMatch = procParamRegex.exec(sql)) !== null) {
    const paramBody = procMatch[1];
    if (!paramBody) {
      continue;
    }

    const paramVarRegex = /@(\w+)/g;
    let paramVarMatch: RegExpExecArray | null;
    while ((paramVarMatch = paramVarRegex.exec(paramBody)) !== null) {
      variables.add(`@${paramVarMatch[1]!.toLowerCase()}`);
    }
  }

  return variables;
}

function collectMarkersByLineScan(currentModel: monaco.editor.ITextModel, declaredVariables: Set<string>): monaco.editor.IMarkerData[] {
  const markers: monaco.editor.IMarkerData[] = [];
  for (let lineNumber = 1; lineNumber <= currentModel.getLineCount(); lineNumber++) {
    const line = currentModel.getLineContent(lineNumber);
    const variableRegex = /(?<!@)@(?!@)\w+/g;
    let match: RegExpExecArray | null;
    while ((match = variableRegex.exec(line)) !== null) {
      const fullVariable = match[0];
      if (declaredVariables.has(fullVariable.toLowerCase())) {
        continue;
      }

      markers.push({
        severity: monaco.MarkerSeverity.Error,
        message: `未声明的变量 ${fullVariable}`,
        startLineNumber: lineNumber,
        startColumn: match.index + 1,
        endLineNumber: lineNumber,
        endColumn: match.index + fullVariable.length + 1,
      });
    }
  }

  return markers;
}

function collectGlobalVariableMarkers(currentModel: monaco.editor.ITextModel): monaco.editor.IMarkerData[] {
  const markers: monaco.editor.IMarkerData[] = [];
  for (let lineNumber = 1; lineNumber <= currentModel.getLineCount(); lineNumber++) {
    const line = currentModel.getLineContent(lineNumber);
    const globalVariableRegex = /@@\w+/g;
    let match: RegExpExecArray | null;
    while ((match = globalVariableRegex.exec(line)) !== null) {
      const fullVariable = match[0].toLowerCase();
      if (highlightGlobalVariables.includes(fullVariable)) {
        continue;
      }

      markers.push({
        severity: monaco.MarkerSeverity.Error,
        message: `不存在的全局变量 ${match[0]}`,
        startLineNumber: lineNumber,
        startColumn: match.index + 1,
        endLineNumber: lineNumber,
        endColumn: match.index + match[0].length + 1,
      });
    }
  }

  return markers;
}

function applyValidationState(currentModel: monaco.editor.ITextModel, markers: monaco.editor.IMarkerData[]): void {
  monaco.editor.setModelMarkers(currentModel, MONACO_OWNER, markers);

  if (!editor) {
    return;
  }

  const invalidGlobalDecorations = markers
    .filter((marker) => marker.message.startsWith('不存在的全局变量'))
    .map((marker) => ({
      range: new monaco.Range(marker.startLineNumber, marker.startColumn, marker.endLineNumber, marker.endColumn),
      options: {
        inlineClassName: 'dbv-invalid-global-variable',
      },
    }));

  invalidGlobalDecorationIds = editor.deltaDecorations(invalidGlobalDecorationIds, invalidGlobalDecorations);
}

function validateVariableDeclarations(currentModel: monaco.editor.ITextModel): void {
  const context = getModelContext(currentModel);
  if (!context || context.provider !== 'sqlserver') {
    applyValidationState(currentModel, []);
    return;
  }

  if (context.astAnalysis && context.astAnalysis.declaredVariables.length >= 0) {
    const declaredSet = new Set(context.astAnalysis.declaredVariables);
    if (context.astAnalysis.referencedVariables.length > 0) {
      const markers = context.astAnalysis.referencedVariables.flatMap((reference) => {
        const lineNumber = reference.line + 1;
        const lineContent = currentModel.getLineContent(lineNumber);
        const zeroBasedColumn = Math.max(0, reference.column);
        const hasGlobalPrefix = zeroBasedColumn > 0 && lineContent[zeroBasedColumn - 1] === '@';
        const normalizedName = reference.name.toLowerCase();

        if (hasGlobalPrefix) {
          return [];
        }

        if (declaredSet.has(normalizedName)) {
          return [];
        }

        return [{
          severity: monaco.MarkerSeverity.Error,
          message: `未声明的变量 ${reference.name}`,
          startLineNumber: lineNumber,
          startColumn: reference.column + 1,
          endLineNumber: lineNumber,
          endColumn: reference.column + reference.name.length + 1,
        } satisfies monaco.editor.IMarkerData];
      });
      applyValidationState(currentModel, [...markers, ...collectGlobalVariableMarkers(currentModel)]);
      return;
    }

    applyValidationState(currentModel, [
      ...collectMarkersByLineScan(currentModel, declaredSet),
      ...collectGlobalVariableMarkers(currentModel),
    ]);
    return;
  }

  applyValidationState(currentModel, [
    ...collectMarkersByLineScan(currentModel, extractDeclaredVariables(currentModel.getValue())),
    ...collectGlobalVariableMarkers(currentModel),
  ]);
}

function scheduleVariableValidation(currentModel: monaco.editor.ITextModel): void {
  if (validationTimer) {
    clearTimeout(validationTimer);
  }

  validationTimer = setTimeout(() => {
    validateVariableDeclarations(currentModel);
  }, 300);
}

function cutLineOrSelection(instance: monaco.editor.IStandaloneCodeEditor): void {
  const currentModel = instance.getModel();
  const selections = instance.getSelections();
  if (!currentModel || !selections || selections.length === 0) {
    return;
  }

  if (selections.some((selection: monaco.Selection) => !selection.isEmpty())) {
    const selectedText = selections.map((selection: monaco.Selection) => currentModel.getValueInRange(selection)).join(currentModel.getEOL());
    void navigator.clipboard.writeText(selectedText);
    instance.executeEdits('dbv-cut-selection', selections.map((selection: monaco.Selection) => ({ range: selection, text: '' })));
    return;
  }

  const position = instance.getPosition();
  if (!position) {
    return;
  }

  const lineNumber = position.lineNumber;
  const lineText = currentModel.getLineContent(lineNumber);
  const endLineNumber = lineNumber < currentModel.getLineCount() ? lineNumber + 1 : lineNumber;
  const endColumn = lineNumber < currentModel.getLineCount() ? 1 : currentModel.getLineMaxColumn(lineNumber);
  const textToCopy = lineText + (lineNumber < currentModel.getLineCount() ? currentModel.getEOL() : '');

  void navigator.clipboard.writeText(textToCopy);
  instance.executeEdits('dbv-cut-line', [{
    range: new monaco.Range(lineNumber, 1, endLineNumber, endColumn),
    text: '',
  }]);
}

function createModelContext(): ModelContext {
  return {
    provider: props.provider,
    database: props.database,
    databases: [...props.databases],
    tables: [...props.tables],
    routines: [...props.routines],
    astAnalysis: null,
    astSql: '',
    astPending: false,
  };
}

function syncModelContext(): void {
  if (!model) {
    return;
  }

  const currentContext = getModelContext(model) ?? createModelContext();
  currentContext.provider = props.provider;
  currentContext.database = props.database;
  currentContext.databases = [...props.databases];
  currentContext.tables = [...props.tables];
  currentContext.routines = [...props.routines];
  currentContext.astAnalysis = null;
  currentContext.astSql = '';
  modelContexts.set(model.uri.toString(), currentContext);
}

function createEditor(): void {
  if (!hostRef.value) {
    return;
  }

  editorReady.value = false;

  ensureMonacoRegistration();
  monaco.editor.setTheme(SQL_THEME_ID);

  modelCounter += 1;
  const uri = monaco.Uri.parse(`inmemory://database-viewer/sql-${modelCounter}.sql`);
  model = monaco.editor.createModel(props.modelValue, SQL_LANGUAGE_ID, uri);
  modelContexts.set(uri.toString(), createModelContext());

  editor = monaco.editor.create(hostRef.value, {
    model,
    theme: SQL_THEME_ID,
    automaticLayout: true,
    fontFamily: "'Cascadia Code', 'JetBrains Mono', 'Cascadia Mono', 'Consolas', monospace",
    fontSize: editorFontSize.value,
    lineNumbers: 'on',
    glyphMargin: false,
    folding: false,
    minimap: { enabled: false },
    renderLineHighlight: 'line',
    guides: { indentation: false },
    wordWrap: 'off',
    insertSpaces: true,
    tabSize: 2,
    scrollBeyondLastLine: false,
    smoothScrolling: false,
    contextmenu: true,
    mouseWheelZoom: true,
    dragAndDrop: true,
    fixedOverflowWidgets: true,
    hover: {
      above: false,
    },
    quickSuggestions: {
      other: true,
      comments: false,
      strings: false,
    },
    quickSuggestionsDelay: 0,
    suggestOnTriggerCharacters: true,
    acceptSuggestionOnEnter: 'on',
    suggest: {
      showWords: false,
      showSnippets: false,
      preview: false,
      selectionMode: 'always',
    },
    scrollbar: {
      verticalScrollbarSize: 10,
      horizontalScrollbarSize: 10,
    },
    padding: {
      top: 0,
      bottom: 8,
    },
  });

  editor.addCommand(monaco.KeyCode.F5, () => emit('execute'));
  editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter, () => emit('execute'));
  editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyD, () => {
    void editor?.getAction('editor.action.copyLinesDownAction')?.run();
  });
  editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyX, () => {
    if (editor) {
      cutLineOrSelection(editor);
    }
  });

  editor.onDidChangeModelContent(() => {
    if (!editor || !model || suppressModelSync) {
      return;
    }

    const value = model.getValue();
    emit('update:modelValue', value);

    const currentContext = getModelContext(model);
    if (currentContext) {
      triggerAstAnalysis(currentContext, value, value);
    }

    scheduleVariableValidation(model);
  });

  editor.onDidChangeConfiguration((event: monaco.editor.ConfigurationChangedEvent) => {
    if (event.hasChanged(monaco.editor.EditorOption.fontSize) && editor) {
      editorFontSize.value = editor.getOption(monaco.editor.EditorOption.fontSize);
    }
  });

  editor.onMouseDown((event) => {
    if (!editor || !event.target.position) {
      selectionClickState = null;
      return;
    }

    const currentSelection = editor.getSelection();
    if (!currentSelection || currentSelection.isEmpty() || !currentSelection.containsPosition(event.target.position)) {
      selectionClickState = null;
      return;
    }

    selectionClickState = {
      selection: currentSelection,
      position: event.target.position,
      moved: false,
    };
  });

  editor.onMouseMove((event) => {
    if (!selectionClickState || !event.target.position) {
      return;
    }

    if (event.target.position.lineNumber !== selectionClickState.position.lineNumber
      || event.target.position.column !== selectionClickState.position.column) {
      selectionClickState.moved = true;
    }
  });

  editor.onMouseUp((event) => {
    if (!editor || !selectionClickState) {
      return;
    }

    const clickState = selectionClickState;
    selectionClickState = null;

    const targetPosition = event.target.position ?? clickState.position;
    const currentSelection = editor.getSelection();
    if (!targetPosition || !currentSelection || currentSelection.isEmpty()) {
      return;
    }

    if (clickState.moved || !currentSelection.equalsSelection(clickState.selection) || !currentSelection.containsPosition(targetPosition)) {
      return;
    }

    editor.setPosition(targetPosition);
    editor.setSelection(new monaco.Selection(targetPosition.lineNumber, targetPosition.column, targetPosition.lineNumber, targetPosition.column));
    editor.focus();
  });

  syncModelContext();
  validateVariableDeclarations(model);
  editor.layout();

  requestAnimationFrame(() => {
    editorReady.value = true;
  });
}

onMounted(() => {
  requestAnimationFrame(() => {
    createEditor();
  });
});

watch(() => props.modelValue, (value) => {
  if (!editor || !model) {
    return;
  }

  const currentValue = model.getValue();
  if (currentValue === value) {
    return;
  }

  const selection = editor.getSelection();
  suppressModelSync = true;
  model.pushEditOperations([], [{ range: model.getFullModelRange(), text: value }], () => selection ? [selection] : null);
  suppressModelSync = false;
});

watch(() => props.height, () => {
  editor?.layout();
}, { immediate: true });

watch(() => [
  props.provider,
  props.database,
  props.databases.join('|'),
  props.tables.map((table) => `${table.qualifiedName}:${table.columns.join(',')}`).join('|'),
  props.routines.map((routine) => `${routine.schema ?? ''}.${routine.name}:${routine.parameters.map((param) => `${param.name}:${param.dataType}`).join(',')}`).join('|'),
] as const, () => {
  syncModelContext();
  if (model) {
    validateVariableDeclarations(model);
  }
});

onBeforeUnmount(() => {
  if (validationTimer) {
    clearTimeout(validationTimer);
  }

  if (model) {
    monaco.editor.setModelMarkers(model, MONACO_OWNER, []);
    modelContexts.delete(model.uri.toString());
    model.dispose();
    model = null;
  }

  if (editor) {
    invalidGlobalDecorationIds = editor.deltaDecorations(invalidGlobalDecorationIds, []);
  }

  editor?.dispose();
  editor = null;
});
</script>

<template>
  <div
    ref="hostRef"
    class="sql-code-editor"
    :class="{ 'sql-code-editor-ready': editorReady }"
    :style="{ height: fill ? '100%' : `${height}px` }"
  ></div>
</template>

<style scoped lang="scss">
.sql-code-editor {
  width: 100%;
  min-height: 160px;
  border: 1px solid $color-border-light;
  border-radius: 0;
  overflow: hidden;
  background: $color-surface-white;
  opacity: 0;

  &-ready {
    opacity: 1;
  }

  :deep(.monaco-editor),
  :deep(.monaco-editor .margin),
  :deep(.monaco-editor .monaco-editor-background) {
    background: $color-surface-white;
  }

  :deep(.monaco-editor .margin) {
    background: $color-surface-light;
  }

  :deep(.monaco-editor .line-numbers) {
    color: $color-text-secondary;
  }

  :deep(.monaco-editor .current-line),
  :deep(.monaco-editor .current-line-margin) {
    border: none !important;
  }

  :deep(.monaco-editor .squiggly-error) {
    border-bottom-width: 2px;
  }

  :deep(.monaco-editor .dbv-invalid-global-variable) {
    color: #dc2626 !important;
  }

  :deep(.monaco-editor .suggest-widget) {
    border-radius: 0;
    box-shadow: 0 20px 44px $shadow-medium;
    overflow: hidden;
  }

  :deep(.monaco-editor .suggest-widget .monaco-list-row .contents) {
    font-family: $font-family-mono-ext;
  }

  :deep(.monaco-editor .suggest-widget .monaco-highlighted-label .highlight) {
    font-weight: 700;
  }

  :deep(.monaco-editor .suggest-widget .details-label),
  :deep(.monaco-editor .suggest-widget .label-description),
  :deep(.monaco-editor .suggest-widget .label-detail) {
    color: $color-text-muted;
  }
}
</style>
