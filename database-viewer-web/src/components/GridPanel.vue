<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { NAlert, NButton, NCard, NEmpty, NInput, NModal, NSelect, NSpin, NTag, useDialog } from 'naive-ui';
import type { DropdownOption } from 'naive-ui';
import ContextDropdown from './ContextDropdown.vue';
import GridCellEditorModal from './GridCellEditorModal.vue';
import { useExplorerStore } from '../stores/explorer';
import type { CellContentPreview, CellValue, ForeignKeyRef, TableColumn, TableRow, TableRowWriteValueRequest } from '../types/explorer';

const props = defineProps<{ tableKey: string }>();
const store = useExplorerStore();
const dialog = useDialog();

const table = computed(() => store.getLoadedTable(props.tableKey));
const tableMeta = computed(() => store.getTable(props.tableKey));
const rows = computed(() => store.getFilteredRows(props.tableKey));
const searchState = computed(() => store.getTableSearchState(props.tableKey));
const searchActive = computed(() => store.isSearchActive(props.tableKey));
const sortState = computed(() => store.getTableSortState(props.tableKey));
const columnWidths = ref<Record<string, number>>({});
const previewCache = ref<Record<string, CellContentPreview>>({});
const previewState = ref<{
  show: boolean
  loading: boolean
  title: string
  content: CellContentPreview | null
  error: string | null
}>({
  show: false,
  loading: false,
  title: '',
  content: null,
  error: null,
});
type VisibleColumn = TableColumn & { fk?: ForeignKeyRef }
type ActiveEditorState = {
  rowKey: string
  columnName: string
  draft: boolean
  saving: boolean
}
type DraftCellState = {
  valueKind: 'text' | 'binary' | 'null'
  textValue: string
  base64Value: string | null
  setNull: boolean
  fileName: string
}
type EditorPayload = {
  valueKind: 'text' | 'binary' | 'null'
  textValue: string | null
  base64Value: string | null
  setNull: boolean
}

const contextMenu = ref<{
  x: number
  y: number
  rowKey: string
  column: VisibleColumn
  value: CellValue | undefined
  draft: boolean
} | null>(null);
const dialogEditorState = ref<{
  show: boolean
  rowKey: string | null
  column: VisibleColumn | null
  foreignKey: ForeignKeyRef | null
  value: CellValue | undefined
  saving: boolean
}>({
  show: false,
  rowKey: null,
  column: null,
  foreignKey: null,
  value: undefined,
  saving: false,
});
const editMode = ref(false);
const activeEditor = ref<ActiveEditorState | null>(null);
const activeEditorAnchor = ref<{ top: number; left: number; width: number; height: number; verticalOffset: number; horizontalOffset: number; fontWeight: string } | null>(null);
const activeEditorInitialPayload = ref<EditorPayload | null>(null);
const activeEditorDirty = ref(false);
const activeEditorTextValue = ref('');
const activeEditorSetNull = ref(false);
const activeEditorBinaryBase64 = ref<string | null>(null);
const activeEditorBinaryFileName = ref('');
const activeEditorInputRef = ref<HTMLInputElement | null>(null);
const activeEditorTextareaRef = ref<HTMLTextAreaElement | null>(null);
const suppressWindowClickUntil = ref(0);
const editorMouseDownActive = ref(false);
const draftRowValues = ref<Record<string, DraftCellState>>({});
const selectedRowKeys = ref<string[]>([]);
const dragSelectionMode = ref<'select' | 'unselect' | null>(null);
const dragSelectionLastRowKey = ref<string | null>(null);
const binaryFileInputRef = ref<HTMLInputElement | null>(null);
const contextBinaryFileInputRef = ref<HTMLInputElement | null>(null);
const pendingBinaryReplaceTarget = ref<{ rowKey: string; column: VisibleColumn; draft: boolean } | null>(null);

function isBinaryColumn(columnName: string, columnType?: string) {
  const normalizedType = (columnType ?? '').toLowerCase();
  const normalizedName = columnName.toLowerCase();
  return normalizedType.includes('binary')
    || normalizedType.includes('image')
    || normalizedType.includes('blob')
    || normalizedName.endsWith('image')
    || normalizedName.endsWith('photo')
    || normalizedName.endsWith('thumbnail');
}

const visibleColumns = computed<VisibleColumn[]>(() => {
  if (!table.value) {
    return [];
  }

  return table.value.columns
    .filter((column) => !column.isHiddenRowId)
    .map((column) => {
      const fk = store.getForeignKey(props.tableKey, column.name);
      return {
        ...column,
        isPrimaryKey: !!column.isPrimaryKey,
        fk,
      };
    });
});

const hasPrimaryKeyColumns = computed(() =>
  (table.value?.columns ?? []).some((column) => column.isPrimaryKey),
);

function cellValue(row: TableRow, columnName: string, columnType: string | undefined) {
  return store.formatFieldValue(columnName, columnType, row[columnName] ?? null);
}

function displayCellValue(row: TableRow, column: VisibleColumn) {
  const rawValue = row[column.name] ?? null;
  if (typeof rawValue === 'string' && !isBinaryColumn(column.name, column.type)) {
    const normalized = rawValue.replace(/\r\n/g, '\n');
    if (normalized.includes('\n')) {
      const [firstLine, ...rest] = normalized.split('\n');
      const preview = `${firstLine || ' '} ↵${rest.length > 0 ? ` +${rest.length}` : ''}`;
      return store.formatFieldValue(column.name, column.type, preview);
    }
  }

  return cellValue(row, column.name, column.type);
}

function isNullCell(row: TableRow, columnName: string) {
  return (row[columnName] ?? null) === null;
}

function isDraftNullCell(columnName: string) {
  return draftRowValue(columnName)?.setNull === true;
}

function displayColumnType(column: TableColumn) {
  if (!column.type) {
    return 'unknown';
  }

  if (column.maxLength === null || column.maxLength === undefined) {
    return column.type;
  }

  if (column.maxLength < 0) {
    return `${column.type}(max)`;
  }

  return `${column.type}(${column.maxLength})`;
}

function handleRowOpen(rowKey: string) {
  store.openRowFromGrid(props.tableKey, rowKey);
}

function handleForeignKeyClick(rowKey: string, sourceColumn: string) {
  if (editMode.value) {
    return;
  }

  const foreignKey = store.getForeignKey(props.tableKey, sourceColumn);
  if (!foreignKey) {
    return;
  }

  store.navigateForeignKeyFromGrid(props.tableKey, rowKey, foreignKey);
}

function isReadOnlySystemColumn(columnType: string | undefined) {
  const normalized = (columnType ?? '').toLowerCase();
  return normalized === 'timestamp' || normalized === 'rowversion';
}

function isEditableColumn(column: VisibleColumn) {
  return !!table.value?.primaryKeys.length
    && !column.isAutoGenerated
    && !column.isComputed
    && !isReadOnlySystemColumn(column.type);
}

function isWritableOnInsert(column: VisibleColumn) {
  return !column.isAutoGenerated && !column.isComputed && !isReadOnlySystemColumn(column.type);
}

function isBooleanColumn(column: VisibleColumn) {
  return ['bit', 'bool', 'boolean'].includes((column.type ?? '').toLowerCase());
}

function isMultilineTextColumn(column: VisibleColumn) {
  const normalizedType = (column.type ?? '').toLowerCase();
  if (isBinaryColumn(column.name, column.type) || isBooleanColumn(column)) {
    return false;
  }

  return normalizedType.includes('text')
    || normalizedType.includes('json')
    || normalizedType.includes('xml')
    || ((normalizedType.includes('char') || normalizedType.includes('string'))
      && ((column.maxLength ?? 0) < 0 || (column.maxLength ?? 0) > 255));
}

function isRowOpenCell(columnName: string, columnType: string | undefined, isPrimaryKey: boolean | undefined, columnIndex: number) {
  if (isPrimaryKey) {
    return true;
  }

  if (hasPrimaryKeyColumns.value) {
    return false;
  }

  return columnIndex === 0 && !isBinaryColumn(columnName, columnType);
}

function sortIndicator(columnName: string) {
  if (sortState.value.columnName !== columnName) {
    return '';
  }

  return sortState.value.direction === 'asc'
    ? '↑'
    : sortState.value.direction === 'desc'
      ? '↓'
      : '';
}

function handleHeaderSort(columnName: string, columnType: string | undefined) {
  void store.toggleTableSort(props.tableKey, columnName, columnType);
}

const hasRows = computed(() => rows.value.length > 0);
const loadedRowCount = computed(() => searchActive.value ? (searchState.value?.rows.length ?? 0) : (table.value?.rows.length ?? 0));
const totalRowCount = computed(() => searchActive.value ? (searchState.value?.totalMatches ?? 0) : (tableMeta.value?.rowCount ?? loadedRowCount.value));
const hasMoreRows = computed(() => searchActive.value ? !!searchState.value?.hasMoreRows : !!table.value?.hasMoreRows);
const isReadOnlyConnection = computed(() => store.isTableConnectionReadOnly(props.tableKey));
const canEnableEditMode = computed(() => !isReadOnlyConnection.value && !!table.value && visibleColumns.value.some((column) => isEditableColumn(column) || isWritableOnInsert(column)));
const draftRowHasValues = computed(() => Object.values(draftRowValues.value).some((item) => item.setNull || item.base64Value !== null || item.textValue.trim().length > 0));
const selectedRowCount = computed(() => selectedRowKeys.value.length);
const contextMenuDeleteRowCount = computed(() => {
  const menu = contextMenu.value;
  if (!menu || menu.draft) {
    return 0;
  }

  return editMode.value && selectedRowKeys.value.length > 1 && selectedRowKeys.value.includes(menu.rowKey)
    ? selectedRowKeys.value.length
    : 1;
});
const contextMenuDeleteLabel = computed(() => contextMenuDeleteRowCount.value > 1 ? `删除所选 ${contextMenuDeleteRowCount.value} 行` : '删除行');
const contextMenuOptions = computed<DropdownOption[]>(() => {
  const menu = contextMenu.value;
  if (!menu) {
    return [];
  }

  const options: DropdownOption[] = [];
  if (isBinaryColumn(menu.column.name, menu.column.type)) {
    if (!isReadOnlyConnection.value) {
      options.push({ label: '替换二进制文件...', key: 'replace-binary' });
    }

    options.push(
      { label: '预览二进制', key: 'preview-binary' },
      { label: '保存二进制到文件...', key: 'save-binary' },
    );
  }

  if (!isReadOnlyConnection.value && isEditableColumn(menu.column)) {
    options.push({ label: '编辑单元格...', key: 'edit-cell' });
    if (!editMode.value) {
      options.push({ label: '进入编辑模式', key: 'enter-edit-mode' });
    }
  }

  if (!isReadOnlyConnection.value && menu.column.isNullable && (menu.draft ? isWritableOnInsert(menu.column) : isEditableColumn(menu.column))) {
    options.push({ label: '设为 NULL', key: 'set-null' });
  }

  if (!isReadOnlyConnection.value && !menu.draft) {
    options.push({ label: contextMenuDeleteLabel.value, key: 'delete-row' });
  }

  return options;
});

// ── 筛选 / 搜索 ──
const filterOpen = ref(false);
const searchColumnOptions = computed(() =>
  store.getSearchableColumns(props.tableKey).map((column) => ({
    label: `${column.name} (${column.type})`,
    value: column.name,
  })),
);

const previewImageUrl = computed(() => {
  const content = previewState.value.content;
  if (!content?.base64Data || content.kind !== 'image') {
    return null;
  }

  return `data:${content.mimeType};base64,${content.base64Data}`;
});

const activeEditorColumn = computed(() => activeEditor.value
  ? visibleColumns.value.find((column) => column.name === activeEditor.value?.columnName) ?? null
  : null);

const activeEditorIsBinary = computed(() => activeEditorColumn.value ? isBinaryColumn(activeEditorColumn.value.name, activeEditorColumn.value.type) : false);
const activeEditorIsBoolean = computed(() => activeEditorColumn.value ? isBooleanColumn(activeEditorColumn.value) : false);
const activeEditorIsMultiline = computed(() => activeEditorColumn.value ? isMultilineTextColumn(activeEditorColumn.value) : false);
const activeBooleanOptions = [
  { label: 'True', value: 'true' },
  { label: 'False', value: 'false' },
];

/** 编辑浮层样式：与 td 单元格精确对齐 */
const activeEditorPopupStyle = computed(() => {
  if (!activeEditorAnchor.value) {
    return undefined;
  }

  const { top, left, width: anchorWidth, height: anchorHeight, verticalOffset, horizontalOffset, fontWeight } = activeEditorAnchor.value;
  const width = Math.max(120, anchorWidth);
  /** 多行 textarea line-height (1.45 * 10.5 = 15.225px) 与 span line-height (20px) 的差值补偿 */
  const multilineLineHeightCompensation = (20 - 10.5 * 1.45) / 2;
  const textareaVOffset = verticalOffset + multilineLineHeightCompensation;
  return {
    top: `${top}px`,
    left: `${left}px`,
    width: `${width}px`,
    height: activeEditorIsMultiline.value ? undefined : `${anchorHeight}px`,
    minHeight: activeEditorIsMultiline.value ? `${Math.max(24, anchorHeight) + 4}px` : undefined,
    '--grid-editor-inner-height': `${anchorHeight}px`,
    '--grid-editor-v-offset': `${verticalOffset}px`,
    '--grid-editor-v-offset-multiline': `${textareaVOffset}px`,
    '--grid-editor-h-offset': `${horizontalOffset}px`,
    '--grid-editor-font-weight': fontWeight,
  };
});

function focusActiveEditorSoon() {
  requestAnimationFrame(() => {
    requestAnimationFrame(() => {
      if (activeEditorIsMultiline.value) {
        const textarea = activeEditorTextareaRef.value;
        if (!textarea) {
          return;
        }

        textarea.focus();
        textarea.setSelectionRange(textarea.value.length, textarea.value.length);
        return;
      }

      const input = activeEditorInputRef.value;
      if (!input) {
        return;
      }

      input.focus();
      input.setSelectionRange(input.value.length, input.value.length);
    });
  });
}

watch(activeEditor, async (value) => {
  if (!value) {
    return;
  }

  await nextTick();
  focusActiveEditorSoon();
}, { flush: 'post' });

function draftRowValue(columnName: string) {
  return draftRowValues.value[columnName] ?? null;
}

function draftRowDisplayValue(column: VisibleColumn) {
  const state = draftRowValue(column.name);
  if (!state) {
    return column.isAutoGenerated ? '自动生成' : '双击输入';
  }

  if (state.setNull) {
    return 'NULL';
  }

  if (state.valueKind === 'binary') {
    if (!state.base64Value) {
      return '空二进制';
    }

    return `[binary ${Math.ceil(state.base64Value.length * 0.75)} B]`;
  }

  if (state.textValue.includes('\n')) {
    const [firstLine, ...rest] = state.textValue.replace(/\r\n/g, '\n').split('\n');
    return `${firstLine || ' '} ↵${rest.length > 0 ? ` +${rest.length}` : ''}`;
  }

  return state.textValue || '空字符串';
}

function isActiveEditor(rowKey: string, columnName: string, draft = false) {
  return !!activeEditor.value
    && activeEditor.value.rowKey === rowKey
    && activeEditor.value.columnName === columnName
    && activeEditor.value.draft === draft;
}

function resetActiveEditor() {
  activeEditor.value = null;
  activeEditorAnchor.value = null;
  activeEditorInitialPayload.value = null;
  activeEditorDirty.value = false;
  activeEditorTextValue.value = '';
  activeEditorSetNull.value = false;
  activeEditorBinaryBase64.value = null;
  activeEditorBinaryFileName.value = '';
  editorMouseDownActive.value = false;
}

function toggleEditMode() {
  editMode.value = !editMode.value;
  resetActiveEditor();
  selectedRowKeys.value = [];
  stopRowDragSelection();
  if (!editMode.value) {
    draftRowValues.value = {};
  }
}

function clearSelectedRows() {
  selectedRowKeys.value = [];
  stopRowDragSelection();
}

function stopRowDragSelection() {
  dragSelectionMode.value = null;
  dragSelectionLastRowKey.value = null;
}

function isRowSelected(rowKey: string) {
  return selectedRowKeys.value.includes(rowKey);
}

function setRowSelected(rowKey: string, selected: boolean) {
  if (selected) {
    if (!selectedRowKeys.value.includes(rowKey)) {
      selectedRowKeys.value = [...selectedRowKeys.value, rowKey];
    }
    return;
  }

  selectedRowKeys.value = selectedRowKeys.value.filter((key) => key !== rowKey);
}

function handleGridRowPointerDown(rowKey: string, event: MouseEvent) {
  if (!editMode.value || event.button !== 0 || (!event.ctrlKey && !event.metaKey)) {
    return;
  }

  event.preventDefault();
  event.stopPropagation();
  closeContextMenu();
  const shouldSelect = !isRowSelected(rowKey);
  setRowSelected(rowKey, shouldSelect);
  dragSelectionMode.value = shouldSelect ? 'select' : 'unselect';
  dragSelectionLastRowKey.value = rowKey;
}

function handleGridRowClick(event: MouseEvent) {
  if (!editMode.value || event.ctrlKey || event.metaKey) {
    return;
  }

  if (selectedRowKeys.value.length > 0) {
    clearSelectedRows();
  }
}

function applyRowSelectionRange(startRowKey: string, endRowKey: string, selected: boolean) {
  const rowKeys = rows.value.map((row) => row.rowKey);
  const startIndex = rowKeys.indexOf(startRowKey);
  const endIndex = rowKeys.indexOf(endRowKey);
  if (startIndex < 0 || endIndex < 0) {
    return;
  }

  const [rangeStart, rangeEnd] = startIndex <= endIndex ? [startIndex, endIndex] : [endIndex, startIndex];
  rowKeys.slice(rangeStart, rangeEnd + 1).forEach((rowKey) => {
    setRowSelected(rowKey, selected);
  });
}

function handleGridDragPointerMove(event: MouseEvent) {
  if (!editMode.value || !dragSelectionMode.value || event.buttons !== 1) {
    stopRowDragSelection();
    return;
  }

  const hoveredRowElement = document.elementFromPoint(event.clientX, event.clientY)?.closest('tr[data-row-key]');
  const hoveredRowKey = hoveredRowElement instanceof HTMLElement ? hoveredRowElement.dataset.rowKey ?? null : null;
  if (!hoveredRowKey) {
    return;
  }

  const startRowKey = dragSelectionLastRowKey.value ?? hoveredRowKey;
  if (hoveredRowKey === startRowKey) {
    return;
  }

  applyRowSelectionRange(startRowKey, hoveredRowKey, dragSelectionMode.value === 'select');
  dragSelectionLastRowKey.value = hoveredRowKey;
}

function markActiveEditorDirty() {
  activeEditorDirty.value = true;
}

function buildInitialPayload(column: VisibleColumn, value: CellValue | undefined, draft: boolean): EditorPayload {
  if (draft) {
    const draftState = draftRowValue(column.name);
    return {
      valueKind: draftState?.valueKind ?? 'text',
      textValue: draftState?.textValue ?? '',
      base64Value: draftState?.base64Value ?? null,
      setNull: draftState?.setNull ?? false,
    };
  }

  if (value === null || value === undefined) {
    return {
      valueKind: 'null',
      textValue: null,
      base64Value: null,
      setNull: true,
    };
  }

  if (isBinaryColumn(column.name, column.type)) {
    return {
      valueKind: 'binary',
      textValue: null,
      base64Value: typeof value === 'string' ? value : null,
      setNull: false,
    };
  }

  return {
    valueKind: 'text',
    textValue: String(value),
    base64Value: null,
    setNull: false,
  };
}

function payloadEquals(left: EditorPayload | null, right: EditorPayload) {
  if (!left) {
    return false;
  }

  return left.valueKind === right.valueKind
    && left.textValue === right.textValue
    && left.base64Value === right.base64Value
    && left.setNull === right.setNull;
}

function beginInlineEdit(rowKey: string, column: VisibleColumn, value: CellValue | undefined, draft = false, anchorTarget?: EventTarget | null) {
  if ((!draft && !isEditableColumn(column)) || (draft && !isWritableOnInsert(column))) {
    return;
  }

  closeContextMenu();
  /** 锚定到 td 单元格边界，通过 CSS 变量补偿 vertical-align:middle 和 padding 子像素偏移 */
  const anchorTd = anchorTarget instanceof HTMLElement ? anchorTarget.closest('td') as HTMLElement | null : null;
  if (anchorTd) {
    const tdRect = anchorTd.getBoundingClientRect();
    const anchorSpan = anchorTd.querySelector('.grid-cell-text, .grid-primary-value, .grid-link-button, .grid-draft-cell-text') as HTMLElement | null;
    const spanRect = anchorSpan?.getBoundingClientRect();
    const spanWeight = anchorSpan ? getComputedStyle(anchorSpan).fontWeight : '400';
    const verticalOffset = spanRect ? spanRect.top - tdRect.top : 0;
    const horizontalOffset = spanRect ? spanRect.left - tdRect.left : 3;
    activeEditorAnchor.value = {
      top: tdRect.top,
      left: tdRect.left,
      width: tdRect.width,
      height: tdRect.height,
      verticalOffset,
      horizontalOffset,
      fontWeight: spanWeight,
    };
  }
  const draftValue = draft ? draftRowValue(column.name) : null;
  activeEditor.value = {
    rowKey,
    columnName: column.name,
    draft,
    saving: false,
  };
  suppressWindowClickUntil.value = Date.now() + 220;
  activeEditorInitialPayload.value = buildInitialPayload(column, value, draft);
  activeEditorDirty.value = false;

  if (draftValue) {
    activeEditorTextValue.value = draftValue.textValue;
    activeEditorSetNull.value = draftValue.setNull;
    activeEditorBinaryBase64.value = draftValue.base64Value;
    activeEditorBinaryFileName.value = draftValue.fileName;
    return;
  }

  if (draft) {
    activeEditorSetNull.value = false;
    activeEditorBinaryBase64.value = null;
    activeEditorBinaryFileName.value = '';
    activeEditorTextValue.value = '';
    return;
  }

  activeEditorSetNull.value = false;
  activeEditorBinaryBase64.value = typeof value === 'string' && isBinaryColumn(column.name, column.type) ? value : null;
  activeEditorBinaryFileName.value = '';
  if (activeEditorIsBoolean.value) {
    activeEditorTextValue.value = value === null || value === undefined
      ? ''
      : (String(value).toLowerCase() === 'true' ? 'true' : 'false');
    return;
  }

  activeEditorTextValue.value = value === null || value === undefined ? '' : String(value);
}

function cancelInlineEdit() {
  if (activeEditor.value?.saving) {
    return;
  }

  resetActiveEditor();
}

function currentBinarySize() {
  const base64Value = activeEditorBinaryBase64.value;
  if (!base64Value) {
    return 0;
  }

  const padding = base64Value.endsWith('==') ? 2 : base64Value.endsWith('=') ? 1 : 0;
  return Math.max(0, Math.floor(base64Value.length * 0.75) - padding);
}

function chooseBinaryFile() {
  binaryFileInputRef.value?.click();
}

async function handleBinaryFileSelected(event: Event) {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  if (!file) {
    return;
  }

  activeEditorBinaryFileName.value = file.name;
  const buffer = await file.arrayBuffer();
  const bytes = new Uint8Array(buffer);
  let binary = '';
  bytes.forEach((value) => {
    binary += String.fromCharCode(value);
  });
  activeEditorBinaryBase64.value = btoa(binary);
  activeEditorSetNull.value = false;
  markActiveEditorDirty();
  input.value = '';
}

function clearInlineBinary() {
  activeEditorBinaryBase64.value = null;
  activeEditorBinaryFileName.value = '';
  activeEditorSetNull.value = !!activeEditorColumn.value?.isNullable;
  markActiveEditorDirty();
}

function buildActiveEditorPayload(column: VisibleColumn) {
  if (activeEditorSetNull.value) {
    return {
      valueKind: 'null' as const,
      textValue: null,
      base64Value: null,
      setNull: true,
    };
  }

  if (isBinaryColumn(column.name, column.type)) {
    return {
      valueKind: 'binary' as const,
      textValue: null,
      base64Value: activeEditorBinaryBase64.value,
      setNull: false,
    };
  }

  return {
    valueKind: 'text' as const,
    textValue: activeEditorTextValue.value,
    base64Value: null,
    setNull: false,
  };
}

async function saveInlineEdit() {
  if (!activeEditor.value || !activeEditorColumn.value) {
    return;
  }

  const payload = buildActiveEditorPayload(activeEditorColumn.value);
  if (!activeEditor.value.draft && (!activeEditorDirty.value || payloadEquals(activeEditorInitialPayload.value, payload))) {
    resetActiveEditor();
    return;
  }

  if (activeEditor.value.draft) {
    if (!activeEditorDirty.value && payloadEquals(activeEditorInitialPayload.value, payload)) {
      resetActiveEditor();
      return;
    }

    draftRowValues.value = {
      ...draftRowValues.value,
      [activeEditorColumn.value.name]: {
        valueKind: payload.valueKind,
        textValue: payload.textValue ?? '',
        base64Value: payload.base64Value ?? null,
        setNull: payload.setNull,
        fileName: activeEditorBinaryFileName.value,
      },
    };
    resetActiveEditor();
    return;
  }

  activeEditor.value = {
    ...activeEditor.value,
    saving: true,
  };

  try {
    await store.updateTableCell({
      tableKey: props.tableKey,
      rowKey: activeEditor.value.rowKey,
      columnName: activeEditorColumn.value.name,
      valueKind: payload.valueKind,
      textValue: payload.textValue,
      base64Value: payload.base64Value,
      setNull: payload.setNull,
    });

    const nextCache = { ...previewCache.value };
    delete nextCache[previewCacheKey(activeEditor.value.rowKey, activeEditorColumn.value.name)];
    previewCache.value = nextCache;
    resetActiveEditor();
  }
  catch {
    activeEditor.value = {
      ...activeEditor.value,
      saving: false,
    };
  }
}

async function insertDraftRow() {
  const values = Object.entries(draftRowValues.value)
    .map(([columnName, state]) => ({
      columnName,
      valueKind: state.valueKind,
      textValue: state.valueKind === 'text' ? state.textValue : null,
      base64Value: state.valueKind === 'binary' ? state.base64Value : null,
      setNull: state.setNull,
    })) satisfies TableRowWriteValueRequest[];

  if (values.length === 0) {
    return;
  }

  try {
    await store.insertTableRow({
      tableKey: props.tableKey,
      values,
    });
    draftRowValues.value = {};
    resetActiveEditor();
  }
  catch {
    // 错误提示由 store 统一展示，这里不重复处理。
  }
}

function clearDraftRow() {
  draftRowValues.value = {};
  if (activeEditor.value?.draft) {
    resetActiveEditor();
  }
}

function handleEditableCellDoubleClick(event: MouseEvent, rowKey: string, column: VisibleColumn, value: CellValue | undefined, draft = false) {
  if (!editMode.value) {
    return;
  }

  if (isBinaryColumn(column.name, column.type)) {
    return;
  }

  event.preventDefault();
  event.stopPropagation();
  beginInlineEdit(rowKey, column, value, draft, event.currentTarget);
}

function handlePrimaryCellDoubleClick(event: MouseEvent, rowKey: string, column: VisibleColumn, value: CellValue | undefined) {
  if (editMode.value) {
    handleEditableCellDoubleClick(event, rowKey, column, value);
    return;
  }

  event.preventDefault();
  event.stopPropagation();
  handleRowOpen(rowKey);
}

function handleForeignKeyDoubleClick(event: MouseEvent, rowKey: string, column: VisibleColumn, value: CellValue | undefined) {
  if (editMode.value) {
    handleEditableCellDoubleClick(event, rowKey, column, value);
    return;
  }

  event.preventDefault();
  event.stopPropagation();
  handleForeignKeyClick(rowKey, column.name);
}

function handleEditorKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape') {
    event.preventDefault();
    cancelInlineEdit();
    return;
  }

  if (event.key === 'Enter' && !event.shiftKey && (!activeEditorIsMultiline.value || event.ctrlKey)) {
    event.preventDefault();
    void saveInlineEdit();
  }
}

function columnStyle(columnName: string) {
  const width = columnWidths.value[columnName];
  if (!width) {
    return undefined;
  }

  return {
    width: `${width}px`,
    minWidth: `${width}px`,
    maxWidth: `${width}px`,
  };
}

function beginResize(event: MouseEvent, columnName: string) {
  event.preventDefault();
  event.stopPropagation();

  const header = (event.currentTarget as HTMLElement).closest('th');
  const startWidth = header?.getBoundingClientRect().width ?? columnWidths.value[columnName] ?? 120;
  const startX = event.clientX;

  const onMouseMove = (moveEvent: MouseEvent) => {
    const nextWidth = Math.max(56, startWidth + moveEvent.clientX - startX);
    columnWidths.value = {
      ...columnWidths.value,
      [columnName]: nextWidth,
    };
  };

  const onMouseUp = () => {
    window.removeEventListener('mousemove', onMouseMove);
    window.removeEventListener('mouseup', onMouseUp);
  };

  window.addEventListener('mousemove', onMouseMove);
  window.addEventListener('mouseup', onMouseUp);
}

function resetColumnWidth(columnName: string) {
  const next = { ...columnWidths.value };
  delete next[columnName];
  columnWidths.value = next;
}

function previewCacheKey(rowKey: string, columnName: string) {
  return `${rowKey}::${columnName}`;
}

async function ensureCellContent(rowKey: string, columnName: string) {
  const key = previewCacheKey(rowKey, columnName);
  if (previewCache.value[key]) {
    return previewCache.value[key];
  }

  const content = await store.fetchCellContent(props.tableKey, rowKey, columnName);
  previewCache.value = {
    ...previewCache.value,
    [key]: content,
  };
  return content;
}

async function openBinaryPreview(rowKey: string, columnName: string) {
  try {
    const content = await ensureCellContent(rowKey, columnName);
    if (content.kind === 'empty' || content.sizeBytes === 0) {
      return;
    }

    previewState.value = {
      show: true,
      loading: false,
      title: `${tableMeta.value?.name ?? props.tableKey}.${columnName}`,
      content,
      error: null,
    };
  }
  catch (error) {
    previewState.value = {
      show: true,
      loading: false,
      title: `${tableMeta.value?.name ?? props.tableKey}.${columnName}`,
      content: null,
      error: error instanceof Error ? error.message : '二进制内容读取失败',
    };
  }
}

function closeContextMenu() {
  contextMenu.value = null;
}

/** 判断右键菜单是否有可用操作 */
function hasCellContextActions(column: VisibleColumn, draft: boolean) {
  if (isReadOnlyConnection.value) {
    return isBinaryColumn(column.name, column.type);
  }

  // 非草稿行始终有「删除行」操作可用
  if (!draft) {
    return true;
  }

  const editable = isWritableOnInsert(column);
  return editable
    || (column.isNullable && editable)
    || isBinaryColumn(column.name, column.type);
}

/** 打开单元格右键菜单。 */
function openCellContextMenu(event: MouseEvent, rowKey: string, column: VisibleColumn, value: CellValue | undefined, draft = false) {
  if (!hasCellContextActions(column, draft)) {
    closeContextMenu();
    return;
  }

  closeContextMenu();
  if (editMode.value && !draft && selectedRowKeys.value.length > 0 && !selectedRowKeys.value.includes(rowKey)) {
    selectedRowKeys.value = [rowKey];
  }
  contextMenu.value = {
    x: event.clientX,
    y: event.clientY,
    rowKey,
    column,
    value,
    draft,
  };
}

function handleContextMenuShow(value: boolean) {
  if (!value) {
    closeContextMenu();
  }
}

function handleContextMenuSelect(key: string | number) {
  switch (key) {
    case 'replace-binary':
      pickBinaryReplacement();
      return;
    case 'preview-binary':
      if (contextMenu.value) {
        void openBinaryPreview(contextMenu.value.rowKey, contextMenu.value.column.name);
        closeContextMenu();
      }
      return;
    case 'save-binary':
      if (contextMenu.value) {
        void saveCellContent(contextMenu.value.rowKey, contextMenu.value.column.name);
      }
      return;
    case 'edit-cell':
      if (contextMenu.value) {
        openCellEditor(contextMenu.value.rowKey, contextMenu.value.column, contextMenu.value.value);
      }
      return;
    case 'enter-edit-mode':
      editMode.value = true;
      closeContextMenu();
      return;
    case 'set-null':
      void setContextMenuValueNull();
      return;
    case 'delete-row':
      confirmDeleteRow();
      return;
    default:
      return;
  }
}

function openCellEditor(rowKey: string, column: VisibleColumn, value: CellValue | undefined) {
  if (!isEditableColumn(column)) {
    return;
  }

  closeContextMenu();
  dialogEditorState.value = {
    show: true,
    rowKey,
    column,
    foreignKey: column.fk ?? null,
    value,
    saving: false,
  };
}

function handleCellDoubleClick(event: MouseEvent, rowKey: string, column: VisibleColumn, value: CellValue | undefined) {
  if (editMode.value) {
    handleEditableCellDoubleClick(event, rowKey, column, value);
    return;
  }

  if (isBinaryColumn(column.name, column.type)) {
    event.preventDefault();
    event.stopPropagation();
    void openBinaryPreview(rowKey, column.name);
    return;
  }
}

function handleCellMouseDown(event: MouseEvent, suppressSelection: boolean) {
  if (suppressSelection && event.detail > 1) {
    event.preventDefault();
  }
}

function handleBinaryCellClick(event: MouseEvent, rowKey: string, columnName: string, columnType: string | undefined) {
  if (!isBinaryColumn(columnName, columnType) || editMode.value) {
    return;
  }

  event.stopPropagation();
  void openBinaryPreview(rowKey, columnName);
}

function base64ToBytes(base64: string) {
  const raw = atob(base64);
  const bytes = new Uint8Array(raw.length);
  for (let index = 0; index < raw.length; index += 1) {
    bytes[index] = raw.charCodeAt(index);
  }
  return bytes;
}

async function saveCellContent(rowKey: string, columnName: string) {
  closeContextMenu();
  const content = await ensureCellContent(rowKey, columnName);
  if (!content.base64Data) {
    return;
  }

  const bytes = base64ToBytes(content.base64Data);
  const blob = new Blob([bytes], { type: content.mimeType || 'application/octet-stream' });
  const windowWithPicker = window as Window & {
    showSaveFilePicker?: (options?: unknown) => Promise<{
      createWritable: () => Promise<{ write: (data: Blob) => Promise<void>; close: () => Promise<void> }>
    }>
  };

  if (windowWithPicker.showSaveFilePicker) {
    const extension = content.suggestedFileName.includes('.') ? `.${content.suggestedFileName.split('.').pop()}` : '.bin';
    const handle = await windowWithPicker.showSaveFilePicker({
      suggestedName: content.suggestedFileName,
      types: [{
        description: content.mimeType,
        accept: {
          [content.mimeType || 'application/octet-stream']: [extension],
        },
      }],
    });
    const writable = await handle.createWritable();
    await writable.write(blob);
    await writable.close();
    return;
  }

  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = content.suggestedFileName;
  anchor.click();
  URL.revokeObjectURL(url);
}

function pickBinaryReplacement() {
  if (!contextMenu.value || !isBinaryColumn(contextMenu.value.column.name, contextMenu.value.column.type)) {
    return;
  }

  pendingBinaryReplaceTarget.value = {
    rowKey: contextMenu.value.rowKey,
    column: contextMenu.value.column,
    draft: contextMenu.value.draft,
  };
  closeContextMenu();
  contextBinaryFileInputRef.value?.click();
}

async function handleContextBinaryFileSelected(event: Event) {
  const target = pendingBinaryReplaceTarget.value;
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  if (!target || !file) {
    pendingBinaryReplaceTarget.value = null;
    input.value = '';
    return;
  }

  const buffer = await file.arrayBuffer();
  const bytes = new Uint8Array(buffer);
  let binary = '';
  bytes.forEach((value) => {
    binary += String.fromCharCode(value);
  });
  const base64Value = btoa(binary);

  if (target.draft) {
    draftRowValues.value = {
      ...draftRowValues.value,
      [target.column.name]: {
        valueKind: 'binary',
        textValue: '',
        base64Value,
        setNull: false,
        fileName: file.name,
      },
    };
  }
  else {
    try {
      await store.updateTableCell({
        tableKey: props.tableKey,
        rowKey: target.rowKey,
        columnName: target.column.name,
        valueKind: 'binary',
        textValue: null,
        base64Value,
        setNull: false,
      });

      const nextCache = { ...previewCache.value };
      delete nextCache[previewCacheKey(target.rowKey, target.column.name)];
      previewCache.value = nextCache;
    }
    catch {
      // 错误提示已经由 store 展示，这里不重复处理。
    }
  }

  pendingBinaryReplaceTarget.value = null;
  input.value = '';
}

async function handleDialogEditorSave(payload: { valueKind: 'text' | 'binary' | 'null'; textValue?: string | null; base64Value?: string | null; setNull: boolean }) {
  const rowKey = dialogEditorState.value.rowKey;
  const column = dialogEditorState.value.column;
  if (!rowKey || !column) {
    return;
  }

  dialogEditorState.value = {
    ...dialogEditorState.value,
    saving: true,
  };

  try {
    await store.updateTableCell({
      tableKey: props.tableKey,
      rowKey,
      columnName: column.name,
      valueKind: payload.valueKind,
      textValue: payload.textValue ?? null,
      base64Value: payload.base64Value ?? null,
      setNull: payload.setNull,
    });

    const nextCache = { ...previewCache.value };
    delete nextCache[previewCacheKey(rowKey, column.name)];
    previewCache.value = nextCache;
    dialogEditorState.value = {
      show: false,
      rowKey: null,
      column: null,
      foreignKey: null,
      value: undefined,
      saving: false,
    };
  }
  catch {
    dialogEditorState.value = {
      ...dialogEditorState.value,
      saving: false,
    };
  }
}

async function setContextMenuValueNull() {
  const menu = contextMenu.value;
  if (!menu || !menu.column.isNullable) {
    return;
  }

  closeContextMenu();

  if (menu.draft) {
    draftRowValues.value = {
      ...draftRowValues.value,
      [menu.column.name]: {
        valueKind: 'null',
        textValue: '',
        base64Value: null,
        setNull: true,
        fileName: '',
      },
    };
    if (activeEditor.value?.draft && activeEditor.value.columnName === menu.column.name) {
      resetActiveEditor();
    }
    return;
  }

  try {
    await store.updateTableCell({
      tableKey: props.tableKey,
      rowKey: menu.rowKey,
      columnName: menu.column.name,
      valueKind: 'null',
      textValue: null,
      base64Value: null,
      setNull: true,
    });

    const nextCache = { ...previewCache.value };
    delete nextCache[previewCacheKey(menu.rowKey, menu.column.name)];
    previewCache.value = nextCache;
  }
  catch {
    // 错误提示已经由 store 展示，这里不重复处理。
  }
}

/** 右键菜单「删除行」：弹出确认对话框后删除 */
function confirmDeleteRow() {
  const menu = contextMenu.value;
  if (!menu || menu.draft) {
    return;
  }

  const rowKeysToDelete = editMode.value && selectedRowKeys.value.length > 1 && selectedRowKeys.value.includes(menu.rowKey)
    ? [...selectedRowKeys.value]
    : [menu.rowKey];

  closeContextMenu();

  dialog.warning({
    title: '确认删除',
    content: rowKeysToDelete.length > 1 ? `确定要删除所选 ${rowKeysToDelete.length} 行数据吗？此操作不可撤销。` : '确定要删除这一行数据吗？此操作不可撤销。',
    positiveText: '删除',
    negativeText: '取消',
    onPositiveClick: async () => {
      try {
        await store.deleteTableRows(props.tableKey, rowKeysToDelete);
        clearSelectedRows();
      }
      catch {
        // 错误提示已经由 store 展示，这里不重复处理。
      }
    },
  });
}

function handleWindowClick() {
  closeContextMenu();
  if (Date.now() < suppressWindowClickUntil.value) {
    return;
  }

  if (editorMouseDownActive.value) {
    editorMouseDownActive.value = false;
    return;
  }

  if (activeEditor.value) {
    void saveInlineEdit();
  }
}

function handleWindowContextMenu() {
  closeContextMenu();
}

function handleWindowBlur() {
  closeContextMenu();
  cancelInlineEdit();
}

onMounted(() => {
  window.addEventListener('click', handleWindowClick);
  window.addEventListener('contextmenu', handleWindowContextMenu, true);
  window.addEventListener('blur', handleWindowBlur);
});

onBeforeUnmount(() => {
  window.removeEventListener('click', handleWindowClick);
  window.removeEventListener('contextmenu', handleWindowContextMenu, true);
  window.removeEventListener('blur', handleWindowBlur);
});

watch(() => props.tableKey, () => {
  editMode.value = false;
  selectedRowKeys.value = [];
  stopRowDragSelection();
  resetActiveEditor();
  closeContextMenu();
  dialogEditorState.value = {
    show: false,
    rowKey: null,
    column: null,
    foreignKey: null,
    value: undefined,
    saving: false,
  };
});

watch(() => rows.value.map((row) => row.rowKey).join('|'), (rowKeySignature) => {
  const rowKeySet = new Set(rowKeySignature ? rowKeySignature.split('|') : []);
  selectedRowKeys.value = selectedRowKeys.value.filter((rowKey) => rowKeySet.has(rowKey));
});

watch(dragSelectionMode, (mode, _, onCleanup) => {
  if (!mode) {
    return;
  }

  const handlePointerMove = (event: MouseEvent) => {
    handleGridDragPointerMove(event);
  };

  const handlePointerUp = () => {
    stopRowDragSelection();
  };

  window.addEventListener('mousemove', handlePointerMove, true);
  window.addEventListener('mouseup', handlePointerUp, true);
  onCleanup(() => {
    window.removeEventListener('mousemove', handlePointerMove, true);
    window.removeEventListener('mouseup', handlePointerUp, true);
  });
});
</script>

<template>
  <NCard embedded class="workspace-panel grid-panel" :class="{ 'grid-panel-editing': editMode }">
    <template #header>
      <div class="panel-header grid-panel-header-compact">
        <div class="grid-panel-title-row">
          <h3>{{ tableMeta?.schema ? `${tableMeta.schema}.${tableMeta.name}` : tableMeta?.name }}</h3>
          <span class="panel-meta">{{ loadedRowCount }} / {{ totalRowCount }} rows</span>
        </div>
        <div class="grid-panel-header-meta">
          <NButton size="small" :tertiary="!filterOpen" :type="filterOpen || searchActive ? 'primary' : 'default'" @click="filterOpen = !filterOpen">
            筛选
          </NButton>
          <NButton size="small" :tertiary="!editMode" :type="editMode ? 'success' : 'default'" :disabled="!canEnableEditMode" @click="toggleEditMode">
            编辑模式
          </NButton>
        </div>
      </div>
      <Transition name="grid-filter">
        <div v-if="filterOpen" class="grid-filter-bar">
          <NInput
            v-model:value="store.globalSearch"
            size="small"
            class="grid-filter-input"
            placeholder="搜索当前表（服务端）"
            @keyup.enter="store.applyActiveTableSearch()"
          />
          <NSelect
            v-model:value="store.searchColumns"
            class="grid-filter-select"
            size="small"
            multiple
            filterable
            max-tag-count="responsive"
            :options="searchColumnOptions"
            placeholder="指定列（留空=全部）"
          />
          <NButton size="small" tertiary type="primary" @click="store.applyActiveTableSearch()">搜索</NButton>
          <NButton v-if="searchActive" size="small" tertiary @click="store.clearActiveTableSearch()">清除</NButton>
        </div>
      </Transition>
    </template>

    <div class="grid-panel-body">
      <NAlert v-if="store.getTableError(props.tableKey)" type="warning" :show-icon="false" class="panel-inline-alert">
        {{ store.getTableError(props.tableKey) }}
      </NAlert>
      <NAlert v-else-if="store.getTableSearchError(props.tableKey)" type="warning" :show-icon="false" class="panel-inline-alert">
        {{ store.getTableSearchError(props.tableKey) }}
      </NAlert>

      <div v-if="editMode && selectedRowCount > 0" class="grid-selection-hint is-active">
        <NTag size="small" :bordered="false" type="info">已选 {{ selectedRowCount }} 行</NTag>
        <NButton size="tiny" tertiary @click="clearSelectedRows">取消选择</NButton>
      </div>

      <NSpin :show="store.isTableLoading(props.tableKey) || store.isSearchLoading(props.tableKey)" class="grid-panel-spin">
        <div v-if="table" class="grid-table-wrap">
          <template v-if="true">
            <table class="grid-table">
              <thead>
                <tr>
                  <th
                    v-for="column in visibleColumns"
                    :key="column.name"
                    :style="columnStyle(column.name)"
                    :class="{ 'grid-header-sortable': store.isSortableColumn(column.name, column.type) }"
                    @click="handleHeaderSort(column.name, column.type)"
                  >
                    <span class="grid-header-text">
                      <span class="grid-header-name-row">
                        <span>{{ column.name }}</span>
                        <span v-if="sortIndicator(column.name)" class="grid-sort-indicator">{{ sortIndicator(column.name) }}</span>
                      </span>
                      <span class="grid-header-type">{{ displayColumnType(column) }}</span>
                    </span>
                    <button
                      type="button"
                      class="grid-resize-handle"
                      :title="`拖拽调整 ${column.name} 列宽，双击恢复自动宽度`"
                      @click.stop
                      @mousedown="beginResize($event, column.name)"
                      @dblclick.stop="resetColumnWidth(column.name)"
                    />
                  </th>
                  <th v-if="editMode" class="grid-action-column">操作</th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="row in rows"
                  :key="row.rowKey"
                  :data-row-key="row.rowKey"
                  class="grid-row"
                  :class="{ 'grid-row-selected': isRowSelected(row.rowKey) }"
                  @mousedown.capture="handleGridRowPointerDown(row.rowKey, $event)"
                  @click="handleGridRowClick($event)"
                >
                  <td
                    v-for="(column, columnIndex) in visibleColumns"
                    :key="column.name"
                    :style="columnStyle(column.name)"
                    :class="{ 'grid-binary-value-cell': isBinaryColumn(column.name, column.type) }"
                    @mousedown="handleCellMouseDown($event, editMode || !!column.fk || isBinaryColumn(column.name, column.type) || isRowOpenCell(column.name, column.type, column.isPrimaryKey, columnIndex))"
                    @click="handleBinaryCellClick($event, row.rowKey, column.name, column.type)"
                    @dblclick="handleCellDoubleClick($event, row.rowKey, column, row[column.name] as CellValue)"
                    @contextmenu.prevent.stop="openCellContextMenu($event, row.rowKey, column, row[column.name] as CellValue)"
                  >
                    <button
                      v-if="column.fk"
                      type="button"
                      class="grid-link-button"
                      :title="displayCellValue(row, column)"
                      @click="handleForeignKeyClick(row.rowKey, column.name)"
                      @dblclick.stop.prevent="handleForeignKeyDoubleClick($event, row.rowKey, column, row[column.name] as CellValue)"
                    >
                      {{ displayCellValue(row, column) }}
                    </button>
                    <span
                      v-else-if="column.isPrimaryKey"
                      class="grid-primary-value"
                      :class="{ 'grid-editing-cell-value': isActiveEditor(row.rowKey, column.name), 'grid-null-value': isNullCell(row, column.name) }"
                      :title="displayCellValue(row, column)"
                      @dblclick="handlePrimaryCellDoubleClick($event, row.rowKey, column, row[column.name] as CellValue)"
                    >
                      {{ displayCellValue(row, column) }}
                    </span>
                    <span
                      v-else-if="isRowOpenCell(column.name, column.type, column.isPrimaryKey, columnIndex)"
                      class="grid-cell-text grid-detail-open-value"
                      :class="{ 'grid-editing-cell-value': isActiveEditor(row.rowKey, column.name), 'grid-null-value': isNullCell(row, column.name) }"
                      :title="displayCellValue(row, column)"
                      @dblclick="handlePrimaryCellDoubleClick($event, row.rowKey, column, row[column.name] as CellValue)"
                    >
                      {{ displayCellValue(row, column) }}
                    </span>
                    <span
                      v-else
                      class="grid-cell-text"
                      :class="{ 'grid-editing-cell-value': isActiveEditor(row.rowKey, column.name), 'grid-null-value': isNullCell(row, column.name), 'grid-edited-value': activeEditorInitialPayload && isActiveEditor(row.rowKey, column.name) && activeEditorDirty }"
                      :title="displayCellValue(row, column)"
                    >
                      {{ displayCellValue(row, column) }}
                    </span>
                  </td>
                  <td v-if="editMode" class="grid-row-action-cell">
                    <span class="grid-row-action-hint">双击单元格编辑</span>
                  </td>
                </tr>

                <tr v-if="!hasRows && !editMode" class="grid-row-empty-state">
                  <td :colspan="visibleColumns.length" class="grid-empty-cell">当前表没有数据</td>
                </tr>

                <tr v-if="editMode" class="grid-row grid-row-draft">
                  <td
                    v-for="column in visibleColumns"
                    :key="`draft:${column.name}`"
                    :style="columnStyle(column.name)"
                    :class="{ 'grid-binary-value-cell': isBinaryColumn(column.name, column.type), 'grid-draft-readonly-cell': !isWritableOnInsert(column) }"
                    @dblclick.stop.prevent="handleEditableCellDoubleClick($event, '__draft__', column, undefined, true)"
                    @contextmenu.prevent.stop="isWritableOnInsert(column) ? openCellContextMenu($event, '__draft__', column, draftRowValue(column.name)?.textValue ?? null, true) : undefined"
                  >
                    <span class="grid-cell-text grid-draft-cell-text" :class="{ 'grid-editing-cell-value': isActiveEditor('__draft__', column.name, true), 'grid-null-value': isDraftNullCell(column.name) }">
                      {{ draftRowDisplayValue(column) }}
                    </span>
                  </td>
                  <td class="grid-row-action-cell grid-row-action-cell-draft">
                    <div class="grid-draft-actions">
                      <NButton size="tiny" type="primary" :disabled="!draftRowHasValues" @click="insertDraftRow">新增</NButton>
                      <NButton size="tiny" tertiary :disabled="!draftRowHasValues" @click="clearDraftRow">清空</NButton>
                    </div>
                  </td>
                </tr>
              </tbody>
            </table>
          </template>
        </div>
        <NEmpty v-else description="表数据尚未加载完成" />
      </NSpin>

      <div v-if="table" class="grid-footer">
        <div class="grid-footer-meta">{{ searchActive ? '搜索结果' : '已加载' }} {{ loadedRowCount }} / {{ totalRowCount }} 行</div>
        <div class="grid-footer-actions">
          <NButton size="small" tertiary :disabled="!hasMoreRows || store.isTableLoading(props.tableKey) || store.isSearchLoading(props.tableKey)" @click="searchActive ? store.loadMoreSearchRows(props.tableKey) : store.loadMoreTableRows(props.tableKey)">
            再加载 {{ store.defaultPageSize }} 行
          </NButton>
          <NButton size="small" tertiary type="primary" :disabled="!hasMoreRows || store.isTableLoading(props.tableKey) || store.isSearchLoading(props.tableKey)" @click="searchActive ? store.loadAllSearchRows(props.tableKey) : store.loadAllTableRows(props.tableKey)">
            加载全部
          </NButton>
          <NButton v-if="searchActive" size="small" tertiary @click="store.clearActiveTableSearch()">
            清除筛选
          </NButton>
        </div>
      </div>
    </div>

    <ContextDropdown
      :show="!!contextMenu"
      :x="contextMenu?.x ?? 0"
      :y="contextMenu?.y ?? 0"
      :options="contextMenuOptions"
      @update:show="handleContextMenuShow"
      @select="handleContextMenuSelect"
    />

    <input ref="binaryFileInputRef" type="file" class="grid-inline-file-input" @change="handleBinaryFileSelected" />
    <input ref="contextBinaryFileInputRef" type="file" class="grid-inline-file-input" @change="handleContextBinaryFileSelected" />

    <GridCellEditorModal
      v-model:show="dialogEditorState.show"
      :table-label="tableMeta?.schema ? `${tableMeta.schema}.${tableMeta.name}` : (tableMeta?.name ?? props.tableKey)"
      :row-key="dialogEditorState.rowKey"
      :column="dialogEditorState.column"
      :foreign-key="dialogEditorState.foreignKey"
      :value="dialogEditorState.value"
      :saving="dialogEditorState.saving"
      @save="handleDialogEditorSave"
    />

    <NModal v-model:show="previewState.show" class="binary-preview-modal" preset="card" style="width: min(960px, 92vw)" :title="previewState.title">
      <NSpin :show="previewState.loading">
        <div v-if="previewState.error" class="binary-preview-empty">{{ previewState.error }}</div>
        <template v-else-if="previewState.content">
          <img v-if="previewState.content.kind === 'image' && previewImageUrl" :src="previewImageUrl" class="binary-preview-image">
          <pre v-else-if="previewState.content.kind === 'text'" class="binary-preview-text">{{ previewState.content.textContent }}</pre>
          <div v-else class="binary-preview-empty">该二进制内容不是可直接预览的图片或文本，但可以保存到本地文件。</div>

          <div class="binary-preview-meta">
            <span>{{ previewState.content.kind }}</span>
            <span>{{ previewState.content.mimeType }}</span>
            <span>{{ previewState.content.sizeBytes }} B</span>
          </div>
          <div class="binary-preview-actions">
            <NButton type="primary" @click="saveCellContent(previewState.content.rowKey, previewState.content.columnName)">保存到文件...</NButton>
          </div>
        </template>
      </NSpin>
    </NModal>

    <Teleport to="body">
      <div
        v-if="activeEditor && activeEditorColumn && activeEditorPopupStyle"
        class="grid-floating-editor"
        :class="{ 'grid-floating-editor-dirty': activeEditorDirty }"
        :style="activeEditorPopupStyle"
        @click.stop
        @mousedown.stop="editorMouseDownActive = true"
      >
        <div class="grid-inline-editor" :class="{ 'grid-inline-editor-multiline': activeEditorIsMultiline, 'grid-inline-editor-binary': activeEditorIsBinary }">
          <template v-if="activeEditorIsBinary">
            <div class="grid-inline-binary-meta">
              <span v-if="activeEditorBinaryFileName">{{ activeEditorBinaryFileName }}</span>
              <span v-else-if="activeEditorBinaryBase64">{{ activeEditor.draft ? '待写入' : '当前内容' }} {{ currentBinarySize() }} B</span>
              <span v-else>{{ activeEditor.draft ? '请选择文件' : '当前内容为空' }}</span>
            </div>
            <div class="grid-inline-binary-actions">
              <NButton size="tiny" type="primary" :disabled="activeEditor.saving" @click.stop="chooseBinaryFile">选择文件</NButton>
              <NButton size="tiny" tertiary :disabled="activeEditor.saving" @click.stop="clearInlineBinary">清空</NButton>
              <NButton size="tiny" type="primary" :loading="activeEditor.saving" @click.stop="saveInlineEdit">{{ activeEditor.draft ? '确定' : '保存' }}</NButton>
              <NButton size="tiny" tertiary :disabled="activeEditor.saving" @click.stop="cancelInlineEdit">取消</NButton>
            </div>
          </template>

          <template v-else-if="activeEditorIsBoolean">
            <NSelect
              v-model:value="activeEditorTextValue"
              :options="activeBooleanOptions"
              size="small"
              :disabled="activeEditor.saving"
              class="grid-floating-select"
              @update:value="markActiveEditorDirty"
            />
          </template>

          <template v-else-if="activeEditorIsMultiline">
            <textarea
              ref="activeEditorTextareaRef"
              v-model="activeEditorTextValue"
              class="grid-floating-textarea"
              :disabled="activeEditor.saving"
              @input="markActiveEditorDirty"
              @keydown="handleEditorKeydown"
            />
          </template>

          <template v-else>
            <div class="grid-floating-inline-row">
              <input
                ref="activeEditorInputRef"
                :value="activeEditorTextValue"
                type="text"
                class="grid-floating-input"
                :disabled="activeEditor.saving"
                @input="(e) => { activeEditorTextValue = (e.target as HTMLInputElement).value; markActiveEditorDirty(); }"
                @keydown="handleEditorKeydown"
              />
            </div>
          </template>
        </div>
      </div>
    </Teleport>
  </NCard>
</template>

<style scoped lang="scss">
// ── 数据网格面板 ──
.grid-panel {
  display: flex;
  flex-direction: column;
  min-height: 0;

  :deep(.n-card-header) {
    padding: 6px 8px 4px;
  }

  :deep(.n-card-content) {
    display: flex;
    flex-direction: column;
    min-height: 0;
    flex: 1;
    padding: 0 !important;
  }
}

.grid-panel-body {
  display: flex;
  flex-direction: column;
  min-height: 0;
  height: 100%;
  flex: 1 1 auto;
  padding: 0;
  overflow: hidden;

  > :deep(.n-empty),
  > :deep(.n-alert),
  > :deep(.n-spin) {
    margin: 0;
  }

  > :deep(.n-spin) {
    display: flex;
    flex: 1 1 auto;
    min-height: 0;
    overflow: hidden;
  }
}

.grid-selection-hint {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: nowrap;
  margin-bottom: 8px;
  min-height: 34px;
  padding: 6px 10px;
  border: 1px solid rgba(148, 163, 184, 0.14);
  border-radius: 8px;
  background: rgba(248, 250, 252, 0.72);
  overflow: hidden;
}

.grid-selection-hint.is-active {
  border-color: rgba(37, 99, 235, 0.18);
  background: rgba(239, 246, 255, 0.82);
}

.grid-selection-hint-title {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 12px;
  font-weight: 800;
  color: #334155;
}

.grid-panel-header-compact {
  gap: $gap-lg;
  min-width: 0;
}

.grid-panel-editing {
  border-color: rgba(16, 185, 129, 0.45) !important;
  box-shadow: 0 0 6px 1px rgba(16, 185, 129, 0.15);
}

.grid-panel-title-row {
  display: flex;
  align-items: center;
  gap: $gap-md;
  min-width: 0;
  flex: 1 1 auto;

  h3 {
    min-width: 0;
    font-size: $font-size-lg;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  :deep(.n-tag) {
    flex: 0 0 auto;
  }
}

.grid-panel-header-meta {
  display: flex;
  align-items: center;
  gap: $gap-md;
  flex: 0 0 auto;
}

.grid-filter-bar {
  display: flex;
  align-items: center;
  gap: $gap-md;
  padding-top: $gap-md;
}

.grid-filter-input {
  width: 200px;
}

.grid-filter-select {
  width: min(300px, 30vw);
}

.grid-filter-enter-active,
.grid-filter-leave-active {
  transition: opacity 180ms ease, max-height 180ms ease;
  overflow: hidden;
}

.grid-filter-enter-from,
.grid-filter-leave-to {
  opacity: 0;
  max-height: 0;
}

.grid-filter-enter-to,
.grid-filter-leave-from {
  max-height: 50px;
}

.grid-panel-editing-badge {
  display: inline-flex;
  align-items: center;
  min-height: 20px;
  padding: 0 $gap-lg;
  border-radius: 999px;
  background: rgba(220, 252, 231, 0.95);
  color: $color-accent-green;
  font-size: $font-size-sm;
  font-weight: 700;
  line-height: 1;
}

.grid-panel-spin {
  display: flex !important;
  flex-direction: column;
  min-height: 0;
  height: 100%;
  flex: 1 1 auto;
  overflow: hidden;

  &:deep(.n-spin-container) {
    display: flex !important;
    flex-direction: column;
    min-height: 0;
    height: 100%;
    flex: 1 1 auto;
    overflow: hidden;
  }

  :deep(.n-spin-content) {
    display: flex;
    flex-direction: column;
    min-height: 0;
    height: 100%;
    flex: 1 1 auto;
    overflow: hidden;
  }
}

// ── 数据网格容器与滚动条 ──
.grid-table-wrap {
  display: block;
  flex: 1 1 auto;
  min-width: 0;
  width: 100%;
  min-height: 0;
  height: 100%;
  padding: 0;
  overflow-x: auto;
  overflow-y: auto;
}

// ── 数据网格表格 ──
.grid-table {
  border-collapse: collapse;
  table-layout: auto;
  min-width: 100%;
  background: rgba(255, 255, 255, 0.92);

  thead th {
    position: sticky;
    top: 0;
    z-index: 2;
    padding: 0 3px;
    height: 38px;
    border-bottom: 1px solid $color-border-strong;
    border-right: 1px solid rgba(148, 163, 184, 0.14);
    background: $color-surface-light;
    color: $color-text-tertiary;
    font-size: 9.5px;
    font-weight: 700;
    letter-spacing: 0.01em;
    text-align: left;

    &:last-child {
      border-right: 0;
      overflow: hidden; // 防止 resize-handle 溢出导致水平滚动条
    }
  }

  tbody td {
    padding: 0 3px;
    height: 20px;
    border-bottom: 1px solid $color-border-row;
    border-right: 1px solid rgba(241, 245, 249, 0.8);
    color: $color-text-primary;
    font-size: 10.5px;
    line-height: 1.2;
    vertical-align: middle;

    &:last-child {
      border-right: 0;
    }
  }
}

.grid-header-sortable {
  cursor: pointer;

  &:hover {
    background: #eef6ff;
  }
}

.grid-action-column {
  min-width: 112px;
  width: 112px;
}

.grid-row-selected td {
  background: rgba(191, 219, 254, 0.78);
  box-shadow: inset 0 -1px 0 rgba(59, 130, 246, 0.18);
}

// ── 数据网格行 ──
.grid-row {
  &:hover td {
    background: rgba(239, 246, 255, 0.56);
  }

  &.grid-row-selected:hover td {
    background: rgba(191, 219, 254, 0.78);
  }
}

.grid-row-draft {
  td {
    background: rgba(240, 253, 244, 0.68);
  }

  &:hover td {
    background: rgba(220, 252, 231, 0.82);
  }
}

.grid-row-empty-state td {
  background: rgba(255, 255, 255, 0.96);
}

// ── 表头单元格 ──
.grid-header-text,
.grid-cell-text,
.grid-link-button,
.grid-primary-value {
  display: block;
  white-space: nowrap;
}

.grid-header-text {
  display: grid;
  gap: 2px;
  line-height: 1.15;
  padding-right: 8px;
}

.grid-header-name-row {
  display: inline-flex;
  align-items: center;
  gap: $gap-sm;
  color: $color-text-primary;
  font-size: $font-size-sm;
}

.grid-header-type {
  color: $color-text-secondary;
  font-size: $font-size-xs;
  font-weight: 600;
}

.grid-sort-indicator {
  color: $color-accent-blue;
  font-weight: 800;
  font-size: $font-size-sm;
}

// ── 单元格内容 ──
.grid-cell-text,
.grid-primary-value,
.grid-link-button {
  width: 100%;
  min-width: 0;
  max-width: none;
  overflow: hidden;
  text-overflow: ellipsis;
  line-height: 20px;
  font-size: 10.5px;
}

.grid-binary-value-cell {
  cursor: pointer;
  transition: background 140ms ease;

  &:hover {
    background: rgba(240, 249, 255, 0.72);

    .grid-cell-text {
      color: #0369a1;
    }
  }
}

.grid-cell-text {
  color: $color-text-primary;
}

.grid-draft-cell-text {
  color: $color-text-secondary;
  font-style: italic;
}

.grid-empty-cell {
  padding: 18px $gap-2xl !important;
  color: $color-text-secondary;
  font-size: $font-size-base;
  text-align: center;
}

.grid-detail-open-value {
  cursor: default;
}

.grid-draft-readonly-cell .grid-draft-cell-text {
  opacity: 0.6;
}

.grid-link-button {
  border: 0;
  padding: 0;
  background: transparent;
  color: $color-accent-teal;
  cursor: pointer;
  text-align: left;
  font-size: inherit;
}

.grid-primary-value {
  color: $color-accent-green-cell;
  font-weight: 700;
}

.grid-editing-cell-value {
  color: $color-accent-teal;
}

.grid-null-value {
  color: $color-text-muted !important;
}

// ── 列宽拖拽手柄 ──
.grid-resize-handle {
  position: absolute;
  top: 0;
  right: -2px;
  width: 8px;
  height: 100%;
  padding: 0;
  border: 0;
  background: transparent;
  cursor: col-resize;

  &::before {
    content: '';
    position: absolute;
    top: 4px;
    bottom: 4px;
    left: 3px;
    width: 1px;
    background: rgba(148, 163, 184, 0.45);
  }
}

// ── 浮动编辑器 ──
.grid-floating-editor {
  position: fixed;
  z-index: 95;
  max-width: min(520px, calc(100vw - 24px));
  box-sizing: border-box;
  padding: 0;
  border: 0;
  border-radius: 0;
  background: $color-surface-white;
  box-shadow: 0 6px 18px rgba(15, 23, 42, 0.14), inset 0 0 0 2px #3b82f6;
  transform: none;
  overflow: hidden;

  &-dirty {
    background: #eefbf4;
  }
}

.grid-floating-input,
.grid-floating-textarea {
  width: 100%;
  box-sizing: border-box;
  border: 0;
  outline: none;
  border-radius: 0;
  background: transparent;
  color: $color-text-primary;
  font: inherit;
  font-size: 10.5px;
  font-weight: var(--grid-editor-font-weight, 400);
}

.grid-floating-input {
  height: 100%;
  min-height: 0;
  margin: 0;
  padding: var(--grid-editor-v-offset, 0px) 3px 0 var(--grid-editor-h-offset, 3px);
  line-height: 20px;
  appearance: none;
  display: block;
  box-sizing: border-box;
  resize: none;
  overflow: hidden;
  white-space: nowrap;
}

.grid-floating-select {
  min-width: 0;

  :deep(.n-base-selection-label) {
    padding-right: 3px;
  }
}

.grid-floating-textarea {
  min-height: 84px;
  padding: var(--grid-editor-v-offset-multiline, 2px) 3px 2px var(--grid-editor-h-offset, 3px);
  line-height: 1.45;
  resize: vertical;
}

.grid-floating-inline-row {
  display: flex;
  align-items: stretch;
  min-width: 0;
  height: 100%;
}

// ── 行内编辑器 ──
.grid-inline-editor {
  display: grid;
  gap: 4px;
  min-width: 0;
  height: 100%;

  &-multiline,
  &-binary {
    min-width: 0;
  }

  :deep(.n-base-selection) {
    width: 100%;
  }

  :deep(.n-base-selection .n-base-selection-label) {
    min-height: 20px;
    padding: 0 3px;
    font-size: 10.5px;
  }
}

.grid-inline-action-buttons,
.grid-inline-binary-actions,
.grid-draft-actions {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-wrap: wrap;
}

.grid-inline-binary-meta,
.grid-row-action-hint {
  color: $color-text-secondary;
  font-size: $font-size-sm;
  line-height: 1.3;
}

// ── 行操作区 ──
.grid-row-action-cell {
  min-width: 112px;
  width: 112px;
  white-space: nowrap;
  vertical-align: middle;

  &-draft {
    background: rgba(240, 253, 244, 0.9);
  }
}

.grid-inline-file-input {
  display: none;
}

// ── 网格底部区域 ──
.grid-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: $gap-lg;
  padding: $gap-xs $gap-md;

  &-meta {
    color: $color-text-secondary;
    font-size: $font-size-base;
  }

  &-actions {
    display: flex;
    gap: $gap-md;
    flex-wrap: wrap;
  }
}

.grid-editing-tip {
  margin-bottom: 8px;
}
</style>
