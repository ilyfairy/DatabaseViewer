<script setup lang="ts">
import { computed, watch } from 'vue';
import { NAlert, NButton, NCode, NEmpty, NSpin } from 'naive-ui';
import { useExplorerStore } from '../stores/explorer';
import type { CatalogObjectWorkspaceTab } from '../types/explorer';

const props = defineProps<{
  tab: CatalogObjectWorkspaceTab
}>();

const store = useExplorerStore();
const state = computed(() => store.getCatalogObjectState(props.tab.id));
const detail = computed(() => state.value.value);

const objectTypeLabel = computed(() => {
  switch (props.tab.objectType) {
    case 'synonym': return '同义词';
    case 'sequence': return '序列';
    case 'rule': return '规则';
    case 'default': return '默认值';
    case 'user-defined-type': return '类型';
    case 'database-trigger': return '数据库触发器';
    case 'xml-schema-collection': return 'XML 架构集合';
    default: return '对象';
  }
});

watch(
  () => props.tab.id,
  () => {
    void store.ensureCatalogObjectLoaded(props.tab);
  },
  { immediate: true },
);
</script>

<template>
  <NSpin :show="state.loading" class="catalog-object-spin">
    <div class="catalog-object-shell">
      <div class="catalog-object-header compact-panel">
        <div>
          <h3 class="catalog-object-title">{{ detail?.title ?? (tab.schema ? `${tab.schema}.${tab.name}` : tab.name) }}</h3>
          <p class="catalog-object-subtitle">{{ objectTypeLabel }} · {{ tab.database }}</p>
        </div>
        <div class="catalog-object-actions">
          <NButton size="small" tertiary @click="store.ensureCatalogObjectLoaded(tab, true)">刷新</NButton>
        </div>
      </div>

      <NAlert v-if="state.error" type="warning" :show-icon="false" class="panel-inline-alert">
        {{ state.error }}
      </NAlert>

      <template v-else-if="detail">
        <section class="catalog-object-main compact-panel">
          <div class="catalog-object-summary-row">
            <strong>摘要</strong>
            <span class="panel-meta">{{ detail.summary || `${objectTypeLabel}属性` }}</span>
          </div>
          <table class="catalog-object-grid">
            <tbody>
              <tr v-for="property in detail.properties" :key="property.label">
                <th>{{ property.label }}</th>
                <td>{{ property.value || '-' }}</td>
              </tr>
            </tbody>
          </table>
        </section>

        <section v-if="detail.definition" class="catalog-object-main compact-panel">
          <div class="catalog-object-summary-row">
            <strong>定义</strong>
            <span class="panel-meta">只读</span>
          </div>
          <NCode :code="detail.definition" language="sql" word-wrap />
        </section>
      </template>

      <div v-else class="catalog-object-main compact-panel">
        <NEmpty size="small" description="当前对象没有可显示的属性" />
      </div>
    </div>
  </NSpin>
</template>

<style scoped lang="scss">
.catalog-object-spin,
.catalog-object-shell {
  display: flex;
  flex-direction: column;
  min-height: 0;
  height: 100%;
  gap: 12px;
}

.catalog-object-header,
.catalog-object-main {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.catalog-object-header {
  flex-direction: row;
  justify-content: space-between;
  align-items: flex-start;
}

.catalog-object-title {
  margin: 0;
  font-size: 18px;
  font-weight: 800;
}

.catalog-object-subtitle {
  margin: 4px 0 0;
  color: var(--color-text-subtle, #64748b);
  font-size: 13px;
}

.catalog-object-actions {
  flex: 0 0 auto;
}

.catalog-object-summary-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.catalog-object-grid {
  width: 100%;
  border-collapse: collapse;

  th,
  td {
    padding: 9px 10px;
    border-top: 1px solid rgba(148, 163, 184, 0.18);
    text-align: left;
    vertical-align: top;
    font-size: 13px;
  }

  th {
    width: 180px;
    color: var(--color-text-subtle, #64748b);
    font-weight: 700;
  }

  td {
    color: var(--color-text-heading, #0f172a);
    word-break: break-word;
  }
}
</style>