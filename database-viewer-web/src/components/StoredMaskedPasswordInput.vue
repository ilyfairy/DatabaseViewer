<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { NInput } from 'naive-ui';

defineOptions({
  inheritAttrs: false,
});

const props = withDefaults(defineProps<{
  modelValue: string;
  hasStoredValue: boolean;
  storedMask?: string;
  disabled?: boolean;
  placeholder?: string;
  size?: 'tiny' | 'small' | 'medium' | 'large';
  showPasswordOn?: 'mousedown' | 'click';
}>(), {
  storedMask: '••••••••',
  disabled: false,
  placeholder: '',
  size: 'medium',
  showPasswordOn: 'click',
});

const emit = defineEmits<{
  (event: 'begin-edit'): void;
  (event: 'update:modelValue', value: string): void;
}>();

const isEditingStoredValue = ref(false);

watch(() => props.hasStoredValue, (value) => {
  if (value) {
    isEditingStoredValue.value = false;
  }
});

const displayValue = computed(() => {
  if (props.hasStoredValue && !isEditingStoredValue.value && !props.modelValue) {
    return props.storedMask;
  }

  return props.modelValue;
});

function beginStoredValueEdit(nextValue: string): void {
  if (!isEditingStoredValue.value) {
    isEditingStoredValue.value = true;
    emit('begin-edit');
  }

  emit('update:modelValue', nextValue);
}

function normalizeInitialEditValue(nextValue: string): string {
  if (!props.hasStoredValue || isEditingStoredValue.value) {
    return nextValue;
  }

  if (!nextValue || nextValue === props.storedMask) {
    return '';
  }

  if (nextValue.startsWith(props.storedMask)) {
    return nextValue.slice(props.storedMask.length);
  }

  if (props.storedMask.startsWith(nextValue)) {
    return '';
  }

  return nextValue;
}

function handleValueUpdate(nextValue: string): void {
  if (props.hasStoredValue && !isEditingStoredValue.value) {
    beginStoredValueEdit(normalizeInitialEditValue(nextValue));
    return;
  }

  emit('update:modelValue', nextValue);
}

function handleKeydown(event: KeyboardEvent): void {
  if (!props.hasStoredValue || isEditingStoredValue.value || event.isComposing) {
    return;
  }

  if (event.key === 'Backspace' || event.key === 'Delete') {
    event.preventDefault();
    beginStoredValueEdit('');
    return;
  }

  if (event.ctrlKey || event.metaKey || event.altKey) {
    return;
  }

  if (event.key.length === 1) {
    event.preventDefault();
    beginStoredValueEdit(event.key);
  }
}

function handlePaste(event: ClipboardEvent): void {
  if (!props.hasStoredValue || isEditingStoredValue.value) {
    return;
  }

  event.preventDefault();
  beginStoredValueEdit(event.clipboardData?.getData('text') ?? '');
}
</script>

<template>
  <NInput
    v-bind="$attrs"
    :value="displayValue"
    type="password"
    :size="size"
    :disabled="disabled"
    :placeholder="placeholder"
    :show-password-on="showPasswordOn"
    @keydown="handleKeydown"
    @paste="handlePaste"
    @update:value="handleValueUpdate"
  />
</template>