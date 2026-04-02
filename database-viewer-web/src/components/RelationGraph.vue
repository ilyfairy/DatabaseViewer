<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { NButton, NTag } from 'naive-ui';
import { useExplorerStore } from '../stores/explorer';
import type { CellValue, ExplorerDetailPanel, ForeignKeyRef, ReverseReferenceGroup, TableColumn, TableRow } from '../types/explorer';

const props = defineProps<{
  panel: ExplorerDetailPanel
  chainPanels: ExplorerDetailPanel[]
  tableKey: string
  title: string
  identityText: string
  outgoingRelations: Array<{
    fk: ForeignKeyRef
    column: TableColumn
    value: CellValue
  }>
  reverseGroups: ReverseReferenceGroup[]
}>();

type GraphNodeData = {
  kind: 'current' | 'ancestor' | 'incoming' | 'outgoing'
  title: string
  subtitle: string
  panelId: string
  tableKey?: string
  sourceTableKey?: string
  rowKey?: string
  columnName?: string
}

type LayoutNode = GraphNodeData & {
  id: string
  x: number
  y: number
  width: number
  height: number
  tone: 'current' | 'ancestor' | 'incoming' | 'outgoing'
  badge: string
}

type LayoutEdge = {
  id: string
  from: string
  to: string
  label: string
  tone: 'chain' | 'incoming' | 'outgoing'
}

const emit = defineEmits<{
  selectNode: [payload: GraphNodeData]
}>();

const store = useExplorerStore();

const NODE_WIDTH = 226;
const NODE_HEIGHT = 74;
const COLUMN_GAP = 92;
const ROW_GAP = 22;
const PADDING_X = 12;
const PADDING_Y = 12;
const CENTER_COLUMN_X = PADDING_X + NODE_WIDTH + COLUMN_GAP;
const RIGHT_COLUMN_X = CENTER_COLUMN_X + NODE_WIDTH + COLUMN_GAP;
const MAX_SIDE_NODES = 8;
const MIN_ZOOM = 0.45;
const MAX_ZOOM = 1.8;

const viewportRef = ref<HTMLDivElement | null>(null);
const zoom = ref(1);
const panX = ref(0);
const panY = ref(0);
const viewportSize = ref({ width: 0, height: 0 });
let resizeObserver: ResizeObserver | null = null;
let stopViewportPan: (() => void) | null = null;

function recordNodeId(tableKey: string, rowKey: string) {
  return `record:${tableKey}:${rowKey}`;
}

function fullTableLabel(tableKey: string) {
  const table = store.getTable(tableKey);
  if (!table) {
    return tableKey;
  }

  return table.schema ? `${table.schema}.${table.name}` : table.name;
}

function summarizeReverseRow(sourceTableKey: string, values: Record<string, unknown>) {
  const sourceTable = store.getLoadedTable(sourceTableKey);
  const syntheticRow = {
    rowKey: '__relation-preview__',
    ...values,
  } as TableRow;
  return sourceTable
    ? store.rowSummary(sourceTable, syntheticRow)
    : store.summarizeRowValues(syntheticRow);
}

const chainRecords = computed(() => props.chainPanels
  .map((panel) => {
    const table = store.getTable(panel.tableKey);
    const detail = store.getPanelRecord(panel);
    return {
      panel,
      table,
      detail,
    };
  })
  .filter((entry) => !!entry.detail));

const chainIdentityKeys = computed(() => new Set(chainRecords.value.map((entry) => `${entry.panel.tableKey}::${entry.panel.rowKey}`)));

const ancestorNodes = computed<GraphNodeData[]>(() => chainRecords.value
  .slice(0, -1)
  .map((entry) => ({
    kind: 'ancestor' as const,
    title: entry.table ? (entry.table.schema ? `${entry.table.schema}.${entry.table.name}` : entry.table.name) : entry.panel.tableKey,
    subtitle: entry.detail
      ? `${entry.panel.sourceLabel} · ${entry.detail.primaryKeys.map((key) => `${key}=${store.formatValue(entry.detail?.row[key] ?? null)}`).join(', ')}`
      : entry.panel.sourceLabel,
    panelId: entry.panel.id,
    tableKey: entry.panel.tableKey,
    rowKey: entry.panel.rowKey,
  })));

const currentNode = computed(() => ({
  id: recordNodeId(props.panel.tableKey, props.panel.rowKey),
  kind: 'current' as const,
  title: props.title,
  subtitle: props.identityText,
  panelId: props.panel.id,
  tableKey: props.panel.tableKey,
  rowKey: props.panel.rowKey,
}));

const incomingNodes = computed<GraphNodeData[]>(() => props.reverseGroups
  .flatMap((group) => group.rows.map((row) => ({ group, row })))
  .filter((entry, index, items) => {
    const identity = `${entry.group.sourceTableKey}::${entry.row.rowKey}`;
    if (chainIdentityKeys.value.has(identity)) {
      return false;
    }

    return items.findIndex((candidate) => `${candidate.group.sourceTableKey}::${candidate.row.rowKey}` === identity) === index;
  })
  .slice(0, MAX_SIDE_NODES)
  .map((entry) => ({
    kind: 'incoming' as const,
    title: fullTableLabel(entry.group.sourceTableKey),
    subtitle: summarizeReverseRow(entry.group.sourceTableKey, entry.row.values),
    panelId: props.panel.id,
    tableKey: entry.group.sourceTableKey,
    sourceTableKey: entry.group.sourceTableKey,
    rowKey: entry.row.rowKey,
  })));

const outgoingNodes = computed<GraphNodeData[]>(() => props.outgoingRelations
  .slice(0, MAX_SIDE_NODES)
  .map((entry) => ({
    kind: 'outgoing' as const,
    title: fullTableLabel(entry.fk.targetTableKey),
    subtitle: `${entry.fk.sourceColumn} = ${store.formatFieldValue(entry.column.name, entry.column.type, entry.value)}`,
    panelId: props.panel.id,
    tableKey: entry.fk.targetTableKey,
    columnName: entry.fk.sourceColumn,
  })));

const hiddenIncomingCount = computed(() => Math.max(0, props.reverseGroups.flatMap((group) => group.rows).length - incomingNodes.value.length));
const hiddenOutgoingCount = computed(() => Math.max(0, props.outgoingRelations.length - outgoingNodes.value.length));

const selectedNodeId = ref(currentNode.value.id);

watch(() => [
  currentNode.value.id,
  ancestorNodes.value.map((node) => recordNodeId(node.tableKey ?? '', node.rowKey ?? '')).join('|'),
  incomingNodes.value.map((node) => recordNodeId(node.sourceTableKey ?? node.tableKey ?? '', node.rowKey ?? '')).join('|'),
  outgoingNodes.value.map((node) => `outgoing:${node.columnName ?? ''}`).join('|'),
] as const, () => {
  const validIds = new Set([
    currentNode.value.id,
    ...ancestorNodes.value.map((node) => recordNodeId(node.tableKey ?? '', node.rowKey ?? '')),
    ...incomingNodes.value.map((node) => recordNodeId(node.sourceTableKey ?? node.tableKey ?? '', node.rowKey ?? '')),
    ...outgoingNodes.value.map((node) => `outgoing:${node.columnName ?? ''}`),
  ]);

  if (!validIds.has(selectedNodeId.value)) {
    selectedNodeId.value = currentNode.value.id;
  }
}, { immediate: true });

function sideColumnStartY(count: number, currentY: number) {
  if (count <= 0) {
    return currentY;
  }

  const stackHeight = (count * NODE_HEIGHT) + ((count - 1) * ROW_GAP);
  return Math.max(PADDING_Y + NODE_HEIGHT + 92, currentY + Math.round((NODE_HEIGHT - stackHeight) / 2));
}

const layoutNodes = computed<LayoutNode[]>(() => {
  const ancestors = ancestorNodes.value;
  const currentY = PADDING_Y + (ancestors.length ? NODE_HEIGHT + 104 : 56);
  const current = {
    ...currentNode.value,
    x: CENTER_COLUMN_X,
    y: currentY,
    width: NODE_WIDTH,
    height: NODE_HEIGHT,
    tone: 'current' as const,
    badge: '当前',
  };

  const ancestorsWidth = ancestors.length * NODE_WIDTH + Math.max(0, ancestors.length - 1) * 44;
  const ancestorsStartX = CENTER_COLUMN_X + Math.round((NODE_WIDTH - ancestorsWidth) / 2);
  const ancestorLayout = ancestors.map((node, index) => ({
    ...node,
    id: recordNodeId(node.tableKey ?? '', node.rowKey ?? ''),
    x: ancestorsStartX + index * (NODE_WIDTH + 44),
    y: PADDING_Y,
    width: NODE_WIDTH,
    height: NODE_HEIGHT,
    tone: 'ancestor' as const,
    badge: index === ancestors.length - 1 ? '上一步' : `链路 ${index + 1}`,
  }));

  const incomingStartY = sideColumnStartY(incomingNodes.value.length, currentY);
  const outgoingStartY = sideColumnStartY(outgoingNodes.value.length, currentY);

  const incomingLayout = incomingNodes.value.map((node, index) => ({
    ...node,
    id: recordNodeId(node.sourceTableKey ?? node.tableKey ?? '', node.rowKey ?? ''),
    x: PADDING_X,
    y: incomingStartY + index * (NODE_HEIGHT + ROW_GAP),
    width: NODE_WIDTH,
    height: NODE_HEIGHT,
    tone: 'incoming' as const,
    badge: '入向',
  }));

  const outgoingLayout = outgoingNodes.value.map((node, index) => ({
    ...node,
    id: `outgoing:${node.columnName ?? ''}`,
    x: RIGHT_COLUMN_X,
    y: outgoingStartY + index * (NODE_HEIGHT + ROW_GAP),
    width: NODE_WIDTH,
    height: NODE_HEIGHT,
    tone: 'outgoing' as const,
    badge: '出向',
  }));

  return [...ancestorLayout, current, ...incomingLayout, ...outgoingLayout];
});

const contentBounds = computed(() => {
  const minX = layoutNodes.value.reduce((value, node) => Math.min(value, node.x), Number.POSITIVE_INFINITY);
  const minY = layoutNodes.value.reduce((value, node) => Math.min(value, node.y), Number.POSITIVE_INFINITY);
  const maxX = layoutNodes.value.reduce((value, node) => Math.max(value, node.x + node.width), 0);
  const maxY = layoutNodes.value.reduce((value, node) => Math.max(value, node.y + node.height), 0);

  return {
    minX: Number.isFinite(minX) ? minX : 0,
    minY: Number.isFinite(minY) ? minY : 0,
    maxX,
    maxY,
    width: Math.max(760, maxX + PADDING_X),
    height: Math.max(420, maxY + PADDING_Y),
  };
});

const layoutNodeMap = computed(() => new Map(layoutNodes.value.map((node) => [node.id, node])));

const layoutEdges = computed<LayoutEdge[]>(() => {
  const edges: LayoutEdge[] = [];
  const ancestors = ancestorNodes.value;
  const current = currentNode.value;

  ancestors.forEach((node, index) => {
    const fromId = recordNodeId(node.tableKey ?? '', node.rowKey ?? '');
    const toId = index === ancestors.length - 1
      ? current.id
      : recordNodeId(ancestors[index + 1]?.tableKey ?? '', ancestors[index + 1]?.rowKey ?? '');
    edges.push({
      id: `chain:${fromId}`,
      from: fromId,
      to: toId,
      label: index === ancestors.length - 1 ? props.panel.sourceLabel : props.chainPanels[index + 1]?.sourceLabel ?? '关系',
      tone: 'chain',
    });
  });

  incomingNodes.value.forEach((node) => {
    const nodeId = recordNodeId(node.sourceTableKey ?? node.tableKey ?? '', node.rowKey ?? '');
    edges.push({
      id: `incoming:${nodeId}`,
      from: nodeId,
      to: current.id,
      label: '引用当前记录',
      tone: 'incoming',
    });
  });

  outgoingNodes.value.forEach((node) => {
    const nodeId = `outgoing:${node.columnName ?? ''}`;
    edges.push({
      id: `outgoing:${nodeId}`,
      from: current.id,
      to: nodeId,
      label: node.columnName ?? '外键跳转',
      tone: 'outgoing',
    });
  });

  return edges;
});

function edgePath(edge: LayoutEdge) {
  const from = layoutNodeMap.value.get(edge.from);
  const to = layoutNodeMap.value.get(edge.to);
  if (!from || !to) {
    return '';
  }

  const fromOnLeft = from.x < to.x;
  const sameColumn = Math.abs(from.x - to.x) < 8;

  const startX = sameColumn ? from.x + from.width / 2 : fromOnLeft ? from.x + from.width : from.x;
  const startY = sameColumn ? from.y + from.height : from.y + from.height / 2;
  const endX = sameColumn ? to.x + to.width / 2 : fromOnLeft ? to.x : to.x + to.width;
  const endY = sameColumn ? to.y : to.y + to.height / 2;

  const controlOffset = sameColumn
    ? Math.max(38, Math.abs(endY - startY) * 0.38)
    : Math.max(44, Math.abs(endX - startX) * 0.35);

  if (sameColumn) {
    return `M ${startX} ${startY} C ${startX} ${startY + controlOffset}, ${endX} ${endY - controlOffset}, ${endX} ${endY}`;
  }

  const controlX1 = fromOnLeft ? startX + controlOffset : startX - controlOffset;
  const controlX2 = fromOnLeft ? endX - controlOffset : endX + controlOffset;
  return `M ${startX} ${startY} C ${controlX1} ${startY}, ${controlX2} ${endY}, ${endX} ${endY}`;
}

const canvasHeight = computed(() => contentBounds.value.height);
const canvasWidth = computed(() => contentBounds.value.width);

const viewportTransform = computed(() => `translate(${panX.value}px, ${panY.value}px) scale(${zoom.value})`);

function clampZoom(value: number) {
  return Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, value));
}

function fitToView() {
  const viewport = viewportRef.value;
  if (!viewport) {
    return;
  }

  const bounds = contentBounds.value;
  const viewportWidth = viewport.clientWidth;
  const viewportHeight = viewport.clientHeight;
  if (!viewportWidth || !viewportHeight) {
    return;
  }

  const padding = 12;
  const availableWidth = Math.max(120, viewportWidth - padding * 2);
  const availableHeight = Math.max(120, viewportHeight - padding * 2);
  const nextZoom = clampZoom(Math.min(availableWidth / (bounds.maxX - bounds.minX || 1), availableHeight / (bounds.maxY - bounds.minY || 1), 1));

  zoom.value = nextZoom;
  panX.value = Math.round((viewportWidth - (bounds.maxX - bounds.minX) * nextZoom) / 2 - bounds.minX * nextZoom);
  panY.value = Math.round((viewportHeight - (bounds.maxY - bounds.minY) * nextZoom) / 2 - bounds.minY * nextZoom);
}

function zoomAt(nextZoom: number, originX: number, originY: number) {
  const boundedZoom = clampZoom(nextZoom);
  const scaleRatio = boundedZoom / zoom.value;
  panX.value = originX - ((originX - panX.value) * scaleRatio);
  panY.value = originY - ((originY - panY.value) * scaleRatio);
  zoom.value = boundedZoom;
}

function handleWheel(event: WheelEvent) {
  event.preventDefault();

  const viewport = viewportRef.value;
  if (!viewport) {
    return;
  }

  const rect = viewport.getBoundingClientRect();
  const originX = event.clientX - rect.left;
  const originY = event.clientY - rect.top;
  const factor = event.deltaY < 0 ? 1.12 : 0.9;
  zoomAt(zoom.value * factor, originX, originY);
}

function handleViewportMouseDown(event: MouseEvent) {
  if (event.button !== 0) {
    return;
  }

  const target = event.target as HTMLElement | null;
  if (target?.closest('.relation-graph-node')) {
    return;
  }

  event.preventDefault();
  const startX = event.clientX;
  const startY = event.clientY;
  const originX = panX.value;
  const originY = panY.value;

  const handleMouseMove = (moveEvent: MouseEvent) => {
    panX.value = originX + (moveEvent.clientX - startX);
    panY.value = originY + (moveEvent.clientY - startY);
  };

  const handleMouseUp = () => {
    window.removeEventListener('mousemove', handleMouseMove);
    window.removeEventListener('mouseup', handleMouseUp);
    stopViewportPan = null;
  };

  window.addEventListener('mousemove', handleMouseMove);
  window.addEventListener('mouseup', handleMouseUp);
  stopViewportPan = handleMouseUp;
}

const selectedNode = computed(() => layoutNodes.value.find((node) => node.id === selectedNodeId.value) ?? layoutNodes.value.find((node) => node.kind === 'current') ?? null);

const selectedActionLabel = computed(() => {
  if (!selectedNode.value || selectedNode.value.kind === 'current') {
    return null;
  }

  return selectedNode.value.kind === 'ancestor'
    ? '定位到该层详情'
    : selectedNode.value.kind === 'incoming'
      ? '打开引用记录'
      : '沿外键打开详情';
});

function selectNode(node: LayoutNode) {
  selectedNodeId.value = node.id;
}

function activateNode(node: LayoutNode | null) {
  if (!node || node.kind === 'current') {
    return;
  }

  emit('selectNode', {
    kind: node.kind,
    title: node.title,
    subtitle: node.subtitle,
    panelId: node.panelId,
    tableKey: node.tableKey,
    sourceTableKey: node.sourceTableKey,
    rowKey: node.rowKey,
    columnName: node.columnName,
  });
}

watch(() => [
  viewportSize.value.width,
  viewportSize.value.height,
  layoutNodes.value.map((node) => `${node.id}:${node.x}:${node.y}`).join('|'),
] as const, async ([width, height]) => {
  if (!width || !height) {
    return;
  }

  await nextTick();
  fitToView();
}, { immediate: true });

onMounted(() => {
  if (typeof ResizeObserver === 'undefined' || !viewportRef.value) {
    viewportSize.value = {
      width: viewportRef.value?.clientWidth ?? 0,
      height: viewportRef.value?.clientHeight ?? 0,
    };
    return;
  }

  resizeObserver = new ResizeObserver((entries) => {
    const entry = entries[0];
    if (!entry) {
      return;
    }

    viewportSize.value = {
      width: Math.round(entry.contentRect.width),
      height: Math.round(entry.contentRect.height),
    };
  });

  resizeObserver.observe(viewportRef.value);
});

onBeforeUnmount(() => {
  stopViewportPan?.();
  resizeObserver?.disconnect();
});
</script>

<template>
  <div class="relation-graph-shell">
    <div class="relation-graph-toolbar">
      <div class="relation-graph-legend">
        <span class="relation-graph-legend-pill relation-graph-legend-pill-current">当前</span>
        <span class="relation-graph-legend-pill relation-graph-legend-pill-incoming">入向 {{ props.reverseGroups.reduce((count, group) => count + group.rows.length, 0) }}</span>
        <span class="relation-graph-legend-pill relation-graph-legend-pill-outgoing">出向 {{ props.outgoingRelations.length }}</span>
      </div>
      <div class="relation-graph-toolbar-actions">
        <div class="relation-graph-controls">
          <NButton size="tiny" @click="zoom = clampZoom(zoom * 0.9)">-</NButton>
          <NButton size="tiny" @click="zoom = clampZoom(zoom * 1.12)">+</NButton>
          <NButton size="tiny" @click="fitToView">重置</NButton>
        </div>
      </div>
    </div>

    <div
      ref="viewportRef"
      class="relation-graph-viewport"
      @wheel="handleWheel"
      @mousedown="handleViewportMouseDown"
    >
      <div class="relation-graph-view-layer" :style="{ transform: viewportTransform, width: `${canvasWidth}px`, height: `${canvasHeight}px` }">
        <svg class="relation-graph-edges" :viewBox="`0 0 ${canvasWidth} ${canvasHeight}`">
          <g v-for="edge in layoutEdges" :key="edge.id">
            <path class="relation-graph-edge" :class="`relation-graph-edge-${edge.tone}`" :d="edgePath(edge)" />
          </g>
        </svg>

        <div class="relation-graph-canvas" :style="{ width: `${canvasWidth}px`, height: `${canvasHeight}px` }">
        <button
          v-for="node in layoutNodes"
          :key="node.id"
          type="button"
          class="relation-graph-node"
          :class="[
            `relation-graph-node-${node.tone}`,
            { 'relation-graph-node-selected': selectedNodeId === node.id },
          ]"
          :style="{
            left: `${node.x}px`,
            top: `${node.y}px`,
            width: `${node.width}px`,
            minHeight: `${node.height}px`,
          }"
          @click="selectNode(node)"
          @dblclick.stop="activateNode(node)"
        >
          <span class="relation-graph-node-badge">{{ node.badge }}</span>
          <span class="relation-graph-node-title">{{ node.title }}</span>
          <span class="relation-graph-node-subtitle">{{ node.subtitle }}</span>
        </button>

        <div v-if="hiddenIncomingCount > 0" class="relation-graph-overflow relation-graph-overflow-left">
          还有 {{ hiddenIncomingCount }} 条入向关系未展开
        </div>
        <div v-if="hiddenOutgoingCount > 0" class="relation-graph-overflow relation-graph-overflow-right">
          还有 {{ hiddenOutgoingCount }} 条出向关系未展开
        </div>
        </div>
      </div>
    </div>

    <div v-if="selectedNode" class="relation-graph-inspector">
      <div class="relation-graph-inspector-copy">
        <div class="relation-graph-inspector-title">{{ selectedNode.title }}</div>
        <div class="relation-graph-inspector-text">{{ selectedNode.subtitle }}</div>
      </div>
      <div class="relation-graph-inspector-actions">
        <NTag size="small" :bordered="false" :type="selectedNode.kind === 'current' ? 'success' : selectedNode.kind === 'incoming' ? 'info' : selectedNode.kind === 'outgoing' ? 'warning' : 'default'">
          {{ selectedNode.kind === 'current' ? '当前记录' : selectedNode.kind === 'incoming' ? '入向关系' : selectedNode.kind === 'outgoing' ? '出向关系' : '上游链路' }}
        </NTag>
        <span v-if="selectedActionLabel" class="relation-graph-inspector-hint">双击节点可{{ selectedActionLabel }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped lang="scss">
.relation-graph-shell {
  display: grid;
  gap: 6px;
  padding: 6px;
  border-radius: var(--radius-md);
  border: 1px solid rgba(148, 163, 184, 0.18);
  background:
    radial-gradient(circle at top, rgba(219, 234, 254, 0.58), rgba(255, 255, 255, 0.96) 42%),
    linear-gradient(180deg, rgba(255, 255, 255, 0.98), rgba(248, 250, 252, 0.98));
}

.relation-graph-toolbar,
.relation-graph-legend,
.relation-graph-inspector,
.relation-graph-inspector-actions,
.relation-graph-toolbar-actions,
.relation-graph-controls {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.relation-graph-toolbar {
  align-items: flex-start;
}

.relation-graph-legend {
  justify-content: flex-start;
  flex-wrap: wrap;
}

.relation-graph-toolbar-note {
  color: #64748b;
  font-size: 9.5px;
  line-height: 1.35;
  text-align: right;
  min-width: 0;
  flex: 1 1 180px;
}

.relation-graph-toolbar-actions {
  flex: 1 1 360px;
  min-width: 0;
  justify-content: flex-end;
  flex-wrap: wrap;
}

.relation-graph-controls {
  justify-content: flex-end;
  flex: 0 1 auto;
  flex-wrap: wrap;
}

.relation-graph-controls :deep(.n-button) {
  flex: 0 0 auto;
}

.relation-graph-legend-pill {
  display: inline-flex;
  align-items: center;
  min-height: 22px;
  padding: 0 8px;
  border-radius: 999px;
  font-size: 11px;
  font-weight: 700;
  line-height: 1;
  white-space: nowrap;
}

.relation-graph-legend-pill-current {
  background: #dcfce7;
  color: #15803d;
}

.relation-graph-legend-pill-incoming {
  background: #dbeafe;
  color: #2563eb;
}

.relation-graph-legend-pill-outgoing {
  background: #fef3c7;
  color: #d97706;
}

@media (max-width: 900px) {
  .relation-graph-toolbar,
  .relation-graph-toolbar-actions {
    align-items: stretch;
  }

  .relation-graph-toolbar-actions,
  .relation-graph-controls {
    justify-content: flex-start;
  }

  .relation-graph-toolbar-note {
    text-align: left;
  }
}

.relation-graph-viewport {
  position: relative;
  overflow: hidden;
  min-height: 430px;
  border-radius: 10px;
  border: 1px solid rgba(148, 163, 184, 0.16);
  background:
    linear-gradient(rgba(148, 163, 184, 0.08) 1px, transparent 1px) 0 0 / 100% 22px,
    linear-gradient(90deg, rgba(148, 163, 184, 0.08) 1px, transparent 1px) 0 0 / 22px 100%,
    rgba(255, 255, 255, 0.92);
  cursor: grab;
}

.relation-graph-viewport:active {
  cursor: grabbing;
}

.relation-graph-view-layer {
  position: relative;
  transform-origin: 0 0;
  will-change: transform;
}

.relation-graph-canvas {
  position: relative;
}

.relation-graph-edges {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  pointer-events: none;
}

.relation-graph-edge {
  fill: none;
  stroke-width: 2;
  stroke-linecap: round;
}

.relation-graph-edge-chain {
  stroke: rgba(100, 116, 139, 0.66);
}

.relation-graph-edge-incoming {
  stroke: rgba(37, 99, 235, 0.72);
  stroke-dasharray: 6 5;
}

.relation-graph-edge-outgoing {
  stroke: rgba(5, 150, 105, 0.72);
  stroke-dasharray: 6 5;
}

.relation-graph-node {
  position: absolute;
  display: grid;
  gap: 6px;
  padding: 12px 13px;
  border-radius: 14px;
  border: 1px solid rgba(148, 163, 184, 0.22);
  background: rgba(255, 255, 255, 0.98);
  box-shadow: 0 14px 30px rgba(15, 23, 42, 0.08);
  text-align: left;
  cursor: pointer;
  transition: transform 160ms ease, box-shadow 160ms ease, border-color 160ms ease;
}

.relation-graph-node:hover,
.relation-graph-node-selected {
  transform: translateY(-1px);
  box-shadow: 0 18px 34px rgba(15, 23, 42, 0.12);
}

.relation-graph-node-current {
  border-color: rgba(16, 185, 129, 0.26);
  background: linear-gradient(180deg, rgba(255, 255, 255, 0.99), rgba(236, 253, 245, 0.99));
}

.relation-graph-node-ancestor {
  border-color: rgba(148, 163, 184, 0.28);
  background: linear-gradient(180deg, rgba(255, 255, 255, 0.99), rgba(248, 250, 252, 0.99));
}

.relation-graph-node-incoming {
  border-color: rgba(59, 130, 246, 0.24);
  background: linear-gradient(180deg, rgba(255, 255, 255, 0.99), rgba(239, 246, 255, 0.99));
}

.relation-graph-node-outgoing {
  border-color: rgba(16, 185, 129, 0.22);
  background: linear-gradient(180deg, rgba(255, 255, 255, 0.99), rgba(240, 253, 244, 0.99));
}

.relation-graph-node-selected {
  border-color: rgba(15, 23, 42, 0.24);
}

.relation-graph-node-badge {
  width: fit-content;
  padding: 2px 8px;
  border-radius: 999px;
  background: rgba(15, 23, 42, 0.06);
  color: #475569;
  font-size: 10px;
  font-weight: 700;
}

.relation-graph-node-title {
  color: #0f172a;
  font-size: 12px;
  font-weight: 800;
  line-height: 1.25;
  word-break: break-word;
}

.relation-graph-node-subtitle {
  color: #475569;
  font-size: 10.5px;
  line-height: 1.45;
  word-break: break-word;
}

.relation-graph-overflow {
  position: absolute;
  bottom: 16px;
  padding: 6px 10px;
  border-radius: 999px;
  background: rgba(15, 23, 42, 0.72);
  color: rgba(255, 255, 255, 0.94);
  font-size: 10px;
}

.relation-graph-overflow-left {
  left: 16px;
}

.relation-graph-overflow-right {
  right: 16px;
}

.relation-graph-inspector {
  padding: 10px 12px;
  border-radius: 10px;
  border: 1px solid rgba(148, 163, 184, 0.16);
  background: rgba(255, 255, 255, 0.9);
}

.relation-graph-inspector-copy {
  display: grid;
  gap: 3px;
  min-width: 0;
}

.relation-graph-inspector-title {
  color: #0f172a;
  font-size: 12px;
  font-weight: 800;
}

.relation-graph-inspector-text {
  color: #475569;
  font-size: 10.5px;
  line-height: 1.45;
}

.relation-graph-inspector-hint {
  color: #64748b;
  font-size: 10.5px;
  white-space: nowrap;
}

.relation-graph-inspector-actions {
  flex: 0 0 auto;
}

@media (max-width: 980px) {
  .relation-graph-toolbar,
  .relation-graph-inspector,
  .relation-graph-toolbar-actions {
    display: grid;
    justify-content: stretch;
  }

  .relation-graph-toolbar-note {
    text-align: left;
  }

  .relation-graph-controls {
    justify-content: flex-start;
    flex-wrap: wrap;
  }
}
</style>