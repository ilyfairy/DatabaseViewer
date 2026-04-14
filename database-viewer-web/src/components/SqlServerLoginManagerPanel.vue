<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { NAlert, NButton, NEmpty, NInput, NModal, NSpin } from 'naive-ui';
import type { DropdownOption } from 'naive-ui';
import ContextDropdown from './ContextDropdown.vue';
import { useExplorerStore } from '../stores/explorer';
import { deleteSqlServerLogin, fetchSqlServerLogins } from '../lib/sqlserver-login-api';
import type { SqlServerLoginManagerWorkspaceTab } from '../types/explorer';
import type { SqlServerLoginSummary } from '../types/sqlserver-login';

const props = defineProps<{
  tab: SqlServerLoginManagerWorkspaceTab;
}>();

const store = useExplorerStore();
const loading = ref(false);
const listError = ref<string | null>(null);
const filterQuery = ref('');
const summaries = ref<SqlServerLoginSummary[]>([]);
const deleting = ref(false);
const loginContextMenu = ref<{ x: number; y: number; login: SqlServerLoginSummary } | null>(null);
const deleteConfirmLogin = ref<SqlServerLoginSummary | null>(null);
const loginContextMenuOptions = computed<DropdownOption[]>(() => loginContextMenu.value
  ? [
      { label: '编辑用户', key: 'edit-login' },
      { label: '删除用户', key: 'delete-login', disabled: !loginContextMenu.value.login.canDelete },
    ]
  : []);

const connection = computed(() => store.getConnectionInfo(props.tab.connectionId));
const selectedLoginName = computed(() => props.tab.selectedLoginName);
const filteredSummaries = computed(() => {
  const query = filterQuery.value.trim().toLowerCase();
  if (!query) {
    return summaries.value;
  }

  return summaries.value.filter((item) => `${item.name} ${item.defaultDatabase ?? ''} ${item.principalTypeLabel}`.toLowerCase().includes(query));
});

function updateSelection(loginName: string | null): void {
  store.updateSqlServerLoginManagerState(props.tab.id, loginName, 'browse');
}

function closeLoginContextMenu(): void {
  loginContextMenu.value = null;
}

function openCreateTab(): void {
  store.openSqlServerLoginEditor(props.tab.connectionId, null, 'create');
}

function openSelectedLogin(): void {
  if (!selectedLoginName.value) {
    return;
  }

  store.openSqlServerLoginEditor(props.tab.connectionId, selectedLoginName.value, 'browse');
}

function handleRowClick(loginName: string): void {
  updateSelection(loginName);
}

function handleRowDoubleClick(loginName: string): void {
  updateSelection(loginName);
  store.openSqlServerLoginEditor(props.tab.connectionId, loginName, 'browse');
}

function openLoginContextMenu(event: MouseEvent, login: SqlServerLoginSummary): void {
  event.preventDefault();
  event.stopPropagation();
  updateSelection(login.name);
  loginContextMenu.value = {
    x: event.clientX,
    y: event.clientY,
    login,
  };
}

function editLoginFromContextMenu(): void {
  if (!loginContextMenu.value) {
    return;
  }

  store.openSqlServerLoginEditor(props.tab.connectionId, loginContextMenu.value.login.name, 'browse');
  closeLoginContextMenu();
}

function openDeleteConfirm(): void {
  if (!loginContextMenu.value) {
    return;
  }

  deleteConfirmLogin.value = loginContextMenu.value.login;
  closeLoginContextMenu();
}

function handleLoginContextMenuShow(value: boolean): void {
  if (!value) {
    closeLoginContextMenu();
  }
}

function handleLoginContextMenuSelect(key: string | number): void {
  switch (key) {
    case 'edit-login':
      editLoginFromContextMenu();
      return;
    case 'delete-login':
      openDeleteConfirm();
      return;
    default:
      return;
  }
}

async function confirmDeleteLogin(): Promise<void> {
  if (!deleteConfirmLogin.value) {
    return;
  }

  deleting.value = true;
  try {
    await deleteSqlServerLogin(props.tab.connectionId, deleteConfirmLogin.value.name);
    window.dispatchEvent(new CustomEvent('dbv-sqlserver-login-changed', {
      detail: {
        connectionId: props.tab.connectionId,
      },
    }));
    store.showNotice('success', '登录已删除');
    deleteConfirmLogin.value = null;
    await loadLogins();
  }
  catch (error) {
    store.showNotice('warning', error instanceof Error ? error.message : '删除登录失败');
  }
  finally {
    deleting.value = false;
  }
}

async function loadLogins(): Promise<void> {
  loading.value = true;
  listError.value = null;
  try {
    summaries.value = await fetchSqlServerLogins(props.tab.connectionId);

    if (props.tab.selectedLoginName && !summaries.value.some((item) => item.name === props.tab.selectedLoginName)) {
      updateSelection(null);
    }
  }
  catch (error) {
    listError.value = error instanceof Error ? error.message : '登录列表加载失败';
  }
  finally {
    loading.value = false;
  }
}

function handleLoginChanged(event: Event): void {
  const detail = (event as CustomEvent<{ connectionId?: string }>).detail;
  if (!detail || detail.connectionId !== props.tab.connectionId) {
    return;
  }

  void loadLogins();
}

watch(() => props.tab.connectionId, () => {
  void loadLogins();
}, { immediate: true });

onMounted(() => {
  window.addEventListener('dbv-sqlserver-login-changed', handleLoginChanged as EventListener);
  window.addEventListener('click', closeLoginContextMenu);
  window.addEventListener('contextmenu', closeLoginContextMenu);
});

onBeforeUnmount(() => {
  window.removeEventListener('dbv-sqlserver-login-changed', handleLoginChanged as EventListener);
  window.removeEventListener('click', closeLoginContextMenu);
  window.removeEventListener('contextmenu', closeLoginContextMenu);
});
</script>

<template>
  <NSpin :show="loading || deleting" class="login-list-spin">
    <div class="login-list-shell compact-panel">
      <div class="login-list-header">
        <div>
          <h3>SQL Server 用户管理</h3>
          <p>{{ connection?.name ?? '未找到连接' }}</p>
        </div>
        <div class="login-list-actions">
          <NButton size="small" tertiary @click="loadLogins">刷新</NButton>
          <NButton size="small" tertiary :disabled="!selectedLoginName" @click="openSelectedLogin">编辑</NButton>
          <NButton size="small" type="primary" @click="openCreateTab">新建登录</NButton>
        </div>
      </div>

      <NInput v-model:value="filterQuery" size="small" placeholder="筛选登录名 / 默认数据库 / 类型" clearable />

      <NAlert v-if="listError" type="warning" :show-icon="false">{{ listError }}</NAlert>

      <div class="login-list-table-wrap">
        <table class="login-list-table">
          <thead>
            <tr>
              <th>名称</th>
              <th>用户 ID</th>
              <th>默认数据库</th>
              <th>已启用</th>
              <th>用户类型</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="item in filteredSummaries"
              :key="item.name"
              :class="{ 'is-active': item.name === selectedLoginName }"
              @click="handleRowClick(item.name)"
              @dblclick="handleRowDoubleClick(item.name)"
              @contextmenu="openLoginContextMenu($event, item)"
            >
              <td>{{ item.name }}</td>
              <td>{{ item.principalId }}</td>
              <td>{{ item.defaultDatabase || 'master' }}</td>
              <td>{{ item.isEnabled ? '是' : '否' }}</td>
              <td>{{ item.principalTypeCode }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <NEmpty v-if="!filteredSummaries.length && !listError" description="没有匹配的登录" size="small" />

      <ContextDropdown
        :show="!!loginContextMenu"
        :x="loginContextMenu?.x ?? 0"
        :y="loginContextMenu?.y ?? 0"
        :options="loginContextMenuOptions"
        @update:show="handleLoginContextMenuShow"
        @select="handleLoginContextMenuSelect"
      />

      <NModal :show="!!deleteConfirmLogin" preset="card" style="width: min(420px, 92vw)" title="删除登录" @update:show="(show) => { if (!show) deleteConfirmLogin = null }">
        <div class="login-delete-confirm">
          <NAlert type="warning" :show-icon="false">
            确认删除登录 {{ deleteConfirmLogin?.name }}？此操作无法撤销。
          </NAlert>
          <div class="login-delete-actions">
            <NButton tertiary @click="deleteConfirmLogin = null">取消</NButton>
            <NButton type="error" :loading="deleting" @click="confirmDeleteLogin">确认删除</NButton>
          </div>
        </div>
      </NModal>
    </div>
  </NSpin>
</template>

<style scoped lang="scss">
.login-list-spin,
.login-list-shell {
  height: 100%;
}

.login-list-spin {
  display: flex;
  flex: 1;
  min-height: 0;
  overflow: hidden;
}

.login-list-spin :deep(.n-spin-container) {
  display: flex;
  flex: 1;
  min-height: 0;
  height: 100%;
}

.login-list-spin :deep(.n-spin-content) {
  display: flex;
  flex: 1;
  min-height: 0;
  height: 100%;
}

.login-list-shell {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
  gap: 8px;
  padding: 4px 6px;
  box-sizing: border-box;
  overflow: hidden;
  background: transparent;
}

.login-list-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}

.login-list-header h3 {
  margin: 0;
  font-size: 14px;
}

.login-list-header p {
  margin: 2px 0 0;
  color: #607085;
  font-size: 11px;
}

.login-list-actions {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}

.login-list-table-wrap {
  min-height: 0;
  flex: 1;
  overflow: auto;
  border: 1px solid rgba(155, 172, 190, 0.28);
  border-radius: 10px;
  background: rgba(255, 255, 255, 0.92);
}

.login-list-table {
  width: 100%;
  border-collapse: collapse;
}

.login-list-table th,
.login-list-table td {
  padding: 6px 8px;
  border-bottom: 1px solid rgba(221, 229, 236, 0.92);
  text-align: left;
  white-space: nowrap;
  font-size: 12px;
}

.login-list-table thead th {
  position: sticky;
  top: 0;
  z-index: 1;
  background: #eef4f9;
  color: #456178;
  font-weight: 600;
}

.login-list-table tbody tr {
  cursor: pointer;
  transition: background-color 0.16s ease;
}

.login-list-table tbody tr:hover {
  background: rgba(35, 114, 184, 0.08);
}

.login-list-table tbody tr.is-active {
  background: rgba(35, 114, 184, 0.14);
}

.login-delete-confirm {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.login-delete-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
</style>