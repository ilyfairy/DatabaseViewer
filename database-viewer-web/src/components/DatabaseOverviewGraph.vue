<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue';
import { Background } from '@vue-flow/background';
import { Controls } from '@vue-flow/controls';
import { MiniMap } from '@vue-flow/minimap';
import { VueFlow, type Edge, type EdgeProps, type Node, type NodeMouseEvent } from '@vue-flow/core';
import '@vue-flow/core/dist/style.css';
import '@vue-flow/core/dist/theme-default.css';
import '@vue-flow/controls/dist/style.css';
import '@vue-flow/minimap/dist/style.css';
import { NButton, NCheckbox, NInput, NSelect } from 'naive-ui';
import { buildDatabaseOverviewLayout, type DatabaseOverviewEdgeData, type DatabaseOverviewLayoutOptions, type DatabaseOverviewNodeData } from '../lib/databaseOverviewLayout';
import { useExplorerStore } from '../stores/explorer';
import type { DatabaseGraph } from '../types/explorer';

const props = defineProps<{
  graph: DatabaseGraph
}>();

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

const store = useExplorerStore();
const flowRef = ref<{ fitView?: (options?: unknown) => Promise<boolean> | boolean } | null>(null);
const graphNodes = shallowRef<Node<DatabaseOverviewNodeData>[]>([]);
const graphEdges = shallowRef<Edge<DatabaseOverviewEdgeData>[]>([]);
const layoutPending = ref(false);
const layoutError = ref<string | null>(null);
const centerTableKey = ref<string | null>(null);
const visibleDepth = ref<number>(2);
const searchQuery = ref('');
const focusedTableKey = ref<string | null>(null);
const contextMenu = ref<OverviewContextMenuState | null>(null);
const expandAllFields = ref(false);
const showNotNull = ref(false);
const showTableIcons = ref(true);
const expandedTableKeys = ref<string[]>([]);
let layoutGeneration = 0;

const tableOptions = computed(() => props.graph.nodes
  .slice()
  .sort((left, right) => left.title.localeCompare(right.title))
  .map((node) => ({
    label: node.title,
    value: node.tableKey,
  })));

const depthOptions = [1, 2, 3, 4, 5].map((depth) => ({
  label: `${depth} 层`,
  value: depth,
}));

const overviewPalette = [
  { accent: '#4f8cc9', soft: '#eaf3ff', border: '#72a8de' },
  { accent: '#d8923b', soft: '#fff4e5', border: '#ebb45a' },
  { accent: '#56a878', soft: '#edf9f1', border: '#72c292' },
  { accent: '#8b6fb8', soft: '#f2ecfb', border: '#a188cf' },
  { accent: '#cc6f7a', soft: '#fff0f2', border: '#e08c96' },
  { accent: '#4ea6a6', soft: '#eaf8f8', border: '#73bcbc' },
];

function colorForNode(tableKey: string, isJunction: boolean) {
  if (isJunction) {
    return { accent: '#c98b2f', soft: '#fff4df', border: '#e2aa57' };
  }

  let hash = 0;
  for (const char of tableKey) {
    hash = ((hash << 5) - hash) + char.charCodeAt(0);
    hash |= 0;
  }

  return overviewPalette[Math.abs(hash) % overviewPalette.length]!;
}

function normalizeText(value: string) {
  return value.trim().toLowerCase();
}

const normalizedSearchQuery = computed(() => normalizeText(searchQuery.value));
const layoutOptions = computed<DatabaseOverviewLayoutOptions>(() => ({
  expandAllFields: expandAllFields.value,
  expandedTableKeys: expandedTableKeys.value,
}));

function buildScopedGraph(graph: DatabaseGraph, centerKey: string | null, depth: number): DatabaseGraph {
  if (!centerKey) {
    return graph;
  }

  const adjacency = new Map<string, Set<string>>();
  graph.nodes.forEach((node) => adjacency.set(node.tableKey, new Set<string>()));
  graph.edges.forEach((edge) => {
    adjacency.get(edge.sourceTableKey)?.add(edge.targetTableKey);
    adjacency.get(edge.targetTableKey)?.add(edge.sourceTableKey);
  });

  const visited = new Set<string>([centerKey]);
  const queue: Array<{ key: string; level: number }> = [{ key: centerKey, level: 0 }];

  while (queue.length) {
    const current = queue.shift();
    if (!current) {
      continue;
    }

    if (current.level >= depth) {
      continue;
    }

    for (const next of adjacency.get(current.key) ?? []) {
      if (visited.has(next)) {
        continue;
      }

      visited.add(next);
      queue.push({ key: next, level: current.level + 1 });
    }
  }

  return {
    ...graph,
    nodes: graph.nodes.filter((node) => visited.has(node.tableKey)),
    edges: graph.edges.filter((edge) => visited.has(edge.sourceTableKey) && visited.has(edge.targetTableKey)),
  };
}

const scopedGraph = computed(() => buildScopedGraph(props.graph, centerTableKey.value, visibleDepth.value));

const graphSignature = computed(() => JSON.stringify({
  database: scopedGraph.value.database,
  center: centerTableKey.value,
  depth: visibleDepth.value,
  expandAllFields: expandAllFields.value,
  expandedTableKeys: expandedTableKeys.value,
  nodes: scopedGraph.value.nodes.map((node) => node.tableKey),
  edges: scopedGraph.value.edges.map((edge) => `${edge.sourceTableKey}:${edge.targetTableKey}:${edge.relationType}:${edge.viaTableKey ?? ''}:${edge.sourceColumn}:${edge.targetColumn}`),
}));

const visibleNodeIds = computed(() => new Set(scopedGraph.value.nodes.map((node) => node.tableKey)));

const searchMatches = computed(() => {
  const query = normalizedSearchQuery.value;
  if (!query) {
    return [];
  }

  return props.graph.nodes
    .filter((node) => normalizeText(node.title).includes(query))
    .sort((left, right) => {
      const leftExact = normalizeText(left.title) === query ? 0 : 1;
      const rightExact = normalizeText(right.title) === query ? 0 : 1;
      if (leftExact !== rightExact) {
        return leftExact - rightExact;
      }

      return left.title.localeCompare(right.title);
    });
});

const visibleMatchedNodeIds = computed(() => new Set(searchMatches.value
  .filter((node) => visibleNodeIds.value.has(node.tableKey))
  .map((node) => node.tableKey)));

const renderedNodes = computed((): Node<RenderNodeData>[] => {
  const matchedIds = visibleMatchedNodeIds.value;
  const searchActive = normalizedSearchQuery.value.length > 0;

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
    };
    const palette = colorForNode(baseData.tableKey, baseData.isJunction);

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
    };
  });
});

async function fitGraphToContent() {
  await nextTick();
  await flowRef.value?.fitView?.({ padding: 0.16, duration: 260, maxZoom: 1.08 });
}

async function focusNode(tableKey: string | null) {
  if (!tableKey) {
    await fitGraphToContent();
    return;
  }

  const targetNode = graphNodes.value.find((node) => node.id === tableKey);
  if (!targetNode) {
    await fitGraphToContent();
    return;
  }

  await nextTick();
  await flowRef.value?.fitView?.({ nodes: [targetNode], padding: 0.52, duration: 320, maxZoom: 1.35 });
}

function resetScope() {
  centerTableKey.value = null;
  focusedTableKey.value = null;
  contextMenu.value = null;
}

function isExpanded(tableKey: string) {
  return expandAllFields.value || expandedTableKeys.value.includes(tableKey);
}

function toggleNodeExpansion(tableKey: string) {
  if (expandAllFields.value) {
    return;
  }

  expandedTableKeys.value = expandedTableKeys.value.includes(tableKey)
    ? expandedTableKeys.value.filter((entry) => entry !== tableKey)
    : [...expandedTableKeys.value, tableKey];
}

function visibleColumns(data: RenderNodeData | undefined) {
  if (!data) {
    return [];
  }

  return data.columns.slice(0, data.visibleFieldCount);
}

async function locateTable(tableKey: string) {
  focusedTableKey.value = tableKey;
  contextMenu.value = null;
  if (centerTableKey.value && !visibleNodeIds.value.has(tableKey)) {
    centerTableKey.value = tableKey;
    return;
  }

  await focusNode(tableKey);
}

async function locateFirstMatch() {
  const firstMatch = searchMatches.value[0];
  if (!firstMatch) {
    return;
  }

  await locateTable(firstMatch.tableKey);
}

async function rebuildLayout() {
  const currentGeneration = ++layoutGeneration;
  layoutPending.value = true;
  layoutError.value = null;

  try {
    const layout = await buildDatabaseOverviewLayout(scopedGraph.value, layoutOptions.value);
    if (currentGeneration !== layoutGeneration) {
      return;
    }

    graphNodes.value = layout.nodes;
    graphEdges.value = layout.edges;
    if (layout.nodes.length === 1) {
      await focusNode(layout.nodes[0]?.id ?? null);
    }
    else if (focusedTableKey.value && layout.nodes.some((node) => node.id === focusedTableKey.value)) {
      await focusNode(focusedTableKey.value);
    }
    else {
      await fitGraphToContent();
    }
  }
  catch (error) {
    if (currentGeneration !== layoutGeneration) {
      return;
    }

    layoutError.value = error instanceof Error ? error.message : '关系图布局失败';
  }
  finally {
    if (currentGeneration === layoutGeneration) {
      layoutPending.value = false;
    }
  }
}

watch(graphSignature, () => {
  void rebuildLayout();
}, { immediate: true });

watch(() => props.graph.nodes.map((node) => node.tableKey).join('|'), () => {
  if (centerTableKey.value && !props.graph.nodes.some((node) => node.tableKey === centerTableKey.value)) {
    centerTableKey.value = null;
  }

  if (focusedTableKey.value && !props.graph.nodes.some((node) => node.tableKey === focusedTableKey.value)) {
    focusedTableKey.value = null;
  }
});

function handleNodeDoubleClick(event: NodeMouseEvent) {
  const data = event.node.data as DatabaseOverviewNodeData | undefined;
  if (!data) {
    return;
  }

  store.openTable(data.tableKey);
}

function handleNodeContextMenu(event: NodeMouseEvent) {
  const data = event.node.data as DatabaseOverviewNodeData | undefined;
  const pointerEvent = event.event;
  if (!data || !(pointerEvent instanceof MouseEvent)) {
    return;
  }

  pointerEvent.preventDefault();
  pointerEvent.stopPropagation();
  contextMenu.value = {
    x: pointerEvent.clientX,
    y: pointerEvent.clientY,
    tableKey: data.tableKey,
    title: data.title,
  };
}

async function centerOnContextNode() {
  const target = contextMenu.value;
  if (!target) {
    return;
  }

  centerTableKey.value = target.tableKey;
  focusedTableKey.value = target.tableKey;
  contextMenu.value = null;
}

function openContextTable() {
  const target = contextMenu.value;
  if (!target) {
    return;
  }

  store.openTable(target.tableKey);
  contextMenu.value = null;
}

function expandAllVisibleFields() {
  expandAllFields.value = true;
}

function collapseAllVisibleFields() {
  expandAllFields.value = false;
  expandedTableKeys.value = [];
}

function closeContextMenu() {
  contextMenu.value = null;
}

onMounted(() => {
  window.addEventListener('click', closeContextMenu);
  window.addEventListener('contextmenu', closeContextMenu, true);
  window.addEventListener('blur', closeContextMenu);
});

onBeforeUnmount(() => {
  window.removeEventListener('click', closeContextMenu);
  window.removeEventListener('contextmenu', closeContextMenu, true);
  window.removeEventListener('blur', closeContextMenu);
});

function miniMapNodeColor(node: Node<DatabaseOverviewNodeData>) {
  return node.data?.isJunction ? '#f59e0b' : '#14b8a6';
}

function edgePath(props: EdgeProps<DatabaseOverviewEdgeData>) {
  return props.data?.path || `M ${props.sourceX} ${props.sourceY} L ${props.targetX} ${props.targetY}`;
}
</script>

<template>
  <div class="database-overview-shell">
    <div class="database-overview-toolbar">
      <div class="database-overview-toolbar-group database-overview-toolbar-group-wide">
        <NSelect
          v-model:value="centerTableKey"
          clearable
          filterable
          size="small"
          class="database-overview-center-select"
          placeholder="中心表（留空 = 全库）"
          :options="tableOptions"
        />
        <NSelect
          v-model:value="visibleDepth"
          size="small"
          class="database-overview-depth-select"
          :disabled="!centerTableKey"
          :options="depthOptions"
        />
        <NButton size="small" tertiary :disabled="!centerTableKey" @click="resetScope">查看全库</NButton>
      </div>
      <div class="database-overview-toolbar-group">
        <NInput
          v-model:value="searchQuery"
          size="small"
          clearable
          class="database-overview-search-input"
          placeholder="按表名搜索并定位"
          @keyup.enter="locateFirstMatch"
        />
        <NButton size="small" tertiary type="primary" :disabled="!searchMatches.length" @click="locateFirstMatch">定位</NButton>
        <NButton size="small" tertiary :loading="layoutPending" @click="rebuildLayout">重新整理</NButton>
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
          <NCheckbox v-model:checked="expandAllFields">直接加载所有字段</NCheckbox>
          <NCheckbox v-model:checked="showNotNull">显示字段 NOT NULL 属性</NCheckbox>
          <NCheckbox v-model:checked="showTableIcons">显示表格图标</NCheckbox>
        </div>
        <div class="database-overview-options-actions">
          <NButton size="small" tertiary @click="expandAllVisibleFields">展开全部字段</NButton>
          <NButton size="small" tertiary @click="collapseAllVisibleFields">恢复默认 10 列</NButton>
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

<style scoped lang="scss">
.database-overview-shell {
  position: relative;
  height: 100%;
  min-height: 0;
  display: flex;
  flex-direction: column;
  gap: $gap-xl;
  padding: $gap-2xl;
  border-radius: var(--radius-xl);
  overflow: hidden;
  border: 1px solid $color-border-light;
  background: $color-surface-white;
}

.database-overview-toolbar {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: $gap-xl;

  &-group {
    display: flex;
    align-items: center;
    gap: $gap-lg;
    min-width: 0;
    padding: $gap-lg $gap-xl;
    border-radius: var(--radius-lg);
    background: rgba(255, 255, 255, 0.9);
    border: 1px solid $color-border-light;
    box-shadow: 0 10px 28px $shadow-subtle;
    backdrop-filter: blur(10px);

    &-wide {
      flex: 1 1 auto;
      max-width: min(700px, 62vw);
    }
  }
}

.database-overview-center-select {
  width: min(360px, 34vw);
}

.database-overview-depth-select {
  width: 104px;
}

.database-overview-search-input {
  width: min(260px, 24vw);
}

.database-overview-stage {
  position: relative;
  min-height: 0;
  height: 100%;
  flex: 1 1 auto;
  display: grid;
  grid-template-columns: minmax(0, 1fr) 250px;
  gap: $gap-xl;
  overflow: hidden;
}

.database-overview-canvas-wrap {
  min-width: 0;
  min-height: 0;
  height: 100%;
  overflow: hidden;
  border-radius: calc(var(--radius-xl) - 2px);
}

.database-overview-flow {
  width: 100%;
  height: 100%;

  :deep(.vue-flow__node),
  :deep(.vue-flow__node-default) {
    width: 172px;
    padding: 0;
    background: transparent;
    border: 0;
    box-shadow: none;
  }

  :deep(.vue-flow__controls) {
    box-shadow: 0 10px 24px rgba(15, 23, 42, 0.12);
    border-radius: var(--radius-md);
    overflow: hidden;
  }
}

.database-overview-minimap {
  background: rgba(255, 255, 255, 0.9);
  border: 1px solid $color-border-strong;
  border-radius: var(--radius-md);
  box-shadow: 0 12px 28px $shadow-medium;
}

.database-overview-edge-path {
  fill: none;
  stroke-linecap: round;
  stroke-linejoin: round;
}

.database-overview-edge-hit {
  fill: none;
  stroke: transparent;
  stroke-width: 14;
}

// ── Overview node ──
.database-overview-node {
  display: grid;
  gap: 0;
  min-width: 156px;
  padding: 0;
  border-radius: var(--radius-xl);
  border: 2px solid var(--overview-node-border, #72a8de);
  background: $color-surface-white;
  overflow: hidden;
  box-shadow: 0 10px 22px $shadow-subtle;
  transition: transform 180ms ease, box-shadow 180ms ease, border-color 180ms ease, opacity 180ms ease;

  &-header {
    position: relative;
    display: grid;
    grid-template-columns: auto 1fr auto;
    align-items: center;
    gap: $gap-sm;
    min-height: 30px;
    padding: $gap-sm $gap-md;
    border-bottom: 2px solid var(--overview-node-border, #72a8de);
    background: var(--overview-node-soft, #eaf3ff);
  }

  &-table-icon {
    width: 14px;
    height: 14px;
    border: 1.5px solid var(--overview-node-accent, #4f8cc9);
    border-radius: 3px;
    background:
      linear-gradient(var(--overview-node-accent, #4f8cc9), var(--overview-node-accent, #4f8cc9)) 50% 33% / 100% 1px no-repeat,
      linear-gradient(var(--overview-node-accent, #4f8cc9), var(--overview-node-accent, #4f8cc9)) 50% 66% / 100% 1px no-repeat,
      linear-gradient(var(--overview-node-accent, #4f8cc9), var(--overview-node-accent, #4f8cc9)) 33% 50% / 1px 100% no-repeat,
      linear-gradient(var(--overview-node-accent, #4f8cc9), var(--overview-node-accent, #4f8cc9)) 66% 50% / 1px 100% no-repeat;

    &-hidden {
      visibility: hidden;
    }
  }

  &-junction {
    border-color: var(--overview-node-border, #e2aa57);
  }

  &-matched {
    box-shadow: 0 16px 32px rgba(59, 130, 246, 0.14);
  }

  &-focused {
    transform: translateY(-1px);
    box-shadow: 0 18px 36px $shadow-strong;
  }

  &-dimmed {
    opacity: 0.42;
  }

  &-title {
    color: $color-text-primary;
    font-size: 10.5px;
    font-weight: 700;
    line-height: 1.15;
    white-space: nowrap;
    text-align: center;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  &-meta {
    color: rgba(15, 23, 42, 0.72);
    font-size: $font-size-2xs;
    font-weight: 700;
    white-space: nowrap;
  }

  &-subtitle {
    color: $color-text-secondary;
    font-size: $font-size-sm;
    line-height: 1.45;
    word-break: break-word;
    padding: 6px 12px 8px;
    display: -webkit-box;
    overflow: hidden;
    line-clamp: 2;
    -webkit-box-orient: vertical;
    -webkit-line-clamp: 2;
  }
}

// ── Overview field list ──
.database-overview-field-list {
  display: grid;
  gap: 0;
  padding: 5px $gap-md $gap-md;
}

.database-overview-field-row {
  display: grid;
  grid-template-columns: 14px minmax(0, 1fr);
  align-items: center;
  gap: 3px;
  min-height: 18px;
}

.database-overview-field-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 16px;
  color: $color-accent-amber-light;
  font-size: $font-size-base;
  line-height: 1;
}

.database-overview-field-main {
  min-width: 0;
  display: flex;
  align-items: baseline;
  gap: 3px;
  overflow: hidden;
}

.database-overview-field-name {
  flex: 0 1 auto;
  min-width: 0;
  color: #1e293b;
  font-size: 10.5px;
  line-height: 1.3;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  text-align: left;
}

.database-overview-field-type {
  flex: 0 0 auto;
  color: $color-text-muted;
  font-size: $font-size-sm;
  line-height: 1.3;
  white-space: nowrap;
  text-align: left;
}

.database-overview-field-flag {
  margin-left: $gap-md;
  color: $color-text-secondary;
  font-size: $font-size-xs;
  font-weight: 700;
}

.database-overview-field-more {
  display: block;
  width: 100%;
  margin-top: $gap-xs;
  padding: 3px $gap-sm $gap-xs 15px;
  border: 0;
  outline: 0;
  box-shadow: none;
  appearance: none;
  border-radius: var(--radius-sm);
  color: $color-text-muted;
  font-size: $font-size-sm;
  text-align: left;
  background: transparent;
  cursor: pointer;

  &:hover {
    background: rgba(148, 163, 184, 0.08);
    color: $color-text-tertiary;
  }
}

// ── Overview options panel ──
.database-overview-options-panel {
  display: grid;
  align-content: start;
  gap: $gap-xl;
  padding: $gap-2xl;
  border-radius: calc(var(--radius-xl) - 2px);
  border: 1px solid $color-border-light;
  background: $color-bg-subtle;
  overflow: auto;
}

.database-overview-options-title {
  color: $color-text-primary;
  font-size: $font-size-md;
  font-weight: 700;
}

.database-overview-options-group {
  display: grid;
  gap: $gap-lg;
}

.database-overview-options-actions {
  display: grid;
  gap: $gap-md;
}

.database-overview-options-note {
  color: $color-text-secondary;
  font-size: $font-size-sm;
  line-height: 1.45;
}

.database-overview-empty {
  padding: 20px $gap-lg;
  color: $color-text-tertiary;
}

.database-overview-loading {
  position: absolute;
  left: 50%;
  bottom: 14px;
  transform: translateX(-50%);
  z-index: 5;
  padding: 6px 10px;
  border-radius: var(--radius-md);
  background: rgba(15, 23, 42, 0.76);
  color: rgba(255, 255, 255, 0.94);
  font-size: 11px;
  box-shadow: 0 12px 28px rgba(15, 23, 42, 0.18);
}

// ── Overview context menu ──
.database-overview-context-menu {
  position: fixed;
  z-index: 20;
  min-width: 160px;
  padding: $gap-md;
  border-radius: var(--radius-md);
  background: rgba(255, 255, 255, 0.98);
  border: 1px solid rgba(148, 163, 184, 0.2);
  box-shadow: 0 16px 36px $shadow-strong;
  transform-origin: top left;
  animation: context-menu-enter 140ms ease;

  &-item {
    width: 100%;
    padding: 7px $gap-xl;
    border: 0;
    outline: 0;
    box-shadow: none;
    appearance: none;
    border-radius: var(--radius-sm);
    text-align: left;
    color: $color-text-primary;
    background: transparent;
    cursor: pointer;

    &:hover {
      background: rgba(239, 246, 255, 0.92);
    }
  }
}

@media (max-width: 1120px) {
  .database-overview-toolbar {
    flex-direction: column;
    align-items: stretch;
  }

  .database-overview-stage {
    grid-template-columns: 1fr;
  }

  .database-overview-toolbar-group,
  .database-overview-toolbar-group-wide {
    max-width: none;
  }

  .database-overview-center-select,
  .database-overview-search-input {
    width: 100%;
  }
}
</style>