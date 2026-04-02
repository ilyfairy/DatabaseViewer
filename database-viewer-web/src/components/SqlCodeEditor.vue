<script setup lang="ts">
import ace from 'ace-builds';
import 'ace-builds/src-noconflict/ext-language_tools';
import 'ace-builds/src-noconflict/mode-sql';
import 'ace-builds/src-noconflict/theme-textmate';
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import type { Ace } from 'ace-builds';
import type { ProviderType, SqlContextTable } from '../types/explorer';

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

const providerKeywords = computed(() => props.provider === 'mysql'
  ? MYSQL_KEYWORDS
  : props.provider === 'postgresql'
    ? POSTGRESQL_KEYWORDS
    : props.provider === 'sqlite'
      ? SQLITE_KEYWORDS
    : MSSQL_KEYWORDS);

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

function extractAliases(sqlText: string) {
  const aliases = new Map<string, string>();
  const referencedTables = new Set<string>();
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

  return { aliases, referencedTables };
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

function buildSqlCompleter(): AceCompleter {
  return {
    getCompletions: (_editor, session, pos, prefix, callback) => {
      const lines = session.getDocument().getAllLines();
      const beforeText = `${lines.slice(0, pos.row).join('\n')}${pos.row > 0 ? '\n' : ''}${(lines[pos.row] ?? '').slice(0, pos.column)}`;
      const sqlText = session.getValue();
      const { aliases, referencedTables } = extractAliases(sqlText);
      const context = inferContext(beforeText);
      const qualifierMatch = beforeText.match(/([A-Za-z_][\w$]*(?:\.[A-Za-z_][\w$]*)*)\.[A-Za-z_\d$]*$/i);
      const qualifier = qualifierMatch?.[1];

      // eslint-disable-next-line no-useless-assignment
      let items: AceCompletionItem[] = [];

      if (context === 'database') {
        items = toCompletionItems(props.databases.map((database) => ({
          caption: database,
          meta: 'database',
          score: 1100,
        })));
      }
      else if (context === 'table') {
        items = toCompletionItems(props.tables.map((table) => ({
          caption: table.qualifiedName,
          value: completionInsertValue(table.qualifiedName, qualifier),
          meta: 'table',
          score: 1050,
        })));
      }
      else if (context === 'qualified-column' && qualifier) {
        const table = resolveTableReference(qualifier, aliases);
        items = toCompletionItems((table?.columns ?? []).map((column) => ({
          caption: column,
          meta: table?.qualifiedName ?? 'column',
          score: 1200,
        })));
      }
      else if (context === 'column') {
        const referenced = props.tables.filter((table) => referencedTables.has(table.qualifiedName.toLowerCase()) || referencedTables.has(table.name.toLowerCase()));
        const sourceTables = referenced.length ? referenced : props.tables.slice(0, 20);
        items = [
          ...toCompletionItems(sourceTables.flatMap((table) => table.columns.map((column) => ({
            caption: column,
            meta: table.qualifiedName,
            score: 1150,
          })))),
          ...toCompletionItems(providerKeywords.value.map((keyword) => ({
            caption: keyword,
            meta: 'keyword',
            score: 900,
          }))),
        ];
      }
      else {
        items = [
          ...toCompletionItems(providerKeywords.value.map((keyword) => ({
            caption: keyword,
            meta: 'keyword',
            score: 900,
          }))),
          ...toCompletionItems(props.tables.slice(0, 24).map((table) => ({
            caption: table.qualifiedName,
            value: completionInsertValue(table.qualifiedName, qualifier),
            meta: 'table',
            score: 850,
          }))),
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

      const normalizedPrefix = prefix.toLowerCase();
      const results = [...deduped.values()].filter((item) => !normalizedPrefix || item.caption.toLowerCase().includes(normalizedPrefix));
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

function configureEditor(nextEditor: Ace.Editor) {
  nextEditor.session.setMode('ace/mode/sql');
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

  nextEditor.session.on('change', () => {
    if (suppressModelSync) {
      return;
    }

    emit('update:modelValue', nextEditor.getValue());
  });
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

  applyCompleters(editor);
});

onBeforeUnmount(() => {
  editor?.destroy();
  editor = null;
});

const MSSQL_KEYWORDS = [
  'SELECT', 'FROM', 'WHERE', 'JOIN', 'LEFT', 'RIGHT', 'INNER', 'OUTER', 'ON', 'GROUP', 'BY', 'ORDER', 'HAVING', 'TOP', 'DISTINCT', 'AS', 'CASE', 'WHEN', 'THEN', 'ELSE', 'END', 'INSERT', 'INTO', 'VALUES', 'UPDATE', 'SET', 'DELETE', 'CREATE', 'ALTER', 'DROP', 'TABLE', 'VIEW', 'INDEX', 'USE', 'UNION', 'ALL', 'AND', 'OR', 'NOT', 'NULL', 'IS', 'IN', 'EXISTS', 'BETWEEN', 'LIKE', 'MERGE', 'OVER', 'PARTITION', 'WITH'
];

const MYSQL_KEYWORDS = [
  'SELECT', 'FROM', 'WHERE', 'JOIN', 'LEFT', 'RIGHT', 'INNER', 'OUTER', 'ON', 'GROUP', 'BY', 'ORDER', 'HAVING', 'LIMIT', 'DISTINCT', 'AS', 'CASE', 'WHEN', 'THEN', 'ELSE', 'END', 'INSERT', 'INTO', 'VALUES', 'UPDATE', 'SET', 'DELETE', 'CREATE', 'ALTER', 'DROP', 'TABLE', 'VIEW', 'INDEX', 'USE', 'UNION', 'ALL', 'AND', 'OR', 'NOT', 'NULL', 'IS', 'IN', 'EXISTS', 'BETWEEN', 'LIKE', 'DESCRIBE', 'SHOW'
];

const POSTGRESQL_KEYWORDS = [
  'SELECT', 'FROM', 'WHERE', 'JOIN', 'LEFT', 'RIGHT', 'INNER', 'FULL', 'OUTER', 'ON', 'GROUP', 'BY', 'ORDER', 'HAVING', 'LIMIT', 'OFFSET', 'DISTINCT', 'AS', 'CASE', 'WHEN', 'THEN', 'ELSE', 'END', 'INSERT', 'INTO', 'VALUES', 'UPDATE', 'SET', 'DELETE', 'CREATE', 'ALTER', 'DROP', 'TABLE', 'VIEW', 'INDEX', 'SCHEMA', 'UNION', 'ALL', 'AND', 'OR', 'NOT', 'NULL', 'IS', 'IN', 'EXISTS', 'BETWEEN', 'LIKE', 'ILIKE', 'RETURNING', 'WITH'
];

const SQLITE_KEYWORDS = [
  'SELECT', 'FROM', 'WHERE', 'JOIN', 'LEFT', 'INNER', 'ON', 'GROUP', 'BY', 'ORDER', 'HAVING', 'LIMIT', 'OFFSET', 'DISTINCT', 'AS', 'CASE', 'WHEN', 'THEN', 'ELSE', 'END', 'INSERT', 'INTO', 'VALUES', 'UPDATE', 'SET', 'DELETE', 'CREATE', 'ALTER', 'DROP', 'TABLE', 'VIEW', 'INDEX', 'TRIGGER', 'UNION', 'ALL', 'AND', 'OR', 'NOT', 'NULL', 'IS', 'IN', 'EXISTS', 'BETWEEN', 'LIKE', 'PRAGMA', 'ATTACH', 'DETACH', 'WITH'
];
</script>

<template>
  <div ref="hostRef" class="sql-code-editor" />
</template>

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
    color: $color-accent-green-cell;
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
