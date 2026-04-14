<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue';
import { NAlert, NButton, NCheckbox, NEmpty, NInput, NSelect, NSpin, NTabPane, NTabs } from 'naive-ui';
import { useExplorerStore } from '../stores/explorer';
import {
  deleteSqlServerLogin,
  fetchSqlServerLoginDetail,
  fetchSqlServerLoginEditorOptions,
  previewSqlServerLogin,
  saveSqlServerLogin,
} from '../lib/sqlserver-login-api';
import StoredMaskedPasswordInput from './StoredMaskedPasswordInput.vue';
import { attachSmoothHorizontalWheelScroll } from '../lib/smooth-horizontal-wheel-scroll';
import type { SqlServerLoginEditorWorkspaceTab } from '../types/explorer';
import type {
  SaveSqlServerLoginRequest,
  SqlServerLoginAuthenticationType,
  SqlServerLoginDetail,
  SqlServerLoginEditorOptions,
  SqlServerLoginPermissionTarget,
  SqlServerPermissionAssignment,
  SqlServerPermissionState,
} from '../types/sqlserver-login';

type EditorTabKey = 'general' | 'roles' | 'server-permissions' | 'endpoint-permissions' | 'login-permissions' | 'sql-preview';

type EditableLoginDraft = {
  originalName: string | null;
  name: string;
  authenticationType: SqlServerLoginAuthenticationType;
  isEnabled: boolean;
  checkPasswordPolicy: boolean;
  checkPasswordExpiration: boolean;
  mustChangePasswordOnNextLogin: boolean;
  defaultDatabase: string | null;
  defaultLanguage: string | null;
  password: string;
  confirmPassword: string;
  useOldPassword: boolean;
  oldPassword: string;
  serverRoles: string[];
  serverPermissions: SqlServerPermissionAssignment[];
  endpointPermissions: Array<{ endpointName: string; permissions: SqlServerPermissionAssignment[] }>;
  loginPermissions: SqlServerLoginPermissionTarget[];
};

const props = defineProps<{
  tab: SqlServerLoginEditorWorkspaceTab;
}>();

const store = useExplorerStore();
const loading = ref(false);
const saving = ref(false);
const previewLoading = ref(false);
const deleting = ref(false);
const detailError = ref<string | null>(null);
const previewError = ref<string | null>(null);
const activeEditorTab = ref<EditorTabKey>('general');
const sqlPreview = ref('');
const passwordEditState = ref<Record<'password' | 'confirmPassword', boolean>>({
  password: false,
  confirmPassword: false,
});
const editorOptions = ref<SqlServerLoginEditorOptions | null>(null);
const detail = ref<SqlServerLoginDetail | null>(null);
const draft = ref<EditableLoginDraft | null>(null);
const loginEditorShellRef = ref<HTMLElement | null>(null);
let previewRefreshTimer: ReturnType<typeof setTimeout> | null = null;
let detachSubtabWheelScroll: (() => void) | null = null;

const connection = computed(() => store.getConnectionInfo(props.tab.connectionId));
const databaseOptions = computed(() => (editorOptions.value?.databases ?? []).map((item) => ({ label: item, value: item })));
const languageOptions = computed(() => editorOptions.value?.languages ?? []);
const authenticationOptions = computed(() => {
  const options = [
    { label: 'SQL Server Authentication', value: 'sql' },
    { label: 'Windows Authentication', value: 'windows' },
  ];

  if (draft.value?.authenticationType === 'certificate') {
    options.push({ label: 'Certificate Mapped Login', value: 'certificate' });
  }

  if (draft.value?.authenticationType === 'asymmetric-key') {
    options.push({ label: 'Asymmetric Key Mapped Login', value: 'asymmetric-key' });
  }

  return options;
});
const isCreateMode = computed(() => props.tab.mode === 'create');
const canEditGeneral = computed(() => isCreateMode.value || detail.value?.supportsGeneralEditing || false);
const canEditPassword = computed(() => isCreateMode.value || detail.value?.supportsPasswordEditing || false);
const canEditRoles = computed(() => isCreateMode.value || detail.value?.supportsRoleEditing || false);
const canEditPermissions = computed(() => isCreateMode.value || detail.value?.supportsPermissionEditing || false);
const canDelete = computed(() => !isCreateMode.value && (detail.value?.canDelete ?? false));
const canShowStoredPasswordMask = computed(() => !isCreateMode.value
  && !!detail.value?.supportsPasswordEditing
  && !!draft.value);
const headerTitle = computed(() => {
  if (isCreateMode.value) {
    return '新建登录';
  }

  return props.tab.loginName ?? detail.value?.name ?? '登录';
});
const headerSubtitle = computed(() => {
  if (isCreateMode.value) {
    return `${connection.value?.name ?? '未找到连接'} · 创建并配置新的 SQL Server 登录`;
  }

  const principalLabel = detail.value?.principalTypeLabel ?? '正在加载登录属性';
  return `${connection.value?.name ?? '未找到连接'} · ${principalLabel}`;
});

function showStoredPasswordMask(field: 'password' | 'confirmPassword'): boolean {
  return !!canShowStoredPasswordMask.value
    && !passwordEditState.value[field]
    && !draft.value?.[field];
}

function emitLoginChanged(): void {
  window.dispatchEvent(new CustomEvent('dbv-sqlserver-login-changed', {
    detail: {
      connectionId: props.tab.connectionId,
    },
  }));
}

function clonePermissions(items: SqlServerPermissionAssignment[]): SqlServerPermissionAssignment[] {
  return items.map((item) => ({ ...item }));
}

function cloneLoginPermissions(items: SqlServerLoginPermissionTarget[]): SqlServerLoginPermissionTarget[] {
  return items.map((item) => ({
    ...item,
    permissions: clonePermissions(item.permissions),
  }));
}

function normalizeLanguageValue(languageValue: string | null | undefined, options: SqlServerLoginEditorOptions['languages']): string | null {
  if (!languageValue) {
    return null;
  }

  const exactMatch = options.find((item) => item.value === languageValue || item.label === languageValue);
  if (exactMatch) {
    return exactMatch.value;
  }

  return languageValue;
}

function createDraftFromDetail(source: SqlServerLoginDetail): EditableLoginDraft {
  const languageOptions = source.options.languages;

  return {
    originalName: source.originalName,
    name: source.name,
    authenticationType: source.authenticationType,
    isEnabled: source.isEnabled,
    checkPasswordPolicy: source.checkPasswordPolicy,
    checkPasswordExpiration: source.checkPasswordExpiration,
    mustChangePasswordOnNextLogin: false,
    defaultDatabase: source.defaultDatabase ?? null,
    defaultLanguage: normalizeLanguageValue(source.defaultLanguage ?? null, languageOptions),
    password: '',
    confirmPassword: '',
    useOldPassword: false,
    oldPassword: '',
    serverRoles: [...source.serverRoles],
    serverPermissions: clonePermissions(source.serverPermissions),
    endpointPermissions: source.endpointPermissions.map((item) => ({
      endpointName: item.endpointName,
      permissions: clonePermissions(item.permissions),
    })),
    loginPermissions: cloneLoginPermissions(source.loginPermissions),
  };
}

function createEmptyDraft(options: SqlServerLoginEditorOptions): EditableLoginDraft {
  return {
    originalName: null,
    name: '',
    authenticationType: 'sql',
    isEnabled: true,
    checkPasswordPolicy: true,
    checkPasswordExpiration: false,
    mustChangePasswordOnNextLogin: false,
    defaultDatabase: options.databases[0] ?? 'master',
    defaultLanguage: options.languages[0]?.value ?? null,
    password: '',
    confirmPassword: '',
    useOldPassword: false,
    oldPassword: '',
    serverRoles: [],
    serverPermissions: options.serverPermissions.map((name) => ({ name, state: 'none' })),
    endpointPermissions: options.endpoints.map((endpointName) => ({
      endpointName,
      permissions: options.endpointPermissions.map((name) => ({ name, state: 'none' })),
    })),
    loginPermissions: options.loginTargets.map((item) => ({
      loginName: item.name,
      principalTypeCode: item.principalTypeCode,
      principalTypeLabel: item.principalTypeLabel,
      isSystem: item.isSystem,
      permissions: options.loginPermissions.map((name) => ({ name, state: 'none' })),
    })),
  };
}

function buildSaveRequest(): SaveSqlServerLoginRequest {
  if (!draft.value) {
    throw new Error('当前没有可保存的登录草稿。');
  }

  const currentDraft = draft.value;
  const trimmedName = currentDraft.name.trim();
  if (!trimmedName) {
    throw new Error('登录名不能为空。');
  }

  if (currentDraft.authenticationType === 'sql' && !currentDraft.originalName && !currentDraft.password) {
    throw new Error('新建 SQL Server 登录时必须填写密码。');
  }

  if (currentDraft.password || currentDraft.confirmPassword) {
    if (currentDraft.password !== currentDraft.confirmPassword) {
      throw new Error('两次输入的密码不一致。');
    }

    if (currentDraft.authenticationType !== 'sql') {
      throw new Error('仅 SQL Server 登录支持设置密码。');
    }
  }

  if (currentDraft.useOldPassword && !currentDraft.oldPassword) {
    throw new Error('启用旧密码校验时必须填写旧密码。');
  }

  if (currentDraft.mustChangePasswordOnNextLogin && !currentDraft.password) {
    throw new Error('要求下次登录修改密码时，当前必须同时设置新密码。');
  }

  return {
    connectionId: props.tab.connectionId,
    originalName: currentDraft.originalName,
    name: trimmedName,
    authenticationType: currentDraft.authenticationType,
    password: currentDraft.password || null,
    oldPassword: currentDraft.useOldPassword ? (currentDraft.oldPassword || null) : null,
    useOldPassword: currentDraft.useOldPassword,
    isEnabled: currentDraft.isEnabled,
    checkPasswordPolicy: currentDraft.checkPasswordPolicy,
    checkPasswordExpiration: currentDraft.checkPasswordExpiration,
    mustChangePasswordOnNextLogin: currentDraft.mustChangePasswordOnNextLogin,
    defaultDatabase: currentDraft.defaultDatabase,
    defaultLanguage: currentDraft.defaultLanguage,
    serverRoles: [...currentDraft.serverRoles],
    serverPermissions: clonePermissions(currentDraft.serverPermissions),
    endpointPermissions: currentDraft.endpointPermissions.map((item) => ({
      endpointName: item.endpointName,
      permissions: clonePermissions(item.permissions),
    })),
    loginPermissions: cloneLoginPermissions(currentDraft.loginPermissions),
  };
}

function beginPasswordEdit(field: 'password' | 'confirmPassword'): void {
  if (!draft.value || !showStoredPasswordMask(field)) {
    return;
  }

  passwordEditState.value[field] = true;
  draft.value[field] = '';
}

function updatePasswordField(field: 'password' | 'confirmPassword', nextValue: string): void {
  if (!draft.value) {
    return;
  }

  draft.value[field] = nextValue;
}

async function loadEditorState(): Promise<void> {
  loading.value = true;
  detailError.value = null;
  previewError.value = null;
  sqlPreview.value = '';
  passwordEditState.value = {
    password: false,
    confirmPassword: false,
  };

  if (previewRefreshTimer) {
    clearTimeout(previewRefreshTimer);
    previewRefreshTimer = null;
  }

  try {
    const options = await fetchSqlServerLoginEditorOptions(props.tab.connectionId);
    editorOptions.value = options;

    if (props.tab.mode === 'create') {
      detail.value = null;
      draft.value = createEmptyDraft(options);
      activeEditorTab.value = 'general';
      return;
    }

    if (!props.tab.loginName) {
      throw new Error('未指定需要编辑的登录。');
    }

    const nextDetail = await fetchSqlServerLoginDetail(props.tab.connectionId, props.tab.loginName);
    detail.value = nextDetail;
    editorOptions.value = nextDetail.options;
    draft.value = createDraftFromDetail(nextDetail);
    activeEditorTab.value = 'general';
  }
  catch (error) {
    detailError.value = error instanceof Error ? error.message : '登录详情加载失败';
    detail.value = null;
    draft.value = null;
  }
  finally {
    loading.value = false;
  }
}

async function refreshSqlPreview(): Promise<void> {
  previewLoading.value = true;
  previewError.value = null;
  try {
    sqlPreview.value = await previewSqlServerLogin(buildSaveRequest());
  }
  catch (error) {
    previewError.value = error instanceof Error ? error.message : 'SQL 预览生成失败';
    sqlPreview.value = '';
  }
  finally {
    previewLoading.value = false;
  }
}

function scheduleSqlPreviewRefresh(immediate = false): void {
  if (activeEditorTab.value !== 'sql-preview') {
    return;
  }

  if (previewRefreshTimer) {
    clearTimeout(previewRefreshTimer);
    previewRefreshTimer = null;
  }

  if (immediate) {
    void refreshSqlPreview();
    return;
  }

  previewRefreshTimer = setTimeout(() => {
    previewRefreshTimer = null;
    void refreshSqlPreview();
  }, 180);
}

async function persistCurrentLogin(): Promise<void> {
  const request = buildSaveRequest();
  saving.value = true;
  try {
    await saveSqlServerLogin(request);
    emitLoginChanged();
    store.showNotice('success', request.originalName ? '登录已更新' : '登录已创建');

    if (props.tab.mode === 'create' || props.tab.loginName !== request.name) {
      store.closeWorkspaceTab(props.tab.id);
      store.openSqlServerLoginEditor(props.tab.connectionId, request.name, 'browse');
      return;
    }

    await loadEditorState();
  }
  finally {
    saving.value = false;
  }
}

async function removeCurrentLogin(): Promise<void> {
  if (!detail.value || !canDelete.value) {
    return;
  }

  const confirmed = window.confirm(`确认删除登录 ${detail.value.name}？`);
  if (!confirmed) {
    return;
  }

  deleting.value = true;
  try {
    await deleteSqlServerLogin(props.tab.connectionId, detail.value.name);
    emitLoginChanged();
    store.showNotice('success', '登录已删除');
    store.closeWorkspaceTab(props.tab.id);
  }
  finally {
    deleting.value = false;
  }
}

function setServerPermissionState(permissionName: string, nextState: SqlServerPermissionState): void {
  if (!draft.value) {
    return;
  }

  draft.value.serverPermissions = draft.value.serverPermissions.map((item) => item.name === permissionName
    ? {
        ...item,
        state: item.state === nextState ? 'none' : nextState,
      }
    : item);
}

function setGrantMatrixState(section: 'endpoint' | 'login', rowKey: string, permissionName: string, checked: boolean): void {
  if (!draft.value) {
    return;
  }

  if (section === 'endpoint') {
    draft.value.endpointPermissions = draft.value.endpointPermissions.map((row) => row.endpointName === rowKey
      ? {
          ...row,
          permissions: row.permissions.map((permission) => permission.name === permissionName
            ? { ...permission, state: checked ? 'grant' : 'none' }
            : permission),
        }
      : row);
    return;
  }

  draft.value.loginPermissions = draft.value.loginPermissions.map((row) => row.loginName === rowKey
    ? {
        ...row,
        permissions: row.permissions.map((permission) => permission.name === permissionName
          ? { ...permission, state: checked ? 'grant' : 'none' }
          : permission),
      }
    : row);
}

function toggleRole(roleName: string, checked: boolean): void {
  if (!draft.value) {
    return;
  }

  const next = new Set(draft.value.serverRoles);
  if (checked) {
    next.add(roleName);
  }
  else {
    next.delete(roleName);
  }

  draft.value.serverRoles = Array.from(next).sort((left, right) => left.localeCompare(right));
}

function clearSubtabWheelScroll(): void {
  detachSubtabWheelScroll?.();
  detachSubtabWheelScroll = null;
}

function bindSubtabWheelScroll(): void {
  clearSubtabWheelScroll();

  const nextScrollElement = loginEditorShellRef.value?.querySelector('.login-subtabs .n-tabs-nav-scroll-wrapper > .v-x-scroll');
  if (!(nextScrollElement instanceof HTMLElement)) {
    return;
  }

  detachSubtabWheelScroll = attachSmoothHorizontalWheelScroll(nextScrollElement);
}

watch(() => [props.tab.connectionId, props.tab.loginName, props.tab.mode], () => {
  void loadEditorState();
}, { immediate: true });

watch(activeEditorTab, (value) => {
  if (value === 'sql-preview') {
    scheduleSqlPreviewRefresh(true);
  }
});

watch(draft, () => {
  scheduleSqlPreviewRefresh();
}, { deep: true });

watch(draft, async (value) => {
  if (!value) {
    clearSubtabWheelScroll();
    return;
  }

  await nextTick();
  bindSubtabWheelScroll();
}, { immediate: true });

onBeforeUnmount(() => {
  clearSubtabWheelScroll();

  if (previewRefreshTimer) {
    clearTimeout(previewRefreshTimer);
  }
});
</script>

<template>
  <div class="login-editor-page">
    <NSpin :show="loading || saving || deleting" class="login-editor-spin">
      <div ref="loginEditorShellRef" class="login-editor-shell compact-panel">
      <div class="login-editor-header">
        <div>
          <h3>{{ headerTitle }}</h3>
          <p>{{ headerSubtitle }}</p>
        </div>
        <div class="login-editor-actions">
          <NButton size="small" tertiary @click="loadEditorState">刷新</NButton>
          <NButton size="small" tertiary :disabled="!canDelete" :loading="deleting" @click="removeCurrentLogin">删除</NButton>
          <NButton size="small" type="primary" :loading="saving" @click="persistCurrentLogin">保存</NButton>
        </div>
      </div>

      <NAlert v-if="detailError" type="warning" :show-icon="false">{{ detailError }}</NAlert>

      <NTabs v-if="draft" v-model:value="activeEditorTab" class="login-subtabs compact-panel" type="line" size="small">
        <NTabPane name="general" tab="常规">
          <div class="login-tab-body login-tab-body-scroll">
          <div class="login-general-grid">
            <label class="form-field">
              <span>登录名</span>
              <NInput v-model:value="draft.name" size="small" :disabled="!isCreateMode && !(detail?.supportsRename ?? false)" />
            </label>

            <label class="form-field">
              <span>验证类型</span>
              <NSelect v-model:value="draft.authenticationType" size="small" :options="authenticationOptions" :disabled="!isCreateMode" />
            </label>

            <label class="form-field">
              <span>密码</span>
              <StoredMaskedPasswordInput
                :model-value="draft.password"
                :has-stored-value="showStoredPasswordMask('password')"
                size="small"
                :disabled="!canEditPassword || draft.authenticationType !== 'sql'"
                @copy.prevent
                @begin-edit="beginPasswordEdit('password')"
                @update:model-value="(value) => updatePasswordField('password', value)"
              />
            </label>

            <label class="form-field">
              <span>确认密码</span>
              <StoredMaskedPasswordInput
                :model-value="draft.confirmPassword"
                :has-stored-value="showStoredPasswordMask('confirmPassword')"
                size="small"
                :disabled="!canEditPassword || draft.authenticationType !== 'sql'"
                @copy.prevent
                @begin-edit="beginPasswordEdit('confirmPassword')"
                @update:model-value="(value) => updatePasswordField('confirmPassword', value)"
              />
            </label>

            <label class="form-field checkbox-field">
              <NCheckbox v-model:checked="draft.useOldPassword" :disabled="!canEditPassword || draft.authenticationType !== 'sql'">指定旧密码</NCheckbox>
              <NInput v-model:value="draft.oldPassword" size="small" type="password" show-password-on="click" :disabled="!draft.useOldPassword || !canEditPassword || draft.authenticationType !== 'sql'" @copy.prevent />
            </label>

            <div class="form-field checkbox-stack">
              <NCheckbox v-model:checked="draft.checkPasswordPolicy" :disabled="!canEditGeneral || !canEditPassword || draft.authenticationType !== 'sql'">强制密码策略</NCheckbox>
              <NCheckbox v-model:checked="draft.checkPasswordExpiration" :disabled="!draft.checkPasswordPolicy || !canEditGeneral || !canEditPassword || draft.authenticationType !== 'sql'">强制密码过期</NCheckbox>
              <NCheckbox v-model:checked="draft.mustChangePasswordOnNextLogin" :disabled="!draft.checkPasswordPolicy || !canEditPassword || draft.authenticationType !== 'sql'">用户必须在下次登录时更改密码</NCheckbox>
              <NCheckbox v-model:checked="draft.isEnabled" :disabled="!(detail?.supportsEnableDisable ?? isCreateMode)">已启用</NCheckbox>
            </div>

            <label class="form-field">
              <span>默认数据库</span>
              <NSelect v-model:value="draft.defaultDatabase" size="small" :options="databaseOptions" clearable :disabled="!canEditGeneral" />
            </label>

            <label class="form-field">
              <span>默认语言</span>
              <NSelect v-model:value="draft.defaultLanguage" size="small" :options="languageOptions" clearable :disabled="!canEditGeneral" />
            </label>
          </div>
          </div>
        </NTabPane>

        <NTabPane name="roles" tab="角色">
          <div class="login-tab-body login-tab-body-scroll">
          <div class="role-grid">
            <label class="role-item role-item-fixed">
              <NCheckbox :checked="true" disabled>public</NCheckbox>
            </label>
            <label v-for="roleName in editorOptions?.serverRoles ?? []" :key="roleName" class="role-item">
              <NCheckbox :checked="draft.serverRoles.includes(roleName)" :disabled="!canEditRoles" @update:checked="(checked) => toggleRole(roleName, checked)">
                {{ roleName }}
              </NCheckbox>
            </label>
          </div>
          </div>
        </NTabPane>

        <NTabPane name="server-permissions" tab="服务器权限">
          <div class="login-tab-body login-tab-body-scroll">
          <div class="permission-state-table-wrap">
            <table class="permission-state-table">
              <thead>
                <tr>
                  <th>权限</th>
                  <th>授予</th>
                  <th>含授予选项</th>
                  <th>拒绝</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="permission in draft.serverPermissions" :key="permission.name">
                  <td>{{ permission.name }}</td>
                  <td><input type="checkbox" :checked="permission.state === 'grant'" :disabled="!canEditPermissions" @change="setServerPermissionState(permission.name, 'grant')"></td>
                  <td><input type="checkbox" :checked="permission.state === 'grant-with-grant-option'" :disabled="!canEditPermissions" @change="setServerPermissionState(permission.name, 'grant-with-grant-option')"></td>
                  <td><input type="checkbox" :checked="permission.state === 'deny'" :disabled="!canEditPermissions" @change="setServerPermissionState(permission.name, 'deny')"></td>
                </tr>
              </tbody>
            </table>
          </div>
          </div>
        </NTabPane>

        <NTabPane name="endpoint-permissions" tab="终端节点权限">
          <div class="login-tab-body login-tab-body-scroll">
          <div class="permission-matrix-wrap">
            <table class="permission-matrix-table">
              <thead>
                <tr>
                  <th>终端节点</th>
                  <th v-for="permissionName in editorOptions?.endpointPermissions ?? []" :key="permissionName">{{ permissionName }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="endpoint in draft.endpointPermissions" :key="endpoint.endpointName">
                  <td>{{ endpoint.endpointName }}</td>
                  <td v-for="permission in endpoint.permissions" :key="permission.name">
                    <input type="checkbox" :checked="permission.state === 'grant' || permission.state === 'grant-with-grant-option'" :disabled="!canEditPermissions" @change="(event) => setGrantMatrixState('endpoint', endpoint.endpointName, permission.name, (event.target as HTMLInputElement).checked)">
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
          </div>
        </NTabPane>

        <NTabPane name="login-permissions" tab="登录权限">
          <div class="login-tab-body login-tab-body-scroll">
          <div class="permission-matrix-wrap">
            <table class="permission-matrix-table">
              <thead>
                <tr>
                  <th>登录</th>
                  <th v-for="permissionName in editorOptions?.loginPermissions ?? []" :key="permissionName">{{ permissionName }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="loginPermission in draft.loginPermissions" :key="loginPermission.loginName">
                  <td>
                    <div class="permission-target-cell">
                      <span>{{ loginPermission.loginName }}</span>
                      <small>{{ loginPermission.principalTypeCode }}</small>
                    </div>
                  </td>
                  <td v-for="permission in loginPermission.permissions" :key="permission.name">
                    <input type="checkbox" :checked="permission.state === 'grant' || permission.state === 'grant-with-grant-option'" :disabled="!canEditPermissions" @change="(event) => setGrantMatrixState('login', loginPermission.loginName, permission.name, (event.target as HTMLInputElement).checked)">
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
          </div>
        </NTabPane>

        <NTabPane name="sql-preview" tab="SQL 预览">
          <div class="login-tab-body login-tab-body-scroll">
          <NSpin v-if="previewLoading && !sqlPreview" size="small" />
          <NAlert v-if="previewError" type="warning" :show-icon="false">{{ previewError }}</NAlert>
          <textarea class="sql-preview-textarea" :value="sqlPreview" readonly />
          </div>
        </NTabPane>
      </NTabs>

      <NEmpty v-else description="未能加载当前登录" />
      </div>
    </NSpin>
  </div>
</template>

<style scoped lang="scss">
.login-editor-page {
  display: flex;
  flex: 1;
  min-height: 0;
  height: 100%;
}

.login-editor-spin,
.login-editor-shell {
  height: 100%;
}

:deep(.login-editor-spin),
.login-editor-page :deep(.login-editor-spin) {
  display: flex;
  flex: 1;
  min-height: 0;
  overflow-y: hidden;
  overflow-x: hidden;
}

:deep(.login-editor-spin .n-spin-container),
.login-editor-page :deep(.login-editor-spin .n-spin-container) {
  display: flex;
  flex: 1;
  min-height: 0;
  height: 100%;
}

:deep(.login-editor-spin .n-spin-content),
.login-editor-page :deep(.login-editor-spin .n-spin-content) {
  display: flex;
  flex: 1;
  min-width: 0;
  width: 100%;
}

.login-editor-shell {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
  min-width: 0;
  gap: 8px;
  padding: 4px 6px;
  box-sizing: border-box;
  background: transparent;
}

.login-editor-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}

.login-editor-header h3 {
  margin: 0;
  font-size: 14px;
}

.login-editor-header p {
  margin: 2px 0 0;
  color: #607085;
  font-size: 11px;
}

.login-editor-actions {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
  justify-content: flex-end;
}

.login-general-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  min-width: 420px;
  gap: 10px;
}

.form-field {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.form-field > span {
  font-size: 11px;
  color: #50677d;
}

.checkbox-field,
.checkbox-stack {
  grid-column: span 2;
}

.checkbox-stack {
  display: grid;
  gap: 6px;
}

.role-grid {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.role-item {
  padding: 6px 8px;
  border: 1px solid rgba(201, 214, 225, 0.9);
  border-radius: 8px;
  background: rgba(247, 250, 252, 0.92);
}

.role-item-fixed {
  background: rgba(240, 244, 248, 0.92);
}

.permission-state-table-wrap,
.permission-matrix-wrap {
  min-height: 0;
  overflow: auto;
  border: 1px solid rgba(155, 172, 190, 0.28);
  border-radius: 10px;
  background: rgba(255, 255, 255, 0.92);
}

.permission-state-table,
.permission-matrix-table {
  width: 100%;
  border-collapse: collapse;
}

.permission-state-table th,
.permission-state-table td,
.permission-matrix-table th,
.permission-matrix-table td {
  padding: 6px 8px;
  border-bottom: 1px solid rgba(221, 229, 236, 0.92);
  text-align: left;
  white-space: nowrap;
  font-size: 12px;
}

.permission-state-table thead th,
.permission-matrix-table thead th {
  position: sticky;
  top: 0;
  z-index: 1;
  background: #eef4f9;
  color: #456178;
  font-weight: 600;
}

.permission-target-cell {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.permission-target-cell small {
  color: #6f8193;
}

.sql-preview-textarea {
  width: 100%;
  min-height: 280px;
  resize: vertical;
  border: 1px solid rgba(155, 172, 190, 0.36);
  border-radius: 8px;
  padding: 10px;
  background: #f7fafc;
  color: #243342;
  font: 13px/1.6 'Consolas', 'Courier New', monospace;
  outline: none;

  &:focus {
    border-color: rgba(24, 160, 88, 0.6);
  }
}

.login-editor-shell :deep(.login-subtabs) {
  display: flex;
  flex: 1;
  min-height: 0;
  flex-direction: column;
  padding: 2px 6px 0;
}

.login-editor-shell :deep(.login-subtabs.n-tabs) {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-height: 0;
}

.login-editor-shell :deep(.login-subtabs .n-tabs-pane-wrapper) {
  display: flex;
  flex: 1 1 0;
  min-height: 0;
  overflow: hidden;
}

.login-editor-shell :deep(.login-subtabs .n-tab-pane) {
  display: flex;
  flex: 1 1 0;
  min-height: 0;
  overflow: hidden;
}

.login-subtabs :deep(.n-tabs-nav) {
  margin-bottom: 0;
  flex-shrink: 0;
}

.login-subtabs :deep(.n-tabs-nav-scroll-wrapper) {
  overflow: hidden !important;
}

.login-subtabs :deep(.n-tabs-nav-scroll-wrapper > .v-x-scroll) {
  overflow-x: auto !important;
  overflow-y: hidden !important;
  scrollbar-width: none;
  -ms-overflow-style: none;
}

.login-subtabs :deep(.n-tabs-nav-scroll-content) {
  min-width: max-content;
}

.login-subtabs :deep(.n-tabs-nav-scroll-wrapper > .v-x-scroll::-webkit-scrollbar) {
  display: none;
}

.login-editor-shell :deep(.login-subtabs .n-tab-pane > .login-tab-body) {
  display: flex;
  flex: 1 1 0;
  min-height: 0;
  overflow: hidden;
}

.login-editor-shell :deep(.login-subtabs .n-tab-pane > .login-tab-body.login-tab-body-scroll) {
  overflow-x: auto;
  overflow-y: auto;
  scrollbar-gutter: stable;
  padding-right: 4px;
  padding-bottom: 8px;
  flex-wrap: wrap;
  align-content: flex-start;
}

.login-subtabs :deep(.n-tabs-tab) {
  padding-left: 4px;
  padding-right: 4px;
}

.sql-preview-status {
  margin-bottom: 8px;
  color: #607085;
  font-size: 12px;
}


</style>