import type {
  ChatTimelineItem,
  ClarificationItem,
  PermissionItem,
  ReasoningItem,
  ToolCallItem
} from "./chatTypes";

export type ChatTimelineSegment =
  | { kind: "item"; item: ChatTimelineItem }
  | ChatTimelineTurn;

export type ChatTimelineTurn = {
  kind: "turn";
  id: string;
  user: ChatTimelineItem & { kind: "user-message" };
  worked: ChatTimelineItem[];
  final: ChatTimelineItem | null;
  complete: boolean;
};

export type ChatWorkedSegment =
  | { kind: "item"; item: ChatTimelineItem }
  | { kind: "tool-group"; id: string; tools: ToolCallItem[]; summary: string };

export function groupTimelineTurns(items: readonly ChatTimelineItem[]): ChatTimelineSegment[] {
  const segments: ChatTimelineSegment[] = [];
  let current: ChatTimelineTurn | null = null;

  for (const item of items) {
    if (item.kind === "user-message") {
      pushTurn(segments, current);
      current = {
        kind: "turn",
        id: `turn:${item.id}`,
        user: item,
        worked: [],
        final: null,
        complete: false
      };
      continue;
    }

    if (!current) {
      segments.push({ kind: "item", item });
      continue;
    }

    current.worked.push(item);
  }

  pushTurn(segments, current);
  return segments;
}

export function groupWorkedItems(items: readonly ChatTimelineItem[]): ChatWorkedSegment[] {
  const segments: ChatWorkedSegment[] = [];
  let pendingTools: ToolCallItem[] = [];

  for (const item of items) {
    if (item.kind === "tool-call") {
      pendingTools.push(item);
      continue;
    }

    flushToolGroup(segments, pendingTools);
    pendingTools = [];
    segments.push({ kind: "item", item });
  }

  flushToolGroup(segments, pendingTools);
  return segments;
}

export function summarizeTurnDetails(details: readonly ChatTimelineItem[]): string {
  const counts = {
    messages: 0,
    thoughts: 0,
    tools: 0,
    other: 0
  };

  for (const item of details) {
    if (item.kind === "assistant-text") {
      counts.messages += 1;
    } else if (item.kind === "reasoning") {
      counts.thoughts += 1;
    } else if (item.kind === "tool-call") {
      counts.tools += 1;
    } else {
      counts.other += 1;
    }
  }

  const parts: string[] = [];
  if (counts.messages > 0) parts.push(`${counts.messages} ${counts.messages === 1 ? "message" : "messages"}`);
  if (counts.thoughts > 0) parts.push(`${counts.thoughts} ${counts.thoughts === 1 ? "thought" : "thoughts"}`);
  if (counts.tools > 0) parts.push(`${counts.tools} ${counts.tools === 1 ? "tool" : "tools"}`);
  if (counts.other > 0) parts.push(`${counts.other} ${counts.other === 1 ? "event" : "events"}`);

  return parts.join(" / ");
}

function flushToolGroup(segments: ChatWorkedSegment[], tools: readonly ToolCallItem[]): void {
  if (tools.length === 0) return;

  segments.push({
    kind: "tool-group",
    id: `tools:${tools.map((tool) => tool.id).join(":")}`,
    tools: [...tools],
    summary: summarizeToolGroup(tools)
  });
}

function summarizeToolGroup(tools: readonly ToolCallItem[]): string {
  const counts = new Map<string, number>();

  for (const tool of tools) {
    const label = toolSummaryLabel(tool);
    counts.set(label, (counts.get(label) ?? 0) + 1);
  }

  return Array.from(counts.entries())
    .map(([label, count]) => `${label} ${count} ${toolSummaryNoun(label, count)}`)
    .join(", ");
}

function toolSummaryLabel(tool: ToolCallItem): string {
  const name = tool.name.toLowerCase();

  if (tool.fileMutation?.type === "edit") return "edited";
  if (tool.fileMutation?.type === "write") return tool.fileMutation.created ? "created" : "wrote";
  if (tool.command) return "ran";
  if (name === "read" || name === "readfile") return "read";
  if (name === "list" || name === "ls" || name === "listfiles" || name === "listdirectory") return "explored";
  if (name === "grep" || name === "globsearch" || name === "search" || name === "rg") return "searched";

  return "used";
}

function toolSummaryNoun(label: string, count: number): string {
  const plural = count !== 1;

  if (label === "explored") return plural ? "folders" : "folder";
  if (label === "read" || label === "edited" || label === "created" || label === "wrote") return plural ? "files" : "file";
  if (label === "ran") return plural ? "commands" : "command";
  if (label === "searched") return plural ? "searches" : "search";
  return plural ? "tools" : "tool";
}

function pushTurn(segments: ChatTimelineSegment[], turn: ChatTimelineTurn | null): void {
  if (!turn) return;

  const finalIndex = findFinalItemIndex(turn.worked);
  const final = finalIndex >= 0 ? turn.worked[finalIndex] : null;
  const worked = finalIndex >= 0
    ? turn.worked.filter((_, index) => index !== finalIndex)
    : turn.worked;

  segments.push({
    ...turn,
    worked,
    final,
    complete: isTurnComplete(worked, final)
  });
}

function findFinalItemIndex(items: readonly ChatTimelineItem[]): number {
  const index = items.length - 1;
  if (index < 0) return -1;

  const item = items[index];
  if ((item.kind === "assistant-text" && item.complete) || item.kind === "error") {
    return index;
  }

  return -1;
}

function isTurnComplete(worked: readonly ChatTimelineItem[], final: ChatTimelineItem | null): boolean {
  return final !== null && !worked.some(isOpenDetailItem);
}

function isOpenDetailItem(item: ChatTimelineItem): boolean {
  if (item.kind === "reasoning") return !isReasoningComplete(item);
  if (item.kind === "tool-call") return isToolOpen(item);
  if (item.kind === "permission") return isPermissionPending(item);
  if (item.kind === "clarification") return isClarificationPending(item);
  return false;
}

function isReasoningComplete(item: ReasoningItem): boolean {
  return item.complete;
}

function isToolOpen(item: ToolCallItem): boolean {
  return item.status === "pending" || item.status === "running";
}

function isPermissionPending(item: PermissionItem): boolean {
  return item.pending;
}

function isClarificationPending(item: ClarificationItem): boolean {
  return item.pending;
}
