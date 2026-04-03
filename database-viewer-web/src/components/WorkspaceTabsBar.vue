<script setup lang="ts">
import { computed, ref } from 'vue';
import { NButton } from 'naive-ui';
import { useExplorerStore } from '../stores/explorer';

const store = useExplorerStore();
const tabs = computed(() => store.workspaceTabs);
const activeTabId = computed(() => store.activeTabId);
const draggingTabId = ref<string | null>(null);
const tabsListRef = ref<HTMLDivElement | null>(null);

/** 将竖向滚轮转换为横向滚动 */
function handleTabsWheel(event: WheelEvent) {
  if (!tabsListRef.value) {
    return;
  }

  if (event.deltaY !== 0) {
    event.preventDefault();
    tabsListRef.value.scrollLeft += event.deltaY;
  }
}

function tabTitle(tab: typeof tabs.value[number]) {
  return tab.type === 'sql' && tab.filePath
    ? tab.filePath
    : store.getWorkspaceTabLabel(tab);
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
</script>

<template>
  <div class="workspace-tabs-shell compact-panel">
    <div ref="tabsListRef" class="workspace-tabs-list" @wheel="handleTabsWheel">
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
        <span class="workspace-tab-chip-kind">{{ tab.type === 'table' ? '表' : tab.type === 'design' ? '设计' : tab.type === 'graph' ? '图' : 'SQL' }}</span>
        <span v-if="tab.type === 'sql' && store.isSqlTabDirty(tab.id)" class="workspace-tab-chip-dirty" title="未保存">●</span>
        <span class="workspace-tab-chip-label">{{ store.getWorkspaceTabLabel(tab) }}</span>
        <span class="workspace-tab-chip-close" @click.stop="store.closeWorkspaceTab(tab.id)">
          <span class="workspace-tab-chip-close-glyph">✕</span>
        </span>
      </button>
      <div v-if="!tabs.length" class="workspace-tabs-empty">打开表或新建 SQL 标签页开始工作</div>
    </div>
    <div class="workspace-tabs-actions">
      <NButton size="small" tertiary type="primary" @click="store.openSqlTab()">新建 SQL</NButton>
    </div>
  </div>
</template>

<style scoped lang="scss">
.workspace-tabs-shell {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: $gap-xs;
  padding: $gap-xs $gap-md;
  background: $color-bg-panel;
  border: 1px solid $color-border-subtle;
  border-radius: var(--radius-lg);
}

.workspace-tabs-list {
  display: flex;
  align-items: center;
  gap: $gap-sm;
  min-width: 0;
  flex: 1;
  overflow-x: auto;

  // 隐藏滚动条，保留鼠标滚动功能
  scrollbar-width: none;

  &::-webkit-scrollbar {
    display: none;
  }
}

.workspace-tabs-actions {
  flex: 0 0 auto;
}

.workspace-tab-chip {
  display: inline-flex;
  align-items: center;
  gap: 0;
  flex: 0 0 auto;
  max-width: 220px;
  padding: 0;
  border: 1px solid rgba(148, 163, 184, 0.2);
  border-radius: var(--radius-lg);
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
    padding: 0 $gap-sm;
    border-radius: calc(var(--radius-lg) - 1px) 0 0 calc(var(--radius-lg) - 1px);
    background: rgba(14, 165, 233, 0.12);
    color: $color-accent-sky;
    font-size: $font-size-xs;
    font-weight: 800;
    line-height: 1;
    white-space: nowrap;
  }

  &-label {
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    padding: $gap-xs $gap-sm;
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
  color: $color-text-secondary;
  font-size: $font-size-base;
  padding: 0 $gap-sm;
}
</style>
