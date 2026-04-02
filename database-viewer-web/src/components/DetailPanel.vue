<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue';
import { NAlert, NButton, NCard, NEmpty, NModal, NSpin, NTag } from 'naive-ui';
import RelationGraph from './RelationGraph.vue';
import { useExplorerStore } from '../stores/explorer';
import type { CellContentPreview, ExplorerDetailPanel, ReverseReferenceRow, TableColumn } from '../types/explorer';

const props = defineProps<{ panel: ExplorerDetailPanel; chainPanels?: ExplorerDetailPanel[] }>();
const store = useExplorerStore();
const table = computed(() => store.getTable(props.panel.tableKey));
const record = computed(() => store.getPanelRecord(props.panel));
const detail = computed(() => {
  if (!table.value || !record.value) {
    return null;
  }

  return {
    table: table.value,
    row: record.value.row,
    primaryKeys: record.value.primaryKeys,
    columns: record.value.columns,
    foreignKeys: record.value.foreignKeys,
    reverseReferences: record.value.reverseReferences,
  };
});
const reverseGroups = computed(() => store.getReverseReferences(props.panel));
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
const contextMenu = ref<{
  show: boolean
  x: number
  y: number
  columnName: string
} | null>(null);
const outgoingRelations = computed(() => {
  if (!detail.value) {
    return [];
  }

  return detail.value.foreignKeys
    .map((fk) => {
      const column = detail.value?.columns.find((entry) => entry.name === fk.sourceColumn);
      const value = detail.value?.row[fk.sourceColumn];
      return {
        fk,
        column,
        value,
      };
    })
    .filter((entry): entry is { fk: typeof entry.fk; column: TableColumn; value: string | number | boolean } => !!entry.column && entry.value !== null && entry.value !== '');
});
const plainColumns = computed(() => {
  if (!detail.value) {
    return [];
  }

  const currentDetail = detail.value;
  return currentDetail.columns.filter((column) => {
    const isPrimary = currentDetail.primaryKeys.includes(column.name);
    const isForeign = !!foreignKeyForColumn(currentDetail.table.key, column.name);
    return !isPrimary && !isForeign;
  });
});

function displayValue(value: unknown) {
  if (value === '') {
    return '空';
  }

  return store.formatValue((value as string | number | boolean | null | undefined) ?? null);
}

function displayFieldValue(columnName: string, columnType: string, value: unknown) {
  if (value === '') {
    return '空';
  }

  return store.formatFieldValue(columnName, columnType, (value as string | number | boolean | null | undefined) ?? null);
}

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

const previewImageUrl = computed(() => {
  const content = previewState.value.content;
  if (!content?.base64Data || content.kind !== 'image') {
    return null;
  }

  return `data:${content.mimeType};base64,${content.base64Data}`;
});

function foreignKeyForColumn(tableKey: string, columnName: string) {
  return store.getForeignKey(tableKey, columnName);
}

async function handleGraphSelect(data: {
  kind: 'current' | 'ancestor' | 'incoming' | 'outgoing'
  panelId: string
  sourceTableKey?: string
  rowKey?: string
  columnName?: string
}) {
  if (data.kind === 'ancestor') {
    store.focusDetailPanel(data.panelId);
    return;
  }

  if (data.kind === 'current') {
    return;
  }

  if (data.kind === 'incoming' && data.sourceTableKey && data.rowKey) {
    await store.navigateReverseReference(props.panel.id, data.sourceTableKey, data.rowKey, '关系图导航');
    return;
  }

  if (data.kind === 'outgoing' && data.columnName) {
    await store.navigateForeignKeyFromDetail(props.panel.id, props.panel.tableKey, props.panel.rowKey, data.columnName);
  }
}

function detailIdentityText() {
  if (!detail.value) {
    return '';
  }

  return detail.value.primaryKeys.map((key: string) => `${key}=${displayValue(detail.value?.row[key])}`).join(', ');
}

function reverseIdentityText(sourceTableKey: string, candidate: ReverseReferenceRow) {
  const sourceTable = store.getLoadedTable(sourceTableKey);
  if (sourceTable) {
    return sourceTable.primaryKeys.map((key) => `${key}=${displayValue(candidate.values[key])}`).join(', ');
  }

  return store.summarizeRowValues(candidate.values);
}

function rowTitle(candidate: ReverseReferenceRow, sourceTableKey: string) {
  const source = store.getLoadedTable(sourceTableKey);
  return source ? store.rowSummary(source, candidate.values) : store.summarizeRowValues(candidate.values);
}

function targetTableLabel(tableKey: string) {
  const candidate = store.getTable(tableKey);
  return candidate ? (candidate.schema ? `${candidate.schema}.${candidate.name}` : candidate.name) : tableKey;
}

function previewCacheKey(columnName: string) {
  return `${props.panel.rowKey}::${columnName}`;
}

async function ensureCellContent(columnName: string) {
  const key = previewCacheKey(columnName);
  if (previewCache.value[key]) {
    return previewCache.value[key];
  }

  const content = await store.fetchCellContent(props.panel.tableKey, props.panel.rowKey, columnName);
  previewCache.value = {
    ...previewCache.value,
    [key]: content,
  };
  return content;
}

async function openBinaryPreview(columnName: string) {
  try {
    const content = await ensureCellContent(columnName);
    if (content.kind === 'empty' || content.sizeBytes === 0) {
      return;
    }

    previewState.value = {
      show: true,
      loading: false,
      title: `${table.value?.name ?? props.panel.tableKey}.${columnName}`,
      content,
      error: null,
    };
  }
  catch (error) {
    previewState.value = {
      show: true,
      loading: false,
      title: `${table.value?.name ?? props.panel.tableKey}.${columnName}`,
      content: null,
      error: error instanceof Error ? error.message : '二进制内容读取失败',
    };
  }
}

function openBinaryContextMenu(event: MouseEvent, columnName: string) {
  contextMenu.value = {
    show: true,
    x: event.clientX,
    y: event.clientY,
    columnName,
  };
}

function closeContextMenu() {
  contextMenu.value = null;
}

function handleFieldClick(event: MouseEvent, columnName: string, columnType: string) {
  if (!isBinaryColumn(columnName, columnType)) {
    return;
  }

  event.stopPropagation();
  openBinaryPreview(columnName);
}

function handleFieldMouseDown(event: MouseEvent) {
  if (event.detail > 1) {
    event.preventDefault();
  }
}

function base64ToBytes(base64: string) {
  const raw = atob(base64);
  const bytes = new Uint8Array(raw.length);
  for (let index = 0; index < raw.length; index += 1) {
    bytes[index] = raw.charCodeAt(index);
  }
  return bytes;
}

async function saveCellContent(columnName: string) {
  closeContextMenu();
  const content = await ensureCellContent(columnName);
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

function handleWindowClick() {
  closeContextMenu();
}

function handleWindowContextMenu() {
  closeContextMenu();
}

function handleWindowBlur() {
  closeContextMenu();
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
</script>

<template>
  <NCard embedded class="workspace-panel detail-panel" :content-style="{ padding: '4px 6px 6px' }" :header-style="{ padding: '6px 6px 4px' }">
    <template #header>
      <div class="detail-header">
        <div class="detail-header-top">
          <div class="detail-kicker">记录详情</div>
          <div class="detail-header-actions">
            <NTag size="small" :bordered="false" type="warning">{{ panel.sourceLabel }}</NTag>
            <NButton size="small" tertiary @click="store.closeDetailPanel(panel.id)">关闭</NButton>
          </div>
        </div>
        <h3>{{ table?.schema ? `${table.schema}.${table.name}` : table?.name }}</h3>
        <div class="detail-identity">{{ detailIdentityText() }}</div>
        <div class="detail-chip-row">
          <NTag size="small" :bordered="false" type="info">{{ outgoingRelations.length }} 外键</NTag>
          <NTag size="small" :bordered="false" type="warning">{{ reverseGroups.length }} 入向关系</NTag>
        </div>
      </div>
    </template>

    <NAlert v-if="store.getRecordError(panel.id)" type="warning" :show-icon="false" class="panel-inline-alert">
      {{ store.getRecordError(panel.id) }}
    </NAlert>

    <NSpin :show="store.isRecordLoading(panel.id)">
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
                <NTag size="small" :bordered="false" type="success">{{ targetTableLabel(relation.fk.targetTableKey) }}</NTag>
              </div>
              <div class="relation-card-value">{{ displayFieldValue(relation.column!.name, relation.column!.type, relation.value) }}</div>
              <div class="relation-card-meta">跳转到 {{ relation.fk.targetColumn }}</div>
            </button>
          </div>
          <NEmpty v-else description="当前记录没有可用的外键引用" />
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
                <NTag size="small" :bordered="false" type="info">{{ group.rows.length }} 条</NTag>
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
          <NEmpty v-else description="当前记录没有被其他表引用" />
        </section>
      </template>
      <NEmpty v-else description="正在加载记录详情" />
    </NSpin>

    <div
      v-if="contextMenu"
      class="grid-context-menu"
      :style="{ left: `${contextMenu.x}px`, top: `${contextMenu.y}px` }"
    >
      <button type="button" class="grid-context-menu-item" @click="saveCellContent(contextMenu.columnName)">
        保存二进制到文件...
      </button>
    </div>

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
            <NButton type="primary" @click="saveCellContent(previewState.content.columnName)">保存到文件...</NButton>
          </div>
        </template>
      </NSpin>
    </NModal>
  </NCard>
</template>

<style scoped lang="scss">
// ── Detail panel ──
.detail-panel {
  min-width: 0;
  height: fit-content;
  border-left: none;
  border-radius: 0;
}

.detail-header {
  display: grid;
  gap: 3px;

  h3 {
    font-size: $font-size-xl;
    margin: 0;
    line-height: 1.1;
  }
}

.detail-header-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: $gap-md;
}

.detail-header-actions {
  display: flex;
  align-items: center;
  gap: $gap-md;
  flex: 0 0 auto;
}

.detail-kicker {
  color: $color-accent-amber;
  font-size: $font-size-xs;
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}

.detail-identity {
  color: $color-text-tertiary;
  font-size: 10.5px;
  font-family: $font-family-mono;
  overflow-wrap: anywhere;
  line-height: 1.3;
}

.detail-chip-row {
  display: flex;
  gap: $gap-md;
  flex-wrap: wrap;
}

.detail-section {
  display: grid;
  gap: $gap-md;
  margin-top: $gap-sm;
}

.detail-section-title {
  color: $color-text-primary;
  font-size: $font-size-base;
  font-weight: 700;
}

// ── Detail fields ──
.detail-fields {
  display: grid;
  gap: $gap-xs;
}

.detail-field-row {
  display: grid;
  grid-template-columns: 64px minmax(0, 1fr);
  gap: 3px;
  align-items: center;
}

.detail-field-label {
  display: flex;
  align-items: center;
  min-height: 20px;
  padding: 1px 5px;
  border-radius: var(--radius-sm);
  background: #eef2f7;
  color: $color-text-heading;
  font-size: 9.5px;
  font-weight: 700;
}

.detail-field-value {
  display: flex;
  align-items: center;
  min-width: 0;
  min-height: 20px;
  padding: 1px $gap-md;
  border-radius: var(--radius-sm);
  background: $color-surface-white;
  border: 1px solid $color-border-medium;
  color: $color-text-primary;
  font-size: 10.5px;
  line-height: 1.15;
  word-break: break-word;
  white-space: normal;

  &-binary {
    cursor: pointer;
    transition: background 140ms ease, color 140ms ease, border-color 140ms ease;

    &:hover {
      color: $color-accent-sky;
      background: rgba(240, 249, 255, 0.72);
      border-color: rgba(125, 211, 252, 0.6);
    }
  }
}

// ── Relation cards ──
.relation-list {
  display: grid;
  gap: $gap-md;
}

.compact-relation-list {
  gap: $gap-sm;
}

.relation-card {
  display: grid;
  gap: $gap-sm;
  width: 100%;
  border: 1px solid rgba(15, 118, 110, 0.14);
  background: rgba(240, 253, 250, 0.78);
  padding: $gap-lg;
  border-radius: var(--radius-md);
  text-align: left;
  cursor: pointer;
}

.compact-relation-card {
  padding: 6px 7px;
  border-radius: var(--radius-sm);
}

.relation-card-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 6px;
}

.compact-relation-card-top {
  gap: 4px;
}

.relation-card-value {
  color: $color-text-primary;
  font-size: $font-size-base;
  font-weight: 600;
  word-break: break-word;
  min-height: 18px;
}

.relation-card-meta {
  color: $color-text-secondary;
  font-size: $font-size-sm;
}

// ── Reverse groups ──
.reverse-groups {
  display: grid;
  gap: 6px;
  margin-top: 4px;
}

.reverse-group-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.reverse-group-card {
  border-radius: var(--radius-xl);
  background: rgba(237, 246, 244, 0.8);
}

.reverse-group-block {
  display: grid;
  gap: 4px;
  padding: 8px;
  border-radius: var(--radius-md);
  background: rgba(239, 246, 255, 0.72);
  border: 1px solid rgba(59, 130, 246, 0.08);
}

.compact-reverse-group-header {
  align-items: start;
}

.reverse-relation-label {
  color: $color-text-secondary;
  font-size: $font-size-sm;
}

.reverse-row-button {
  display: grid;
  gap: 3px;
  width: 100%;
  min-width: 0;
  border: 0;
  background: transparent;
  padding: 9px 10px;
  border-radius: var(--radius-lg);
  background: rgba(255, 255, 255, 0.92);
  text-align: left;
  cursor: pointer;
  margin-top: 8px;

  strong,
  span {
    word-break: break-word;
    white-space: normal;
  }

  &:hover {
    transform: translateY(-2px);
    box-shadow: 0 12px 24px rgba(15, 23, 42, 0.08);
  }

  span {
    margin: $gap-sm 0 0;
    color: #6b7280;
    font-size: $font-size-base;
  }
}

.dense-reverse-row-button {
  padding: 7px 8px;
  margin-top: 0;
  border-radius: var(--radius-md);
}
</style>
