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
import { NButton, NCheckbox, NInput } from 'naive-ui';
import type { DropdownOption } from 'naive-ui';
import ContextDropdown from './ContextDropdown.vue';
import { buildDatabaseOverviewLayout, type DatabaseOverviewEdgeData, type DatabaseOverviewLayoutOptions, type DatabaseOverviewNodeData } from '../lib/databaseOverviewLayout';
import { useExplorerStore } from '../stores/explorer';
import type { DatabaseGraph } from '../types/explorer';

const props = defineProps<{
  graph: DatabaseGraph
}>();

type FlowViewport = {
  x: number
  y: number
  zoom: number
}

type LayoutAnchorSnapshot = {
  tableKey: string
  x: number
  y: number
}

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
const flowRef = ref<{
  fitView?: (options?: unknown) => Promise<boolean> | boolean
  getViewport?: () => FlowViewport
  setViewport?: (viewport: FlowViewport, options?: unknown) => Promise<boolean> | boolean
} | null>(null);
const graphNodes = shallowRef<Node<DatabaseOverviewNodeData>[]>([]);
const graphEdges = shallowRef<Edge<DatabaseOverviewEdgeData>[]>([]);
const layoutPending = ref(false);
const layoutError = ref<string | null>(null);
const searchQuery = ref('');
const focusedTableKey = ref<string | null>(null);
const contextMenu = ref<OverviewContextMenuState | null>(null);
const contextMenuOptions = computed<DropdownOption[]>(() => contextMenu.value
  ? [
      { label: '查看主表数据', key: 'open-table' },
      { label: '表设计', key: 'open-table-design' },
    ]
  : []);
const expandAllFields = ref(false);
const showNotNull = ref(false);
const showTableIcons = ref(true);
const expandedTableKeys = ref<string[]>([]);
const pendingViewportAnchorTableKey = ref<string | null>(null);
let layoutGeneration = 0;

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
const scopedGraph = computed(() => props.graph);

const graphStructureSignature = computed(() => JSON.stringify({
  database: scopedGraph.value.database,
  nodes: scopedGraph.value.nodes.map((node) => node.tableKey),
  edges: scopedGraph.value.edges.map((edge) => `${edge.sourceTableKey}:${edge.targetTableKey}:${edge.relationType}:${edge.viaTableKey ?? ''}:${edge.sourceColumn}:${edge.targetColumn}`),
}));

const fieldVisibilitySignature = computed(() => JSON.stringify({
  expandAllFields: expandAllFields.value,
  expandedTableKeys: expandedTableKeys.value,
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

function isExpanded(tableKey: string) {
  return expandAllFields.value || expandedTableKeys.value.includes(tableKey);
}

function toggleNodeExpansion(tableKey: string) {
  if (expandAllFields.value) {
    return;
  }

  pendingViewportAnchorTableKey.value = tableKey;
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
  await focusNode(tableKey);
}

async function locateFirstMatch() {
  const firstMatch = searchMatches.value[0];
  if (!firstMatch) {
    return;
  }

  await locateTable(firstMatch.tableKey);
}

async function rebuildLayout(options?: { preserveViewport?: boolean; preserveAnchorTableKey?: string | null }) {
  const currentGeneration = ++layoutGeneration;
  layoutPending.value = true;
  layoutError.value = null;
  const savedViewport = options?.preserveViewport ? flowRef.value?.getViewport?.() ?? null : null;
  const anchorSnapshot: LayoutAnchorSnapshot | null = savedViewport && options?.preserveAnchorTableKey
    ? (() => {
        const anchorNode = graphNodes.value.find((node) => node.id === options.preserveAnchorTableKey);
        if (!anchorNode) {
          return null;
        }

        return {
          tableKey: anchorNode.id,
          x: anchorNode.position.x,
          y: anchorNode.position.y,
        };
      })()
    : null;

  try {
    const layout = await buildDatabaseOverviewLayout(scopedGraph.value, layoutOptions.value);
    if (currentGeneration !== layoutGeneration) {
      return;
    }

    graphNodes.value = layout.nodes;
    graphEdges.value = layout.edges;
    if (savedViewport) {
      await nextTick();
      const anchorNode = anchorSnapshot
        ? layout.nodes.find((node) => node.id === anchorSnapshot.tableKey)
        : null;
      const nextViewport = anchorNode
        ? {
            ...savedViewport,
            x: savedViewport.x + ((anchorSnapshot?.x ?? anchorNode.position.x) - anchorNode.position.x) * savedViewport.zoom,
            y: savedViewport.y + ((anchorSnapshot?.y ?? anchorNode.position.y) - anchorNode.position.y) * savedViewport.zoom,
          }
        : savedViewport;
      await flowRef.value?.setViewport?.(nextViewport, { duration: 0 });
      return;
    }

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

watch(graphStructureSignature, () => {
  void rebuildLayout();
}, { immediate: true });

watch(fieldVisibilitySignature, (_nextValue, previousValue) => {
  if (previousValue === undefined) {
    return;
  }

  const anchorTableKey = pendingViewportAnchorTableKey.value;
  pendingViewportAnchorTableKey.value = null;
  void rebuildLayout({ preserveViewport: true, preserveAnchorTableKey: anchorTableKey });
});

watch(() => props.graph.nodes.map((node) => node.tableKey).join('|'), () => {
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

function openContextTable() {
  const target = contextMenu.value;
  if (!target) {
    return;
  }

  store.openTable(target.tableKey);
  contextMenu.value = null;
}

function openContextTableDesign() {
  const target = contextMenu.value;
  if (!target) {
    return;
  }

  void store.openTableDesign(target.tableKey);
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

function handleContextMenuShow(value: boolean) {
  if (!value) {
    closeContextMenu();
  }
}

function handleContextMenuSelect(key: string | number) {
  switch (key) {
    case 'open-table':
      openContextTable();
      return;
    case 'open-table-design':
      openContextTableDesign();
      return;
    default:
      return;
  }
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
        <NButton size="small" tertiary :loading="layoutPending" @click="() => rebuildLayout()">重新整理</NButton>
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

      <ContextDropdown
        :show="!!contextMenu"
        :x="contextMenu?.x ?? 0"
        :y="contextMenu?.y ?? 0"
        :options="contextMenuOptions"
        @update:show="handleContextMenuShow"
        @select="handleContextMenuSelect"
      />
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

.database-overview-flow {
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