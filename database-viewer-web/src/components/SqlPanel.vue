<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useLocalStorage } from '@vueuse/core'
import { NAlert, NButton, NCard, NEmpty, NModal, NSelect, NSpin, NTag } from 'naive-ui'
import SqlCodeEditor from './SqlCodeEditor.vue'
import { useExplorerStore } from '../stores/explorer'
import type { SqlWorkspaceTab } from '../types/explorer'

const props = defineProps<{ tab: SqlWorkspaceTab }>()
const store = useExplorerStore()
const defaultEditorHeight = typeof window !== 'undefined' ? Math.round(window.innerHeight * 0.5) : 360
const editorHeight = useLocalStorage('dbv-sql-editor-height-v3', defaultEditorHeight)
const panelBodyRef = ref<HTMLElement | null>(null)
const controlsRef = ref<HTMLElement | null>(null)
const availableEditorHeight = ref<number>(defaultEditorHeight)
const previewState = ref<{
  show: boolean
  title: string
  kind: 'image' | 'text' | 'binary' | 'empty'
  mimeType: string
  sizeBytes: number
  textContent: string | null
  base64Data: string | null
}>({
  show: false,
  title: '',
  kind: 'empty',
  mimeType: 'application/octet-stream',
  sizeBytes: 0,
  textContent: null,
  base64Data: null,
})

let stopEditorResize: (() => void) | null = null
let resizeObserver: ResizeObserver | null = null

const connectionOptions = computed(() => store.connections.map((connection) => ({
  label: `${connection.name} · ${connection.host}`,
  value: connection.id,
})))

const databaseOptions = computed(() => {
  if (!props.tab.connectionId) {
    return []
  }

  return store.getConnectionDatabases(props.tab.connectionId).map((database) => ({
    label: database.name,
    value: database.name,
  }))
})

const connectionInfo = computed(() => props.tab.connectionId ? store.getConnectionInfo(props.tab.connectionId) : null)
const sqlProvider = computed(() => connectionInfo.value?.provider ?? 'sqlserver')
const sqlContext = computed(() => props.tab.connectionId && props.tab.database ? store.getSqlContext(props.tab.connectionId, props.tab.database) : null)
const sqlDatabases = computed(() => props.tab.connectionId ? store.getConnectionDatabases(props.tab.connectionId).map((database) => database.name) : [])
const editorText = ref(props.tab.sqlText)
const lastLocalSqlText = ref(props.tab.sqlText)
const sqlFileName = computed(() => props.tab.filePath ? props.tab.filePath.replace(/\\/g, '/').split('/').pop() ?? props.tab.filePath : null)
const isDirty = computed(() => store.isSqlTabDirty(props.tab.id))

const selectedResultSet = computed(() => {
  if (!props.tab.result) {
    return null
  }

  return props.tab.result.resultSets[props.tab.selectedResultIndex] ?? props.tab.result.resultSets[0] ?? null
})

const hasVisibleResults = computed(() => !!props.tab.result)

const effectiveEditorHeight = computed(() => {
  if (!hasVisibleResults.value) {
    return Math.max(220, availableEditorHeight.value)
  }

  return Math.max(180, Math.min(editorHeight.value, Math.round(availableEditorHeight.value * 0.75)))
})

function measureAvailableEditorHeight() {
  const body = panelBodyRef.value
  const controls = controlsRef.value
  if (!body || !controls) {
    return
  }

  const bodyHeight = body.clientHeight
  const toolbarHeight = Array.from(controls.children)
    .filter((element) => !(element as HTMLElement).classList.contains('sql-editor'))
    .reduce((sum, element) => sum + (element as HTMLElement).offsetHeight, 0)
  const gapAllowance = 18
  availableEditorHeight.value = Math.max(220, bodyHeight - toolbarHeight - gapAllowance)
}

function handleEditorTextUpdate(value: string) {
  editorText.value = value
  lastLocalSqlText.value = value
  store.updateSqlTabText(props.tab.id, value)
}

function clearEditorText() {
  editorText.value = ''
  lastLocalSqlText.value = ''
  store.updateSqlTabText(props.tab.id, '')
}

function executeCurrentSql() {
  const sqlText = editorText.value
  lastLocalSqlText.value = sqlText
  store.updateSqlTabText(props.tab.id, sqlText)
  void store.executeSqlTab(props.tab.id, sqlText)
}

function saveCurrentSql(saveAs = false) {
  lastLocalSqlText.value = editorText.value
  store.updateSqlTabText(props.tab.id, editorText.value)
  void store.saveSqlTab(props.tab.id, saveAs)
}

function handleWindowExecuteSql() {
  if (!props.tab.loading) {
    executeCurrentSql()
  }
}

function displayValue(value: unknown) {
  return store.formatValue((value as string | number | boolean | null | undefined) ?? null)
}

function isBinarySqlColumn(columnName: string, columnType: string, value: unknown) {
  const normalized = columnType.toLowerCase()
  const normalizedName = columnName.toLowerCase()
  const hasBinaryType = normalized.includes('byte')
    || normalized.includes('binary')
    || normalized.includes('blob')
    || normalized.includes('image')
  if (hasBinaryType) {
    return true
  }

  if (typeof value !== 'string' || value.length < 16) {
    return false
  }

  const base64Like = /^[A-Za-z0-9+/]+={0,2}$/.test(value) && value.length % 4 === 0
  return base64Like && (normalizedName.includes('image') || normalizedName.includes('photo') || normalizedName.includes('thumbnail') || normalizedName.includes('blob') || normalizedName.includes('binary'))
}

function base64Size(base64: string) {
  const padding = base64.endsWith('==') ? 2 : base64.endsWith('=') ? 1 : 0
  return Math.max(0, Math.floor(base64.length * 0.75) - padding)
}

function displaySqlCellValue(columnName: string, columnType: string, value: unknown) {
  if (value === null || value === undefined) {
    return 'NULL'
  }

  if (isBinarySqlColumn(columnName, columnType, value) && typeof value === 'string') {
    return `[binary ${base64Size(value)} B]`
  }

  return displayValue(value)
}

function binaryPreviewable(columnName: string, columnType: string, value: unknown) {
  return isBinarySqlColumn(columnName, columnType, value) && typeof value === 'string' && value.length > 0
}

function bytesFromBase64(base64: string) {
  const raw = atob(base64)
  const bytes = new Uint8Array(raw.length)
  for (let index = 0; index < raw.length; index += 1) {
    bytes[index] = raw.charCodeAt(index)
  }
  return bytes
}

function imageMime(bytes: Uint8Array) {
  if (bytes.length >= 8
    && bytes[0] === 0x89 && bytes[1] === 0x50 && bytes[2] === 0x4e && bytes[3] === 0x47
    && bytes[4] === 0x0d && bytes[5] === 0x0a && bytes[6] === 0x1a && bytes[7] === 0x0a) {
    return 'image/png'
  }

  if (bytes.length >= 3 && bytes[0] === 0xff && bytes[1] === 0xd8 && bytes[2] === 0xff) {
    return 'image/jpeg'
  }

  if (bytes.length >= 6) {
    const header = String.fromCharCode(...bytes.slice(0, 6))
    if (header === 'GIF87a' || header === 'GIF89a') {
      return 'image/gif'
    }
  }

  if (bytes.length >= 12) {
    const riff = String.fromCharCode(...bytes.slice(0, 4))
    const webp = String.fromCharCode(...bytes.slice(8, 12))
    if (riff === 'RIFF' && webp === 'WEBP') {
      return 'image/webp'
    }
  }

  if (bytes.length >= 2 && bytes[0] === 0x42 && bytes[1] === 0x4d) {
    return 'image/bmp'
  }

  return null
}

function decodeText(bytes: Uint8Array) {
  try {
    const text = new TextDecoder('utf-8', { fatal: true }).decode(bytes)
    const controlCount = Array.from(text).filter((char) => {
      const code = char.charCodeAt(0)
      return code < 32 && char !== '\n' && char !== '\r' && char !== '\t'
    }).length
    if (controlCount > Math.max(2, text.length * 0.02)) {
      return null
    }

    return text
  }
  catch {
    return null
  }
}

function openBinaryPreview(columnName: string, columnType: string, value: unknown) {
  if (!binaryPreviewable(columnName, columnType, value) || typeof value !== 'string') {
    return
  }

  const bytes = bytesFromBase64(value)
  const mimeType = imageMime(bytes)
  if (mimeType) {
    previewState.value = {
      show: true,
      title: columnName,
      kind: 'image',
      mimeType,
      sizeBytes: bytes.length,
      textContent: null,
      base64Data: value,
    }
    return
  }

  const textContent = decodeText(bytes)
  if (textContent !== null) {
    previewState.value = {
      show: true,
      title: columnName,
      kind: 'text',
      mimeType: 'text/plain; charset=utf-8',
      sizeBytes: bytes.length,
      textContent,
      base64Data: value,
    }
    return
  }

  previewState.value = {
    show: true,
    title: columnName,
    kind: 'binary',
    mimeType: 'application/octet-stream',
    sizeBytes: bytes.length,
    textContent: null,
    base64Data: value,
  }
}

const previewImageUrl = computed(() => {
  if (previewState.value.kind !== 'image' || !previewState.value.base64Data) {
    return null
  }

  return `data:${previewState.value.mimeType};base64,${previewState.value.base64Data}`
})

function beginEditorResize(event: MouseEvent) {
  event.preventDefault()
  const startY = event.clientY
  const startHeight = editorHeight.value

  const onMouseMove = (moveEvent: MouseEvent) => {
    const nextHeight = Math.max(180, Math.min(Math.round(window.innerHeight * 0.75), startHeight + moveEvent.clientY - startY))
    editorHeight.value = nextHeight
  }

  const onMouseUp = () => {
    window.removeEventListener('mousemove', onMouseMove)
    window.removeEventListener('mouseup', onMouseUp)
    stopEditorResize = null
  }

  stopEditorResize = onMouseUp
  window.addEventListener('mousemove', onMouseMove)
  window.addEventListener('mouseup', onMouseUp)
}

onBeforeUnmount(() => {
  stopEditorResize?.()
  resizeObserver?.disconnect()
  window.removeEventListener('dbv-request-execute-sql', handleWindowExecuteSql)
})

onMounted(() => {
  window.addEventListener('dbv-request-execute-sql', handleWindowExecuteSql)

  if (typeof ResizeObserver === 'undefined') {
    return
  }

  resizeObserver = new ResizeObserver(() => {
    measureAvailableEditorHeight()
  })

  if (panelBodyRef.value) {
    resizeObserver.observe(panelBodyRef.value)
  }

  if (controlsRef.value) {
    resizeObserver.observe(controlsRef.value)
  }

  measureAvailableEditorHeight()
})

watch(() => [props.tab.connectionId, props.tab.database] as const, ([connectionId, database]) => {
  if (connectionId && database) {
    void store.ensureSqlContextLoaded(connectionId, database)
  }
}, { immediate: true })

watch(() => props.tab.id, () => {
  editorText.value = props.tab.sqlText
  lastLocalSqlText.value = props.tab.sqlText
}, { immediate: true })

watch(() => props.tab.sqlText, (value) => {
  if (value === lastLocalSqlText.value) {
    return
  }

  editorText.value = value
})

watch(() => hasVisibleResults.value, () => {
  measureAvailableEditorHeight()
})
</script>

<template>
  <n-card embedded class="workspace-panel sql-panel">
    <template #header>
      <div class="panel-header sql-panel-header">
        <div class="sql-panel-title-row">
          <span class="grid-panel-kicker">SQL 执行器</span>
          <h3>{{ store.getWorkspaceTabLabel(tab) }}</h3>
          <n-tag v-if="sqlFileName" size="small" :bordered="false">{{ sqlFileName }}</n-tag>
          <n-tag v-if="isDirty" size="small" :bordered="false" type="warning">未保存</n-tag>
        </div>
        <div class="sql-panel-header-meta">
          <n-tag v-if="tab.result" size="small" :bordered="false" type="info">{{ tab.result.elapsedMs }} ms</n-tag>
          <n-tag v-if="tab.result?.affectedRows !== null && tab.result?.affectedRows !== undefined" size="small" :bordered="false" type="success">影响 {{ tab.result.affectedRows }} 行</n-tag>
        </div>
      </div>
    </template>

    <div ref="panelBodyRef" class="sql-panel-body">
      <div ref="controlsRef" class="sql-panel-controls">
        <div class="sql-toolbar">
          <n-select
            :value="tab.connectionId"
            size="small"
            class="sql-toolbar-select"
            placeholder="选择连接"
            :options="connectionOptions"
            @update:value="store.updateSqlTabConnection(tab.id, $event)"
          />
          <n-select
            :value="tab.database"
            size="small"
            class="sql-toolbar-select"
            placeholder="选择数据库"
            :options="databaseOptions"
            @update:value="store.updateSqlTabDatabase(tab.id, $event)"
          />
          <n-button size="small" type="primary" :disabled="!tab.connectionId || !tab.database || !editorText.trim() || tab.loading" @click="executeCurrentSql">
            执行 SQL
          </n-button>
          <n-button size="small" tertiary @click="saveCurrentSql()">保存</n-button>
          <n-button size="small" tertiary @click="saveCurrentSql(true)">另存为</n-button>
          <n-button size="small" tertiary :disabled="tab.loading" @click="clearEditorText">清空文本</n-button>
        </div>

        <SqlCodeEditor
          :model-value="editorText"
          :provider="sqlProvider"
          :database="tab.database"
          :databases="sqlDatabases"
          :tables="sqlContext?.tables ?? []"
          class="sql-editor"
          :height="effectiveEditorHeight"
          @update:model-value="handleEditorTextUpdate"
          @execute="executeCurrentSql"
        />
        <div class="sql-editor-resize-handle" @mousedown="beginEditorResize" />

        <n-alert v-if="tab.error" type="warning" :show-icon="false" class="panel-inline-alert">
          {{ tab.error }}
        </n-alert>
      </div>

      <div class="sql-panel-results">
        <div class="sql-result-tabs" v-if="tab.result && tab.result.resultSets.length > 1">
          <button
            v-for="(resultSet, index) in tab.result.resultSets"
            :key="resultSet.name"
            type="button"
            class="sql-result-tab"
            :class="{ 'sql-result-tab-active': index === tab.selectedResultIndex }"
            @click="store.selectSqlResultSet(tab.id, index)"
          >
            {{ resultSet.name }} · {{ resultSet.rowCount }} 行
          </button>
        </div>

        <n-spin :show="tab.loading" class="sql-results-spin">
          <div v-if="selectedResultSet" class="sql-result-table-wrap">
            <table class="sql-result-table">
              <thead>
                <tr>
                  <th v-for="column in selectedResultSet.columns" :key="column.name">
                    <div class="sql-result-header-name">{{ column.name }}</div>
                    <div class="sql-result-header-type">{{ column.type }}</div>
                  </th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(row, rowIndex) in selectedResultSet.rows" :key="rowIndex">
                  <td
                    v-for="column in selectedResultSet.columns"
                    :key="column.name"
                    :title="displaySqlCellValue(column.name, column.type, row[column.name])"
                    @click="binaryPreviewable(column.name, column.type, row[column.name]) ? openBinaryPreview(column.name, column.type, row[column.name]) : undefined"
                  >
                    <span
                      class="sql-result-cell-text"
                      :class="{ 'sql-result-binary-cell': binaryPreviewable(column.name, column.type, row[column.name]) }"
                    >
                      {{ displaySqlCellValue(column.name, column.type, row[column.name]) }}
                    </span>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <div v-else-if="tab.result" class="sql-results-empty">
            <n-empty description="SQL 已执行，但没有结果集返回。" />
          </div>

          <div v-else class="sql-results-empty">
            <n-empty description="选择连接和数据库，输入 SQL 后执行。" />
          </div>
        </n-spin>

        <div v-if="selectedResultSet" class="sql-result-footer">
          <span>显示 {{ selectedResultSet.rows.length }} / {{ selectedResultSet.rowCount }} 行</span>
          <span v-if="selectedResultSet.truncated">结果已截断，最多展示 500 行</span>
        </div>
      </div>
    </div>

    <n-modal
      v-model:show="previewState.show"
      preset="card"
      class="binary-preview-modal"
      style="width: min(760px, 92vw)"
      :title="previewState.title"
    >
      <div v-if="previewState.kind === 'image' && previewImageUrl" class="binary-preview-body">
        <img :src="previewImageUrl" alt="binary preview" class="binary-preview-image" />
      </div>
      <pre v-else-if="previewState.kind === 'text'" class="binary-preview-text">{{ previewState.textContent }}</pre>
      <div v-else class="binary-preview-empty">无法将该字节数据识别为文本或图像。</div>
      <div class="binary-preview-meta">{{ previewState.mimeType }} · {{ previewState.sizeBytes }} B</div>
    </n-modal>
  </n-card>
</template>
