import type {
  ClientHarnessDefinition,
  ClientToolInvokeRequestEvent,
  ClientToolInvokeResponse
} from "@hpd/hpd-agent-client";
import {
  createErrorResponse,
  createJsonResponse,
  normalizeClientToolName
} from "@hpd/hpd-agent-client";

export type ArtifactView = "preview" | "code";
export type ArtifactType = "text" | "markdown" | "code" | "html" | "json";

export interface ArtifactRecord {
  id: string;
  title: string;
  type: ArtifactType;
  content: string;
  language?: string;
  createdAt: string;
  updatedAt: string;
}

export class HpdosArtifacts {
  readonly harness: ClientHarnessDefinition = browserArtifactHarness;
 
  private readonly artifacts = new Map<string, ArtifactRecord>();
  private openArtifactIdValue: string | null = null;

  get all() {
    return Array.from(this.artifacts.values());
  }

  get openArtifactId() {
    return this.openArtifactIdValue;
  }

  get context() {
    return {
      openArtifactId: this.openArtifactIdValue,
      artifactCount: this.artifacts.size
    };
  }

  clear() {
    this.artifacts.clear();
    this.openArtifactIdValue = null;
  }

  handleToolRequest(request: ClientToolInvokeRequestEvent): ClientToolInvokeResponse | null {
    const toolName = normalizeClientToolName(request.toolName);
    if (toolName === "create_artifact") {
      const artifact = this.applyToolCall(toolName, request.arguments);
      if (!artifact) return createErrorResponse(request.requestId, "Failed to create artifact.");
      return createJsonResponse(request.requestId, { artifact, opened: this.openArtifactIdValue === artifact.id });
    }

    if (toolName === "update_artifact") {
      const id = stringArg(request.arguments, "id");
      if (!id || !this.artifacts.has(id)) {
        return createErrorResponse(request.requestId, `Artifact not found: ${id || "(missing id)"}`);
      }

      const artifact = this.applyToolCall(toolName, request.arguments);
      if (!artifact) return createErrorResponse(request.requestId, `Artifact not found: ${id}`);
      return createJsonResponse(request.requestId, { artifact, opened: this.openArtifactIdValue === artifact.id });
    }

    if (toolName === "open_artifact") {
      const id = stringArg(request.arguments, "id");
      if (!id || !this.artifacts.has(id)) {
        return createErrorResponse(request.requestId, `Artifact not found: ${id || "(missing id)"}`);
      }

      this.open(id);
      return createJsonResponse(request.requestId, { id, opened: true });
    }

    if (toolName === "list_artifacts") {
      return createJsonResponse(request.requestId, {
        openArtifactId: this.openArtifactIdValue,
        artifacts: this.all.map(({ id, title, type, language, updatedAt }) => ({ id, title, type, language, updatedAt }))
      });
    }

    if (toolName === "close_artifact") {
      this.close();
      return createJsonResponse(request.requestId, { opened: false });
    }

    return null;
  }

  applyHistoryToolCall(toolName: string, args: Record<string, unknown>, timestamp: string) {
    if (!isArtifactToolName(toolName)) return false;

    try {
      this.applyToolCall(toolName, args, timestamp);
      return true;
    } catch {
      return false;
    }
  }

  open(id: string) {
    if (!this.artifacts.has(id)) return;
    this.openArtifactIdValue = id;
  }

  close() {
    this.openArtifactIdValue = null;
  }

  private applyToolCall(toolName: string, args: Record<string, unknown>, timestamp = new Date().toISOString()) {
    if (toolName === "create_artifact") {
      const artifact = this.upsert(args, true, timestamp);
      if (args.open !== false) this.openArtifactIdValue = artifact.id;
      return artifact;
    }

    if (toolName === "update_artifact") {
      const artifact = this.upsert(args, false, timestamp);
      if (args.open === true || this.openArtifactIdValue === artifact.id) this.openArtifactIdValue = artifact.id;
      return artifact;
    }

    if (toolName === "open_artifact") {
      const id = stringArg(args, "id");
      if (id && this.artifacts.has(id)) {
        this.openArtifactIdValue = id;
        return this.artifacts.get(id) || null;
      }
    }

    if (toolName === "close_artifact") {
      this.openArtifactIdValue = null;
    }

    return null;
  }

  private upsert(args: Record<string, unknown>, create: boolean, timestamp = new Date().toISOString()): ArtifactRecord {
    const id = stringArg(args, "id") || `artifact-${crypto.randomUUID().slice(0, 8)}`;
    const previous = this.artifacts.get(id);
    const artifact: ArtifactRecord = {
      id,
      title: stringArg(args, "title") || previous?.title || "Untitled artifact",
      type: artifactTypeArg(args, "type") || previous?.type || "text",
      content: stringArg(args, "content") ?? previous?.content ?? "",
      language: stringArg(args, "language") || previous?.language,
      createdAt: previous?.createdAt || timestamp,
      updatedAt: timestamp
    };
    if (!create && !previous) throw new Error(`Artifact not found: ${id}`);
    this.artifacts.set(id, artifact);
    return artifact;
  }
}

export function isArtifactToolName(toolName: string) {
  return toolName === "create_artifact"
    || toolName === "update_artifact"
    || toolName === "open_artifact"
    || toolName === "close_artifact"
    || toolName === "list_artifacts";
}

function stringArg(args: Record<string, unknown>, key: string): string | undefined {
  const value = args[key];
  return typeof value === "string" && value.trim() ? value.trim() : undefined;
}

function artifactTypeArg(args: Record<string, unknown>, key: string): ArtifactType | undefined {
  const value = stringArg(args, key);
  return value === "text" || value === "markdown" || value === "code" || value === "html" || value === "json" ? value : undefined;
}

const browserArtifactHarness: ClientHarnessDefinition = {
  name: "hpdos.browser",
  description: "Tools for inspecting the current HPD-OS browser shell and creating artifacts in the UI.",
  startCollapsed: false,
  tools: [
    {
      name: "create_artifact",
      description: "Create or replace a browser-side artifact and show it inline in the chat.",
      parametersSchema: {
        type: "object",
        properties: {
          id: { type: "string", description: "Optional stable artifact id. A generated id is used when omitted." },
          title: { type: "string", description: "Short title shown in the artifact card." },
          type: { type: "string", enum: ["text", "markdown", "code", "html", "json"], description: "Artifact rendering type." },
          content: { type: "string", description: "Artifact content." },
          language: { type: "string", description: "Optional code language label." },
          open: { type: "boolean", description: "Whether to focus the artifact card immediately." }
        },
        required: ["title", "type", "content"],
        additionalProperties: false
      }
    },
    {
      name: "update_artifact",
      description: "Update an existing browser-side artifact.",
      parametersSchema: {
        type: "object",
        properties: {
          id: { type: "string" },
          title: { type: "string" },
          type: { type: "string", enum: ["text", "markdown", "code", "html", "json"] },
          content: { type: "string" },
          language: { type: "string" },
          open: { type: "boolean" }
        },
        required: ["id"],
        additionalProperties: false
      }
    },
    {
      name: "open_artifact",
      description: "Open an existing browser-side artifact by id.",
      parametersSchema: {
        type: "object",
        properties: { id: { type: "string" } },
        required: ["id"],
        additionalProperties: false
      }
    },
    {
      name: "list_artifacts",
      description: "List browser-side artifacts currently available in the shell.",
      parametersSchema: {
        type: "object",
        properties: {},
        additionalProperties: false
      }
    },
    {
      name: "close_artifact",
      description: "Unfocus the current inline artifact.",
      parametersSchema: {
        type: "object",
        properties: {},
        additionalProperties: false
      }
    }
  ]
};
