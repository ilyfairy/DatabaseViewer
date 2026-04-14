<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { NDropdown } from 'naive-ui';
import type { DropdownOption } from 'naive-ui';

const props = defineProps<{
  show: boolean;
  x: number;
  y: number;
  options: DropdownOption[];
}>();

const emit = defineEmits<{
  'update:show': [value: boolean];
  select: [key: string | number, option: DropdownOption];
}>();

const lastVisiblePosition = ref({
  x: props.x,
  y: props.y,
});

watch(
  () => [props.show, props.x, props.y] as const,
  ([show, x, y]) => {
    if (!show) {
      return;
    }

    // 关闭时父组件常会先把状态清空成 null，模板里的 x/y 会短暂回落到 0。
    // 这里保留上一次可见坐标，避免 Dropdown 退场前闪到左上角。
    lastVisiblePosition.value = { x, y };
  },
  { immediate: true },
);

const mergedX = computed(() => props.show ? props.x : lastVisiblePosition.value.x);
const mergedY = computed(() => props.show ? props.y : lastVisiblePosition.value.y);

function handleUpdateShow(value: boolean) {
  emit('update:show', value);
}

function handleClickoutside() {
  emit('update:show', false);
}

function handleSelect(key: string | number, option: DropdownOption) {
  emit('select', key, option);
  emit('update:show', false);
}
</script>

<template>
  <NDropdown
    :show="props.show"
    trigger="manual"
    placement="bottom-start"
    :x="mergedX"
    :y="mergedY"
    :options="props.options"
    :show-arrow="false"
    @update:show="handleUpdateShow"
    @clickoutside="handleClickoutside"
    @select="handleSelect"
  />
</template>