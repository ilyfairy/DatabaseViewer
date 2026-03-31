<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { NAlert, NButton, NConfigProvider, NLayout, NLayoutContent, NMessageProvider, NModal } from 'naive-ui'
import { Pane, Splitpanes } from 'splitpanes'
import 'splitpanes/dist/splitpanes.css'
import DatabaseOverviewGraph from './components/DatabaseOverviewGraph.vue'
import DetailPanel from './components/DetailPanel.vue'
import EmptyWorkspace from './components/EmptyWorkspace.vue'
import ExplorerSidebar from './components/ExplorerSidebar.vue'
import GridPanel from './components/GridPanel.vue'
import SqlPanel from './components/SqlPanel.vue'
import WorkspaceHeader from './components/WorkspaceHeader.vue'
import WorkspaceTabsBar from './components/WorkspaceTabsBar.vue'
import { useExplorerStore } from './stores/explorer'

const store = useExplorerStore()
const activeTableTab = computed(() => store.activeTableTab)
const activeSqlTab = computed(() => store.activeSqlTab)
const activeGraphTab = computed(() => store.activeGraphTab)
const pendingSqlCloseTab = computed(() => {
  const pending = store.pendingSqlClose
  if (!pending) {
    return null
  }

  return store.workspaceTabs.find((tab) => tab.id === pending.tabId && tab.type === 'sql') ?? null
})
const gridPanel = computed(() => store.gridPanel)
const detailPanels = computed(() => store.detailPanels)
const activeDetailPanel = computed(() => {
  const panels = detailPanels.value
  return panels.length ? panels[panels.length - 1] : undefined
})
const detailRailRef = ref<HTMLElement | null>(null)
const closeAppPromptVisible = ref(false)
const closeAppPromptSaving = ref(false)
const hostWindow = window as typeof window & {
  __dbvHasDirtySqlTabs?: () => boolean
  __dbvSaveAllDirtySqlTabs?: () => Promise<boolean>
}

watch(
  () => detailPanels.value.map((panel) => panel.id).join('|'),
  async () => {
    await nextTick()
    const rail = detailRailRef.value
    if (!rail) {
      return
    }

    const lastPanel = rail.querySelector<HTMLElement>('.detail-panel-shell:last-child')
    lastPanel?.scrollIntoView({ block: 'nearest', behavior: 'smooth' })
  },
)

function handleGlobalKeydown(event: KeyboardEvent) {
  const normalizedKey = event.key.toLowerCase()
  const isRefreshShortcut = event.key === 'F5'
  const isSqlExecuteShortcut = (normalizedKey === 'r' && event.ctrlKey)
    || (event.key === 'Enter' && event.ctrlKey)
  const isSqlSaveShortcut = normalizedKey === 's' && event.ctrlKey

  const sqlTab = activeSqlTab.value
  if (sqlTab && isSqlSaveShortcut) {
    event.preventDefault()
    event.stopPropagation()
    event.stopImmediatePropagation()
    void store.saveSqlTab(sqlTab.id, event.shiftKey)
    return
  }

  if (sqlTab && (isRefreshShortcut || isSqlExecuteShortcut)) {
    event.preventDefault()
    event.stopPropagation()
    event.stopImmediatePropagation()
    window.dispatchEvent(new CustomEvent('dbv-request-execute-sql'))
    return
  }

  if (activeTableTab.value && isRefreshShortcut) {
    event.preventDefault()
    event.stopPropagation()
    event.stopImmediatePropagation()
    void store.refreshActiveTableData()
  }
}

function handleHostMessage(event: MessageEvent) {
  const payload = event.data as { channel?: string; event?: string; payload?: { files?: Array<{ path: string; content: string }> } }
  if (!payload || payload.channel !== 'dbv-event') {
    return
  }

  if (payload.event === 'request-close-with-dirty-sql-tabs') {
    closeAppPromptVisible.value = true
    closeAppPromptSaving.value = false
    return
  }

  if (payload.event !== 'open-sql-files') {
    return
  }

  for (const file of payload.payload?.files ?? []) {
    void store.openSqlFileTab(file.path, file.content)
  }
}

function isSqlFile(file: File) {
  return file.name.toLowerCase().endsWith('.sql')
}

function hasDroppedFiles(event: DragEvent) {
  const types = Array.from(event.dataTransfer?.types ?? [])
  return types.includes('Files') || (event.dataTransfer?.files?.length ?? 0) > 0
}

function handleGlobalDragOver(event: DragEvent) {
  if (!hasDroppedFiles(event)) {
    return
  }

  event.preventDefault()
  event.stopPropagation()
  if (event.dataTransfer) {
    event.dataTransfer.dropEffect = 'copy'
  }
}

async function handleGlobalDrop(event: DragEvent) {
  if (!hasDroppedFiles(event)) {
    return
  }

  event.preventDefault()
  event.stopPropagation()

  const files = Array.from(event.dataTransfer?.files ?? []).filter(isSqlFile)
  if (!files.length) {
    return
  }

  for (const file of files) {
    const path = (file as File & { path?: string }).path ?? file.name
    const content = await file.text()
    await store.openSqlFileTab(path, content)
  }
}

function handleGlobalDropEvent(event: DragEvent) {
  void handleGlobalDrop(event)
}

function handleHostExecuteSql() {
  window.dispatchEvent(new CustomEvent('dbv-request-execute-sql'))
}

function handleHostRefresh() {
  const sqlTab = activeSqlTab.value
  if (sqlTab) {
    window.dispatchEvent(new CustomEvent('dbv-request-execute-sql'))
    return
  }

  if (activeTableTab.value) {
    void store.refreshActiveTableData()
  }
}

function hasDirtySqlTabs() {
  return store.workspaceTabs.some((tab) => tab.type === 'sql' && store.isSqlTabDirty(tab.id))
}

async function saveAllDirtySqlTabs() {
  const dirtySqlTabs = store.workspaceTabs.filter((tab) => tab.type === 'sql' && store.isSqlTabDirty(tab.id))
  for (const tab of dirtySqlTabs) {
    const saved = await store.saveSqlTab(tab.id)
    if (!saved) {
      return false
    }
  }

  return true
}

function respondToHostCloseRequest(result: 'save' | 'discard' | 'cancel') {
  const webview = (window as typeof window & {
    chrome?: {
      webview?: {
        postMessage: (message: unknown) => void
      }
    }
  }).chrome?.webview

  webview?.postMessage({
    channel: 'dbv-request',
    id: `close:${Date.now()}`,
    command: 'close-app-response',
    payload: {
      result,
    },
  })
}

async function confirmCloseAndSave() {
  closeAppPromptSaving.value = true
  const saved = await saveAllDirtySqlTabs()
  if (!saved) {
    closeAppPromptSaving.value = false
    return
  }

  closeAppPromptVisible.value = false
  closeAppPromptSaving.value = false
  respondToHostCloseRequest('save')
}

function confirmCloseWithoutSave() {
  closeAppPromptVisible.value = false
  respondToHostCloseRequest('discard')
}

function cancelCloseAppPrompt() {
  closeAppPromptVisible.value = false
  closeAppPromptSaving.value = false
  respondToHostCloseRequest('cancel')
}

onMounted(() => {
  hostWindow.__dbvHasDirtySqlTabs = hasDirtySqlTabs
  hostWindow.__dbvSaveAllDirtySqlTabs = saveAllDirtySqlTabs
  window.addEventListener('keydown', handleGlobalKeydown, { capture: true })
  window.addEventListener('dragover', handleGlobalDragOver, { capture: true })
  window.addEventListener('drop', handleGlobalDropEvent, { capture: true })
  window.addEventListener('dbv-host-execute-sql', handleHostExecuteSql)
  window.addEventListener('dbv-host-refresh', handleHostRefresh)
  ;(window as typeof window & { chrome?: { webview?: { addEventListener: (type: 'message', listener: (event: MessageEvent) => void) => void } } }).chrome?.webview?.addEventListener('message', handleHostMessage)
})

onBeforeUnmount(() => {
  delete hostWindow.__dbvHasDirtySqlTabs
  delete hostWindow.__dbvSaveAllDirtySqlTabs
  window.removeEventListener('keydown', handleGlobalKeydown, { capture: true })
  window.removeEventListener('dragover', handleGlobalDragOver, { capture: true })
  window.removeEventListener('drop', handleGlobalDropEvent, { capture: true })
  window.removeEventListener('dbv-host-execute-sql', handleHostExecuteSql)
  window.removeEventListener('dbv-host-refresh', handleHostRefresh)
})
</script>

<template>
  <n-config-provider :theme="null">
    <n-message-provider>
      <n-layout class="app-shell">
        <n-layout-content class="content-shell">
          <div class="workspace-layout">
            <aside class="sidebar-column">
              <ExplorerSidebar />
            </aside>
            <section class="workspace-column">
              <div class="workspace-shell">
                <WorkspaceTabsBar />
                <WorkspaceHeader v-if="activeTableTab" />

                <template v-if="gridPanel && activeTableTab">
                  <Splitpanes v-if="activeDetailPanel" class="workspace-split workspace-body">
                    <Pane :size="68" :min-size="36">
                      <div class="workspace-main">
                        <GridPanel :key="activeTableTab.id" :table-key="gridPanel.tableKey" />
                      </div>
                    </Pane>
                    <Pane :size="32" :min-size="22">
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
                <div v-else-if="activeSqlTab" class="workspace-body">
                  <div class="workspace-main">
                    <SqlPanel :tab="activeSqlTab" />
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
          </div>

          <Teleport to="body">
            <Transition name="floating-notice">
              <n-alert
                v-if="store.notice"
                closable
                class="floating-notice"
                :type="store.notice.type === 'warning' ? 'warning' : store.notice.type === 'success' ? 'success' : 'info'"
                @close="store.dismissNotice()"
              >
                {{ store.notice.text }}
              </n-alert>
            </Transition>
          </Teleport>

          <n-modal
            :show="!!pendingSqlCloseTab"
            preset="card"
            style="width: min(460px, 92vw)"
            title="关闭 SQL 标签页"
            @update:show="(show) => { if (!show) store.cancelPendingSqlClose() }"
          >
            <div class="connection-dialog-form">
              <n-alert type="warning" :show-icon="false">
                {{ pendingSqlCloseTab ? `“${store.getWorkspaceTabLabel(pendingSqlCloseTab)}” 尚未保存，是否先保存？` : '' }}
              </n-alert>
              <div class="connection-dialog-actions">
                <n-button tertiary @click="store.cancelPendingSqlClose()">取消</n-button>
                <n-button tertiary @click="store.discardPendingSqlClose()">不保存</n-button>
                <n-button type="primary" @click="store.savePendingSqlClose()">保存</n-button>
              </div>
            </div>
          </n-modal>

          <n-modal
            :show="closeAppPromptVisible"
            preset="card"
            style="width: min(480px, 92vw)"
            title="关闭程序"
            :mask-closable="false"
            :close-on-esc="false"
            :closable="false"
          >
            <div class="connection-dialog-form">
              <n-alert type="warning" :show-icon="false">
                存在尚未保存的 SQL 标签页。是否先保存再关闭？
              </n-alert>
              <div class="connection-dialog-actions">
                <n-button tertiary :disabled="closeAppPromptSaving" @click="cancelCloseAppPrompt()">取消</n-button>
                <n-button tertiary :disabled="closeAppPromptSaving" @click="confirmCloseWithoutSave()">不保存</n-button>
                <n-button type="primary" :loading="closeAppPromptSaving" @click="confirmCloseAndSave()">保存</n-button>
              </div>
            </div>
          </n-modal>
        </n-layout-content>
      </n-layout>
    </n-message-provider>
  </n-config-provider>
</template>
