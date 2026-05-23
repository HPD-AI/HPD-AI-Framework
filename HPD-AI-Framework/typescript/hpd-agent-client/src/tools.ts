import type { ClientToolInvokeRequestEvent } from './types/events.js';
import type {
  ClientHarnessDefinition,
  ClientToolInvokeResponse,
  ToolResultContent,
} from './types/client-tools.js';
import {
  createErrorResponse,
  createJsonResult,
  createSuccessResponse,
  createTextResult,
} from './types/client-tools.js';

type MaybePromise<T> = T | Promise<T>;

export type ClientToolHandlerResult =
  | ClientToolInvokeResponse
  | ToolResultContent[]
  | string
  | unknown;

export type ClientToolHandler =
  (request: ClientToolInvokeRequestEvent) => MaybePromise<ClientToolHandlerResult>;

export class ClientToolRegistry {
  private readonly handlers = new Map<string, ClientToolHandler>();
  private readonly harnesses = new Map<string, ClientHarnessDefinition>();
  private fallbackHandler: ClientToolHandler | null = null;

  register(name: string, handler: ClientToolHandler): this {
    this.handlers.set(normalizeClientToolName(name), handler);
    return this;
  }

  registerHarness(harness: ClientHarnessDefinition, handler: ClientToolHandler): this {
    this.harnesses.set(harness.name, harness);
    for (const tool of harness.tools) this.register(tool.name, handler);
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
    this.harnesses.clear();
    this.fallbackHandler = null;
  }

  get clientHarnesses(): ClientHarnessDefinition[] {
    return [...this.harnesses.values()];
  }

  canHandle(toolName: string): boolean {
    return this.handlers.has(normalizeClientToolName(toolName)) || this.fallbackHandler !== null;
  }

  async handleInvoke(request: ClientToolInvokeRequestEvent): Promise<ClientToolInvokeResponse> {
    const toolName = normalizeClientToolName(request.toolName);
    const handler = this.handlers.get(toolName) ?? this.fallbackHandler;
    if (!handler) {
      return createErrorResponse(request.requestId, `Unknown client tool: ${request.toolName}`);
    }

    try {
      return normalizeToolResult(request.requestId, await handler(request));
    } catch (error) {
      return createErrorResponse(request.requestId, messageOf(error));
    }
  }
}

export function normalizeClientToolName(value: string): string {
  return value.trim().split('.').filter(Boolean).pop() || value.trim();
}

function normalizeToolResult(requestId: string, result: ClientToolHandlerResult): ClientToolInvokeResponse {
  if (isClientToolResponse(result)) return result;
  if (typeof result === 'string') return createSuccessResponse(requestId, createTextResult(result));
  if (Array.isArray(result)) return createSuccessResponse(requestId, result);
  return createSuccessResponse(requestId, createJsonResult(result));
}

function isClientToolResponse(value: unknown): value is ClientToolInvokeResponse {
  return Boolean(
    value &&
      typeof value === 'object' &&
      'requestId' in value &&
      'success' in value &&
      'content' in value,
  );
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error || 'Client tool failed');
}
