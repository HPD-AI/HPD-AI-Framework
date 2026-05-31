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

export type ProviderModelRef = {
  providerKey: string;
  modelId: string;
};

export type ProviderModelUiState = {
  selected?: ProviderModelRef;
  recent: ProviderModelRef[];
  favorites: ProviderModelRef[];
  visibility: Record<string, "visible" | "hidden">;
  providerVisibility: Record<string, "visible" | "hidden">;
  providerOptionsJson: Record<string, string>;
};

const settingsPath = join(Utils.paths.userData, "settings.json");
const shellKey = "shell";
const chatLayoutKey = "chatLayout";
const chatProviderModelKey = "chatProviderModel";

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

export function readChatProviderModel(): ProviderModelUiState | null {
  return normalizeProviderModelUiState(readSettings()[chatProviderModelKey]);
}

export function writeChatProviderModel(snapshot: ProviderModelUiState): void {
  const settings = readSettings();
  settings[chatProviderModelKey] = normalizeProviderModelUiState(snapshot);
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

export function normalizeProviderModelUiState(value: unknown): ProviderModelUiState | null {
  if (typeof value !== "object" || value === null) return null;

  const record = value as Partial<ProviderModelUiState>;

  return {
    selected: normalizeProviderModelRef(record.selected),
    recent: normalizeProviderModelRefs(record.recent),
    favorites: normalizeProviderModelRefs(record.favorites),
    visibility: normalizeVisibility(record.visibility),
    providerVisibility: normalizeVisibility(record.providerVisibility),
    providerOptionsJson: normalizeStringRecord(record.providerOptionsJson)
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

function normalizeProviderModelRefs(value: unknown): ProviderModelRef[] {
  if (!Array.isArray(value)) return [];

  const refs: ProviderModelRef[] = [];
  for (const item of value) {
    const normalized = normalizeProviderModelRef(item);
    if (!normalized) continue;
    if (refs.some((existing) => existing.providerKey === normalized.providerKey && existing.modelId === normalized.modelId)) continue;
    refs.push(normalized);
  }

  return refs;
}

function normalizeProviderModelRef(value: unknown): ProviderModelRef | undefined {
  if (typeof value !== "object" || value === null) return undefined;

  const record = value as Partial<ProviderModelRef>;
  const providerKey = normalizeString(record.providerKey);
  const modelId = normalizeString(record.modelId);
  if (!providerKey || !modelId) return undefined;

  return { providerKey, modelId };
}

function normalizeVisibility(value: unknown): Record<string, "visible" | "hidden"> {
  if (typeof value !== "object" || value === null || Array.isArray(value)) return {};

  const visibility: Record<string, "visible" | "hidden"> = {};
  for (const [key, item] of Object.entries(value)) {
    const normalizedKey = normalizeString(key);
    if (!normalizedKey || (item !== "visible" && item !== "hidden")) continue;
    visibility[normalizedKey] = item;
  }

  return visibility;
}

function normalizeStringRecord(value: unknown): Record<string, string> {
  if (typeof value !== "object" || value === null || Array.isArray(value)) return {};

  const record: Record<string, string> = {};
  for (const [key, item] of Object.entries(value)) {
    const normalizedKey = normalizeString(key);
    const normalizedValue = normalizeString(item);
    if (!normalizedKey || !normalizedValue) continue;
    record[normalizedKey] = normalizedValue;
  }

  return record;
}

function normalizeString(value: unknown): string | undefined {
  if (typeof value !== "string") return undefined;

  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : undefined;
}
