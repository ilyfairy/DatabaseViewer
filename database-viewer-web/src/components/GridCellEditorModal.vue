<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { NAlert, NButton, NCheckbox, NInput, NModal, NSelect, NTag } from 'naive-ui';
import type { CellValue, ForeignKeyRef, TableColumn } from '../types/explorer';

const props = defineProps<{
  show: boolean
  tableLabel: string
  rowKey: string | null
  column: TableColumn | null
  foreignKey: ForeignKeyRef | null
  value: CellValue | undefined
  saving: boolean
}>();

const emit = defineEmits<{
  'update:show': [value: boolean]
  save: [payload: { valueKind: 'text' | 'binary' | 'null'; textValue?: string | null; base64Value?: string | null; setNull: boolean }]
}>();

const textValue = ref('');
const setNull = ref(false);
const binaryBase64Value = ref<string | null>(null);
const binaryFileName = ref('');
const fileInputRef = ref<HTMLInputElement | null>(null);

const normalizedType = computed(() => (props.column?.type ?? '').toLowerCase());
const isBinary = computed(() => normalizedType.value.includes('binary') || normalizedType.value.includes('blob') || normalizedType.value.includes('image'));
const isBoolean = computed(() => ['bit', 'bool', 'boolean'].includes(normalizedType.value));
const isMultilineText = computed(() => {
  if (isBinary.value || isBoolean.value) {
    return false;
  }

  return normalizedType.value.includes('char')
    || normalizedType.value.includes('text')
    || normalizedType.value.includes('json')
    || normalizedType.value.includes('xml');
});
const booleanOptions = computed(() => {
  const options = [
    { label: 'True', value: 'true' },
    { label: 'False', value: 'false' },
  ];
  if (props.column?.isNullable) {
    options.unshift({ label: 'NULL', value: '__NULL__' });
  }
  return options;
});
const currentBinarySize = computed(() => {
  if (typeof props.value !== 'string' || !props.value) {
    return 0;
  }

  const padding = props.value.endsWith('==') ? 2 : props.value.endsWith('=') ? 1 : 0;
  return Math.max(0, Math.floor(props.value.length * 0.75) - padding);
});
const previewState = computed(() => {
  const base64 = binaryBase64Value.value ?? (typeof props.value === 'string' ? props.value : null);
  if (!base64) {
    return { kind: 'empty' as const, mimeType: 'application/octet-stream', imageUrl: null, text: null, sizeBytes: 0 };
  }

  try {
    const bytes = bytesFromBase64(base64);
    const mimeType = imageMime(bytes);
    if (mimeType) {
      return { kind: 'image' as const, mimeType, imageUrl: `data:${mimeType};base64,${base64}`, text: null, sizeBytes: bytes.length };
    }

    const text = decodeText(bytes);
    if (text !== null) {
      return { kind: 'text' as const, mimeType: 'text/plain; charset=utf-8', imageUrl: null, text, sizeBytes: bytes.length };
    }

    return { kind: 'binary' as const, mimeType: 'application/octet-stream', imageUrl: null, text: null, sizeBytes: bytes.length };
  }
  catch {
    return { kind: 'binary' as const, mimeType: 'application/octet-stream', imageUrl: null, text: null, sizeBytes: 0 };
  }
});
const rowIdentityText = computed(() => {
  if (!props.rowKey) {
    return '-';
  }

  try {
    const decodedText = atob(props.rowKey);
    const parsed = JSON.parse(decodedText) as Record<string, unknown>;
    const entries = Object.entries(parsed);
    if (!entries.length) {
      return props.rowKey;
    }

    return entries.map(([key, value]) => `${key}=${formatIdentityValue(value)}`).join(', ');
  }
  catch {
    return props.rowKey;
  }
});
const warningText = computed(() => {
  if (!props.column) {
    return null;
  }

  if (props.column.isPrimaryKey && props.foreignKey) {
    return '当前列同时参与主键和外键。修改失败时通常是约束阻止了更新。';
  }

  if (props.column.isPrimaryKey) {
    return '当前列是主键。修改后该行标识可能变化，且若存在引用关系，数据库可能拒绝更新。';
  }

  if (props.foreignKey) {
    return `当前列是外键，必须指向有效记录：${props.foreignKey.targetColumn} in ${props.foreignKey.targetTableKey}`;
  }

  return null;
});

watch(() => [props.show, props.column?.name, props.value] as const, ([show]) => {
  if (!show) {
    return;
  }

  setNull.value = props.value === null;
  binaryBase64Value.value = typeof props.value === 'string' && isBinary.value ? props.value : null;
  binaryFileName.value = '';

  if (isBoolean.value) {
    if (props.value === null || props.value === undefined) {
      textValue.value = '__NULL__';
    }
    else {
      textValue.value = String(props.value).toLowerCase() === 'true' ? 'true' : 'false';
    }
    return;
  }

  textValue.value = props.value === null || props.value === undefined ? '' : String(props.value);
}, { immediate: true });

function bytesFromBase64(base64: string) {
  const raw = atob(base64);
  const bytes = new Uint8Array(raw.length);
  for (let index = 0; index < raw.length; index += 1) {
    bytes[index] = raw.charCodeAt(index);
  }
  return bytes;
}

function imageMime(bytes: Uint8Array) {
  if (bytes.length >= 8
    && bytes[0] === 0x89 && bytes[1] === 0x50 && bytes[2] === 0x4e && bytes[3] === 0x47
    && bytes[4] === 0x0d && bytes[5] === 0x0a && bytes[6] === 0x1a && bytes[7] === 0x0a) {
    return 'image/png';
  }

  if (bytes.length >= 3 && bytes[0] === 0xff && bytes[1] === 0xd8 && bytes[2] === 0xff) {
    return 'image/jpeg';
  }

  if (bytes.length >= 6) {
    const header = String.fromCharCode(...bytes.slice(0, 6));
    if (header === 'GIF87a' || header === 'GIF89a') {
      return 'image/gif';
    }
  }

  if (bytes.length >= 12) {
    const riff = String.fromCharCode(...bytes.slice(0, 4));
    const webp = String.fromCharCode(...bytes.slice(8, 12));
    if (riff === 'RIFF' && webp === 'WEBP') {
      return 'image/webp';
    }
  }

  return null;
}

function decodeText(bytes: Uint8Array) {
  try {
    const text = new TextDecoder('utf-8', { fatal: true }).decode(bytes);
    const controlCount = Array.from(text).filter((char) => {
      const code = char.charCodeAt(0);
      return code < 32 && char !== '\n' && char !== '\r' && char !== '\t';
    }).length;
    if (controlCount > Math.max(2, text.length * 0.02)) {
      return null;
    }

    return text;
  }
  catch {
    return null;
  }
}

function formatIdentityValue(value: unknown): string {
  if (value === null) {
    return 'NULL';
  }

  if (typeof value === 'string') {
    return value;
  }

  if (typeof value === 'number' || typeof value === 'boolean') {
    return String(value);
  }

  return JSON.stringify(value);
}

function close() {
  if (props.saving) {
    return;
  }

  emit('update:show', false);
}

function chooseBinaryFile() {
  fileInputRef.value?.click();
}

async function handleBinaryFileChange(event: Event) {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  if (!file) {
    return;
  }

  binaryFileName.value = file.name;
  const buffer = await file.arrayBuffer();
  const bytes = new Uint8Array(buffer);
  let binary = '';
  bytes.forEach((value) => {
    binary += String.fromCharCode(value);
  });
  binaryBase64Value.value = btoa(binary);
  setNull.value = false;
  input.value = '';
}

function clearBinarySelection() {
  binaryBase64Value.value = null;
  binaryFileName.value = '';
  if (props.column?.isNullable) {
    setNull.value = true;
  }
}

function submit() {
  if (!props.column) {
    return;
  }

  if (setNull.value) {
    emit('save', {
      valueKind: 'null',
      setNull: true,
    });
    return;
  }

  if (isBinary.value) {
    emit('save', {
      valueKind: 'binary',
      base64Value: binaryBase64Value.value,
      setNull: false,
    });
    return;
  }

  if (isBoolean.value && textValue.value === '__NULL__') {
    emit('save', {
      valueKind: 'null',
      setNull: true,
    });
    return;
  }

  emit('save', {
    valueKind: 'text',
    textValue: textValue.value,
    setNull: false,
  });
}
</script>

<template>
  <NModal
    :show="show"
    preset="card"
    class="grid-cell-editor-modal"
    style="width: min(820px, 94vw)"
    :mask-closable="false"
    :closable="!saving"
    :title="column ? `${tableLabel} · ${column.name}` : '编辑单元格'"
    @update:show="(value) => emit('update:show', value)"
  >
    <div v-if="column" class="grid-cell-editor-body">
      <div class="grid-cell-editor-meta">
        <NTag size="small" :bordered="false">{{ column.type || 'unknown' }}</NTag>
        <NTag v-if="column.isPrimaryKey" size="small" type="warning" :bordered="false">主键</NTag>
        <NTag v-if="foreignKey" size="small" type="info" :bordered="false">外键</NTag>
        <NTag v-if="column.isNullable" size="small" :bordered="false">可空</NTag>
        <NTag v-if="column.maxLength" size="small" :bordered="false">max {{ column.maxLength }}</NTag>
      </div>

      <NAlert v-if="warningText" type="warning" :show-icon="false" class="panel-inline-alert">
        {{ warningText }}
      </NAlert>

      <div class="grid-cell-editor-row">
        <label class="grid-cell-editor-label">当前行</label>
        <code class="grid-cell-editor-rowkey">{{ rowIdentityText }}</code>
      </div>

      <div v-if="column.isNullable" class="grid-cell-editor-null-toggle">
        <NCheckbox v-model:checked="setNull" :disabled="saving">将该值设为 NULL</NCheckbox>
      </div>

      <template v-if="isBinary && !setNull">
        <div class="grid-cell-editor-binary-toolbar">
          <NButton size="small" type="primary" :disabled="saving" @click="chooseBinaryFile">选择文件替换</NButton>
          <NButton size="small" tertiary :disabled="saving" @click="clearBinarySelection">清空二进制</NButton>
          <input ref="fileInputRef" type="file" class="grid-cell-editor-file-input" @change="handleBinaryFileChange" />
        </div>
        <div class="grid-cell-editor-binary-meta">
          <span v-if="binaryFileName">已选择 {{ binaryFileName }}</span>
          <span v-else-if="typeof value === 'string' && value">当前内容 {{ currentBinarySize }} B</span>
          <span v-else>当前内容为空</span>
        </div>
        <div v-if="previewState.kind === 'image' && previewState.imageUrl" class="binary-preview-body">
          <img :src="previewState.imageUrl" alt="binary preview" class="binary-preview-image" />
        </div>
        <pre v-else-if="previewState.kind === 'text' && previewState.text" class="binary-preview-text">{{ previewState.text }}</pre>
        <div v-else class="binary-preview-empty">该内容不是可直接预览的图片或文本，保存后会按二进制写入数据库。</div>
      </template>

      <template v-else-if="isBoolean && !setNull">
        <div class="grid-cell-editor-row">
          <label class="grid-cell-editor-label">新值</label>
          <NSelect v-model:value="textValue" :options="booleanOptions" :disabled="saving" class="grid-cell-editor-boolean" />
        </div>
      </template>

      <template v-else-if="!setNull">
        <div class="grid-cell-editor-row grid-cell-editor-row-stacked">
          <label class="grid-cell-editor-label">新值</label>
          <NInput
            v-if="isMultilineText"
            v-model:value="textValue"
            type="textarea"
            :autosize="{ minRows: 8, maxRows: 20 }"
            class="grid-cell-editor-textarea"
            :disabled="saving"
            placeholder="直接输入原始文本，换行和特殊字符会按原样提交"
          />
          <NInput
            v-else
            v-model:value="textValue"
            :disabled="saving"
            placeholder="输入新的字段值"
          />
          <div class="grid-cell-editor-help">
            <span v-if="isMultilineText">多行文本会按原样提交，不会自动转义或折叠换行。</span>
            <span v-else>提交时会由服务端按列类型校验并转换。</span>
          </div>
        </div>
      </template>

      <div class="grid-cell-editor-actions">
        <NButton :disabled="saving" @click="close">取消</NButton>
        <NButton type="primary" :loading="saving" @click="submit">保存修改</NButton>
      </div>
    </div>
  </NModal>
</template>

<style scoped lang="scss">
.grid-cell-editor-body {
  display: flex;
  flex-direction: column;
  gap: $gap-2xl;
}

.grid-cell-editor-meta {
  display: flex;
  flex-wrap: wrap;
  gap: $gap-lg;
}

.grid-cell-editor-row {
  display: flex;
  align-items: center;
  gap: $gap-2xl;

  &-stacked {
    align-items: stretch;
    flex-direction: column;
    gap: $gap-lg;
  }
}

.grid-cell-editor-label {
  min-width: 72px;
  color: $color-text-tertiary;
  font-size: $font-size-md;
  font-weight: 600;
}

.grid-cell-editor-rowkey {
  display: block;
  max-width: 100%;
  padding: $gap-lg $gap-xl;
  overflow: auto;
  border-radius: var(--radius-md);
  background: rgba(248, 250, 252, 0.95);
  color: $color-text-primary;
  font-family: $font-family-mono;
  font-size: $font-size-md;
}

.grid-cell-editor-null-toggle {
  margin-top: -4px;
}

.grid-cell-editor-textarea :deep(textarea) {
  font-family: $font-family-mono;
  line-height: 1.55;
}

.grid-cell-editor-help {
  color: $color-text-secondary;
  font-size: $font-size-md;
}

.grid-cell-editor-binary-toolbar {
  display: flex;
  gap: $gap-lg;
  flex-wrap: wrap;
}

.grid-cell-editor-binary-meta {
  color: $color-text-tertiary;
  font-size: $font-size-md;
}

.grid-cell-editor-file-input {
  display: none;
}

.grid-cell-editor-boolean {
  width: 220px;
}

.grid-cell-editor-actions {
  display: flex;
  justify-content: flex-end;
  gap: $gap-xl;
  margin-top: $gap-sm;
}
</style>
