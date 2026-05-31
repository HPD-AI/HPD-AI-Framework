import type { SearchSessionsRequest } from "@hpd/hpd-agent-client";

export type HpdosSessionMetadata = {
  app: "hpd-os";
  workspaceId?: string;
  defaultRootId?: string;
  defaultRootPath?: string;
  workspaceName?: string;
  defaultRootLabel?: string;
  providerModel?: HpdosSessionProviderModel;
};

export type HpdosSessionProviderModel = {
  providerKey: string;
  modelId: string;
};

export type HpdosRunWorkspaceRoot = {
  id: string;
  path: string;
  label?: string;
};

export type HpdosRunWorkspace = {
  version: 1;
  defaultRootId: string;
  roots: HpdosRunWorkspaceRoot[];
};

export type HpdosWorkspaceDescriptor = {
  id: string;
  name?: string;
  defaultRootId: string;
  roots: HpdosRunWorkspaceRoot[];
};

export function createSessionMetadata(
  workspace: HpdosWorkspaceDescriptor,
  providerModel?: HpdosSessionProviderModel
): HpdosSessionMetadata {
  const defaultRoot = workspace.roots.find((root) => root.id === workspace.defaultRootId) ?? workspace.roots[0];
  const normalizedProviderModel = normalizeSessionProviderModel(providerModel);

  const metadata: HpdosSessionMetadata = {
    app: "hpd-os",
    workspaceId: workspace.id,
    defaultRootId: defaultRoot?.id ?? workspace.defaultRootId,
    defaultRootPath: defaultRoot?.path ?? "",
    workspaceName: workspace.name,
    defaultRootLabel: defaultRoot?.label
  };

  if (normalizedProviderModel) {
    metadata.providerModel = normalizedProviderModel;
  }

  return metadata;
}

export function createSessionSearch(workspace: HpdosWorkspaceDescriptor, limit = 50): SearchSessionsRequest {
  return {
    metadata: {
      app: "hpd-os",
      workspaceId: workspace.id
    },
    limit
  };
}

export function createUnscopedSessionMetadata(
  providerModel?: HpdosSessionProviderModel
): HpdosSessionMetadata {
  const metadata: HpdosSessionMetadata = { app: "hpd-os" };
  const normalizedProviderModel = normalizeSessionProviderModel(providerModel);
  if (normalizedProviderModel) {
    metadata.providerModel = normalizedProviderModel;
  }

  return metadata;
}

export function createUnscopedSessionSearch(limit = 20): SearchSessionsRequest {
  return {
    metadata: {
      app: "hpd-os"
    },
    limit
  };
}

export function isUnscopedSessionMetadata(metadata: Record<string, unknown> | undefined): boolean {
  return metadata?.app === "hpd-os" && typeof metadata.workspaceId !== "string";
}

export function createSessionProviderModelMetadata(
  providerModel: HpdosSessionProviderModel
): Partial<Pick<HpdosSessionMetadata, "providerModel">> {
  const normalized = normalizeSessionProviderModel(providerModel);
  return normalized ? { providerModel: normalized } : {};
}

export function normalizeSessionProviderModel(value: unknown): HpdosSessionProviderModel | undefined {
  if (typeof value !== "object" || value === null) return undefined;

  const record = value as Partial<HpdosSessionProviderModel>;
  const providerKey = normalizeString(record.providerKey);
  const modelId = normalizeString(record.modelId);
  if (!providerKey || !modelId) return undefined;

  return { providerKey, modelId };
}

export function readSessionProviderModel(metadata: Record<string, unknown> | undefined): HpdosSessionProviderModel | undefined {
  return normalizeSessionProviderModel(metadata?.providerModel);
}

export function createRunWorkspace(workspace: HpdosWorkspaceDescriptor): HpdosRunWorkspace {
  return {
    version: 1,
    defaultRootId: workspace.defaultRootId,
    roots: workspace.roots.map((root) => ({
      id: root.id,
      path: root.path,
      label: root.label
    }))
  };
}

export function buildWorkspaceInstructions(workspace: HpdosWorkspaceDescriptor): string {
  const defaultRoot = workspace.roots.find((root) => root.id === workspace.defaultRootId) ?? workspace.roots[0];
  const additionalRoots = workspace.roots.filter((root) => root.id !== defaultRoot?.id);
  const lines = [
    "Current HPD-OS workspace:",
    `- Workspace: ${workspace.name ?? workspace.id}`,
    `- Default root: ${defaultRoot?.path ?? ""}`
  ];

  if (additionalRoots.length > 0) {
    lines.push("- Additional roots:");
    for (const root of additionalRoots) {
      lines.push(`  - @${root.label ?? root.id} => ${root.path}`);
    }
    lines.push("Use root-qualified paths such as @docs/... for non-default roots.");
  }

  return lines.join("\n");
}

function normalizeString(value: unknown): string | undefined {
  if (typeof value !== "string") return undefined;

  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : undefined;
}
