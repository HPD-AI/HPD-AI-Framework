import type { ClientToolInvokeRequestEvent } from './types/events.js';
import type {
  ClientToolHarnessDefinition,
  ClientToolInvokeOutcome,
  ToolResultContent,
} from './types/client-tools.js';
import {
  completeClientTool,
  failClientTool,
  createJsonResult,
  createTextResult,
} from './types/client-tools.js';

type MaybePromise<T> = T | Promise<T>;

export type ClientToolHandlerResult =
  | ClientToolInvokeOutcome
  | ToolResultContent[]
  | string
  | unknown;

export type ClientToolHandler =
  (request: ClientToolInvokeRequestEvent) => MaybePromise<ClientToolHandlerResult>;

export class ClientToolRegistry {
  private readonly handlers = new Map<string, ClientToolHandler>();
  private readonly toolharnesses = new Map<string, ClientToolHarnessDefinition>();
  private fallbackHandler: ClientToolHandler | null = null;

  register(name: string, handler: ClientToolHandler): this {
    this.handlers.set(normalizeClientToolName(name), handler);
    return this;
  }

  registerToolHarness(toolharness: ClientToolHarnessDefinition, handler: ClientToolHandler): this {
    this.toolharnesses.set(toolharness.name, toolharness);
    for (const tool of toolharness.tools) this.register(tool.name, handler);
    return this;
  }

  registerFallback(handler: ClientToolHandler): this {
    this.fallbackHandler = handler;
    return this;
  }

  unregister(name: string): this {
    this.handlers.delete(normalizeClientToolName(name));
    return this;
  }

  unregisterFallback(): this {
    this.fallbackHandler = null;
    return this;
  }

  clear(): void {
    this.handlers.clear();
    this.toolharnesses.clear();
    this.fallbackHandler = null;
  }

  get clientToolHarnesses(): ClientToolHarnessDefinition[] {
    return [...this.toolharnesses.values()];
  }

  get capabilities(): string[] {
    return [...this.handlers.keys()].map((toolName) => `client-tool:${toolName}`);
  }

  canHandle(toolName: string): boolean {
    return this.handlers.has(normalizeClientToolName(toolName)) || this.fallbackHandler !== null;
  }

  async handleInvoke(request: ClientToolInvokeRequestEvent): Promise<ClientToolInvokeOutcome> {
    const toolName = normalizeClientToolName(request.toolName);
    const handler = this.handlers.get(toolName) ?? this.fallbackHandler;
    if (!handler) {
      return failClientTool(request.requestId, `Unknown client tool: ${request.toolName}`);
    }

    try {
      return normalizeToolResult(request.requestId, await handler(request));
    } catch (error) {
      return failClientTool(request.requestId, messageOf(error));
    }
  }
}

export function normalizeClientToolName(value: string): string {
  return value.trim().split('.').filter(Boolean).pop() || value.trim();
}

function normalizeToolResult(requestId: string, result: ClientToolHandlerResult): ClientToolInvokeOutcome {
  if (isClientToolOutcome(result)) return result;
  if (typeof result === 'string') return completeClientTool(requestId, createTextResult(result));
  if (Array.isArray(result)) return completeClientTool(requestId, result);
  return completeClientTool(requestId, createJsonResult(result));
}

function isClientToolOutcome(value: unknown): value is ClientToolInvokeOutcome {
  return Boolean(
    value &&
      typeof value === 'object' &&
      'requestId' in value &&
      'outcome' in value,
  );
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error || 'Client tool failed');
}
