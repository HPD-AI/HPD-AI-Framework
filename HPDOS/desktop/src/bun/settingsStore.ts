import { Utils } from "electrobun/bun";
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";

export type ShellRoute = "chat" | "automations" | "settings";

export type ShellSnapshot = {
  activeRoute: ShellRoute;
  sidebarCollapsed: boolean;
};

export type ChatLayoutSnapshot = {
  expandedAppPaneWidth: number | null;
  collapsedAppPaneWidth: number | null;
};

const settingsPath = join(Utils.paths.userData, "settings.json");
const shellKey = "shell";
const chatLayoutKey = "chatLayout";

export function readShell(): ShellSnapshot | null {
  return normalizeShellSnapshot(readSettings()[shellKey]);
}

export function writeShell(snapshot: ShellSnapshot): void {
  const settings = readSettings();
  settings[shellKey] = normalizeShellSnapshot(snapshot);
  writeSettings(settings);
}

export function readChatLayout(): ChatLayoutSnapshot | null {
  return normalizeChatLayoutSnapshot(readSettings()[chatLayoutKey]);
}

export function writeChatLayout(snapshot: ChatLayoutSnapshot): void {
  const settings = readSettings();
  settings[chatLayoutKey] = normalizeChatLayoutSnapshot(snapshot);
  writeSettings(settings);
}

function readSettings(): Record<string, unknown> {
  if (!existsSync(settingsPath)) return {};

  try {
    const text = readFileSync(settingsPath, "utf8");
    const parsed = JSON.parse(text);
    if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) return {};

    return parsed as Record<string, unknown>;
  } catch {
    return {};
  }
}

function writeSettings(settings: Record<string, unknown>): void {
  mkdirSync(dirname(settingsPath), { recursive: true });
  writeFileSync(settingsPath, `${JSON.stringify(settings, null, 2)}\n`, "utf8");
}

export function normalizeShellSnapshot(value: unknown): ShellSnapshot | null {
  if (typeof value !== "object" || value === null) return null;

  const record = value as Partial<ShellSnapshot>;

  return {
    activeRoute: normalizeRoute(record.activeRoute),
    sidebarCollapsed: record.sidebarCollapsed === true
  };
}

export function normalizeChatLayoutSnapshot(value: unknown): ChatLayoutSnapshot | null {
  if (typeof value !== "object" || value === null) return null;

  const record = value as Partial<ChatLayoutSnapshot>;

  return {
    expandedAppPaneWidth: normalizePaneWidth(record.expandedAppPaneWidth),
    collapsedAppPaneWidth: normalizePaneWidth(record.collapsedAppPaneWidth)
  };
}

function normalizeRoute(value: unknown): ShellRoute {
  switch (value) {
    case "automations":
    case "settings":
      return value;
    default:
      return "chat";
  }
}

function normalizePaneWidth(value: unknown): number | null {
  if (typeof value !== "number" || !Number.isFinite(value) || value <= 0) return null;

  return value;
}
