<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { NAlert, NButton, NCard, NModal, NSelect, NSpin } from 'naive-ui'
import GridCellEditorModal from './GridCellEditorModal.vue'
import { useExplorerStore } from '../stores/explorer'
import type { CellContentPreview, CellValue, ForeignKeyRef, TableColumn, TableRow, TableRowWriteValueRequest } from '../types/explorer'

const props = defineProps<{ tableKey: string }>()
const store = useExplorerStore()

const table = computed(() => store.getLoadedTable(props.tableKey))
const tableMeta = computed(() => store.getTable(props.tableKey))
const rows = computed(() => store.getFilteredRows(props.tableKey))
const searchState = computed(() => store.getTableSearchState(props.tableKey))
const searchActive = computed(() => store.isSearchActive(props.tableKey))
const sortState = computed(() => store.getTableSortState(props.tableKey))
const columnWidths = ref<Record<string, number>>({})
const previewCache = ref<Record<string, CellContentPreview>>({})
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
})
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
  show: boolean
  x: number
  y: number
  rowKey: string
  column: VisibleColumn
  value: CellValue | undefined
  draft: boolean
} | null>(null)
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
})
const editMode = ref(false)
const activeEditor = ref<ActiveEditorState | null>(null)
const activeEditorAnchor = ref<{ top: number; left: number; width: number; height: number } | null>(null)
const activeEditorInitialPayload = ref<EditorPayload | null>(null)
const activeEditorDirty = ref(false)
const activeEditorTextValue = ref('')
const activeEditorSetNull = ref(false)
const activeEditorBinaryBase64 = ref<string | null>(null)
const activeEditorBinaryFileName = ref('')
const draftRowValues = ref<Record<string, DraftCellState>>({})
const binaryFileInputRef = ref<HTMLInputElement | null>(null)
const contextBinaryFileInputRef = ref<HTMLInputElement | null>(null)
const pendingBinaryReplaceTarget = ref<{ rowKey: string; column: VisibleColumn; draft: boolean } | null>(null)

function isBinaryColumn(columnName: string, columnType?: string) {
  const normalizedType = (columnType ?? '').toLowerCase()
  const normalizedName = columnName.toLowerCase()
  return normalizedType.includes('binary')
    || normalizedType.includes('image')
    || normalizedType.includes('blob')
    || normalizedName.endsWith('image')
    || normalizedName.endsWith('photo')
    || normalizedName.endsWith('thumbnail')
}

const visibleColumns = computed<VisibleColumn[]>(() => {
  if (!table.value) {
    return []
  }

  return table.value.columns.map((column) => {
    const fk = store.getForeignKey(props.tableKey, column.name)
    return {
      ...column,
      isPrimaryKey: !!column.isPrimaryKey,
      fk,
    }
  })
})

const hasPrimaryKeyColumns = computed(() => visibleColumns.value.some((column) => column.isPrimaryKey))

function cellValue(row: TableRow, columnName: string, columnType: string | undefined) {
  return store.formatFieldValue(columnName, columnType, row[columnName] ?? null)
}

function displayCellValue(row: TableRow, column: VisibleColumn) {
  const rawValue = row[column.name] ?? null
  if (typeof rawValue === 'string' && !isBinaryColumn(column.name, column.type)) {
    const normalized = rawValue.replace(/\r\n/g, '\n')
    if (normalized.includes('\n')) {
      const [firstLine, ...rest] = normalized.split('\n')
      const preview = `${firstLine || ' '} ↵${rest.length > 0 ? ` +${rest.length}` : ''}`
      return store.formatFieldValue(column.name, column.type, preview)
    }
  }

  return cellValue(row, column.name, column.type)
}

function isNullCell(row: TableRow, columnName: string) {
  return (row[columnName] ?? null) === null
}

function isDraftNullCell(columnName: string) {
  return draftRowValue(columnName)?.setNull === true
}

function displayColumnType(column: TableColumn) {
  if (!column.type) {
    return 'unknown'
  }

  if (column.maxLength === null || column.maxLength === undefined) {
    return column.type
  }

  if (column.maxLength < 0) {
    return `${column.type}(max)`
  }

  return `${column.type}(${column.maxLength})`
}

function handleRowOpen(rowKey: string) {
  store.openRowFromGrid(props.tableKey, rowKey)
}

function handleForeignKeyClick(rowKey: string, sourceColumn: string) {
  if (editMode.value) {
    return
  }

  const foreignKey = store.getForeignKey(props.tableKey, sourceColumn)
  if (!foreignKey) {
    return
  }

  store.navigateForeignKeyFromGrid(props.tableKey, rowKey, foreignKey)
}

function isReadOnlySystemColumn(columnType: string | undefined) {
  const normalized = (columnType ?? '').toLowerCase()
  return normalized === 'timestamp' || normalized === 'rowversion'
}

function isEditableColumn(column: VisibleColumn) {
  return !!table.value?.primaryKeys.length
    && !column.isAutoGenerated
    && !column.isComputed
    && !isReadOnlySystemColumn(column.type)
}

function isWritableOnInsert(column: VisibleColumn) {
  return !column.isAutoGenerated && !column.isComputed && !isReadOnlySystemColumn(column.type)
}

function isBooleanColumn(column: VisibleColumn) {
  return ['bit', 'bool', 'boolean'].includes((column.type ?? '').toLowerCase())
}

function isMultilineTextColumn(column: VisibleColumn) {
  const normalizedType = (column.type ?? '').toLowerCase()
  if (isBinaryColumn(column.name, column.type) || isBooleanColumn(column)) {
    return false
  }

  return normalizedType.includes('char')
    || normalizedType.includes('text')
    || normalizedType.includes('json')
    || normalizedType.includes('xml')
}

function isRowOpenCell(columnName: string, columnType: string | undefined, isPrimaryKey: boolean | undefined, columnIndex: number) {
  if (isPrimaryKey) {
    return true
  }

  if (hasPrimaryKeyColumns.value) {
    return false
  }

  return columnIndex === 0 && !isBinaryColumn(columnName, columnType)
}

function sortIndicator(columnName: string) {
  if (sortState.value.columnName !== columnName) {
    return ''
  }

  return sortState.value.direction === 'asc'
    ? '↑'
    : sortState.value.direction === 'desc'
      ? '↓'
      : ''
}

function handleHeaderSort(columnName: string, columnType: string | undefined) {
  void store.toggleTableSort(props.tableKey, columnName, columnType)
}

const hasRows = computed(() => rows.value.length > 0)
const loadedRowCount = computed(() => searchActive.value ? (searchState.value?.rows.length ?? 0) : (table.value?.rows.length ?? 0))
const totalRowCount = computed(() => searchActive.value ? (searchState.value?.totalMatches ?? 0) : (tableMeta.value?.rowCount ?? loadedRowCount.value))
const hasMoreRows = computed(() => searchActive.value ? !!searchState.value?.hasMoreRows : !!table.value?.hasMoreRows)
const canEnableEditMode = computed(() => !!table.value && visibleColumns.value.some((column) => isEditableColumn(column) || isWritableOnInsert(column)))
const draftRowHasValues = computed(() => Object.values(draftRowValues.value).some((item) => item.setNull || item.base64Value !== null || item.textValue.trim().length > 0))

const previewImageUrl = computed(() => {
  const content = previewState.value.content
  if (!content?.base64Data || content.kind !== 'image') {
    return null
  }

  return `data:${content.mimeType};base64,${content.base64Data}`
})

const activeEditorColumn = computed(() => activeEditor.value
  ? visibleColumns.value.find((column) => column.name === activeEditor.value?.columnName) ?? null
  : null)

const activeEditorIsBinary = computed(() => activeEditorColumn.value ? isBinaryColumn(activeEditorColumn.value.name, activeEditorColumn.value.type) : false)
const activeEditorIsBoolean = computed(() => activeEditorColumn.value ? isBooleanColumn(activeEditorColumn.value) : false)
const activeEditorIsMultiline = computed(() => activeEditorColumn.value ? isMultilineTextColumn(activeEditorColumn.value) : false)
const activeBooleanOptions = [
  { label: 'True', value: 'true' },
  { label: 'False', value: 'false' },
]

const activeEditorPopupStyle = computed(() => {
  if (!activeEditorAnchor.value) {
    return undefined
  }

  const width = Math.max(120, Math.round(activeEditorAnchor.value.width))
  return {
    top: `${Math.round(activeEditorAnchor.value.top)}px`,
    left: `${Math.round(activeEditorAnchor.value.left)}px`,
    width: `${width}px`,
    height: activeEditorIsMultiline.value ? undefined : `${Math.max(20, Math.round(activeEditorAnchor.value.height))}px`,
    minHeight: activeEditorIsMultiline.value ? `${Math.max(24, Math.round(activeEditorAnchor.value.height))}px` : undefined,
  }
})

function draftRowValue(columnName: string) {
  return draftRowValues.value[columnName] ?? null
}

function draftRowDisplayValue(column: VisibleColumn) {
  const state = draftRowValue(column.name)
  if (!state) {
    return column.isAutoGenerated ? '自动生成' : '双击输入'
  }

  if (state.setNull) {
    return 'NULL'
  }

  if (state.valueKind === 'binary') {
    if (!state.base64Value) {
      return '空二进制'
    }

    return `[binary ${Math.ceil(state.base64Value.length * 0.75)} B]`
  }

  if (state.textValue.includes('\n')) {
    const [firstLine, ...rest] = state.textValue.replace(/\r\n/g, '\n').split('\n')
    return `${firstLine || ' '} ↵${rest.length > 0 ? ` +${rest.length}` : ''}`
  }

  return state.textValue || '空字符串'
}

function isActiveEditor(rowKey: string, columnName: string, draft = false) {
  return !!activeEditor.value
    && activeEditor.value.rowKey === rowKey
    && activeEditor.value.columnName === columnName
    && activeEditor.value.draft === draft
}

function resetActiveEditor() {
  activeEditor.value = null
  activeEditorAnchor.value = null
  activeEditorInitialPayload.value = null
  activeEditorDirty.value = false
  activeEditorTextValue.value = ''
  activeEditorSetNull.value = false
  activeEditorBinaryBase64.value = null
  activeEditorBinaryFileName.value = ''
}

function toggleEditMode() {
  editMode.value = !editMode.value
  resetActiveEditor()
  if (!editMode.value) {
    draftRowValues.value = {}
  }
}

function markActiveEditorDirty() {
  activeEditorDirty.value = true
}

function buildInitialPayload(column: VisibleColumn, value: CellValue | undefined, draft: boolean): EditorPayload {
  if (draft) {
    const draftState = draftRowValue(column.name)
    return {
      valueKind: draftState?.valueKind ?? 'text',
      textValue: draftState?.textValue ?? '',
      base64Value: draftState?.base64Value ?? null,
      setNull: draftState?.setNull ?? false,
    }
  }

  if (value === null || value === undefined) {
    return {
      valueKind: 'null',
      textValue: null,
      base64Value: null,
      setNull: true,
    }
  }

  if (isBinaryColumn(column.name, column.type)) {
    return {
      valueKind: 'binary',
      textValue: null,
      base64Value: typeof value === 'string' ? value : null,
      setNull: false,
    }
  }

  return {
    valueKind: 'text',
    textValue: String(value),
    base64Value: null,
    setNull: false,
  }
}

function payloadEquals(left: EditorPayload | null, right: EditorPayload) {
  if (!left) {
    return false
  }

  return left.valueKind === right.valueKind
    && left.textValue === right.textValue
    && left.base64Value === right.base64Value
    && left.setNull === right.setNull
}

function beginInlineEdit(rowKey: string, column: VisibleColumn, value: CellValue | undefined, draft = false, anchorTarget?: EventTarget | null) {
  if ((!draft && !isEditableColumn(column)) || (draft && !isWritableOnInsert(column))) {
    return
  }

  closeContextMenu()
  const anchorElement = anchorTarget instanceof HTMLElement ? anchorTarget.closest('td') as HTMLElement | null : null
  if (anchorElement) {
    const rect = anchorElement.getBoundingClientRect()
    activeEditorAnchor.value = {
      top: rect.top,
      left: rect.left,
      width: rect.width,
      height: rect.height,
    }
  }
  const draftValue = draft ? draftRowValue(column.name) : null
  activeEditor.value = {
    rowKey,
    columnName: column.name,
    draft,
    saving: false,
  }
  activeEditorInitialPayload.value = buildInitialPayload(column, value, draft)
  activeEditorDirty.value = false

  if (draftValue) {
    activeEditorTextValue.value = draftValue.textValue
    activeEditorSetNull.value = draftValue.setNull
    activeEditorBinaryBase64.value = draftValue.base64Value
    activeEditorBinaryFileName.value = draftValue.fileName
    return
  }

  if (draft) {
    activeEditorSetNull.value = false
    activeEditorBinaryBase64.value = null
    activeEditorBinaryFileName.value = ''
    activeEditorTextValue.value = ''
    return
  }

  activeEditorSetNull.value = false
  activeEditorBinaryBase64.value = typeof value === 'string' && isBinaryColumn(column.name, column.type) ? value : null
  activeEditorBinaryFileName.value = ''
  if (activeEditorIsBoolean.value) {
    activeEditorTextValue.value = value === null || value === undefined
      ? ''
      : (String(value).toLowerCase() === 'true' ? 'true' : 'false')
    return
  }

  activeEditorTextValue.value = value === null || value === undefined ? '' : String(value)
}

function cancelInlineEdit() {
  if (activeEditor.value?.saving) {
    return
  }

  resetActiveEditor()
}

function currentBinarySize() {
  const base64Value = activeEditorBinaryBase64.value
  if (!base64Value) {
    return 0
  }

  const padding = base64Value.endsWith('==') ? 2 : base64Value.endsWith('=') ? 1 : 0
  return Math.max(0, Math.floor(base64Value.length * 0.75) - padding)
}

function chooseBinaryFile() {
  binaryFileInputRef.value?.click()
}

async function handleBinaryFileSelected(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) {
    return
  }

  activeEditorBinaryFileName.value = file.name
  const buffer = await file.arrayBuffer()
  const bytes = new Uint8Array(buffer)
  let binary = ''
  bytes.forEach((value) => {
    binary += String.fromCharCode(value)
  })
  activeEditorBinaryBase64.value = btoa(binary)
  activeEditorSetNull.value = false
  markActiveEditorDirty()
  input.value = ''
}

function clearInlineBinary() {
  activeEditorBinaryBase64.value = null
  activeEditorBinaryFileName.value = ''
  activeEditorSetNull.value = !!activeEditorColumn.value?.isNullable
  markActiveEditorDirty()
}

function buildActiveEditorPayload(column: VisibleColumn) {
  if (activeEditorSetNull.value) {
    return {
      valueKind: 'null' as const,
      textValue: null,
      base64Value: null,
      setNull: true,
    }
  }

  if (isBinaryColumn(column.name, column.type)) {
    return {
      valueKind: 'binary' as const,
      textValue: null,
      base64Value: activeEditorBinaryBase64.value,
      setNull: false,
    }
  }

  return {
    valueKind: 'text' as const,
    textValue: activeEditorTextValue.value,
    base64Value: null,
    setNull: false,
  }
}

async function saveInlineEdit() {
  if (!activeEditor.value || !activeEditorColumn.value) {
    return
  }

  const payload = buildActiveEditorPayload(activeEditorColumn.value)
  if (!activeEditor.value.draft && (!activeEditorDirty.value || payloadEquals(activeEditorInitialPayload.value, payload))) {
    resetActiveEditor()
    return
  }

  if (activeEditor.value.draft) {
    if (!activeEditorDirty.value && payloadEquals(activeEditorInitialPayload.value, payload)) {
      resetActiveEditor()
      return
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
    }
    resetActiveEditor()
    return
  }

  activeEditor.value = {
    ...activeEditor.value,
    saving: true,
  }

  try {
    await store.updateTableCell({
      tableKey: props.tableKey,
      rowKey: activeEditor.value.rowKey,
      columnName: activeEditorColumn.value.name,
      valueKind: payload.valueKind,
      textValue: payload.textValue,
      base64Value: payload.base64Value,
      setNull: payload.setNull,
    })

    const nextCache = { ...previewCache.value }
    delete nextCache[previewCacheKey(activeEditor.value.rowKey, activeEditorColumn.value.name)]
    previewCache.value = nextCache
    resetActiveEditor()
  }
  catch {
    activeEditor.value = {
      ...activeEditor.value,
      saving: false,
    }
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
    })) satisfies TableRowWriteValueRequest[]

  if (values.length === 0) {
    return
  }

  try {
    await store.insertTableRow({
      tableKey: props.tableKey,
      values,
    })
    draftRowValues.value = {}
    resetActiveEditor()
  }
  catch {
    // Store already surfaces the failure.
  }
}

function clearDraftRow() {
  draftRowValues.value = {}
  if (activeEditor.value?.draft) {
    resetActiveEditor()
  }
}

function handleEditableCellDoubleClick(event: MouseEvent, rowKey: string, column: VisibleColumn, value: CellValue | undefined, draft = false) {
  if (!editMode.value) {
    return
  }

  if (isBinaryColumn(column.name, column.type)) {
    return
  }

  event.preventDefault()
  event.stopPropagation()
  beginInlineEdit(rowKey, column, value, draft, event.currentTarget)
}

function handlePrimaryCellDoubleClick(event: MouseEvent, rowKey: string, column: VisibleColumn, value: CellValue | undefined) {
  if (editMode.value) {
    handleEditableCellDoubleClick(event, rowKey, column, value)
    return
  }

  event.preventDefault()
  event.stopPropagation()
  handleRowOpen(rowKey)
}

function handleForeignKeyDoubleClick(event: MouseEvent, rowKey: string, column: VisibleColumn, value: CellValue | undefined) {
  if (editMode.value) {
    handleEditableCellDoubleClick(event, rowKey, column, value)
    return
  }

  event.preventDefault()
  event.stopPropagation()
  handleForeignKeyClick(rowKey, column.name)
}

function handleEditorKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape') {
    event.preventDefault()
    cancelInlineEdit()
    return
  }

  if (event.key === 'Enter' && !event.shiftKey && (!activeEditorIsMultiline.value || event.ctrlKey)) {
    event.preventDefault()
    void saveInlineEdit()
  }
}

function columnStyle(columnName: string) {
  const width = columnWidths.value[columnName]
  if (!width) {
    return undefined
  }

  return {
    width: `${width}px`,
    minWidth: `${width}px`,
    maxWidth: `${width}px`,
  }
}

function beginResize(event: MouseEvent, columnName: string) {
  event.preventDefault()
  event.stopPropagation()

  const header = (event.currentTarget as HTMLElement).closest('th')
  const startWidth = header?.getBoundingClientRect().width ?? columnWidths.value[columnName] ?? 120
  const startX = event.clientX

  const onMouseMove = (moveEvent: MouseEvent) => {
    const nextWidth = Math.max(56, startWidth + moveEvent.clientX - startX)
    columnWidths.value = {
      ...columnWidths.value,
      [columnName]: nextWidth,
    }
  }

  const onMouseUp = () => {
    window.removeEventListener('mousemove', onMouseMove)
    window.removeEventListener('mouseup', onMouseUp)
  }

  window.addEventListener('mousemove', onMouseMove)
  window.addEventListener('mouseup', onMouseUp)
}

function resetColumnWidth(columnName: string) {
  const next = { ...columnWidths.value }
  delete next[columnName]
  columnWidths.value = next
}

function previewCacheKey(rowKey: string, columnName: string) {
  return `${rowKey}::${columnName}`
}

async function ensureCellContent(rowKey: string, columnName: string) {
  const key = previewCacheKey(rowKey, columnName)
  if (previewCache.value[key]) {
    return previewCache.value[key]
  }

  const content = await store.fetchCellContent(props.tableKey, rowKey, columnName)
  previewCache.value = {
    ...previewCache.value,
    [key]: content,
  }
  return content
}

async function openBinaryPreview(rowKey: string, columnName: string) {
  try {
    const content = await ensureCellContent(rowKey, columnName)
    if (content.kind === 'empty' || content.sizeBytes === 0) {
      return
    }

    previewState.value = {
      show: true,
      loading: false,
      title: `${tableMeta.value?.name ?? props.tableKey}.${columnName}`,
      content,
      error: null,
    }
  }
  catch (error) {
    previewState.value = {
      show: true,
      loading: false,
      title: `${tableMeta.value?.name ?? props.tableKey}.${columnName}`,
      content: null,
      error: error instanceof Error ? error.message : '二进制内容读取失败',
    }
  }
}

function closeContextMenu() {
  contextMenu.value = null
}

function openCellContextMenu(event: MouseEvent, rowKey: string, column: VisibleColumn, value: CellValue | undefined, draft = false) {
  closeContextMenu()
  contextMenu.value = {
    show: true,
    x: event.clientX,
    y: event.clientY,
    rowKey,
    column,
    value,
    draft,
  }
}

function openCellEditor(rowKey: string, column: VisibleColumn, value: CellValue | undefined) {
  if (!isEditableColumn(column)) {
    return
  }

  closeContextMenu()
  dialogEditorState.value = {
    show: true,
    rowKey,
    column,
    foreignKey: column.fk ?? null,
    value,
    saving: false,
  }
}

function handleCellDoubleClick(event: MouseEvent, rowKey: string, column: VisibleColumn, value: CellValue | undefined) {
  if (editMode.value) {
    handleEditableCellDoubleClick(event, rowKey, column, value)
    return
  }

  if (isBinaryColumn(column.name, column.type)) {
    event.preventDefault()
    event.stopPropagation()
    void openBinaryPreview(rowKey, column.name)
    return
  }
}

function handleCellMouseDown(event: MouseEvent, suppressSelection: boolean) {
  if (suppressSelection && event.detail > 1) {
    event.preventDefault()
  }
}

function handleBinaryCellClick(event: MouseEvent, rowKey: string, columnName: string, columnType: string | undefined) {
  if (!isBinaryColumn(columnName, columnType) || editMode.value) {
    return
  }

  event.stopPropagation()
  void openBinaryPreview(rowKey, columnName)
}

function base64ToBytes(base64: string) {
  const raw = atob(base64)
  const bytes = new Uint8Array(raw.length)
  for (let index = 0; index < raw.length; index += 1) {
    bytes[index] = raw.charCodeAt(index)
  }
  return bytes
}

async function saveCellContent(rowKey: string, columnName: string) {
  closeContextMenu()
  const content = await ensureCellContent(rowKey, columnName)
  if (!content.base64Data) {
    return
  }

  const bytes = base64ToBytes(content.base64Data)
  const blob = new Blob([bytes], { type: content.mimeType || 'application/octet-stream' })
  const windowWithPicker = window as Window & {
    showSaveFilePicker?: (options?: unknown) => Promise<{
      createWritable: () => Promise<{ write: (data: Blob) => Promise<void>; close: () => Promise<void> }>
    }>
  }

  if (windowWithPicker.showSaveFilePicker) {
    const extension = content.suggestedFileName.includes('.') ? `.${content.suggestedFileName.split('.').pop()}` : '.bin'
    const handle = await windowWithPicker.showSaveFilePicker({
      suggestedName: content.suggestedFileName,
      types: [{
        description: content.mimeType,
        accept: {
          [content.mimeType || 'application/octet-stream']: [extension],
        },
      }],
    })
    const writable = await handle.createWritable()
    await writable.write(blob)
    await writable.close()
    return
  }

  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = content.suggestedFileName
  anchor.click()
  URL.revokeObjectURL(url)
}

function pickBinaryReplacement() {
  if (!contextMenu.value || !isBinaryColumn(contextMenu.value.column.name, contextMenu.value.column.type)) {
    return
  }

  pendingBinaryReplaceTarget.value = {
    rowKey: contextMenu.value.rowKey,
    column: contextMenu.value.column,
    draft: contextMenu.value.draft,
  }
  closeContextMenu()
  contextBinaryFileInputRef.value?.click()
}

async function handleContextBinaryFileSelected(event: Event) {
  const target = pendingBinaryReplaceTarget.value
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!target || !file) {
    pendingBinaryReplaceTarget.value = null
    input.value = ''
    return
  }

  const buffer = await file.arrayBuffer()
  const bytes = new Uint8Array(buffer)
  let binary = ''
  bytes.forEach((value) => {
    binary += String.fromCharCode(value)
  })
  const base64Value = btoa(binary)

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
    }
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
      })

      const nextCache = { ...previewCache.value }
      delete nextCache[previewCacheKey(target.rowKey, target.column.name)]
      previewCache.value = nextCache
    }
    catch {
      // Store already surfaced the error.
    }
  }

  pendingBinaryReplaceTarget.value = null
  input.value = ''
}

async function handleDialogEditorSave(payload: { valueKind: 'text' | 'binary' | 'null'; textValue?: string | null; base64Value?: string | null; setNull: boolean }) {
  const rowKey = dialogEditorState.value.rowKey
  const column = dialogEditorState.value.column
  if (!rowKey || !column) {
    return
  }

  dialogEditorState.value = {
    ...dialogEditorState.value,
    saving: true,
  }

  try {
    await store.updateTableCell({
      tableKey: props.tableKey,
      rowKey,
      columnName: column.name,
      valueKind: payload.valueKind,
      textValue: payload.textValue ?? null,
      base64Value: payload.base64Value ?? null,
      setNull: payload.setNull,
    })

    const nextCache = { ...previewCache.value }
    delete nextCache[previewCacheKey(rowKey, column.name)]
    previewCache.value = nextCache
    dialogEditorState.value = {
      show: false,
      rowKey: null,
      column: null,
      foreignKey: null,
      value: undefined,
      saving: false,
    }
  }
  catch {
    dialogEditorState.value = {
      ...dialogEditorState.value,
      saving: false,
    }
  }
}

async function setContextMenuValueNull() {
  const menu = contextMenu.value
  if (!menu || !menu.column.isNullable) {
    return
  }

  closeContextMenu()

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
    }
    if (activeEditor.value?.draft && activeEditor.value.columnName === menu.column.name) {
      resetActiveEditor()
    }
    return
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
    })

    const nextCache = { ...previewCache.value }
    delete nextCache[previewCacheKey(menu.rowKey, menu.column.name)]
    previewCache.value = nextCache
  }
  catch {
    // Store already surfaced the error.
  }
}

function handleWindowClick() {
  closeContextMenu()
  if (activeEditor.value) {
    void saveInlineEdit()
  }
}

function handleWindowContextMenu() {
  closeContextMenu()
}

function handleWindowBlur() {
  closeContextMenu()
  cancelInlineEdit()
}

onMounted(() => {
  window.addEventListener('click', handleWindowClick)
  window.addEventListener('contextmenu', handleWindowContextMenu, true)
  window.addEventListener('blur', handleWindowBlur)
})

onBeforeUnmount(() => {
  window.removeEventListener('click', handleWindowClick)
  window.removeEventListener('contextmenu', handleWindowContextMenu, true)
  window.removeEventListener('blur', handleWindowBlur)
})

watch(() => props.tableKey, () => {
  editMode.value = false
  resetActiveEditor()
  closeContextMenu()
  dialogEditorState.value = {
    show: false,
    rowKey: null,
    column: null,
    foreignKey: null,
    value: undefined,
    saving: false,
  }
})
</script>

<template>
  <n-card embedded class="workspace-panel grid-panel">
    <template #header>
      <div class="panel-header grid-panel-header-compact">
        <div class="grid-panel-title-row">
          <span class="grid-panel-kicker">主表数据</span>
          <h3>{{ tableMeta?.schema ? `${tableMeta.schema}.${tableMeta.name}` : tableMeta?.name }}</h3>
        </div>
        <div class="grid-panel-header-meta">
          <div class="panel-meta">{{ loadedRowCount }} / {{ totalRowCount }} rows</div>
          <n-button size="small" tertiary type="primary" :disabled="!canEnableEditMode" @click="toggleEditMode">
            {{ editMode ? '退出编辑' : '编辑模式' }}
          </n-button>
        </div>
      </div>
    </template>

    <div class="grid-panel-body">
      <n-alert v-if="store.getTableError(props.tableKey)" type="warning" :show-icon="false" class="panel-inline-alert">
        {{ store.getTableError(props.tableKey) }}
      </n-alert>
      <n-alert v-else-if="store.getTableSearchError(props.tableKey)" type="warning" :show-icon="false" class="panel-inline-alert">
        {{ store.getTableSearchError(props.tableKey) }}
      </n-alert>

      <n-spin :show="store.isTableLoading(props.tableKey) || store.isSearchLoading(props.tableKey)" class="grid-panel-spin">
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
                  class="grid-row"
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
                      <n-button size="tiny" type="primary" :disabled="!draftRowHasValues" @click="insertDraftRow">新增</n-button>
                      <n-button size="tiny" tertiary :disabled="!draftRowHasValues" @click="clearDraftRow">清空</n-button>
                    </div>
                  </td>
                </tr>
              </tbody>
            </table>
          </template>
        </div>
        <n-empty v-else description="表数据尚未加载完成" />
      </n-spin>

      <div v-if="table" class="grid-footer">
        <div class="grid-footer-meta">{{ searchActive ? '搜索结果' : '已加载' }} {{ loadedRowCount }} / {{ totalRowCount }} 行</div>
        <div class="grid-footer-actions">
          <n-button size="small" tertiary :disabled="!hasMoreRows || store.isTableLoading(props.tableKey) || store.isSearchLoading(props.tableKey)" @click="searchActive ? store.loadMoreSearchRows(props.tableKey) : store.loadMoreTableRows(props.tableKey)">
            再加载 {{ store.defaultPageSize }} 行
          </n-button>
          <n-button size="small" tertiary type="primary" :disabled="!hasMoreRows || store.isTableLoading(props.tableKey) || store.isSearchLoading(props.tableKey)" @click="searchActive ? store.loadAllSearchRows(props.tableKey) : store.loadAllTableRows(props.tableKey)">
            加载全部
          </n-button>
          <n-button v-if="searchActive" size="small" tertiary @click="store.clearActiveTableSearch()">
            清除搜索
          </n-button>
        </div>
      </div>
    </div>

    <div
      v-if="contextMenu?.show"
      class="grid-context-menu"
      :style="{ left: `${contextMenu.x}px`, top: `${contextMenu.y}px` }"
      @click.stop
      @mousedown.stop
    >
      <button
        v-if="isEditableColumn(contextMenu.column)"
        type="button"
        class="grid-context-menu-item"
        @click="openCellEditor(contextMenu.rowKey, contextMenu.column, contextMenu.value)"
      >
        编辑单元格...
      </button>
      <button
        v-if="isEditableColumn(contextMenu.column) && !editMode"
        type="button"
        class="grid-context-menu-item"
        @click="editMode = true; closeContextMenu()"
      >
        进入编辑模式
      </button>
      <button
        v-if="contextMenu.column.isNullable && (contextMenu.draft ? isWritableOnInsert(contextMenu.column) : isEditableColumn(contextMenu.column))"
        type="button"
        class="grid-context-menu-item"
        @click="setContextMenuValueNull"
      >
        设为 NULL
      </button>
      <button
        v-if="isBinaryColumn(contextMenu.column.name, contextMenu.column.type)"
        type="button"
        class="grid-context-menu-item"
        @click="pickBinaryReplacement"
      >
        替换二进制文件...
      </button>
      <button
        v-if="isBinaryColumn(contextMenu.column.name, contextMenu.column.type)"
        type="button"
        class="grid-context-menu-item"
        @click="openBinaryPreview(contextMenu.rowKey, contextMenu.column.name); closeContextMenu()"
      >
        预览二进制
      </button>
      <button
        v-if="isBinaryColumn(contextMenu.column.name, contextMenu.column.type)"
        type="button"
        class="grid-context-menu-item"
        @click="saveCellContent(contextMenu.rowKey, contextMenu.column.name)"
      >
        保存二进制到文件...
      </button>
    </div>

    <input ref="binaryFileInputRef" type="file" class="grid-inline-file-input" @change="handleBinaryFileSelected" />
  <input ref="contextBinaryFileInputRef" type="file" class="grid-inline-file-input" @change="handleContextBinaryFileSelected" />

    <div
      v-if="activeEditor && activeEditorColumn && activeEditorPopupStyle"
      class="grid-floating-editor"
      :class="{ 'grid-floating-editor-dirty': activeEditorDirty }"
      :style="activeEditorPopupStyle"
      @click.stop
      @mousedown.stop
    >
      <div class="grid-inline-editor" :class="{ 'grid-inline-editor-multiline': activeEditorIsMultiline, 'grid-inline-editor-binary': activeEditorIsBinary }">
        <template v-if="activeEditorIsBinary">
          <div class="grid-inline-binary-meta">
            <span v-if="activeEditorBinaryFileName">{{ activeEditorBinaryFileName }}</span>
            <span v-else-if="activeEditorBinaryBase64">{{ activeEditor.draft ? '待写入' : '当前内容' }} {{ currentBinarySize() }} B</span>
            <span v-else>{{ activeEditor.draft ? '请选择文件' : '当前内容为空' }}</span>
          </div>
          <div class="grid-inline-binary-actions">
            <n-button size="tiny" type="primary" :disabled="activeEditor.saving" @click.stop="chooseBinaryFile">选择文件</n-button>
            <n-button size="tiny" tertiary :disabled="activeEditor.saving" @click.stop="clearInlineBinary">清空</n-button>
            <n-button size="tiny" type="primary" :loading="activeEditor.saving" @click.stop="saveInlineEdit">{{ activeEditor.draft ? '确定' : '保存' }}</n-button>
            <n-button size="tiny" tertiary :disabled="activeEditor.saving" @click.stop="cancelInlineEdit">取消</n-button>
          </div>
        </template>

        <template v-else-if="activeEditorIsBoolean">
          <n-select
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
            v-model="activeEditorTextValue"
            class="grid-floating-textarea"
            :disabled="activeEditor.saving"
            @input="markActiveEditorDirty"
            @keydown="handleEditorKeydown"
          />
        </template>

        <template v-else>
          <input
            v-model="activeEditorTextValue"
            class="grid-floating-input"
            :disabled="activeEditor.saving"
            @input="markActiveEditorDirty"
            @keydown="handleEditorKeydown"
          >
        </template>
      </div>
    </div>

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

    <n-modal v-model:show="previewState.show" preset="card" style="width: min(960px, 92vw)" :title="previewState.title">
      <n-spin :show="previewState.loading">
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
            <n-button type="primary" @click="saveCellContent(previewState.content.rowKey, previewState.content.columnName)">保存到文件...</n-button>
          </div>
        </template>
      </n-spin>
    </n-modal>
  </n-card>
</template>
