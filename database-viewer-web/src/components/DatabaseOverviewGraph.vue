<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import { Background } from '@vue-flow/background'
import { Controls } from '@vue-flow/controls'
import { MiniMap } from '@vue-flow/minimap'
import { VueFlow, type Edge, type EdgeProps, type Node, type NodeMouseEvent } from '@vue-flow/core'
import '@vue-flow/core/dist/style.css'
import '@vue-flow/core/dist/theme-default.css'
import '@vue-flow/controls/dist/style.css'
import '@vue-flow/minimap/dist/style.css'
import { NButton, NCheckbox, NInput, NSelect } from 'naive-ui'
import { buildDatabaseOverviewLayout, type DatabaseOverviewEdgeData, type DatabaseOverviewLayoutOptions, type DatabaseOverviewNodeData } from '../lib/databaseOverviewLayout'
import { useExplorerStore } from '../stores/explorer'
import type { DatabaseGraph } from '../types/explorer'

const props = defineProps<{
  graph: DatabaseGraph
}>()

type RenderNodeData = DatabaseOverviewNodeData & {
  isFocused: boolean
  isMatched: boolean
  isDimmed: boolean
  accentColor: string
  accentSoft: string
  accentBorder: string
}

type OverviewContextMenuState = {
  x: number
  y: number
  tableKey: string
  title: string
}

const store = useExplorerStore()
const flowRef = ref<{ fitView?: (options?: unknown) => Promise<boolean> | boolean } | null>(null)
const graphNodes = shallowRef<Node<DatabaseOverviewNodeData>[]>([])
const graphEdges = shallowRef<Edge<DatabaseOverviewEdgeData>[]>([])
const layoutPending = ref(false)
const layoutError = ref<string | null>(null)
const centerTableKey = ref<string | null>(null)
const visibleDepth = ref<number>(2)
const searchQuery = ref('')
const focusedTableKey = ref<string | null>(null)
const contextMenu = ref<OverviewContextMenuState | null>(null)
const expandAllFields = ref(false)
const showNotNull = ref(false)
const showTableIcons = ref(true)
const expandedTableKeys = ref<string[]>([])
let layoutGeneration = 0

const tableOptions = computed(() => props.graph.nodes
  .slice()
  .sort((left, right) => left.title.localeCompare(right.title))
  .map((node) => ({
    label: node.title,
    value: node.tableKey,
  })))

const depthOptions = [1, 2, 3, 4, 5].map((depth) => ({
  label: `${depth} 层`,
  value: depth,
}))

const overviewPalette = [
  { accent: '#4f8cc9', soft: '#eaf3ff', border: '#72a8de' },
  { accent: '#d8923b', soft: '#fff4e5', border: '#ebb45a' },
  { accent: '#56a878', soft: '#edf9f1', border: '#72c292' },
  { accent: '#8b6fb8', soft: '#f2ecfb', border: '#a188cf' },
  { accent: '#cc6f7a', soft: '#fff0f2', border: '#e08c96' },
  { accent: '#4ea6a6', soft: '#eaf8f8', border: '#73bcbc' },
]

function colorForNode(tableKey: string, isJunction: boolean) {
  if (isJunction) {
    return { accent: '#c98b2f', soft: '#fff4df', border: '#e2aa57' }
  }

  let hash = 0
  for (const char of tableKey) {
    hash = ((hash << 5) - hash) + char.charCodeAt(0)
    hash |= 0
  }

  return overviewPalette[Math.abs(hash) % overviewPalette.length]!
}

function normalizeText(value: string) {
  return value.trim().toLowerCase()
}

const normalizedSearchQuery = computed(() => normalizeText(searchQuery.value))
const layoutOptions = computed<DatabaseOverviewLayoutOptions>(() => ({
  expandAllFields: expandAllFields.value,
  expandedTableKeys: expandedTableKeys.value,
}))

function buildScopedGraph(graph: DatabaseGraph, centerKey: string | null, depth: number): DatabaseGraph {
  if (!centerKey) {
    return graph
  }

  const adjacency = new Map<string, Set<string>>()
  graph.nodes.forEach((node) => adjacency.set(node.tableKey, new Set<string>()))
  graph.edges.forEach((edge) => {
    adjacency.get(edge.sourceTableKey)?.add(edge.targetTableKey)
    adjacency.get(edge.targetTableKey)?.add(edge.sourceTableKey)
  })

  const visited = new Set<string>([centerKey])
  const queue: Array<{ key: string; level: number }> = [{ key: centerKey, level: 0 }]

  while (queue.length) {
    const current = queue.shift()
    if (!current) {
      continue
    }

    if (current.level >= depth) {
      continue
    }

    for (const next of adjacency.get(current.key) ?? []) {
      if (visited.has(next)) {
        continue
      }

      visited.add(next)
      queue.push({ key: next, level: current.level + 1 })
    }
  }

  return {
    ...graph,
    nodes: graph.nodes.filter((node) => visited.has(node.tableKey)),
    edges: graph.edges.filter((edge) => visited.has(edge.sourceTableKey) && visited.has(edge.targetTableKey)),
  }
}

const scopedGraph = computed(() => buildScopedGraph(props.graph, centerTableKey.value, visibleDepth.value))

const graphSignature = computed(() => JSON.stringify({
  database: scopedGraph.value.database,
  center: centerTableKey.value,
  depth: visibleDepth.value,
  expandAllFields: expandAllFields.value,
  expandedTableKeys: expandedTableKeys.value,
  nodes: scopedGraph.value.nodes.map((node) => node.tableKey),
  edges: scopedGraph.value.edges.map((edge) => `${edge.sourceTableKey}:${edge.targetTableKey}:${edge.relationType}:${edge.viaTableKey ?? ''}:${edge.sourceColumn}:${edge.targetColumn}`),
}))

const visibleNodeIds = computed(() => new Set(scopedGraph.value.nodes.map((node) => node.tableKey)))

const searchMatches = computed(() => {
  const query = normalizedSearchQuery.value
  if (!query) {
    return []
  }

  return props.graph.nodes
    .filter((node) => normalizeText(node.title).includes(query))
    .sort((left, right) => {
      const leftExact = normalizeText(left.title) === query ? 0 : 1
      const rightExact = normalizeText(right.title) === query ? 0 : 1
      if (leftExact !== rightExact) {
        return leftExact - rightExact
      }

      return left.title.localeCompare(right.title)
    })
})

const visibleMatchedNodeIds = computed(() => new Set(searchMatches.value
  .filter((node) => visibleNodeIds.value.has(node.tableKey))
  .map((node) => node.tableKey)))

const renderedNodes = computed((): Node<RenderNodeData>[] => {
  const matchedIds = visibleMatchedNodeIds.value
  const searchActive = normalizedSearchQuery.value.length > 0

  return graphNodes.value.map((node) => {
    const baseData = node.data ?? {
      tableKey: node.id,
      title: node.id,
      rowCountLabel: '未统计 rows',
      note: null,
      isJunction: false,
      columns: [],
      width: 256,
      height: 96,
      visibleFieldCount: 0,
      hiddenFieldCount: 0,
    }
    const palette = colorForNode(baseData.tableKey, baseData.isJunction)

    return {
      ...node,
      style: {
        ...(node.style ?? {}),
        '--overview-node-accent': palette.accent,
        '--overview-node-soft': palette.soft,
        '--overview-node-border': palette.border,
      },
      data: {
        tableKey: baseData.tableKey,
        title: baseData.title,
        rowCountLabel: baseData.rowCountLabel,
        note: baseData.note,
        isJunction: baseData.isJunction,
        columns: baseData.columns,
        width: baseData.width,
        height: baseData.height,
        visibleFieldCount: baseData.visibleFieldCount,
        hiddenFieldCount: baseData.hiddenFieldCount,
        isFocused: focusedTableKey.value === node.id,
        isMatched: matchedIds.has(node.id),
        isDimmed: searchActive && !matchedIds.has(node.id),
        accentColor: palette.accent,
        accentSoft: palette.soft,
        accentBorder: palette.border,
      },
    }
  })
})

async function fitGraphToContent() {
  await nextTick()
  await flowRef.value?.fitView?.({ padding: 0.16, duration: 260, maxZoom: 1.08 })
}

async function focusNode(tableKey: string | null) {
  if (!tableKey) {
    await fitGraphToContent()
    return
  }

  const targetNode = graphNodes.value.find((node) => node.id === tableKey)
  if (!targetNode) {
    await fitGraphToContent()
    return
  }

  await nextTick()
  await flowRef.value?.fitView?.({ nodes: [targetNode], padding: 0.52, duration: 320, maxZoom: 1.35 })
}

function resetScope() {
  centerTableKey.value = null
  focusedTableKey.value = null
  contextMenu.value = null
}

function isExpanded(tableKey: string) {
  return expandAllFields.value || expandedTableKeys.value.includes(tableKey)
}

function toggleNodeExpansion(tableKey: string) {
  if (expandAllFields.value) {
    return
  }

  expandedTableKeys.value = expandedTableKeys.value.includes(tableKey)
    ? expandedTableKeys.value.filter((entry) => entry !== tableKey)
    : [...expandedTableKeys.value, tableKey]
}

function visibleColumns(data: RenderNodeData | undefined) {
  if (!data) {
    return []
  }

  return data.columns.slice(0, data.visibleFieldCount)
}

async function locateTable(tableKey: string) {
  focusedTableKey.value = tableKey
  contextMenu.value = null
  if (centerTableKey.value && !visibleNodeIds.value.has(tableKey)) {
    centerTableKey.value = tableKey
    return
  }

  await focusNode(tableKey)
}

async function locateFirstMatch() {
  const firstMatch = searchMatches.value[0]
  if (!firstMatch) {
    return
  }

  await locateTable(firstMatch.tableKey)
}

async function rebuildLayout() {
  const currentGeneration = ++layoutGeneration
  layoutPending.value = true
  layoutError.value = null

  try {
    const layout = await buildDatabaseOverviewLayout(scopedGraph.value, layoutOptions.value)
    if (currentGeneration !== layoutGeneration) {
      return
    }

    graphNodes.value = layout.nodes
    graphEdges.value = layout.edges
    if (layout.nodes.length === 1) {
      await focusNode(layout.nodes[0]?.id ?? null)
    }
    else if (focusedTableKey.value && layout.nodes.some((node) => node.id === focusedTableKey.value)) {
      await focusNode(focusedTableKey.value)
    }
    else {
      await fitGraphToContent()
    }
  }
  catch (error) {
    if (currentGeneration !== layoutGeneration) {
      return
    }

    layoutError.value = error instanceof Error ? error.message : '关系图布局失败'
  }
  finally {
    if (currentGeneration === layoutGeneration) {
      layoutPending.value = false
    }
  }
}

watch(graphSignature, () => {
  void rebuildLayout()
}, { immediate: true })

watch(() => props.graph.nodes.map((node) => node.tableKey).join('|'), () => {
  if (centerTableKey.value && !props.graph.nodes.some((node) => node.tableKey === centerTableKey.value)) {
    centerTableKey.value = null
  }

  if (focusedTableKey.value && !props.graph.nodes.some((node) => node.tableKey === focusedTableKey.value)) {
    focusedTableKey.value = null
  }
})

function handleNodeDoubleClick(event: NodeMouseEvent) {
  const data = event.node.data as DatabaseOverviewNodeData | undefined
  if (!data) {
    return
  }

  store.openTable(data.tableKey)
}

function handleNodeContextMenu(event: NodeMouseEvent) {
  const data = event.node.data as DatabaseOverviewNodeData | undefined
  const pointerEvent = event.event
  if (!data || !(pointerEvent instanceof MouseEvent)) {
    return
  }

  pointerEvent.preventDefault()
  pointerEvent.stopPropagation()
  contextMenu.value = {
    x: pointerEvent.clientX,
    y: pointerEvent.clientY,
    tableKey: data.tableKey,
    title: data.title,
  }
}

async function centerOnContextNode() {
  const target = contextMenu.value
  if (!target) {
    return
  }

  centerTableKey.value = target.tableKey
  focusedTableKey.value = target.tableKey
  contextMenu.value = null
}

function openContextTable() {
  const target = contextMenu.value
  if (!target) {
    return
  }

  store.openTable(target.tableKey)
  contextMenu.value = null
}

function expandAllVisibleFields() {
  expandAllFields.value = true
}

function collapseAllVisibleFields() {
  expandAllFields.value = false
  expandedTableKeys.value = []
}

function closeContextMenu() {
  contextMenu.value = null
}

onMounted(() => {
  window.addEventListener('click', closeContextMenu)
})

onBeforeUnmount(() => {
  window.removeEventListener('click', closeContextMenu)
})

function miniMapNodeColor(node: Node<DatabaseOverviewNodeData>) {
  return node.data?.isJunction ? '#f59e0b' : '#14b8a6'
}

function edgePath(props: EdgeProps<DatabaseOverviewEdgeData>) {
  return props.data?.path || `M ${props.sourceX} ${props.sourceY} L ${props.targetX} ${props.targetY}`
}
</script>

<template>
  <div class="database-overview-shell">
    <div class="database-overview-toolbar">
      <div class="database-overview-toolbar-group database-overview-toolbar-group-wide">
        <n-select
          v-model:value="centerTableKey"
          clearable
          filterable
          size="small"
          class="database-overview-center-select"
          placeholder="中心表（留空 = 全库）"
          :options="tableOptions"
        />
        <n-select
          v-model:value="visibleDepth"
          size="small"
          class="database-overview-depth-select"
          :disabled="!centerTableKey"
          :options="depthOptions"
        />
        <n-button size="small" tertiary :disabled="!centerTableKey" @click="resetScope">查看全库</n-button>
      </div>
      <div class="database-overview-toolbar-group">
        <n-input
          v-model:value="searchQuery"
          size="small"
          clearable
          class="database-overview-search-input"
          placeholder="按表名搜索并定位"
          @keyup.enter="locateFirstMatch"
        />
        <n-button size="small" tertiary type="primary" :disabled="!searchMatches.length" @click="locateFirstMatch">定位</n-button>
        <n-button size="small" tertiary :loading="layoutPending" @click="rebuildLayout">重新整理</n-button>
      </div>
    </div>

    <div v-if="layoutError" class="database-overview-empty">{{ layoutError }}</div>

    <div v-else class="database-overview-stage">
      <div class="database-overview-canvas-wrap">
        <VueFlow
          ref="flowRef"
          :nodes="renderedNodes"
          :edges="graphEdges"
          class="database-overview-flow"
          :fit-view-on-init="false"
          :min-zoom="0.2"
          :max-zoom="2.2"
          :nodes-draggable="false"
          :nodes-focusable="false"
          :elements-selectable="false"
          :edges-updatable="false"
          :pan-on-drag="true"
          :zoom-on-double-click="false"
          @node-double-click="handleNodeDoubleClick"
          @node-context-menu="handleNodeContextMenu"
        >
          <Background :gap="18" :size="1" pattern-color="rgba(100, 116, 139, 0.12)" />
          <Controls position="bottom-left" :show-interactive="false" />
          <MiniMap class="database-overview-minimap" pannable zoomable :node-color="miniMapNodeColor" />

          <template #edge-overview-orthogonal="edgeProps">
            <g class="database-overview-edge">
              <path class="database-overview-edge-hit" :d="edgePath(edgeProps)" />
              <path
                class="database-overview-edge-path"
                :d="edgePath(edgeProps)"
                :style="edgeProps.style"
                :marker-end="edgeProps.markerEnd"
              >
                <title>{{ edgeProps.data?.tooltip }}</title>
              </path>
            </g>
          </template>

          <template #node-default="nodeProps">
            <div
              class="database-overview-node"
              :class="{
                'database-overview-node-junction': nodeProps.data?.isJunction,
                'database-overview-node-matched': nodeProps.data?.isMatched,
                'database-overview-node-focused': nodeProps.data?.isFocused,
                'database-overview-node-dimmed': nodeProps.data?.isDimmed,
              }"
              :style="nodeProps.data ? { width: `${nodeProps.data.width}px`, minHeight: `${nodeProps.data.height}px` } : undefined"
              @dblclick.stop="nodeProps.data && locateTable(nodeProps.data.tableKey)"
            >
              <div class="database-overview-node-header">
                <span class="database-overview-node-table-icon" :class="{ 'database-overview-node-table-icon-hidden': !showTableIcons }" aria-hidden="true"></span>
                <div class="database-overview-node-title">{{ nodeProps.data?.title ?? nodeProps.id }}</div>
                <div class="database-overview-node-meta">{{ nodeProps.data?.rowCountLabel ?? '未统计 rows' }}</div>
              </div>
              <div class="database-overview-field-list">
                <div v-for="column in visibleColumns(nodeProps.data)" :key="column.name" class="database-overview-field-row">
                  <span class="database-overview-field-icon">{{ column.isPrimaryKey || column.isForeignKey ? '🔑' : '·' }}</span>
                  <div class="database-overview-field-main">
                    <span class="database-overview-field-name">{{ column.name }}</span>
                    <span class="database-overview-field-type">
                      {{ column.displayType }}
                      <span v-if="showNotNull && !column.isNullable" class="database-overview-field-flag">NOT NULL</span>
                    </span>
                  </div>
                </div>
                <button
                  v-if="nodeProps.data && nodeProps.data.hiddenFieldCount > 0"
                  type="button"
                  class="database-overview-field-more"
                  @click.stop="toggleNodeExpansion(nodeProps.data.tableKey)"
                >
                  {{ isExpanded(nodeProps.data.tableKey) ? '收起字段' : `更多 (${nodeProps.data.hiddenFieldCount} 列)` }}
                </button>
              </div>
              <div v-if="nodeProps.data?.note" class="database-overview-node-subtitle">{{ nodeProps.data.note }}</div>
            </div>
          </template>
        </VueFlow>
      </div>

      <aside class="database-overview-options-panel">
        <div class="database-overview-options-title">图表选项</div>
        <div class="database-overview-options-group">
          <n-checkbox v-model:checked="expandAllFields">直接加载所有字段</n-checkbox>
          <n-checkbox v-model:checked="showNotNull">显示字段 NOT NULL 属性</n-checkbox>
          <n-checkbox v-model:checked="showTableIcons">显示表格图标</n-checkbox>
        </div>
        <div class="database-overview-options-actions">
          <n-button size="small" tertiary @click="expandAllVisibleFields">展开全部字段</n-button>
          <n-button size="small" tertiary @click="collapseAllVisibleFields">恢复默认 10 列</n-button>
        </div>
        <div class="database-overview-options-note">默认每张表最多显示 10 个字段，超过时可在节点底部点“更多”。</div>
      </aside>

      <div
        v-if="contextMenu"
        class="database-overview-context-menu"
        :style="{ left: `${contextMenu.x}px`, top: `${contextMenu.y}px` }"
      >
        <button type="button" class="database-overview-context-menu-item" @click="centerOnContextNode">设为中心表</button>
        <button type="button" class="database-overview-context-menu-item" @click="openContextTable">查看主表数据</button>
      </div>
    </div>

    <div v-if="layoutPending" class="database-overview-loading">正在整理关系图...</div>
  </div>
</template>