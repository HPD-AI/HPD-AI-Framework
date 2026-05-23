import type { ConversationItem, Session } from "@hpd/hpd-agent-client";
import type { ArtifactRecord } from "./hpdosArtifacts.js";
import type { HpdosRuntime, HpdosWorkspace, HpdosWorkspaceStore } from "./hpdosWorkspace.js";

export interface HpdosState {
  busy: boolean;
  runtime: HpdosRuntime | null;
  workspaceStore: HpdosWorkspaceStore | null;
  activeWorkspace: HpdosWorkspace | null;
  workspaceSessions: Session[];
  recentSessions: Session[];
  activeSessionId: string;
  conversationItems: readonly ConversationItem[];
  artifacts: readonly ArtifactRecord[];
  openArtifactId: string | null;
  providerKey: string;
  modelId: string;
  error: string | null;
}

export interface SendTextCommand {
  text: string;
  providerKey?: string;
  modelId?: string;
}

export interface HpdosStorage {
  get(key: string): string | null;
  set(key: string, value: string): void;
  remove(key: string): void;
}

export interface HpdosRuntimeApi {
  loadRuntime(): Promise<HpdosRuntime>;
}

export interface HpdosDesktopBridge {
  pickWorkspaceFolders(): Promise<string[] | null>;
}

export type HpdosStateListener = (state: HpdosState) => void;
