import type { ChatTimelineItem, ToolCallItem } from "./chatTypes";

export type ChatAgentPreview = {
  turnId: string;
  label: string;
  text: string;
  expandedText: string;
  complete: boolean;
};

export function latestAgentPreview(items: readonly ChatTimelineItem[]): ChatAgentPreview | null {
  const userIndex = lastUserMessageIndex(items);
  const turnId = userIndex >= 0 ? items[userIndex].id : "unscoped";
  const currentTurnItems = items.slice(userIndex + 1);

  const assistant = findLast(currentTurnItems, (item) =>
    item.kind === "assistant-text" && item.text.trim().length > 0
  );
  if (assistant?.kind === "assistant-text") {
    return {
      turnId,
      label: "Agent",
      text: compactPreviewText(assistant.text),
      expandedText: expandedPreviewText(assistant.text),
      complete: assistant.complete
    };
  }

  const error = findLast(currentTurnItems, (item) => item.kind === "error");
  if (error?.kind === "error") {
    return {
      turnId,
      label: error.source ?? "Error",
      text: compactPreviewText(error.message),
      expandedText: expandedPreviewText(error.message),
      complete: true
    };
  }

  const reasoning = findLast(currentTurnItems, (item) =>
    item.kind === "reasoning" && item.text.trim().length > 0
  );
  if (reasoning?.kind === "reasoning") {
    return {
      turnId,
      label: reasoning.complete ? "Thought" : "Thinking",
      text: compactPreviewText(reasoning.text),
      expandedText: expandedPreviewText(reasoning.text),
      complete: reasoning.complete
    };
  }

  const tool = findLast(currentTurnItems, (item) => item.kind === "tool-call" && toolPreviewText(item).length > 0);
  if (tool?.kind === "tool-call") {
    const text = toolPreviewText(tool);
    return {
      turnId,
      label: tool.name,
      text,
      expandedText: text,
      complete: tool.status === "completed" || tool.status === "failed"
    };
  }

  return null;
}

function lastUserMessageIndex(items: readonly ChatTimelineItem[]): number {
  for (let index = items.length - 1; index >= 0; index -= 1) {
    if (items[index].kind === "user-message") return index;
  }

  return -1;
}

function findLast(
  items: readonly ChatTimelineItem[],
  predicate: (item: ChatTimelineItem) => boolean
): ChatTimelineItem | null {
  for (let index = items.length - 1; index >= 0; index -= 1) {
    const item = items[index];
    if (predicate(item)) return item;
  }

  return null;
}

function toolPreviewText(item: ToolCallItem): string {
  if (item.result?.text && item.result.text.trim().length > 0) return compactPreviewText(item.result.text);
  if (item.command?.liveOutput && item.command.liveOutput.trim().length > 0) {
    return compactPreviewText(item.command.liveOutput);
  }

  return item.status === "running" ? "Running" : item.status;
}

function compactPreviewText(text: string): string {
  return text
    .replace(/```[\s\S]*?```/g, " code block ")
    .replace(/`([^`]+)`/g, "$1")
    .replace(/\[([^\]]+)\]\([^)]+\)/g, "$1")
    .replace(/[#*_~>|-]+/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

function expandedPreviewText(text: string): string {
  return text
    .replace(/```[\s\S]*?```/g, " code block ")
    .replace(/`([^`]+)`/g, "$1")
    .replace(/\[([^\]]+)\]\([^)]+\)/g, "$1")
    .replace(/[#*_~>|-]+/g, " ")
    .replace(/[ \t]+/g, " ")
    .replace(/\n{3,}/g, "\n\n")
    .trim();
}
