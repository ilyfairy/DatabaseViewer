import ELK from 'elkjs/lib/elk.bundled.js';
import { MarkerType, Position, type Edge, type Node } from '@vue-flow/core';
import type { DatabaseGraph, DatabaseGraphColumn, DatabaseGraphEdge, DatabaseGraphNode } from '../types/explorer';

export type DatabaseOverviewNodeData = {
  tableKey: string
  title: string
  rowCountLabel: string
  note: string | null
  isJunction: boolean
  columns: DatabaseGraphColumn[]
  width: number
  height: number
  visibleFieldCount: number
  hiddenFieldCount: number
}

export type DatabaseOverviewEdgeData = {
  path: string
  tooltip: string
}

type LayoutPoint = {
  x: number
  y: number
}

type LayoutSection = {
  startPoint?: LayoutPoint
  bendPoints?: LayoutPoint[]
  endPoint?: LayoutPoint
}

type LayoutEdge = {
  id: string
  sections?: LayoutSection[]
}

type LayoutNode = {
  id: string
  x?: number
  y?: number
  width?: number
  height?: number
}

type LayoutGraph = {
  children?: LayoutNode[]
  edges?: LayoutEdge[]
}

const elk = new ELK();

export type DatabaseOverviewLayoutOptions = {
  expandAllFields?: boolean
  expandedTableKeys?: string[]
}

const OVERVIEW_NODE_MIN_WIDTH = 156;
const OVERVIEW_NODE_MAX_WIDTH = 292;
const OVERVIEW_VISIBLE_FIELD_LIMIT = 10;
const OVERVIEW_NODE_HEADER_HEIGHT = 30;
const OVERVIEW_NODE_ROW_HEIGHT = 17;
const OVERVIEW_NODE_FOOTER_HEIGHT = 14;
const OVERVIEW_EDGE_STUB = 18;

function edgeId(edge: DatabaseGraphEdge) {
  return `${edge.sourceTableKey}:${edge.targetTableKey}:${edge.relationType}:${edge.viaTableKey ?? ''}:${edge.sourceColumn}:${edge.targetColumn}`;
}

function clamp(value: number, min: number, max: number) {
  return Math.min(max, Math.max(min, value));
}

function visibleFieldCount(node: DatabaseGraphNode, options?: DatabaseOverviewLayoutOptions) {
  if (options?.expandAllFields || options?.expandedTableKeys?.includes(node.tableKey)) {
    return node.columns.length;
  }

  return Math.min(node.columns.length, OVERVIEW_VISIBLE_FIELD_LIMIT);
}

function hiddenFieldCount(node: DatabaseGraphNode, options?: DatabaseOverviewLayoutOptions) {
  return Math.max(node.columns.length - visibleFieldCount(node, options), 0);
}

/** 估算节点宽度，确保标题完整显示。 */
function estimateNodeWidth(node: DatabaseGraphNode) {
  // header 固定开销：border(4) + padding(8) + icon(14) + gaps(8) + meta间距(4)
  const headerOverhead = 38;
  const titleTextWidth = node.title.length * 7.2;
  const rowCountLabel = `${node.rowCount ?? '未統計'} rows`;
  const metaTextWidth = rowCountLabel.length * 5;
  const headerWidth = titleTextWidth + metaTextWidth + headerOverhead;

  const fieldWidth = node.columns.reduce((maxWidth, column) => {
    const iconWidth = 12;
    const nameWidth = column.name.length * 6.2;
    const typeWidth = column.displayType.length * 5.2;
    return Math.max(maxWidth, iconWidth + nameWidth + typeWidth + 20);
  }, 0) + 12;

  return Math.max(headerWidth, clamp(fieldWidth, OVERVIEW_NODE_MIN_WIDTH, OVERVIEW_NODE_MAX_WIDTH));
}

function estimateNodeHeight(node: DatabaseGraphNode, options?: DatabaseOverviewLayoutOptions) {
  const visibleCount = visibleFieldCount(node, options);
  const moreRowCount = hiddenFieldCount(node, options) > 0 ? 1 : 0;

  return OVERVIEW_NODE_HEADER_HEIGHT
    + ((visibleCount + moreRowCount) * OVERVIEW_NODE_ROW_HEIGHT)
    + (node.comment || node.isJunction ? OVERVIEW_NODE_FOOTER_HEIGHT : 0);
}

function buildEdgeTooltip(edge: DatabaseGraphEdge, titleById: Map<string, string>) {
  const sourceTitle = titleById.get(edge.sourceTableKey) ?? edge.sourceTableKey;
  const targetTitle = titleById.get(edge.targetTableKey) ?? edge.targetTableKey;
  const relationText = edge.relationType === 'many-to-many'
    ? '多对多'
    : edge.relationType === 'one-to-one'
      ? '一对一'
      : '多对一';
  const viaText = edge.viaTableKey ? `，经由 ${titleById.get(edge.viaTableKey) ?? edge.viaTableKey}` : '';
  const logicalText = edge.logical ? '，逻辑关系' : '';
  return `${sourceTitle}.${edge.sourceColumn} -> ${targetTitle}.${edge.targetColumn} (${relationText}${viaText}${logicalText})`;
}

function pointsToPath(points: LayoutPoint[]) {
  if (points.length < 2) {
    return '';
  }

  const first = points[0];
  if (!first) {
    return '';
  }

  const rest = points.slice(1);
  return `M ${first.x} ${first.y} ${rest.map((point) => `L ${point.x} ${point.y}`).join(' ')}`;
}

function compactPathPoints(points: LayoutPoint[]) {
  const compacted: LayoutPoint[] = [];

  for (const point of points) {
    const previous = compacted[compacted.length - 1];
    if (previous && previous.x === point.x && previous.y === point.y) {
      continue;
    }

    compacted.push(point);

    while (compacted.length >= 3) {
      const last = compacted[compacted.length - 1];
      const middle = compacted[compacted.length - 2];
      const first = compacted[compacted.length - 3];
      if (!last || !middle || !first) {
        break;
      }

      const sameVertical = first.x === middle.x && middle.x === last.x;
      const sameHorizontal = first.y === middle.y && middle.y === last.y;
      if (!sameVertical && !sameHorizontal) {
        break;
      }

      compacted.splice(compacted.length - 2, 1);
    }
  }

  return compacted;
}

function extractEdgePath(layoutEdge: LayoutEdge, nodeById: Map<string, LayoutNode>, edge: DatabaseGraphEdge) {
  // 优先使用 ELK 计算的正交折线（自动绕开中间节点）
  if (layoutEdge.sections?.length) {
    const points: LayoutPoint[] = [];
    for (const section of layoutEdge.sections) {
      if (section.startPoint) {
        points.push(section.startPoint);
      }

      if (section.bendPoints) {
        points.push(...section.bendPoints);
      }

      if (section.endPoint) {
        points.push(section.endPoint);
      }
    }

    if (points.length >= 2) {
      return pointsToPath(compactPathPoints(points));
    }
  }

  // 兜底：简单正交折线（不绕开中间节点）
  const sourceNode = nodeById.get(edge.sourceTableKey);
  const targetNode = nodeById.get(edge.targetTableKey);
  if (!sourceNode || !targetNode) {
    return '';
  }

  const sourceCenterY = (sourceNode.y ?? 0) + ((sourceNode.height ?? estimateNodeHeight({ tableKey: '', title: '', rowCount: null, isJunction: false, comment: null, columns: [] })) / 2);
  const targetCenterY = (targetNode.y ?? 0) + ((targetNode.height ?? estimateNodeHeight({ tableKey: '', title: '', rowCount: null, isJunction: false, comment: null, columns: [] })) / 2);
  const sourceRight = (sourceNode.x ?? 0) + (sourceNode.width ?? OVERVIEW_NODE_MIN_WIDTH);
  const targetLeft = targetNode.x ?? 0;
  const startAnchor = { x: sourceRight, y: sourceCenterY };
  const startStub = { x: sourceRight + OVERVIEW_EDGE_STUB, y: sourceCenterY };
  const endStub = { x: targetLeft - OVERVIEW_EDGE_STUB, y: targetCenterY };
  const endAnchor = { x: targetLeft, y: targetCenterY };

  const span = Math.max(targetLeft - sourceRight, OVERVIEW_EDGE_STUB * 4);
  const midX = sourceRight + Math.max(32, span / 2);

  return pointsToPath(compactPathPoints([
    startAnchor,
    startStub,
    { x: midX, y: sourceCenterY },
    { x: midX, y: targetCenterY },
    endStub,
    endAnchor,
  ]));
}

export async function buildDatabaseOverviewLayout(graph: DatabaseGraph, options?: DatabaseOverviewLayoutOptions) {
  const titleById = new Map(graph.nodes.map((node) => [node.tableKey, node.title]));
  const layoutGraph = await elk.layout({
    id: 'database-overview-root',
    layoutOptions: {
      'elk.algorithm': 'layered',
      'elk.direction': 'RIGHT',
      'elk.edgeRouting': 'ORTHOGONAL',
      'elk.padding': '[top=36,left=36,bottom=36,right=36]',
      'elk.separateConnectedComponents': 'true',
      'elk.spacing.componentComponent': '96',
      'elk.spacing.nodeNode': '48',
      'elk.spacing.edgeNode': '28',
      'elk.spacing.edgeEdge': '18',
      'elk.layered.spacing.nodeNodeBetweenLayers': '96',
      'elk.layered.considerModelOrder.strategy': 'NODES_AND_EDGES',
      'elk.layered.crossingMinimization.strategy': 'LAYER_SWEEP',
      'elk.layered.nodePlacement.strategy': 'BRANDES_KOEPF',
      'elk.layered.nodePlacement.bk.fixedAlignment': 'BALANCED',
      'elk.layered.priority.straightness': '10',
    },
    children: graph.nodes.map((node) => ({
      id: node.tableKey,
      width: estimateNodeWidth(node),
      height: estimateNodeHeight(node, options),
    })),
    edges: graph.edges.map((edge) => ({
      id: edgeId(edge),
      sources: [edge.sourceTableKey],
      targets: [edge.targetTableKey],
    })),
  }) as LayoutGraph;

  const layoutNodes = layoutGraph.children ?? [];
  const layoutEdges = layoutGraph.edges ?? [];
  const nodeById = new Map(layoutNodes.map((node) => [node.id, node]));
  const edgeById = new Map(layoutEdges.map((e) => [e.id, e]));

  const nodes: Node<DatabaseOverviewNodeData>[] = graph.nodes.map((node) => {
    const positionedNode = nodeById.get(node.tableKey);
    return {
      id: node.tableKey,
      type: 'default',
      draggable: false,
      selectable: false,
      style: {
        width: `${positionedNode?.width ?? estimateNodeWidth(node)}px`,
        height: `${positionedNode?.height ?? estimateNodeHeight(node, options)}px`,
        padding: '0',
        background: 'transparent',
        border: '0',
        boxShadow: 'none',
      },
      sourcePosition: Position.Right,
      targetPosition: Position.Left,
      position: {
        x: positionedNode?.x ?? 0,
        y: positionedNode?.y ?? 0,
      },
      data: {
        tableKey: node.tableKey,
        title: node.title,
        rowCountLabel: `${node.rowCount ?? '未统计'} rows`,
        note: node.comment ?? (node.isJunction ? '中间表 / junction' : null),
        isJunction: node.isJunction,
        columns: node.columns,
        width: positionedNode?.width ?? estimateNodeWidth(node),
        height: positionedNode?.height ?? estimateNodeHeight(node, options),
        visibleFieldCount: visibleFieldCount(node, options),
        hiddenFieldCount: hiddenFieldCount(node, options),
      },
    };
  });

  const edges: Edge<DatabaseOverviewEdgeData>[] = graph.edges.map((edge) => {
    const stroke = edge.relationType === 'many-to-many' ? '#d97706' : edge.logical ? '#64748b' : '#0f766e';
    const strokeWidth = edge.relationType === 'many-to-many' ? 1.8 : 1.45;
    const strokeDasharray = edge.logical || edge.relationType === 'many-to-many' ? '6 4' : undefined;
    return {
      id: edgeId(edge),
      type: 'overview-orthogonal',
      source: edge.sourceTableKey,
      target: edge.targetTableKey,
      sourcePosition: Position.Right,
      targetPosition: Position.Left,
      markerEnd: {
        type: MarkerType.ArrowClosed,
        width: 18,
        height: 18,
        color: stroke,
      },
      style: {
        stroke,
        strokeWidth,
        strokeDasharray,
      },
      data: {
        path: extractEdgePath(edgeById.get(edgeId(edge)) ?? { id: edgeId(edge) }, nodeById, edge),
        tooltip: buildEdgeTooltip(edge, titleById),
      },
    };
  });

  return { nodes, edges };
}