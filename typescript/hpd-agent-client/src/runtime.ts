import type {
  ClientToolDefinition,
  ClientToolHarnessDefinition,
} from './generated/models.js';

export interface ClientOptions {
  fetch?: typeof globalThis.fetch;
  headers?: Record<string, string>;
  maxRetries?: number;
  timeoutMs?: number;
  abortSignal?: AbortSignal;
}

export interface RequestOptions {
  signal?: AbortSignal;
  timeoutMs?: number;
  headers?: Record<string, string>;
}

export interface HpdSseAppliedCursor {
  generation: number;
  sequence: number;
}

export interface HpdSseMessage {
  event: string;
  id?: string;
  data: unknown;
}

export class HpdApiError extends Error {
  public constructor(
    public readonly status: number,
    public readonly code: string,
    message: string,
    public readonly problem?: unknown,
  ) {
    super(message);
    this.name = 'HpdApiError';
  }
}

export class HpdStreamError extends Error {}

export class HpdJournalRebasedError extends HpdStreamError {
  public constructor(
    public readonly previousGeneration: number,
    public readonly currentGeneration: number,
  ) {
    super(`journal generation changed from ${previousGeneration} to ${currentGeneration}`);
  }
}

export class HpdSseState {
  public constructor(public cursor: HpdSseAppliedCursor = { generation: 1, sequence: 0 }) {}

  public apply(message: HpdSseMessage, sequence?: number): 'live' | 'duplicate' | 'committed' {
    if (message.event === 'thread-journal-rebased') {
      const data = message.data as { previousGeneration?: unknown; currentGeneration?: unknown } | null;
      const previous = data?.previousGeneration;
      const current = data?.currentGeneration;
      if (previous !== this.cursor.generation || typeof current !== 'number'
        || !Number.isSafeInteger(current) || current <= previous) {
        throw new HpdStreamError('Invalid journal rebase');
      }
      this.cursor = { generation: current, sequence: 0 };
      throw new HpdJournalRebasedError(previous, current);
    }
    if (!message.id) return 'live';
    const [generationText, sequenceText] = message.id.split(':', 2);
    if (!/^[1-9]\d*:[1-9]\d*$/.test(message.id)) {
      throw new HpdStreamError(`invalid event cursor ${message.id}`);
    }
    const generation = Number(generationText);
    const cursorSequence = Number(sequenceText);
    if (!Number.isSafeInteger(generation) || !Number.isSafeInteger(cursorSequence)) {
      throw new HpdStreamError(`invalid event cursor ${message.id}`);
    }
    if (sequence !== undefined && sequence !== cursorSequence) {
      throw new HpdStreamError(`event sequence ${sequence} did not match cursor ${message.id}`);
    }
    if (generation !== this.cursor.generation) {
      throw new HpdStreamError(`cursor generation ${generation} did not match ${this.cursor.generation}`);
    }
    if (cursorSequence <= this.cursor.sequence) return 'duplicate';
    this.cursor = { generation, sequence: cursorSequence };
    return 'committed';
  }
}

export interface ClientToolInvocation {
  requestId: string;
  toolName: string;
  callId: string;
  arguments: Record<string, unknown>;
}

export interface ClientToolResult {
  outcome: 'Completed' | 'AcceptedBackground' | 'Rejected' | 'Failed';
  content?: Array<Record<string, unknown>>;
  errorMessage?: string;
  clientOperationId?: string;
  operationKind?: number;
  operationCapabilities?: number;
  augmentation?: unknown;
}

export type ClientToolHandlerResult =
  | string
  | unknown
  | ClientToolResult;

export type ClientToolHandler = (invocation: ClientToolInvocation) =>
  ClientToolHandlerResult | Promise<ClientToolHandlerResult>;

export type PortableClientToolDefinition = ClientToolDefinition;

export type PortableClientToolHarness = ClientToolHarnessDefinition;

export class ClientToolRegistry {
  private readonly handlers = new Map<string, ClientToolHandler>();
  private readonly harnesses = new Map<string, PortableClientToolHarness>();
  private fallback?: ClientToolHandler;

  public register(name: string, handler: ClientToolHandler): this {
    this.handlers.set(normalizeClientToolName(name), handler);
    return this;
  }

  public registerFallback(handler: ClientToolHandler): this {
    this.fallback = handler;
    return this;
  }

  public registerHarness(
    harness: PortableClientToolHarness,
    handler?: ClientToolHandler,
  ): this {
    this.harnesses.set(harness.name, harness);
    if (handler) {
      for (const tool of harness.tools) this.register(tool.name, handler);
    }
    return this;
  }

  public get clientToolHarnesses(): PortableClientToolHarness[] {
    return [...this.harnesses.values()];
  }

  public canHandle(name: string): boolean {
    return this.handlers.has(normalizeClientToolName(name)) || this.fallback !== undefined;
  }

  public get capabilities(): string[] {
    return [...this.handlers.keys()].sort().map(name => `client-tool:${name}`);
  }

  public async invoke(invocation: ClientToolInvocation): Promise<ClientToolResult> {
    const handler = this.handlers.get(normalizeClientToolName(invocation.toolName)) ?? this.fallback;
    if (!handler) return { outcome: 'Failed', errorMessage: `Unknown client tool: ${invocation.toolName}` };
    try {
      const result = await handler(invocation);
      if (typeof result === 'string') return clientToolText(result);
      if (isClientToolResult(result)) return result;
      if (Array.isArray(result)) return { outcome: 'Completed', content: result as Array<Record<string, unknown>> };
      return clientToolJson(result);
    } catch (error) {
      return {
        outcome: 'Failed',
        errorMessage: error instanceof Error ? error.message : String(error || 'Client tool failed.'),
      };
    }
  }
}

export function normalizeClientToolName(value: string): string {
  const trimmed = value.trim();
  const index = trimmed.lastIndexOf('.');
  return index >= 0 && index < trimmed.length - 1 ? trimmed.slice(index + 1) : trimmed;
}

export function clientToolText(text: string): ClientToolResult {
  return { outcome: 'Completed', content: [{ type: 'text', text }] };
}

export function clientToolJson(value: unknown): ClientToolResult {
  return { outcome: 'Completed', content: [{ type: 'json', value }] };
}

export function createCollapsedToolHarness(
  name: string,
  description: string,
  tools: PortableClientToolDefinition[],
  options: Omit<PortableClientToolHarness, 'name' | 'description' | 'tools' | 'startCollapsed'> = {},
): PortableClientToolHarness {
  return { name, description, tools, ...options, startCollapsed: true };
}

export function createExpandedToolHarness(
  name: string,
  description: string,
  tools: PortableClientToolDefinition[],
  options: Omit<PortableClientToolHarness, 'name' | 'description' | 'tools' | 'startCollapsed'> = {},
): PortableClientToolHarness {
  return { name, description, tools, ...options, startCollapsed: false };
}

function isClientToolResult(value: unknown): value is ClientToolResult {
  return typeof value === 'object' && value !== null
    && 'outcome' in value
    && (value.outcome === 'Completed'
      || value.outcome === 'AcceptedBackground'
      || value.outcome === 'Rejected'
      || value.outcome === 'Failed');
}

export class HpdTransport {
  private readonly baseUrl: string;
  private readonly fetchImpl: typeof globalThis.fetch;
  private readonly headers: Record<string, string>;
  private readonly maxRetries: number;
  private readonly timeoutMs: number;
  private readonly abortSignal?: AbortSignal;

  public constructor(baseUrl: string, options: ClientOptions = {}) {
    this.baseUrl = baseUrl.replace(/\/+$/, '') + '/';
    this.fetchImpl = options.fetch ?? globalThis.fetch.bind(globalThis);
    this.headers = { Accept: 'application/json', ...(options.headers ?? {}) };
    this.maxRetries = Math.max(0, options.maxRetries ?? 3);
    this.timeoutMs = options.timeoutMs ?? 60_000;
    this.abortSignal = options.abortSignal;
    if (!Number.isSafeInteger(this.maxRetries) || this.maxRetries > 10) {
      throw new RangeError('maxRetries must be an integer between zero and ten');
    }
    if (!Number.isFinite(this.timeoutMs) || this.timeoutMs <= 0) {
      throw new RangeError('timeoutMs must be a positive finite number');
    }
  }

  public async requestJson<T>(
    method: string,
    path: string,
    query: URLSearchParams,
    body: unknown,
    replaySafe: boolean,
    options: RequestOptions = {},
  ): Promise<T> {
    const lease = await this.request(
      method,
      path,
      query,
      body,
      replaySafe,
      'application/json',
      options,
      'json');
    try {
      const text = await lease.response.text();
      return text.length === 0 ? undefined as T : JSON.parse(text) as T;
    } finally {
      lease.dispose();
    }
  }

  public async requestMultipart<T>(
    method: string,
    path: string,
    query: URLSearchParams,
    body: FormData,
    options: RequestOptions = {},
  ): Promise<T> {
    const lease = await this.request(
      method,
      path,
      query,
      body,
      false,
      'application/json',
      options,
      'form');
    try {
      return await lease.response.json() as T;
    } finally {
      lease.dispose();
    }
  }

  public async requestBytes(
    method: string,
    path: string,
    query: URLSearchParams,
    body: unknown,
    replaySafe: boolean,
    options: RequestOptions = {},
  ): Promise<ArrayBuffer> {
    const lease = await this.request(
      method,
      path,
      query,
      body,
      replaySafe,
      'application/octet-stream',
      options,
      'json');
    try {
      return await lease.response.arrayBuffer();
    } finally {
      lease.dispose();
    }
  }

  public async *streamSse(
    method: string,
    path: string,
    query: URLSearchParams,
    lastEventId?: string,
    options: RequestOptions & { onConnect?: () => void } = {},
  ): AsyncGenerator<HpdSseMessage> {
    const requestOptions = {
      ...options,
      headers: {
        ...(options.headers ?? {}),
        ...(lastEventId ? { 'Last-Event-ID': lastEventId } : {}),
      },
    };
    const lease = await this.request(
      method,
      path,
      query,
      undefined,
      true,
      'text/event-stream',
      requestOptions,
      'json');
    try {
      const response = lease.response;
      if (!response.body) throw new HpdStreamError('SSE response has no body');
      lease.clearTimeout();
      options.onConnect?.();
      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '';
      let eventName = 'message';
      let eventId: string | undefined;
      let data: string[] = [];
      const flush = (): HpdSseMessage | undefined => {
        if (data.length === 0) return undefined;
        const raw = data.join('\n');
        data = [];
        try {
          return { event: eventName, ...(eventId ? { id: eventId } : {}), data: JSON.parse(raw) };
        } catch (cause) {
          throw new HpdStreamError('Invalid SSE JSON', { cause });
        } finally {
          eventName = 'message';
          eventId = undefined;
        }
      };
      try {
        while (true) {
          const { done, value } = await reader.read();
          buffer += decoder.decode(value, { stream: !done });
          const lines = buffer.split(/\r?\n/);
          buffer = lines.pop() ?? '';
          for (const line of lines) {
            if (line === '') {
              const message = flush();
              if (message) yield message;
            } else if (line.startsWith(':')) {
              continue;
            } else {
              const separator = line.indexOf(':');
              const field = separator < 0 ? line : line.slice(0, separator);
              const rawValue = separator < 0 ? '' : line.slice(separator + 1).replace(/^ /, '');
              if (field === 'event') eventName = rawValue;
              else if (field === 'id') eventId = rawValue;
              else if (field === 'data') data.push(rawValue);
            }
          }
          if (done) {
            const message = flush();
            if (message) yield message;
            break;
          }
        }
      } finally {
        void reader.cancel().catch(() => {});
        reader.releaseLock();
      }
    } finally {
      lease.dispose();
    }
  }

  private async request(
    method: string,
    path: string,
    query: URLSearchParams,
    body: unknown,
    replaySafe: boolean,
    accept: string,
    options: RequestOptions,
    bodyKind: 'json' | 'form',
  ): Promise<ResponseLease> {
    const idempotent = ['GET', 'HEAD', 'OPTIONS', 'PUT', 'DELETE'].includes(method);
    const attempts = idempotent || replaySafe ? this.maxRetries + 1 : 1;
    let lastError: unknown;
    for (let attempt = 0; attempt < attempts; attempt++) {
      const requestSignal = this.createRequestSignal(options);
      try {
        const headers = new Headers({
          ...this.headers,
          ...(options.headers ?? {}),
          Accept: accept,
          ...(body !== undefined && bodyKind === 'json' ? { 'Content-Type': 'application/json' } : {}),
        });
        if (bodyKind === 'form') headers.delete('Content-Type');
        const response = await this.fetchImpl(this.url(path, query), {
          method,
          headers,
          body: body === undefined ? undefined : bodyKind === 'json' ? JSON.stringify(body) : body as BodyInit,
          signal: requestSignal.signal,
        });
        if (response.ok) return {
          response,
          clearTimeout: requestSignal.clearTimeout,
          dispose: requestSignal.dispose,
        };
        if (attempt + 1 < attempts && [409, 429, 502, 503, 504].includes(response.status)) {
          try {
            await delay(response.headers.get('Retry-After'), attempt, requestSignal.signal);
          } finally {
            requestSignal.dispose();
          }
          continue;
        }
        const error = await this.apiError(response);
        requestSignal.dispose();
        throw error;
      } catch (error) {
        if (error instanceof HpdApiError) throw error;
        lastError = error;
        if (requestSignal.signal?.aborted) {
          requestSignal.dispose();
          throw requestSignal.signal.reason;
        }
        if (attempt + 1 < attempts) {
          try {
            await delay(null, attempt, requestSignal.signal);
          } finally {
            requestSignal.dispose();
          }
          continue;
        }
        requestSignal.dispose();
        throw new HpdApiError(0, 'transport-error', error instanceof Error ? error.message : String(error));
      }
    }
    throw new HpdApiError(
      0,
      'retry-exhausted',
      lastError instanceof Error ? lastError.message : 'request failed');
  }

  private createRequestSignal(options: RequestOptions): RequestSignal {
    const timeoutMs = options.timeoutMs ?? this.timeoutMs;
    if (!Number.isFinite(timeoutMs) || timeoutMs <= 0) {
      throw new RangeError('timeoutMs must be a positive finite number');
    }
    const signals = [this.abortSignal, options.signal].filter(
      (signal): signal is AbortSignal => signal !== undefined);
    const controller = signals.length > 0 || timeoutMs > 0 ? new AbortController() : undefined;
    if (!controller) {
      return {
        signal: undefined,
        clearTimeout() {},
        dispose() {},
      };
    }
    const abort = (signal: AbortSignal) => controller.abort(signal.reason);
    const listeners = new Map<AbortSignal, () => void>();
    for (const signal of signals) {
      const listener = () => abort(signal);
      listeners.set(signal, listener);
      signal.addEventListener('abort', listener, { once: true });
      if (signal.aborted) abort(signal);
    }
    const timeout = setTimeout(() => controller.abort(abortError(
      `Request timed out after ${timeoutMs}ms`)), timeoutMs);
    if (controller.signal.aborted) {
      clearTimeout(timeout);
      for (const [signal, listener] of listeners) signal.removeEventListener('abort', listener);
      throw controller.signal.reason;
    }
    let disposed = false;
    return {
      signal: controller.signal,
      clearTimeout() {
        clearTimeout(timeout);
      },
      dispose() {
        if (disposed) return;
        disposed = true;
        clearTimeout(timeout);
        for (const [signal, listener] of listeners) signal.removeEventListener('abort', listener);
      },
    };
  }

  private url(path: string, query: URLSearchParams): URL {
    const url = new URL(path.replace(/^\/+/, ''), this.baseUrl);
    url.search = query.toString();
    return url;
  }

  private async apiError(response: Response): Promise<HpdApiError> {
    const text = await response.text();
    let problem: unknown;
    try {
      problem = text ? JSON.parse(text) : undefined;
    } catch {
      problem = text;
    }
    const record = problem as Record<string, unknown> | undefined;
    const code = typeof record?.type === 'string' ? record.type : `http-${response.status}`;
    const message = typeof record?.detail === 'string'
      ? record.detail
      : typeof record?.title === 'string' ? record.title : text || response.statusText;
    return new HpdApiError(response.status, code, message, problem);
  }
}

interface RequestSignal {
  readonly signal?: AbortSignal;
  clearTimeout(): void;
  dispose(): void;
}

interface ResponseLease {
  readonly response: Response;
  clearTimeout(): void;
  dispose(): void;
}

function abortError(message: string): Error {
  const error = new Error(message);
  error.name = 'AbortError';
  return error;
}

async function delay(
  retryAfter: string | null,
  attempt: number,
  signal?: AbortSignal,
): Promise<void> {
  const seconds = retryAfter ? Number(retryAfter) : Number.NaN;
  const milliseconds = Number.isFinite(seconds)
    ? seconds * 1_000
    : Math.min(250 * 2 ** attempt, 4_000);
  signal?.throwIfAborted();
  await new Promise<void>((resolve, reject) => {
    const timer = setTimeout(() => {
      signal?.removeEventListener('abort', abort);
      resolve();
    }, milliseconds);
    const abort = () => {
      clearTimeout(timer);
      reject(signal?.reason);
    };
    signal?.addEventListener('abort', abort, { once: true });
  });
}
