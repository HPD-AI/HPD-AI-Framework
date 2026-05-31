import { requestDesktopSettings } from "../desktopHostBridge";
import {
  defaultProviderModelUiState,
  normalizeProviderModelUiState,
  providerModelSettingsSource,
  type ProviderModelStorage
} from "./runtime/providerModel";

export type ChatLayoutSnapshot = {
  chatSectionCollapsed: boolean;
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
    chatSectionCollapsed: false,
    expandedAppPaneWidth: null,
    collapsedAppPaneWidth: null
  };
}

export function normalizeChatLayoutSnapshot(value: unknown): ChatLayoutSnapshot | null {
  if (typeof value !== "object" || value === null) return null;

  const record = value as Partial<ChatLayoutSnapshot>;

  return {
    chatSectionCollapsed: record.chatSectionCollapsed === true,
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

export function createDesktopProviderModelStorage(): ProviderModelStorage {
  return {
    load: defaultProviderModelUiState,
    async hydrate() {
      const snapshot = await requestDesktopSettings(providerModelSettingsSource, "read", {});
      return normalizeProviderModelUiState(snapshot);
    },
    save(state) {
      const snapshot = normalizeProviderModelUiState(state);
      void requestDesktopSettings(providerModelSettingsSource, "write", snapshot).catch(() => undefined);
    }
  };
}

function normalizePaneWidth(value: unknown): number | null {
  if (typeof value !== "number" || !Number.isFinite(value) || value <= 0) return null;

  return value;
}
