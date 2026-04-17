<script setup lang="ts">
import type { Component } from 'vue';
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { IconFlask2, IconListDetails, IconSchema, IconSettings, IconSql, IconTable, IconTableOptions } from '@tabler/icons-vue';
import { Close } from '@vicons/carbon';
import { NButton, NIcon } from 'naive-ui';
import { useExplorerStore } from '../stores/explorer';
import { attachSmoothHorizontalWheelScroll } from '../lib/smooth-horizontal-wheel-scroll';
import type { WorkspaceTab } from '../types/explorer';

const store = useExplorerStore();
const tabs = computed(() => store.workspaceTabs);
const activeTabId = computed(() => store.activeTabId);
const draggingTabId = ref<string | null>(null);
const tabsListRef = ref<HTMLDivElement | null>(null);
let detachTabsWheelScroll: (() => void) | null = null;
// 这里只保留最小视觉留白；真正的修复点在共享滚动器的整像素吸附。
const tabScrollEdgePadding = 4;

function bindTabsWheelScroll() {
  detachTabsWheelScroll?.();

  if (!tabsListRef.value) {
    detachTabsWheelScroll = null;
    return;
  }

  detachTabsWheelScroll = attachSmoothHorizontalWheelScroll(tabsListRef.value);
}

/**
 * 让激活 tab 始终完整落在可视区域内，并保留一个很小的视觉留白。
 *
 * tab 自带边框和圆角，只按原始可视区域计算时，边缘看起来仍然像被裁了一点。
 */
function scrollActiveTabIntoView(behavior: ScrollBehavior = 'smooth') {
  if (!tabsListRef.value) {
    return;
  }

  const tabsListElement = tabsListRef.value;
  const activeTabElement = tabsListElement.querySelector('.workspace-tab-chip-active');
  if (!(activeTabElement instanceof HTMLElement)) {
    return;
  }

  const listRect = tabsListElement.getBoundingClientRect();
  const activeRect = activeTabElement.getBoundingClientRect();
  const visibleLeft = listRect.left + tabScrollEdgePadding;
  const visibleRight = listRect.right - tabScrollEdgePadding;
  const leftOverflow = activeRect.left - visibleLeft;
  const rightOverflow = activeRect.right - visibleRight;

  if (leftOverflow < 0) {
    const targetScrollLeft = Math.max(0, Math.round(tabsListElement.scrollLeft + leftOverflow));
    tabsListElement.scrollTo({ left: targetScrollLeft, behavior });
    return;
  }

  if (rightOverflow > 0) {
    const targetScrollLeft = Math.max(0, Math.round(tabsListElement.scrollLeft + rightOverflow));
    tabsListElement.scrollTo({ left: targetScrollLeft, behavior });
  }
}

/** 平滑滚动结束后再做一次无动画校正，去掉子像素裁边。 */
function finalizeActiveTabVisibility() {
  window.setTimeout(() => {
    scrollActiveTabIntoView('auto');
  }, 180);
}

function tabTitle(tab: typeof tabs.value[number]) {
  return tab.type === 'sql' && tab.filePath
    ? tab.filePath
    : store.getWorkspaceTabLabel(tab);
}

function tabKindIcon(tab: WorkspaceTab): Component {
  switch (tab.type) {
    case 'table':
      return IconTable;
    case 'design':
      return IconTableOptions;
    case 'graph':
      return IconSchema;
    case 'catalog':
      return IconListDetails;
    case 'mock':
      return IconFlask2;
    case 'settings':
      return IconSettings;
    case 'sql':
    default:
      return IconSql;
  }
}

/** 鼠标中键关闭 tab（兼容 auxclick 和 mouseup） */
function handleTabAuxClick(event: MouseEvent, tabId: string) {
  if (event.button !== 1) {
    return;
  }

  event.preventDefault();
  store.closeWorkspaceTab(tabId);
}


function handleTabDragStart(event: DragEvent, tabId: string) {
  draggingTabId.value = tabId;
  event.dataTransfer?.setData('text/plain', tabId);
  if (event.dataTransfer) {
    event.dataTransfer.effectAllowed = 'move';
  }
}

function handleTabDrop(targetTabId: string) {
  if (!draggingTabId.value || draggingTabId.value === targetTabId) {
    draggingTabId.value = null;
    return;
  }

  store.moveWorkspaceTab(draggingTabId.value, targetTabId);
  draggingTabId.value = null;
}

function handleTabDragEnd() {
  draggingTabId.value = null;
}

onMounted(() => {
  bindTabsWheelScroll();
});

onBeforeUnmount(() => {
  detachTabsWheelScroll?.();
  detachTabsWheelScroll = null;
});

watch(() => [tabs.value.length, activeTabId.value], async () => {
  await nextTick();
  bindTabsWheelScroll();
  scrollActiveTabIntoView();
  finalizeActiveTabVisibility();
}, { immediate: true });
</script>

<template>
  <div class="workspace-tabs-shell compact-panel">
    <div ref="tabsListRef" class="workspace-tabs-list">
      <button
        v-for="tab in tabs"
        :key="tab.id"
        type="button"
        class="workspace-tab-chip"
        :title="tabTitle(tab)"
        :class="{ 'workspace-tab-chip-active': activeTabId === tab.id, 'workspace-tab-chip-dragging': draggingTabId === tab.id }"
        draggable="true"
        @click="store.activateWorkspaceTab(tab.id)"
        @auxclick="handleTabAuxClick($event, tab.id)"
        @mouseup="handleTabAuxClick($event, tab.id)"
        @dragstart="handleTabDragStart($event, tab.id)"
        @dragover.prevent
        @drop.prevent="handleTabDrop(tab.id)"
        @dragend="handleTabDragEnd"
      >
        <span class="workspace-tab-chip-kind"><NIcon size="14"><component :is="tabKindIcon(tab)" /></NIcon></span>
        <span v-if="(tab.type === 'sql' && store.isSqlTabDirty(tab.id)) || (tab.type === 'design' && store.isCreateDesignTabDirty(tab.id)) || (tab.type === 'settings' && store.isSettingsDirty)" class="workspace-tab-chip-dirty" title="未保存">●</span>
        <span class="workspace-tab-chip-label">{{ store.getWorkspaceTabLabel(tab) }}</span>
        <span class="workspace-tab-chip-close" @click.stop="store.closeWorkspaceTab(tab.id)">
          <NIcon size="12"><Close /></NIcon>
        </span>
      </button>
      <div v-if="!tabs.length" class="workspace-tabs-empty">打开表或新建 SQL 标签页开始工作</div>
    </div>
    <div class="workspace-tabs-actions">
      <NButton size="small" tertiary type="primary" @click="store.openSqlTab()">新建 SQL</NButton>
      <NButton size="small" tertiary @click="store.openSettingsTab()">
        <NIcon size="14"><IconSettings /></NIcon>
      </NButton>
    </div>
  </div>
</template>

<style scoped lang="scss">
.workspace-tabs-shell {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: $gap-xs;
  background: $color-bg-panel;
  border: 1px solid $color-border-subtle;
  border-radius: var(--radius-lg);
}

.workspace-tabs-list {
  display: flex;
  align-items: stretch;
  gap: $gap-sm;
  min-width: 0;
  flex: 1;
  overflow-x: auto;
  padding: 2px 4px;
  box-sizing: border-box;
  scroll-padding-inline: 4px;

  // 隐藏滚动条，保留鼠标滚动功能
  scrollbar-width: none;

  &::-webkit-scrollbar {
    display: none;
  }
}

.workspace-tabs-actions {
  flex: 0 0 auto;
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: $gap-sm;
  padding-right: $gap-sm;
}

.workspace-tab-chip {
  display: inline-flex;
  align-items: center;
  gap: 0;
  flex: 0 0 auto;
  max-width: 220px;
  min-height: 28px;
  padding: 0;
  border: 1px solid rgba(148, 163, 184, 0.2);
  border-radius: calc(var(--radius-lg) - 3px);
  background: $color-bg-subtle;
  color: $color-text-heading;
  cursor: pointer;
  transition: background 160ms ease, border-color 160ms ease, box-shadow 160ms ease;

  &:hover {
    background: rgba(241, 245, 249, 0.98);
    border-color: rgba(59, 130, 246, 0.24);
    box-shadow: 0 6px 14px rgba(15, 23, 42, 0.06);
  }

  &-dragging {
    opacity: 0.56;
    box-shadow: none;
    background: rgba(148, 163, 184, 0.34);
  }

  &-active {
    background: rgba(219, 234, 254, 0.7);
    border-color: rgba(37, 99, 235, 0.34);
  }

  &-kind {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    flex: 0 0 auto;
    min-width: 20px;
    align-self: stretch;
    padding: 0 4px;
    border-radius: calc(var(--radius-lg) - 4px) 0 0 calc(var(--radius-lg) - 4px);
    background: rgba(14, 165, 233, 0.12);
    color: $color-accent-sky;
    font-size: $font-size-xs;
    font-weight: 800;
    line-height: 1;
    white-space: nowrap;

    :deep(svg) {
      width: 13px;
      height: 13px;
    }
  }

  &-label {
    display: inline-flex;
    align-items: center;
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    padding: 0 $gap-sm;
    font-size: $font-size-sm;
    font-weight: 700;
  }

  &-close {
    flex: 0 0 auto;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: 18px;
    height: 18px;
    margin-right: 1px;
    border-radius: var(--radius-pill);
    color: $color-text-secondary;

    &:hover {
      background: rgba(226, 232, 240, 0.9);
      color: $color-text-primary;
    }

    &-glyph {
      display: block;
      font-size: 11px;
      line-height: 1;
      transform: translateY(-0.5px);
    }
  }

  &-dirty {
    flex: 0 0 auto;
    color: $color-accent-amber-light;
    font-size: $font-size-sm;
    line-height: 1;
  }
}

.workspace-tabs-empty {
  display: flex;
  align-items: center;
  min-height: 28px;
  color: $color-text-secondary;
  font-size: $font-size-base;
  padding: 0 $gap-sm;
}
</style>
