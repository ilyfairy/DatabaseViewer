<script setup lang="ts">
import { computed } from 'vue'
import { NButton, NInput, NSelect, NTag } from 'naive-ui'
import { useExplorerStore } from '../stores/explorer'

const store = useExplorerStore()
const activeTable = computed(() => store.activeTable)
const searchColumnOptions = computed(() => {
  if (!activeTable.value) {
    return []
  }

  return store.getSearchableColumns(activeTable.value.key).map((column) => ({
    label: `${column.name} (${column.type})`,
    value: column.name,
  }))
})
const activeStats = computed(() => {
  if (!activeTable.value) {
    return { rowCount: 0, foreignKeyCount: 0, reverseCount: 0 }
  }

  return store.tableStats(activeTable.value.key)
})
</script>

<template>
  <div class="workspace-header">
    <div class="workspace-toolbar compact-panel">
      <div class="workspace-toolbar-main">
        <div class="workspace-actions">
          <n-input
            v-model:value="store.globalSearch"
            size="small"
            class="workspace-search-input"
            placeholder="搜索当前主表（服务端）"
            @keyup.enter="store.applyActiveTableSearch()"
          />
          <n-select
            v-model:value="store.searchColumns"
            class="workspace-search-select"
            size="small"
            multiple
            filterable
            max-tag-count="responsive"
            :options="searchColumnOptions"
            placeholder="指定列（留空=全部列）"
          />
          <n-button size="small" tertiary type="primary" :disabled="!activeTable" @click="store.applyActiveTableSearch()">搜索</n-button>
          <n-button size="small" tertiary :disabled="!activeTable" @click="store.clearActiveTableSearch()">清除筛选</n-button>
        </div>
        <div class="workspace-stats" v-if="activeTable">
          <n-tag size="small" :bordered="false" type="success">{{ activeTable.schema ? `${activeTable.schema}.${activeTable.name}` : activeTable.name }}</n-tag>
          <n-tag size="small" :bordered="false" type="default">{{ activeStats.rowCount }} 行</n-tag>
          <n-tag size="small" :bordered="false" type="info">{{ activeStats.foreignKeyCount }} FK</n-tag>
          <n-tag size="small" :bordered="false" type="warning">{{ activeStats.reverseCount }} 反向引用</n-tag>
        </div>
        <div class="workspace-stats" v-else>
          <n-tag size="small" :bordered="false" type="default">{{ activeStats.rowCount }} 行</n-tag>
          <n-tag size="small" :bordered="false" type="info">{{ activeStats.foreignKeyCount }} FK</n-tag>
          <n-tag size="small" :bordered="false" type="warning">{{ activeStats.reverseCount }} 反向引用</n-tag>
        </div>
      </div>
    </div>
  </div>
</template>
