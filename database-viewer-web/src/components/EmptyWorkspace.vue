<script setup lang="ts">
import { computed } from 'vue'
import { NButton, NCard, NTag } from 'naive-ui'
import { useExplorerStore } from '../stores/explorer'

const store = useExplorerStore()
const firstTable = computed(() => store.connections[0]?.databases[0]?.tables[0])
</script>

<template>
  <n-card embedded class="empty-workspace">
    <div class="empty-copy compact-empty-copy">
      <strong>从左侧打开一张表开始浏览。</strong>
      <span>主表会占据主要空间，详情和反向引用显示在右侧窄栏。</span>
    </div>
    <div class="empty-actions compact-empty-actions">
      <n-button v-if="firstTable" size="small" type="primary" @click="store.openTable(firstTable.key)">打开第一张表</n-button>
      <n-button size="small" tertiary @click="store.openSqlTab()">新建 SQL 标签页</n-button>
      <n-tag size="small" :bordered="false" type="success">本地 API 已接通</n-tag>
    </div>
  </n-card>
</template>
