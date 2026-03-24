<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { NAlert, NConfigProvider, NLayout, NLayoutContent, NMessageProvider } from 'naive-ui'
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
const gridPanel = computed(() => store.gridPanel)
const detailPanels = computed(() => store.detailPanels)
const activeDetailPanel = computed(() => {
  const panels = detailPanels.value
  return panels.length ? panels[panels.length - 1] : undefined
})
const detailRailRef = ref<HTMLElement | null>(null)

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

  const sqlTab = activeSqlTab.value
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

onMounted(() => {
  window.addEventListener('keydown', handleGlobalKeydown, { capture: true })
  window.addEventListener('dbv-host-execute-sql', handleHostExecuteSql)
  window.addEventListener('dbv-host-refresh', handleHostRefresh)
})

onBeforeUnmount(() => {
  window.removeEventListener('keydown', handleGlobalKeydown, { capture: true })
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
                        <GridPanel :table-key="gridPanel.tableKey" />
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
                      <GridPanel :table-key="gridPanel.tableKey" />
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
        </n-layout-content>
      </n-layout>
    </n-message-provider>
  </n-config-provider>
</template>
