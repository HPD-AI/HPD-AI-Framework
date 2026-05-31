import { requestDesktopHost } from "../../desktopHostBridge";
import type { HpdosRunWorkspaceRoot, HpdosWorkspaceDescriptor } from "./workspaceContext";

export type HpdosWorkspaceStoreDto = {
  version: 1;
  activeWorkspaceId: string;
  workspaces: HpdosWorkspaceDto[];
};

export type HpdosWorkspaceDto = {
  id: string;
  name: string;
  createdAt: string;
  updatedAt: string;
  defaultRootId: string;
  roots: HpdosWorkspaceRootDto[];
};

export type HpdosWorkspaceRootDto = {
  id: string;
  label: string;
  path: string;
};

const workspacesEndpoint = "/api/hpdos/workspaces";
const workspaceDialogSource = "hpdos.workspace.dialog";

export async function loadWorkspaceStore(): Promise<HpdosWorkspaceStoreDto> {
  const response = await fetch(workspacesEndpoint);
  if (!response.ok) {
    throw new Error(`Failed to load HPD-OS workspaces (${response.status}).`);
  }

  return await response.json() as HpdosWorkspaceStoreDto;
}

export async function saveWorkspaceStore(store: HpdosWorkspaceStoreDto): Promise<HpdosWorkspaceStoreDto> {
  const response = await fetch(workspacesEndpoint, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json"
    },
    body: JSON.stringify(store)
  });

  if (!response.ok) {
    throw new Error(`Failed to save HPD-OS workspaces (${response.status}).`);
  }

  return await response.json() as HpdosWorkspaceStoreDto;
}

export async function loadActiveWorkspace(): Promise<HpdosWorkspaceDescriptor | null> {
  return activeWorkspaceFromStore(await loadWorkspaceStore());
}

export async function pickWorkspaceDirectories(): Promise<string[]> {
  const payload = await requestDesktopHost(workspaceDialogSource, "pickDirectories", {});
  return Array.isArray(payload)
    ? payload.filter((item): item is string => typeof item === "string" && item.trim().length > 0)
    : [];
}

export function activeWorkspaceFromStore(store: HpdosWorkspaceStoreDto): HpdosWorkspaceDescriptor | null {
  const workspace = store.workspaces.find((item) => item.id === store.activeWorkspaceId)
    ?? store.workspaces[0];

  return workspace ? toWorkspaceDescriptor(workspace) : null;
}

export function toWorkspaceDescriptor(workspace: HpdosWorkspaceDto): HpdosWorkspaceDescriptor {
  return {
    id: workspace.id,
    name: workspace.name,
    defaultRootId: workspace.defaultRootId,
    roots: workspace.roots.map(toWorkspaceRoot)
  };
}

export function createWorkspaceFromPaths(paths: string[]): HpdosWorkspaceDto | null {
  const normalizedPaths = uniqueStrings(paths.map((path) => path.trim()).filter(Boolean));
  const firstPath = normalizedPaths[0];
  if (!firstPath) return null;

  const now = new Date().toISOString();
  const name = labelFromPath(firstPath);
  const roots = normalizedPaths.map((path, index) => ({
    id: index === 0 ? "default" : slug(labelFromPath(path)),
    label: labelFromPath(path),
    path
  }));

  return {
    id: slug(name),
    name,
    createdAt: now,
    updatedAt: now,
    defaultRootId: "default",
    roots
  };
}

export function addRootsToWorkspace(workspace: HpdosWorkspaceDto, paths: string[]): HpdosWorkspaceDto {
  const now = new Date().toISOString();
  const existingPaths = new Set(workspace.roots.map((root) => root.path));
  const existingIds = new Set(workspace.roots.map((root) => root.id));
  const roots = [...workspace.roots];

  for (const path of uniqueStrings(paths.map((item) => item.trim()).filter(Boolean))) {
    if (existingPaths.has(path)) continue;
    const label = labelFromPath(path);
    roots.push({
      id: uniqueId(slug(label), existingIds),
      label,
      path
    });
    existingPaths.add(path);
  }

  return {
    ...workspace,
    updatedAt: now,
    roots
  };
}

export function removeRootFromWorkspace(workspace: HpdosWorkspaceDto, rootId: string): HpdosWorkspaceDto {
  if (workspace.roots.length <= 1) return workspace;

  const roots = workspace.roots.filter((root) => root.id !== rootId);
  const defaultRootId = roots.some((root) => root.id === workspace.defaultRootId)
    ? workspace.defaultRootId
    : roots[0]?.id ?? "default";

  return {
    ...workspace,
    updatedAt: new Date().toISOString(),
    defaultRootId,
    roots
  };
}

export function setWorkspaceDefaultRoot(workspace: HpdosWorkspaceDto, rootId: string): HpdosWorkspaceDto {
  if (!workspace.roots.some((root) => root.id === rootId)) return workspace;

  return {
    ...workspace,
    updatedAt: new Date().toISOString(),
    defaultRootId: rootId
  };
}

function toWorkspaceRoot(root: HpdosWorkspaceRootDto): HpdosRunWorkspaceRoot {
  return {
    id: root.id,
    label: root.label,
    path: root.path
  };
}

function uniqueStrings(values: string[]): string[] {
  const seen = new Set<string>();
  const result: string[] = [];
  for (const value of values) {
    if (seen.has(value)) continue;
    seen.add(value);
    result.push(value);
  }
  return result;
}

function labelFromPath(path: string): string {
  const parts = path.replace(/[/\\]+$/, "").split(/[/\\]/);
  return parts.at(-1) || "Workspace";
}

function uniqueId(baseId: string, seen: Set<string>): string {
  const base = baseId || "root";
  if (!seen.has(base)) {
    seen.add(base);
    return base;
  }

  for (let index = 2; ; index += 1) {
    const candidate = `${base}-${index}`;
    if (!seen.has(candidate)) {
      seen.add(candidate);
      return candidate;
    }
  }
}

function slug(value: string): string {
  const normalized = value.trim().toLowerCase().replace(/[^a-z0-9_-]+/g, "-").replace(/^-+|-+$/g, "");
  return normalized || "workspace";
}
