<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { NAlert, NButton, NCard, NEmpty, NModal, NSpin, NTag } from 'naive-ui'
import RelationGraph from './RelationGraph.vue'
import { useExplorerStore } from '../stores/explorer'
import type { CellContentPreview, ExplorerDetailPanel, ReverseReferenceRow, TableColumn } from '../types/explorer'

const props = defineProps<{ panel: ExplorerDetailPanel; chainPanels?: ExplorerDetailPanel[] }>()
const store = useExplorerStore()
const table = computed(() => store.getTable(props.panel.tableKey))
const record = computed(() => store.getPanelRecord(props.panel))
const detail = computed(() => {
  if (!table.value || !record.value) {
    return null
  }

  return {
    table: table.value,
    row: record.value.row,
    primaryKeys: record.value.primaryKeys,
    columns: record.value.columns,
    foreignKeys: record.value.foreignKeys,
    reverseReferences: record.value.reverseReferences,
  }
})
const reverseGroups = computed(() => store.getReverseReferences(props.panel))
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
const contextMenu = ref<{
  show: boolean
  x: number
  y: number
  columnName: string
} | null>(null)
const outgoingRelations = computed(() => {
  if (!detail.value) {
    return []
  }

  return detail.value.foreignKeys
    .map((fk) => {
      const column = detail.value?.columns.find((entry) => entry.name === fk.sourceColumn)
      const value = detail.value?.row[fk.sourceColumn]
      return {
        fk,
        column,
        value,
      }
    })
    .filter((entry): entry is { fk: typeof entry.fk; column: TableColumn; value: string | number | boolean } => !!entry.column && entry.value !== null && entry.value !== '')
})
const plainColumns = computed(() => {
  if (!detail.value) {
    return []
  }

  const currentDetail = detail.value
  return currentDetail.columns.filter((column) => {
    const isPrimary = currentDetail.primaryKeys.includes(column.name)
    const isForeign = !!foreignKeyForColumn(currentDetail.table.key, column.name)
    return !isPrimary && !isForeign
  })
})

function displayValue(value: unknown) {
  if (value === '') {
    return '空'
  }

  return store.formatValue((value as string | number | boolean | null | undefined) ?? null)
}

function displayFieldValue(columnName: string, columnType: string, value: unknown) {
  if (value === '') {
    return '空'
  }

  return store.formatFieldValue(columnName, columnType, (value as string | number | boolean | null | undefined) ?? null)
}

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

const previewImageUrl = computed(() => {
  const content = previewState.value.content
  if (!content?.base64Data || content.kind !== 'image') {
    return null
  }

  return `data:${content.mimeType};base64,${content.base64Data}`
})

function foreignKeyForColumn(tableKey: string, columnName: string) {
  return store.getForeignKey(tableKey, columnName)
}

async function handleGraphSelect(data: {
  kind: 'current' | 'ancestor' | 'incoming' | 'outgoing'
  panelId: string
  sourceTableKey?: string
  rowKey?: string
  columnName?: string
}) {
  if (data.kind === 'ancestor') {
    store.focusDetailPanel(data.panelId)
    return
  }

  if (data.kind === 'current') {
    return
  }

  if (data.kind === 'incoming' && data.sourceTableKey && data.rowKey) {
    await store.navigateReverseReference(props.panel.id, data.sourceTableKey, data.rowKey, '关系图导航')
    return
  }

  if (data.kind === 'outgoing' && data.columnName) {
    await store.navigateForeignKeyFromDetail(props.panel.id, props.panel.tableKey, props.panel.rowKey, data.columnName)
  }
}

function detailIdentityText() {
  if (!detail.value) {
    return ''
  }

  return detail.value.primaryKeys.map((key: string) => `${key}=${displayValue(detail.value?.row[key])}`).join(', ')
}

function reverseIdentityText(sourceTableKey: string, candidate: ReverseReferenceRow) {
  const sourceTable = store.getLoadedTable(sourceTableKey)
  if (sourceTable) {
    return sourceTable.primaryKeys.map((key) => `${key}=${displayValue(candidate.values[key])}`).join(', ')
  }

  return store.summarizeRowValues(candidate.values)
}

function rowTitle(candidate: ReverseReferenceRow, sourceTableKey: string) {
  const source = store.getLoadedTable(sourceTableKey)
  return source ? store.rowSummary(source, candidate.values) : store.summarizeRowValues(candidate.values)
}

function targetTableLabel(tableKey: string) {
  const candidate = store.getTable(tableKey)
  return candidate ? (candidate.schema ? `${candidate.schema}.${candidate.name}` : candidate.name) : tableKey
}

function previewCacheKey(columnName: string) {
  return `${props.panel.rowKey}::${columnName}`
}

async function ensureCellContent(columnName: string) {
  const key = previewCacheKey(columnName)
  if (previewCache.value[key]) {
    return previewCache.value[key]
  }

  const content = await store.fetchCellContent(props.panel.tableKey, props.panel.rowKey, columnName)
  previewCache.value = {
    ...previewCache.value,
    [key]: content,
  }
  return content
}

async function openBinaryPreview(columnName: string) {
  try {
    const content = await ensureCellContent(columnName)
    if (content.kind === 'empty' || content.sizeBytes === 0) {
      return
    }

    previewState.value = {
      show: true,
      loading: false,
      title: `${table.value?.name ?? props.panel.tableKey}.${columnName}`,
      content,
      error: null,
    }
  }
  catch (error) {
    previewState.value = {
      show: true,
      loading: false,
      title: `${table.value?.name ?? props.panel.tableKey}.${columnName}`,
      content: null,
      error: error instanceof Error ? error.message : '二进制内容读取失败',
    }
  }
}

function openBinaryContextMenu(event: MouseEvent, columnName: string) {
  contextMenu.value = {
    show: true,
    x: event.clientX,
    y: event.clientY,
    columnName,
  }
}

function closeContextMenu() {
  contextMenu.value = null
}

function handleFieldClick(event: MouseEvent, columnName: string, columnType: string) {
  if (!isBinaryColumn(columnName, columnType)) {
    return
  }

  event.stopPropagation()
  openBinaryPreview(columnName)
}

function handleFieldMouseDown(event: MouseEvent) {
  if (event.detail > 1) {
    event.preventDefault()
  }
}

function base64ToBytes(base64: string) {
  const raw = atob(base64)
  const bytes = new Uint8Array(raw.length)
  for (let index = 0; index < raw.length; index += 1) {
    bytes[index] = raw.charCodeAt(index)
  }
  return bytes
}

async function saveCellContent(columnName: string) {
  closeContextMenu()
  const content = await ensureCellContent(columnName)
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

function handleWindowClick() {
  closeContextMenu()
}

onMounted(() => {
  window.addEventListener('click', handleWindowClick)
})

onBeforeUnmount(() => {
  window.removeEventListener('click', handleWindowClick)
})
</script>

<template>
  <n-card embedded class="workspace-panel detail-panel">
    <template #header>
      <div class="panel-header detail-header">
        <div class="detail-header-copy">
          <div class="detail-kicker">记录详情</div>
          <h3>{{ table?.schema ? `${table.schema}.${table.name}` : table?.name }}</h3>
          <div class="detail-identity">{{ detailIdentityText() }}</div>
            <div class="detail-chip-row">
              <n-tag size="small" :bordered="false" type="info">{{ outgoingRelations.length }} 外键</n-tag>
              <n-tag size="small" :bordered="false" type="warning">{{ reverseGroups.length }} 入向关系</n-tag>
            </div>
        </div>
        <div class="detail-header-actions">
          <n-tag size="small" :bordered="false" type="warning">{{ panel.sourceLabel }}</n-tag>
          <n-button size="small" tertiary @click="store.closeDetailPanel(panel.id)">关闭</n-button>
        </div>
      </div>
    </template>

    <n-alert v-if="store.getRecordError(panel.id)" type="warning" :show-icon="false" class="panel-inline-alert">
      {{ store.getRecordError(panel.id) }}
    </n-alert>

    <n-spin :show="store.isRecordLoading(panel.id)">
      <template v-if="detail">
        <section class="detail-section">
          <div class="detail-section-title">关系概览</div>
          <RelationGraph
            :panel="panel"
            :chain-panels="chainPanels ?? [panel]"
            :table-key="detail.table.key"
            :title="table?.schema ? `${table.schema}.${table.name}` : (table?.name ?? '')"
            :identity-text="detailIdentityText()"
            :outgoing-relations="outgoingRelations"
            :reverse-groups="reverseGroups"
            @select-node="handleGraphSelect"
          />
        </section>

        <section class="detail-section">
          <div class="detail-section-title">字段</div>
          <div class="detail-fields">
            <div
              v-for="column in plainColumns"
              :key="column.name"
              class="detail-field-row"
            >
              <div class="detail-field-label">{{ column.name }}</div>
              <div
                class="detail-field-value"
                :class="{ 'detail-field-value-binary': isBinaryColumn(column.name, column.type) }"
                @mousedown="handleFieldMouseDown($event)"
                @click="handleFieldClick($event, column.name, column.type)"
                @contextmenu.prevent.stop="isBinaryColumn(column.name, column.type) ? openBinaryContextMenu($event, column.name) : undefined"
              >
                {{ displayFieldValue(column.name, column.type, detail.row[column.name]) }}
              </div>
            </div>
          </div>
        </section>

        <section class="detail-section">
          <div class="detail-section-title">外键引用</div>
          <div v-if="outgoingRelations.length" class="relation-list compact-relation-list">
            <button
              v-for="relation in outgoingRelations"
              :key="relation.fk.sourceColumn"
              type="button"
              class="relation-card compact-relation-card"
              @click="store.navigateForeignKeyFromDetail(panel.id, detail.table.key, panel.rowKey, relation.fk.sourceColumn)"
            >
              <div class="relation-card-top compact-relation-card-top">
                <strong>{{ relation.fk.sourceColumn }}</strong>
                <n-tag size="small" :bordered="false" type="success">{{ targetTableLabel(relation.fk.targetTableKey) }}</n-tag>
              </div>
              <div class="relation-card-value">{{ displayFieldValue(relation.column!.name, relation.column!.type, relation.value) }}</div>
              <div class="relation-card-meta">跳转到 {{ relation.fk.targetColumn }}</div>
            </button>
          </div>
          <n-empty v-else description="当前记录没有可用的外键引用" />
        </section>

        <section class="detail-section">
          <div class="detail-section-title">反向引用</div>
          <div v-if="reverseGroups.length" class="reverse-groups dense-reverse-groups">
            <div v-for="group in reverseGroups" :key="group.sourceTableKey" class="reverse-group-block">
              <div class="reverse-group-header compact-reverse-group-header">
                <div>
                  <strong>{{ targetTableLabel(group.sourceTableKey) }}</strong>
                  <p class="reverse-relation-label">{{ group.relationLabel }}</p>
                </div>
                <n-tag size="small" :bordered="false" type="info">{{ group.rows.length }} 条</n-tag>
              </div>
              <button
                v-for="candidate in group.rows"
                :key="candidate.rowKey"
                type="button"
                class="reverse-row-button dense-reverse-row-button"
                @click="store.navigateReverseReference(panel.id, group.sourceTableKey, candidate.rowKey, group.relationLabel)"
              >
                <strong>{{ reverseIdentityText(group.sourceTableKey, candidate) }}</strong>
                <span>{{ rowTitle(candidate, group.sourceTableKey) }}</span>
              </button>
            </div>
          </div>
          <n-empty v-else description="当前记录没有被其他表引用" />
        </section>
      </template>
      <n-empty v-else description="正在加载记录详情" />
    </n-spin>

    <div
      v-if="contextMenu"
      class="grid-context-menu"
      :style="{ left: `${contextMenu.x}px`, top: `${contextMenu.y}px` }"
    >
      <button type="button" class="grid-context-menu-item" @click="saveCellContent(contextMenu.columnName)">
        保存二进制到文件...
      </button>
    </div>

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
            <n-button type="primary" @click="saveCellContent(previewState.content.columnName)">保存到文件...</n-button>
          </div>
        </template>
      </n-spin>
    </n-modal>
  </n-card>
</template>
