<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { NDataTable, NSpin, NTabPane, NTabs, NText } from 'naive-ui';
import type { DataTableColumns } from 'naive-ui';
import type { DatabaseProperties, DatabasePropertiesWorkspaceTab } from '../types/explorer';

const props = defineProps<{
  tab: DatabasePropertiesWorkspaceTab;
}>();

const loading = ref(false);
const error = ref<string | null>(null);
const data = ref<DatabaseProperties | null>(null);
const activeTab = ref('general');

/** 加载数据库属性。 */
async function loadProperties() {
  loading.value = true;
  error.value = null;

  try {
    const params = new URLSearchParams({
      connectionId: props.tab.connectionId,
      database: props.tab.database,
    });
    const response = await fetch(`/api/explorer/database-properties?${params.toString()}`);
    if (!response.ok) {
      const message = await response.text();
      throw new Error(message || `${response.status} ${response.statusText}`);
    }

    data.value = await response.json() as DatabaseProperties;
  }
  catch (err) {
    error.value = err instanceof Error ? err.message : '加载数据库属性失败';
  }
  finally {
    loading.value = false;
  }
}

watch(() => props.tab.id, () => {
  activeTab.value = 'general';
  void loadProperties();
}, { immediate: true });

const generalRows = computed(() => {
  if (!data.value) {
    return [];
  }

  return data.value.generalProperties.map((prop, index) => ({
    key: index,
    label: prop.label,
    value: prop.value ?? '',
  }));
});

const generalColumns: DataTableColumns = [
  { title: '属性', key: 'label', width: 160, resizable: true, ellipsis: { tooltip: true } },
  { title: '值', key: 'value', ellipsis: { tooltip: true } },
];

const fileRows = computed(() => {
  if (!data.value) {
    return [];
  }

  return data.value.files.map((file, index) => ({
    key: index,
    logicalName: file.logicalName,
    fileType: file.fileType,
    fileGroup: file.fileGroup ?? '',
    sizeMB: file.sizeMB,
    autoGrowth: file.autoGrowth ?? '',
    path: file.path ?? '',
  }));
});

const fileColumns: DataTableColumns = [
  { title: '逻辑名称', key: 'logicalName', width: 160, resizable: true, ellipsis: { tooltip: true } },
  { title: '文件类型', key: 'fileType', width: 80, resizable: true },
  { title: '文件组', key: 'fileGroup', width: 100, resizable: true },
  { title: '大小', key: 'sizeMB', width: 100, resizable: true },
  { title: '自动增长/最大大小', key: 'autoGrowth', width: 200, resizable: true, ellipsis: { tooltip: true } },
  { title: '路径', key: 'path', ellipsis: { tooltip: true } },
];

const permissionRows = computed(() => {
  if (!data.value) {
    return [];
  }

  return data.value.permissions.map((perm, index) => ({
    key: index,
    userName: perm.userName,
    userType: perm.userType,
    defaultSchema: perm.defaultSchema ?? '',
    loginName: perm.loginName ?? '',
    roles: perm.roles.join(', '),
  }));
});

const permissionColumns: DataTableColumns = [
  { title: '用户名', key: 'userName', width: 160, resizable: true, ellipsis: { tooltip: true } },
  { title: '用户类型', key: 'userType', width: 140, resizable: true },
  { title: '默认架构', key: 'defaultSchema', width: 100, resizable: true },
  { title: '登录名', key: 'loginName', width: 160, resizable: true, ellipsis: { tooltip: true } },
  { title: '角色', key: 'roles', ellipsis: { tooltip: true } },
];

const hasFiles = computed(() => (data.value?.files.length ?? 0) > 0);
const hasPermissions = computed(() => (data.value?.permissions.length ?? 0) > 0);
</script>

<template>
  <div class="database-properties-panel">
    <NSpin :show="loading">
      <div v-if="error" class="database-properties-error">
        <NText type="error">{{ error }}</NText>
      </div>

      <div v-else-if="data" class="database-properties-body">
        <NTabs v-model:value="activeTab" type="line" animated>
          <NTabPane name="general" tab="常规">
            <NDataTable
              :columns="generalColumns"
              :data="generalRows"
              :bordered="true"
              :single-line="false"
              size="small"
              :max-height="'calc(100vh - 160px)'"
            />
          </NTabPane>

          <NTabPane v-if="hasFiles" name="files" tab="文件">
            <NDataTable
              :columns="fileColumns"
              :data="fileRows"
              :bordered="true"
              :single-line="false"
              size="small"
              :max-height="'calc(100vh - 160px)'"
            />
          </NTabPane>

          <NTabPane v-if="hasPermissions" name="permissions" tab="权限">
            <NDataTable
              :columns="permissionColumns"
              :data="permissionRows"
              :bordered="true"
              :single-line="false"
              size="small"
              :max-height="'calc(100vh - 160px)'"
            />
          </NTabPane>
        </NTabs>
      </div>

      <div v-else class="database-properties-empty">
        <NText depth="3">加载中...</NText>
      </div>
    </NSpin>
  </div>
</template>

<style scoped lang="scss">
.database-properties-panel {
  height: 100%;
  padding: 12px 16px;
  overflow: hidden;

  :deep(.n-data-table-th) {
    padding: 4px 8px !important;
    font-size: 12px;
  }

  :deep(.n-data-table-td) {
    padding: 3px 8px !important;
    font-size: 12px;
    line-height: 1.4;
  }
}

.database-properties-error {
  padding: 24px;
  text-align: center;
}

.database-properties-empty {
  padding: 24px;
  text-align: center;
}

.database-properties-body {
  height: 100%;
}
</style>
