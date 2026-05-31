import { requestDesktopSettings } from "../desktopHostBridge";
import type { ShellRoute } from "./controller";

export type ShellSnapshot = {
  activeRoute: ShellRoute;
  sidebarCollapsed: boolean;
};

export type ShellStorage = {
  load(): ShellSnapshot | null;
  save(snapshot: ShellSnapshot): void;
  hydrate?(): Promise<ShellSnapshot | null>;
};

const shellSettingsSource = "hpdos.shell";

export function defaultShellSnapshot(): ShellSnapshot {
  return {
    activeRoute: "chat",
    sidebarCollapsed: false
  };
}

export function normalizeShellSnapshot(value: unknown): ShellSnapshot | null {
  if (typeof value !== "object" || value === null) return null;

  const record = value as Partial<ShellSnapshot>;

  return {
    activeRoute: normalizeRoute(record.activeRoute),
    sidebarCollapsed: record.sidebarCollapsed === true
  };
}

export function createDesktopShellStorage(): ShellStorage {
  return {
    load: () => null,
    async hydrate() {
      const snapshot = await requestDesktopSettings(shellSettingsSource, "read", {});
      return normalizeShellSnapshot(snapshot);
    },
    save(snapshot) {
      void requestDesktopSettings(shellSettingsSource, "write", snapshot).catch(() => undefined);
    }
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
