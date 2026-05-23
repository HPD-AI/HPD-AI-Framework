import type { ArtifactView } from "../../core/hpdosArtifacts.js";
import type { HpdosState, SendTextCommand } from "../../core/hpdosState.js";

export type SidebarView = "workspaceSessions" | "conversation";

export interface ViewActions {
  newSession(): void;
  createWorkspace(name: string): void;
  deleteWorkspace(workspaceId: string): void;
  switchWorkspace(workspaceId: string): void;
  switchSession(sessionId: string): void;
  deleteSession(sessionId: string): void;
  pickWorkspaceRoots(): void;
  removeWorkspaceRoot(rootId: string): void;
  sendText(command: SendTextCommand): void;
  setRuntimeOptions(options: { providerKey?: string; modelId?: string }): void;
  openArtifact(id: string): void;
  closeArtifact(): void;
  setArtifactView(id: string, view: ArtifactView): void;
  setDraft(value: string): void;
}

export interface AppShellProps {
  appState: HpdosState;
  actions: ViewActions;
  draft: string;
  artifactViews: ReadonlyMap<string, ArtifactView>;
  sidebarView: SidebarView;
  setSidebarView(view: SidebarView): void;
}
