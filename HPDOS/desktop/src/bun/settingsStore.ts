import { Utils } from "electrobun/bun";
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";

export type ShellLayoutSnapshot = {
  sidebarCollapsed: boolean;
  expandedAppPaneWidth: number | null;
  collapsedAppPaneWidth: number | null;
};

const settingsPath = join(Utils.paths.userData, "settings.json");
const shellLayoutKey = "shellLayout";

export function readShellLayout(): ShellLayoutSnapshot | null {
  const settings = readSettings();
  return normalizeShellLayoutSnapshot(settings[shellLayoutKey]);
}

export function writeShellLayout(snapshot: ShellLayoutSnapshot): void {
  const settings = readSettings();
  settings[shellLayoutKey] = normalizeShellLayoutSnapshot(snapshot);
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

function normalizeShellLayoutSnapshot(value: unknown): ShellLayoutSnapshot | null {
  if (typeof value !== "object" || value === null) return null;

  const record = value as Partial<ShellLayoutSnapshot>;

  return {
    sidebarCollapsed: record.sidebarCollapsed === true,
    expandedAppPaneWidth: normalizePaneWidth(record.expandedAppPaneWidth),
    collapsedAppPaneWidth: normalizePaneWidth(record.collapsedAppPaneWidth)
  };
}

function normalizePaneWidth(value: unknown): number | null {
  if (typeof value !== "number" || !Number.isFinite(value) || value <= 0) return null;

  return value;
}
