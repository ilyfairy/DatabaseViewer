<script setup lang="ts">
import { NAlert, NCard } from 'naive-ui';
import { useExplorerStore } from '../stores/explorer';

const store = useExplorerStore();
</script>

<template>
  <NCard embedded class="empty-workspace">
    <div class="empty-welcome">
      <h2>欢迎使用 Database Viewer</h2>
    </div>
    <NAlert v-if="store.bootstrapError" type="warning" :show-icon="true">
      本地 API 未接通：{{ store.bootstrapError }}
    </NAlert>
  </NCard>
</template>

<style scoped lang="scss">
.empty-workspace {
  display: grid;
  gap: $gap-xl;
  padding: 24px;
  background: rgba(255, 255, 255, 0.76);
  border: 1px solid $color-border-subtle;
  border-radius: var(--radius-xl);
}

.empty-welcome {
  text-align: center;

  h2 {
    margin: 0;
    font-size: 16px;
    font-weight: 600;
    color: $color-text-secondary;
  }
}
</style>
