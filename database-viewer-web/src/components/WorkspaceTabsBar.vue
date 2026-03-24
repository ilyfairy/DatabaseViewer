<script setup lang="ts">
import { computed, ref } from 'vue'
import { NButton } from 'naive-ui'
import { useExplorerStore } from '../stores/explorer'

const store = useExplorerStore()
const tabs = computed(() => store.workspaceTabs)
const activeTabId = computed(() => store.activeTabId)
const draggingTabId = ref<string | null>(null)

function handleTabAuxClick(event: MouseEvent, tabId: string) {
  if (event.button !== 1) {
    return
  }

  event.preventDefault()
  store.closeWorkspaceTab(tabId)
}

function handleTabDragStart(event: DragEvent, tabId: string) {
  draggingTabId.value = tabId
  event.dataTransfer?.setData('text/plain', tabId)
  if (event.dataTransfer) {
    event.dataTransfer.effectAllowed = 'move'
  }
}

function handleTabDrop(targetTabId: string) {
  if (!draggingTabId.value || draggingTabId.value === targetTabId) {
    draggingTabId.value = null
    return
  }

  store.moveWorkspaceTab(draggingTabId.value, targetTabId)
  draggingTabId.value = null
}

function handleTabDragEnd() {
  draggingTabId.value = null
}
</script>

<template>
  <div class="workspace-tabs-shell compact-panel">
    <div class="workspace-tabs-list">
      <button
        v-for="tab in tabs"
        :key="tab.id"
        type="button"
        class="workspace-tab-chip"
        :class="{ 'workspace-tab-chip-active': activeTabId === tab.id, 'workspace-tab-chip-dragging': draggingTabId === tab.id }"
        draggable="true"
        @click="store.activateWorkspaceTab(tab.id)"
        @auxclick="handleTabAuxClick($event, tab.id)"
        @dragstart="handleTabDragStart($event, tab.id)"
        @dragover.prevent
        @drop.prevent="handleTabDrop(tab.id)"
        @dragend="handleTabDragEnd"
      >
        <span class="workspace-tab-chip-kind">{{ tab.type === 'table' ? '表' : tab.type === 'graph' ? '图' : 'SQL' }}</span>
        <span class="workspace-tab-chip-label">{{ store.getWorkspaceTabLabel(tab) }}</span>
        <span class="workspace-tab-chip-close" @click.stop="store.closeWorkspaceTab(tab.id)">
          <span class="workspace-tab-chip-close-glyph">✕</span>
        </span>
      </button>
      <div v-if="!tabs.length" class="workspace-tabs-empty">打开表或新建 SQL 标签页开始工作</div>
    </div>
    <div class="workspace-tabs-actions">
      <n-button size="small" tertiary type="primary" @click="store.openSqlTab()">新建 SQL</n-button>
    </div>
  </div>
</template>
