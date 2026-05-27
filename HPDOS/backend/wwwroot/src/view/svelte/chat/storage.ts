import { requestDesktopSettings } from "../desktopSettingsBridge";

export type ChatLayoutSnapshot = {
  expandedAppPaneWidth: number | null;
  collapsedAppPaneWidth: number | null;
};

export type ChatLayoutStorage = {
  load(): ChatLayoutSnapshot | null;
  save(snapshot: ChatLayoutSnapshot): void;
  hydrate?(): Promise<ChatLayoutSnapshot | null>;
};

const chatLayoutSettingsSource = "hpdos.chat.layout";

export function defaultChatLayoutSnapshot(): ChatLayoutSnapshot {
  return {
    expandedAppPaneWidth: null,
    collapsedAppPaneWidth: null
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

export function createDesktopChatLayoutStorage(): ChatLayoutStorage {
  return {
    load: () => null,
    async hydrate() {
      const snapshot = await requestDesktopSettings(chatLayoutSettingsSource, "read", {});
      return normalizeChatLayoutSnapshot(snapshot);
    },
    save(snapshot) {
      void requestDesktopSettings(chatLayoutSettingsSource, "write", snapshot).catch(() => undefined);
    }
  };
}

function normalizePaneWidth(value: unknown): number | null {
  if (typeof value !== "number" || !Number.isFinite(value) || value <= 0) return null;

  return value;
}
