export interface HpdosProject {
  projectId: string;
  directory: string;
  worktree: string;
  path: string;
  name: string;
}

export interface HpdosRuntime {
  service: string;
  agentApi: string;
  project: HpdosProject;
}

export interface HpdosWorkspaceRoot {
  id: string;
  label: string;
  path: string;
}

export interface HpdosWorkspace {
  id: string;
  name: string;
  createdAt: string;
  updatedAt: string;
  defaultRootId: string;
  roots: HpdosWorkspaceRoot[];
}

export interface AgentWorkspaceContext {
  version: 1;
  defaultRootId: string;
  roots: HpdosWorkspaceRoot[];
}

export interface HpdosWorkspaceStore {
  version: 1;
  activeWorkspaceId: string;
  workspaces: HpdosWorkspace[];
}

export function initializeWorkspaceStore(runtime: HpdosRuntime, savedStore: HpdosWorkspaceStore | null): HpdosWorkspaceStore {
  if (savedStore?.workspaces.length) return normalizeWorkspaceStore(savedStore, runtime);

  const now = new Date().toISOString();
  const root = {
    id: "default",
    label: runtime.project.name || labelFromPath(runtime.project.directory),
    path: runtime.project.directory
  };
  const workspace = buildWorkspace({
    id: slug(runtime.project.name || root.label || "workspace"),
    name: root.label,
    createdAt: now,
    updatedAt: now,
    defaultRootId: root.id,
    roots: [root]
  }, runtime);

  return {
    version: 1 as const,
    activeWorkspaceId: workspace.id,
    workspaces: [workspace]
  };
}

export function activeWorkspace(store: HpdosWorkspaceStore | null) {
  if (!store) return null;
  return store.workspaces.find((workspace) => workspace.id === store.activeWorkspaceId) || store.workspaces[0] || null;
}

export function addWorkspaceRoots(store: HpdosWorkspaceStore, paths: string[], runtime: HpdosRuntime) {
  const workspace = requireActiveWorkspace(store);
  const roots = workspace.roots.slice();
  const seenPaths = new Set(roots.map((root) => pathKey(root.path)));
  for (const path of paths) {
    const trimmed = path.trim();
    if (!trimmed) continue;
    const key = pathKey(trimmed);
    if (seenPaths.has(key)) continue;
    seenPaths.add(key);
    roots.push({ id: "", label: labelFromPath(trimmed), path: trimmed });
  }
  if (roots.length === workspace.roots.length) throw new Error("Those directories are already in this workspace.");
  return updateActiveWorkspace(store, runtime, {
    ...workspace,
    roots,
    updatedAt: new Date().toISOString()
  });
}

export function createWorkspace(store: HpdosWorkspaceStore, name: string, paths: string[], runtime: HpdosRuntime) {
  const now = new Date().toISOString();
  const trimmed = name.trim();
  const roots = paths
    .map((path) => path.trim())
    .filter(Boolean)
    .map((path) => ({ id: "", label: labelFromPath(path), path }));
  if (!roots.length) throw new Error("A workspace needs at least one directory.");
  const root = roots[0];
  const workspace = buildWorkspace({
    id: slug(trimmed || root.label || "workspace"),
    name: trimmed || root.label,
    createdAt: now,
    updatedAt: now,
    defaultRootId: root.id,
    roots
  }, runtime, new Set(store.workspaces.map((item) => item.id)));

  return normalizeWorkspaceStore({
    ...store,
    activeWorkspaceId: workspace.id,
    workspaces: [...store.workspaces, workspace]
  }, runtime);
}

export function deleteWorkspace(store: HpdosWorkspaceStore, workspaceId: string, runtime: HpdosRuntime) {
  if (store.workspaces.length <= 1) return store;
  const workspaces = store.workspaces.filter((workspace) => workspace.id !== workspaceId);
  if (workspaces.length === store.workspaces.length) return store;
  return normalizeWorkspaceStore({
    ...store,
    activeWorkspaceId: store.activeWorkspaceId === workspaceId ? workspaces[0].id : store.activeWorkspaceId,
    workspaces
  }, runtime);
}

export function removeWorkspaceRoot(store: HpdosWorkspaceStore, rootId: string, runtime: HpdosRuntime) {
  const workspace = requireActiveWorkspace(store);
  if (workspace.roots.length <= 1) return store;
  return updateActiveWorkspace(store, runtime, {
    ...workspace,
    roots: workspace.roots.filter((root) => root.id !== rootId),
    updatedAt: new Date().toISOString()
  });
}

export function switchWorkspace(store: HpdosWorkspaceStore, workspaceId: string) {
  if (!store.workspaces.some((workspace) => workspace.id === workspaceId)) return store;
  return {
    ...store,
    activeWorkspaceId: workspaceId
  };
}

export function workspaceSystemInstructions(workspace: HpdosWorkspace) {
  const defaultRoot = workspace.roots.find((root) => root.id === workspace.defaultRootId) || workspace.roots[0];
  const roots = workspace.roots
    .map((root) => `- @${root.id} (${root.label}): ${root.path}`)
    .join("\n");

  return [
    "HPD-OS active workspace:",
    `Workspace: ${workspace.name} (${workspace.id})`,
    `Default root: @${defaultRoot?.id || workspace.defaultRootId} ${defaultRoot?.path || ""}`.trim(),
    "Workspace directories:",
    roots,
    "When using coding tools, relative paths resolve from the default root. Use @rootId/path for a non-default root."
  ].join("\n");
}

export function agentWorkspaceContext(workspace: HpdosWorkspace): AgentWorkspaceContext {
  return {
    version: 1,
    defaultRootId: workspace.defaultRootId,
    roots: workspace.roots.map((root) => ({
      id: root.id,
      label: root.label,
      path: root.path
    }))
  };
}

export function sessionScope(runtime: HpdosRuntime) {
  return {
    "hpdos.projectId": runtime.project.projectId
  };
}

export function workspaceSessionScope(runtime: HpdosRuntime, workspace: HpdosWorkspace) {
  return {
    ...sessionScope(runtime),
    "hpdos.workspaceId": workspace.id
  };
}

export function sessionMetadata(runtime: HpdosRuntime, workspace: HpdosWorkspace, title: string) {
  const defaultRoot = workspace.roots.find((root) => root.id === workspace.defaultRootId) || workspace.roots[0];
  return {
    ...workspaceSessionScope(runtime, workspace),
    "hpdos.projectName": runtime.project.name,
    "hpdos.projectDirectory": runtime.project.directory,
    "hpdos.worktree": runtime.project.worktree,
    "hpdos.projectPath": runtime.project.path,
    "hpdos.workspaceName": workspace.name,
    "hpdos.workspaceDefaultRootId": defaultRoot?.id || workspace.defaultRootId,
    "hpdos.workspaceDefaultRootLabel": defaultRoot?.label || "",
    "hpdos.workspaceDefaultRoot": defaultRoot?.path || runtime.project.directory,
    "hpdos.workspaceRootCount": workspace.roots.length,
    "hpdos.title": title
  };
}

export function sessionWorkspaceId(session: { metadata?: Record<string, unknown> }) {
  const value = session.metadata?.["hpdos.workspaceId"];
  return typeof value === "string" ? value : "";
}

export function parseSavedWorkspaceStore(value: string | null): HpdosWorkspaceStore | null {
  try {
    if (!value) return null;
    const parsed = JSON.parse(value) as unknown;
    if (!parsed || typeof parsed !== "object") return null;
    const candidate = parsed as Partial<HpdosWorkspaceStore>;
    if (!Array.isArray(candidate.workspaces)) return null;
    return {
      version: 1,
      activeWorkspaceId: typeof candidate.activeWorkspaceId === "string" ? candidate.activeWorkspaceId : "",
      workspaces: candidate.workspaces
        .map((item) => item && typeof item === "object" ? item as Partial<HpdosWorkspace> : null)
        .filter((item): item is Partial<HpdosWorkspace> => !!item)
        .map(parseWorkspace)
        .filter((workspace): workspace is HpdosWorkspace => !!workspace)
    };
  } catch {
    return null;
  }
}

export function serializeWorkspaceStore(store: HpdosWorkspaceStore) {
  return JSON.stringify(store);
}

export function labelFromPath(path: string) {
  const trimmed = path.trim().replace(/[\\\/]+$/, "");
  const parts = trimmed.split(/[\\\/]/).filter(Boolean);
  return parts[parts.length - 1] || "Workspace";
}

function parseWorkspace(value: Partial<HpdosWorkspace>) {
  if (!Array.isArray(value.roots)) return null;
  const roots = value.roots
    .map((item) => item && typeof item === "object" ? item as Partial<HpdosWorkspaceRoot> : null)
    .filter((item): item is Partial<HpdosWorkspaceRoot> => !!item?.path && typeof item.path === "string")
    .map((item) => ({
      id: typeof item.id === "string" ? item.id : "",
      label: typeof item.label === "string" && item.label.trim() ? item.label.trim() : labelFromPath(item.path || ""),
      path: item.path!.trim()
    }));
  if (!roots.length) return null;

  const now = new Date().toISOString();
  return {
    id: typeof value.id === "string" && value.id.trim() ? value.id.trim() : "",
    name: typeof value.name === "string" && value.name.trim() ? value.name.trim() : labelFromPath(roots[0].path),
    createdAt: typeof value.createdAt === "string" ? value.createdAt : now,
    updatedAt: typeof value.updatedAt === "string" ? value.updatedAt : now,
    defaultRootId: typeof value.defaultRootId === "string" ? value.defaultRootId : "",
    roots
  };
}

function normalizeWorkspaceStore(store: HpdosWorkspaceStore, runtime: HpdosRuntime): HpdosWorkspaceStore {
  const seenWorkspaceIds = new Set<string>();
  const workspaces = store.workspaces
    .map((workspace) => buildWorkspace(workspace, runtime, seenWorkspaceIds))
    .filter((workspace) => workspace.roots.length);

  if (!workspaces.length) return initializeWorkspaceStore(runtime, null);
  const activeId = workspaces.some((workspace) => workspace.id === store.activeWorkspaceId)
    ? store.activeWorkspaceId
    : workspaces[0].id;

  return {
    version: 1 as const,
    activeWorkspaceId: activeId,
    workspaces
  };
}

function updateActiveWorkspace(store: HpdosWorkspaceStore, runtime: HpdosRuntime, workspace: HpdosWorkspace) {
  return normalizeWorkspaceStore({
    ...store,
    workspaces: store.workspaces.map((item) => item.id === workspace.id ? workspace : item)
  }, runtime);
}

function buildWorkspace(workspace: HpdosWorkspace, runtime: HpdosRuntime, seenWorkspaceIds = new Set<string>()): HpdosWorkspace {
  const seenPaths = new Set<string>();
  const seenRootIds = new Set<string>();
  const cleanRoots = workspace.roots
    .map((root) => ({
      id: root.id,
      label: root.label?.trim() || labelFromPath(root.path),
      path: root.path?.trim() || ""
    }))
    .filter((root) => {
      if (!root.path) return false;
      const key = pathKey(root.path);
      if (seenPaths.has(key)) return false;
      seenPaths.add(key);
      return true;
    })
    .map((root, index) => {
      const baseId = index === 0 ? "default" : slug(root.id || root.label || `root-${index + 1}`);
      const id = uniqueId(baseId, seenRootIds);
      return { id, label: root.label, path: root.path };
    });

  if (!cleanRoots.length) {
    cleanRoots.push({
      id: "default",
      label: runtime.project.name || labelFromPath(runtime.project.directory),
      path: runtime.project.directory
    });
  }

  const workspaceId = uniqueId(slug(workspace.id || workspace.name || cleanRoots[0].label), seenWorkspaceIds);
  const defaultRoot = cleanRoots.find((root) => root.id === workspace.defaultRootId) || cleanRoots[0];
  return {
    ...workspace,
    id: workspaceId,
    name: workspace.name?.trim() || defaultRoot.label || labelFromPath(defaultRoot.path),
    defaultRootId: defaultRoot.id,
    roots: cleanRoots
  };
}

function requireActiveWorkspace(store: HpdosWorkspaceStore) {
  const workspace = activeWorkspace(store);
  if (!workspace) throw new Error("HPDOS workspace is not initialized.");
  return workspace;
}

function uniqueId(baseId: string, seen: Set<string>) {
  let candidate = slug(baseId) || "item";
  if (!seen.has(candidate)) {
    seen.add(candidate);
    return candidate;
  }

  for (let index = 2; ; index++) {
    const next = `${candidate}-${index}`;
    if (!seen.has(next)) {
      seen.add(next);
      return next;
    }
  }
}

function slug(value: string) {
  return value.trim().toLowerCase().replace(/[^a-z0-9_-]+/g, "-").replace(/^-+|-+$/g, "") || "item";
}

function pathKey(path: string) {
  return path.trim().replace(/[\\\/]+$/, "").toLowerCase();
}
