<script setup lang="ts">
import { computed, defineAsyncComponent, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { NAlert, NButton, NConfigProvider, NDialogProvider, NLayout, NLayoutContent, NMessageProvider, NModal } from 'naive-ui';
import type { GlobalThemeOverrides } from 'naive-ui';
import { Pane, Splitpanes } from 'splitpanes';
import 'splitpanes/dist/splitpanes.css';

// Heavy components loaded lazily — not rendered on initial paint
const DatabaseOverviewGraph = defineAsyncComponent(() => import('./components/DatabaseOverviewGraph.vue'));
const CatalogObjectPanel = defineAsyncComponent(() => import('./components/CatalogObjectPanel.vue'));
const SettingsPanel = defineAsyncComponent(() => import('./components/SettingsPanel.vue'));
const SqlServerLoginEditorPanel = defineAsyncComponent(() => import('./components/SqlServerLoginEditorPanel.vue'));
const SqlServerLoginManagerPanel = defineAsyncComponent(() => import('./components/SqlServerLoginManagerPanel.vue'));
const SqlPanel = defineAsyncComponent(() => import('./components/SqlPanel.vue'));
const TableMockDataPanel = defineAsyncComponent(() => import('./components/TableMockDataPanel.vue'));
const TableDesignPanel = defineAsyncComponent(() => import('./components/TableDesignPanel.vue'));

// Always visible on first render
import DetailPanel from './components/DetailPanel.vue';
import EmptyWorkspace from './components/EmptyWorkspace.vue';
import ExplorerSidebar from './components/ExplorerSidebar.vue';
import GridPanel from './components/GridPanel.vue';
import WorkspaceTabsBar from './components/WorkspaceTabsBar.vue';
import { useExplorerStore } from './stores/explorer';

const store = useExplorerStore();

/** Naive UI 全局主题覆盖：让虚拟滚动条与原生滚动条一样 */
const themeOverrides: GlobalThemeOverrides = {
  Scrollbar: {
    color: '#d5dbe2',
    colorHover: '#bcc5cf',
  },
};
const activeTableTab = computed(() => store.activeTableTab);
const activeDesignTab = computed(() => store.activeDesignTab);
const activeSqlTab = computed(() => store.activeSqlTab);
const activeGraphTab = computed(() => store.activeGraphTab);
const activeCatalogTab = computed(() => store.activeCatalogTab);
const activeMockTab = computed(() => store.activeMockTab);
const activeSettingsTab = computed(() => store.activeSettingsTab);
const activeSqlServerLoginManagerTab = computed(() => store.activeSqlServerLoginManagerTab);
const activeSqlServerLoginEditorTab = computed(() => store.activeSqlServerLoginEditorTab);
const pendingSqlCloseTab = computed(() => {
  const pending = store.pendingSqlClose;
  if (!pending) {
    return null;
  }

  return store.workspaceTabs.find((tab) => tab.id === pending.tabId && tab.type === 'sql') ?? null;
});
const pendingDesignCloseTab = computed(() => {
  const pending = store.pendingDesignClose;
  if (!pending) {
    return null;
  }

  return store.workspaceTabs.find((tab) => tab.id === pending.tabId && tab.type === 'design') ?? null;
});
const gridPanel = computed(() => store.gridPanel);
const detailPanels = computed(() => store.detailPanels);
const activeDetailPanel = computed(() => {
  const panels = detailPanels.value;
  return panels.length ? panels[panels.length - 1] : undefined;
});
const detailRailRef = ref<HTMLElement | null>(null);
const closeAppPromptVisible = ref(false);
const closeAppPromptSaving = ref(false);
const sidebarPaneSize = ref(22);
const detailPaneSize = ref(32);
let workspaceLayoutPersistTimer: ReturnType<typeof setTimeout> | null = null;
const hostWindow = window as typeof window & {
  __dbvHasDirtySqlTabs?: () => boolean
  __dbvSaveAllDirtySqlTabs?: () => Promise<boolean>
};

type SplitpanesResizePayload = {
  panes?: Array<{ size: number }>
};

function clampPaneSize(value: number, min: number, max: number) {
  return Math.min(max, Math.max(min, value));
}

const resolvedSidebarPaneSize = computed(() => clampPaneSize(Number(sidebarPaneSize.value) || 22, 14, 38));
const resolvedDetailPaneSize = computed(() => clampPaneSize(Number(detailPaneSize.value) || 32, 22, 64));

watch(() => store.workspaceLayout, (value) => {
  sidebarPaneSize.value = value.sidebarPaneSize;
  detailPaneSize.value = value.detailPaneSize;
}, { deep: true, immediate: true });

function scheduleWorkspaceLayoutPersistence() {
  if (workspaceLayoutPersistTimer) {
    clearTimeout(workspaceLayoutPersistTimer);
  }

  workspaceLayoutPersistTimer = setTimeout(() => {
    workspaceLayoutPersistTimer = null;
    void store.saveWorkspaceLayout({
      sidebarPaneSize: resolvedSidebarPaneSize.value,
      detailPaneSize: resolvedDetailPaneSize.value,
    });
  }, 160);
}

function handleWorkspaceResized(payload: SplitpanesResizePayload) {
  const nextSidebarSize = Number(payload.panes?.[0]?.size);
  if (Number.isNaN(nextSidebarSize)) {
    return;
  }

  sidebarPaneSize.value = clampPaneSize(nextSidebarSize, 14, 38);
  scheduleWorkspaceLayoutPersistence();
}

function handleDetailResized(payload: SplitpanesResizePayload) {
  const nextDetailSize = Number(payload.panes?.[1]?.size);
  if (Number.isNaN(nextDetailSize)) {
    return;
  }

  detailPaneSize.value = clampPaneSize(nextDetailSize, 22, 64);
  scheduleWorkspaceLayoutPersistence();
}

watch(
  () => detailPanels.value.map((panel) => panel.id).join('|'),
  async () => {
    await nextTick();
    const rail = detailRailRef.value;
    if (!rail) {
      return;
    }

    const lastPanel = rail.querySelector<HTMLElement>('.detail-panel-shell:last-child');
    lastPanel?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  },
);

function handleGlobalKeydown(event: KeyboardEvent) {
  const normalizedKey = event.key.toLowerCase();
  const isRefreshShortcut = event.key === 'F5';
  const isSqlExecuteShortcut = (normalizedKey === 'r' && event.ctrlKey)
    || (event.key === 'Enter' && event.ctrlKey);
  const isSqlSaveShortcut = normalizedKey === 's' && event.ctrlKey;

  const sqlTab = activeSqlTab.value;
  if (sqlTab && isSqlSaveShortcut) {
    event.preventDefault();
    event.stopPropagation();
    event.stopImmediatePropagation();
    void store.saveSqlTab(sqlTab.id, event.shiftKey);
    return;
  }

  if (sqlTab && (isRefreshShortcut || isSqlExecuteShortcut)) {
    event.preventDefault();
    event.stopPropagation();
    event.stopImmediatePropagation();
    window.dispatchEvent(new CustomEvent('dbv-request-execute-sql'));
    return;
  }

  if (activeTableTab.value && isRefreshShortcut) {
    event.preventDefault();
    event.stopPropagation();
    event.stopImmediatePropagation();
    void store.refreshActiveTableData();
  }
}

function handleHostMessage(event: MessageEvent) {
  const payload = event.data as { channel?: string; event?: string; payload?: { files?: Array<{ path: string; content: string }> } };
  if (!payload || payload.channel !== 'dbv-event') {
    return;
  }

  if (payload.event === 'request-close-with-dirty-sql-tabs') {
    closeAppPromptVisible.value = true;
    closeAppPromptSaving.value = false;
    return;
  }

  if (payload.event !== 'open-sql-files') {
    return;
  }

  for (const file of payload.payload?.files ?? []) {
    void store.openSqlFileTab(file.path, file.content);
  }
}

function isSqlFile(file: File) {
  return file.name.toLowerCase().endsWith('.sql');
}

function hasDroppedFiles(event: DragEvent) {
  const types = Array.from(event.dataTransfer?.types ?? []);
  return types.includes('Files') || (event.dataTransfer?.files?.length ?? 0) > 0;
}

function handleGlobalDragOver(event: DragEvent) {
  if (!hasDroppedFiles(event)) {
    return;
  }

  event.preventDefault();
  event.stopPropagation();
  if (event.dataTransfer) {
    event.dataTransfer.dropEffect = 'copy';
  }
}

async function handleGlobalDrop(event: DragEvent) {
  if (!hasDroppedFiles(event)) {
    return;
  }

  event.preventDefault();
  event.stopPropagation();

  const files = Array.from(event.dataTransfer?.files ?? []).filter(isSqlFile);
  if (!files.length) {
    return;
  }

  for (const file of files) {
    const path = (file as File & { path?: string }).path ?? file.name;
    const content = await file.text();
    await store.openSqlFileTab(path, content);
  }
}

function handleGlobalDropEvent(event: DragEvent) {
  void handleGlobalDrop(event);
}

function handleHostExecuteSql() {
  window.dispatchEvent(new CustomEvent('dbv-request-execute-sql'));
}

function handleHostRefresh() {
  const sqlTab = activeSqlTab.value;
  if (sqlTab) {
    window.dispatchEvent(new CustomEvent('dbv-request-execute-sql'));
    return;
  }

  if (activeTableTab.value) {
    void store.refreshActiveTableData();
  }
}

function hasDirtySqlTabs() {
  return store.workspaceTabs.some((tab) => tab.type === 'sql' && store.isSqlTabDirty(tab.id));
}

async function saveAllDirtySqlTabs() {
  const dirtySqlTabs = store.workspaceTabs.filter((tab) => tab.type === 'sql' && store.isSqlTabDirty(tab.id));
  for (const tab of dirtySqlTabs) {
    const saved = await store.saveSqlTab(tab.id);
    if (!saved) {
      return false;
    }
  }

  return true;
}

function respondToHostCloseRequest(result: 'save' | 'discard' | 'cancel') {
  const webview = (window as typeof window & {
    chrome?: {
      webview?: {
        postMessage: (message: unknown) => void
      }
    }
  }).chrome?.webview;

  webview?.postMessage({
    channel: 'dbv-request',
    id: `close:${Date.now()}`,
    command: 'close-app-response',
    payload: {
      result,
    },
  });
}

async function confirmCloseAndSave() {
  closeAppPromptSaving.value = true;
  const saved = await saveAllDirtySqlTabs();
  if (!saved) {
    closeAppPromptSaving.value = false;
    return;
  }

  closeAppPromptVisible.value = false;
  closeAppPromptSaving.value = false;
  respondToHostCloseRequest('save');
}

function confirmCloseWithoutSave() {
  closeAppPromptVisible.value = false;
  respondToHostCloseRequest('discard');
}

function cancelCloseAppPrompt() {
  closeAppPromptVisible.value = false;
  closeAppPromptSaving.value = false;
  respondToHostCloseRequest('cancel');
}

onMounted(() => {
  hostWindow.__dbvHasDirtySqlTabs = hasDirtySqlTabs;
  hostWindow.__dbvSaveAllDirtySqlTabs = saveAllDirtySqlTabs;
  window.addEventListener('keydown', handleGlobalKeydown, { capture: true });
  window.addEventListener('dragover', handleGlobalDragOver, { capture: true });
  window.addEventListener('drop', handleGlobalDropEvent, { capture: true });
  window.addEventListener('dbv-host-execute-sql', handleHostExecuteSql);
  window.addEventListener('dbv-host-refresh', handleHostRefresh)
  ;(window as typeof window & { chrome?: { webview?: { addEventListener: (type: 'message', listener: (event: MessageEvent) => void) => void } } }).chrome?.webview?.addEventListener('message', handleHostMessage);
});

onBeforeUnmount(() => {
  if (workspaceLayoutPersistTimer) {
    clearTimeout(workspaceLayoutPersistTimer);
    workspaceLayoutPersistTimer = null;
  }

  delete hostWindow.__dbvHasDirtySqlTabs;
  delete hostWindow.__dbvSaveAllDirtySqlTabs;
  window.removeEventListener('keydown', handleGlobalKeydown, { capture: true });
  window.removeEventListener('dragover', handleGlobalDragOver, { capture: true });
  window.removeEventListener('drop', handleGlobalDropEvent, { capture: true });
  window.removeEventListener('dbv-host-execute-sql', handleHostExecuteSql);
  window.removeEventListener('dbv-host-refresh', handleHostRefresh);
});
</script>

<template>
  <NConfigProvider :theme="null" :theme-overrides="themeOverrides">
    <NMessageProvider>
    <NDialogProvider>
      <NLayout class="app-shell">
        <NLayoutContent class="content-shell">
          <Splitpanes class="workspace-layout workspace-layout-split" @resize="handleWorkspaceResized" @resized="handleWorkspaceResized">
            <Pane :size="resolvedSidebarPaneSize" :min-size="14" :max-size="38">
              <aside class="sidebar-column">
                <ExplorerSidebar />
              </aside>
            </Pane>
            <Pane :size="100 - resolvedSidebarPaneSize" :min-size="62">
              <section class="workspace-column">
                <div class="workspace-shell">
                <WorkspaceTabsBar />

                <template v-if="gridPanel && activeTableTab">
                  <Splitpanes v-if="activeDetailPanel" class="workspace-split workspace-body" @resize="handleDetailResized" @resized="handleDetailResized">
                    <Pane :size="100 - resolvedDetailPaneSize" :min-size="36">
                      <div class="workspace-main">
                        <GridPanel :key="activeTableTab.id" :table-key="gridPanel.tableKey" />
                      </div>
                    </Pane>
                    <Pane :size="resolvedDetailPaneSize" :min-size="22">
                      <aside ref="detailRailRef" class="detail-rail">
                        <div class="detail-panel-shell">
                          <DetailPanel :panel="activeDetailPanel" :chain-panels="detailPanels" />
                        </div>
                      </aside>
                    </Pane>
                  </Splitpanes>
                  <div v-else class="workspace-body">
                    <div class="workspace-main">
                      <GridPanel :key="activeTableTab.id" :table-key="gridPanel.tableKey" />
                    </div>
                  </div>
                </template>
                <div v-else-if="activeDesignTab" class="workspace-body">
                  <div class="workspace-main">
                    <TableDesignPanel :tab="activeDesignTab" />
                  </div>
                </div>
                <div v-else-if="activeSqlTab" class="workspace-body">
                  <div class="workspace-main">
                    <SqlPanel :tab="activeSqlTab" />
                  </div>
                </div>
                <div v-else-if="activeCatalogTab" class="workspace-body">
                  <div class="workspace-main">
                    <CatalogObjectPanel :tab="activeCatalogTab" />
                  </div>
                </div>
                <div v-else-if="activeMockTab" class="workspace-body">
                  <div class="workspace-main">
                    <TableMockDataPanel :tab="activeMockTab" />
                  </div>
                </div>
                <div v-else-if="activeSettingsTab" class="workspace-body">
                  <div class="workspace-main">
                    <SettingsPanel />
                  </div>
                </div>
                <div v-else-if="activeSqlServerLoginManagerTab" class="workspace-body">
                  <div class="workspace-main">
                    <SqlServerLoginManagerPanel :tab="activeSqlServerLoginManagerTab" />
                  </div>
                </div>
                <div v-else-if="activeSqlServerLoginEditorTab" class="workspace-body">
                  <div class="workspace-main">
                    <SqlServerLoginEditorPanel :tab="activeSqlServerLoginEditorTab" />
                  </div>
                </div>
                <div v-else-if="activeGraphTab" class="workspace-body graph-workspace-body">
                  <div class="workspace-main graph-workspace-main">
                    <div v-if="activeGraphTab.error" class="database-overview-empty">{{ activeGraphTab.error }}</div>
                    <div v-else-if="activeGraphTab.loading" class="database-overview-empty">正在准备数据库关系图...</div>
                    <DatabaseOverviewGraph v-else-if="activeGraphTab.graph" class="graph-workspace-graph" :graph="activeGraphTab.graph" />
                    <div v-else class="database-overview-empty">正在准备数据库关系图...</div>
                  </div>
                </div>
                <EmptyWorkspace v-else />
                </div>
              </section>
            </Pane>
          </Splitpanes>

          <Teleport to="body">
            <Transition name="floating-notice">
              <NAlert
                v-if="store.notice"
                closable
                class="floating-notice"
                :type="store.notice.type === 'warning' ? 'warning' : store.notice.type === 'success' ? 'success' : 'info'"
                @close="store.dismissNotice()"
              >
                {{ store.notice.text }}
              </NAlert>
            </Transition>
          </Teleport>

          <NModal
            :show="!!pendingSqlCloseTab"
            preset="card"
            style="width: min(460px, 92vw)"
            title="关闭 SQL 标签页"
            @update:show="(show) => { if (!show) store.cancelPendingSqlClose() }"
          >
            <div class="connection-dialog-form">
              <NAlert type="warning" :show-icon="false">
                {{ pendingSqlCloseTab ? `“${store.getWorkspaceTabLabel(pendingSqlCloseTab)}” 尚未保存，是否先保存？` : '' }}
              </NAlert>
              <div class="connection-dialog-actions">
                <NButton tertiary @click="store.cancelPendingSqlClose()">取消</NButton>
                <NButton tertiary @click="store.discardPendingSqlClose()">不保存</NButton>
                <NButton type="primary" @click="store.savePendingSqlClose()">保存</NButton>
              </div>
            </div>
          </NModal>

          <NModal
            :show="!!pendingDesignCloseTab"
            preset="card"
            style="width: min(460px, 92vw)"
            title="关闭建表设计"
            @update:show="(show) => { if (!show) store.cancelPendingDesignClose() }"
          >
            <div class="connection-dialog-form">
              <NAlert type="warning" :show-icon="false">
                {{ pendingDesignCloseTab ? `"${store.getWorkspaceTabLabel(pendingDesignCloseTab)}" 尚未创建，确定要放弃吗？` : '' }}
              </NAlert>
              <div class="connection-dialog-actions">
                <NButton tertiary @click="store.cancelPendingDesignClose()">取消</NButton>
                <NButton type="error" @click="store.discardPendingDesignClose()">放弃</NButton>
              </div>
            </div>
          </NModal>

          <NModal
            :show="closeAppPromptVisible"
            preset="card"
            style="width: min(480px, 92vw)"
            title="关闭程序"
            :mask-closable="false"
            :close-on-esc="false"
            :closable="false"
          >
            <div class="connection-dialog-form">
              <NAlert type="warning" :show-icon="false">
                存在尚未保存的 SQL 标签页。是否先保存再关闭？
              </NAlert>
              <div class="connection-dialog-actions">
                <NButton tertiary :disabled="closeAppPromptSaving" @click="cancelCloseAppPrompt()">取消</NButton>
                <NButton tertiary :disabled="closeAppPromptSaving" @click="confirmCloseWithoutSave()">不保存</NButton>
                <NButton type="primary" :loading="closeAppPromptSaving" @click="confirmCloseAndSave()">保存</NButton>
              </div>
            </div>
          </NModal>
        </NLayoutContent>
      </NLayout>
    </NDialogProvider>
    </NMessageProvider>
  </NConfigProvider>
</template>

<style lang="scss">
// App-only layout classes (not shared with other components)

.app-shell {
  height: 100vh;
  background: transparent;
  overflow: hidden;
}

.content-shell {
  height: 100vh;
  padding: 4px;
  overflow: hidden;
}

.floating-notice {
  position: fixed;
  right: 18px;
  bottom: 18px;
  z-index: 4000;
  width: min(420px, calc(100vw - 28px));
  border-radius: var(--radius-xl);
  box-shadow: 0 18px 42px rgba(15, 23, 42, 0.18);

  &-enter-active,
  &-leave-active {
    transition: opacity 220ms ease, transform 220ms ease;
  }

  &-enter-from,
  &-leave-to {
    opacity: 0;
    transform: translateY(10px);
  }
}

.graph-workspace-body,
.graph-workspace-main {
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-height: 0;
  height: 100%;
  width: 100%;
}

.graph-workspace-main {

  > *,
  .graph-workspace-graph,
  .database-overview-shell,
  .database-overview-stage,
  .database-overview-canvas-wrap,
  .database-overview-flow,
  .vue-flow {
    flex: 1 1 auto;
    min-height: 0;
    height: 100%;
    width: 100%;
  }
}

.graph-workspace-body {

  > .workspace-main,
  > .workspace-main > * {
    min-height: 0;
    height: 100%;
  }
}
</style>
