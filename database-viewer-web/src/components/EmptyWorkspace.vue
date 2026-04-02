<script setup lang="ts">
import { computed } from 'vue';
import { NButton, NCard, NTag } from 'naive-ui';
import { useExplorerStore } from '../stores/explorer';

const store = useExplorerStore();
const firstTable = computed(() => store.connections[0]?.databases[0]?.tables[0]);
</script>

<template>
  <NCard embedded class="empty-workspace">
    <div class="empty-copy compact-empty-copy">
      <strong>从左侧打开一张表开始浏览。</strong>
      <span>主表会占据主要空间，详情和反向引用显示在右侧窄栏。</span>
    </div>
    <div class="empty-actions compact-empty-actions">
      <NButton v-if="firstTable" size="small" type="primary" @click="store.openTable(firstTable.key)">打开第一张表</NButton>
      <NButton size="small" tertiary @click="store.openSqlTab()">新建 SQL 标签页</NButton>
      <NTag size="small" :bordered="false" type="success">本地 API 已接通</NTag>
    </div>
  </NCard>
</template>

<style scoped lang="scss">
.empty-workspace {
  display: grid;
  gap: $gap-2xl;
  padding: 18px;
  background: rgba(255, 255, 255, 0.76);
  border: 1px solid $color-border-subtle;
  border-radius: var(--radius-xl);
}

.empty-copy,
.empty-actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: $gap-2xl;
}

.compact-empty-copy,
.compact-empty-actions {
  display: flex;
  align-items: center;
  gap: $gap-xl;
  flex-wrap: wrap;
}

.empty-grid {
  display: grid;
  gap: $gap-md;
}

.eyebrow {
  color: $color-accent-amber;
  font-size: $font-size-xs;
  font-weight: 800;
  letter-spacing: 0.12em;
  text-transform: uppercase;
}
</style>
