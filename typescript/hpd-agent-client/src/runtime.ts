/* eslint-disable */
export interface ClientOptions {
  fetch?: typeof globalThis.fetch;
  headers?: Record<string, string>;
  maxRetries?: number;
  timeoutMs?: number;
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
  public constructor(public cursor: HpdSseAppliedCursor = { generation: 0, sequence: 0 }) {}

  public apply(message: HpdSseMessage, sequence?: number): 'live' | 'duplicate' | 'committed' {
    if (message.event === 'thread-journal-rebased') {
      const data = message.data as { previousGeneration?: number; currentGeneration?: number };
      const previous = Number(data?.previousGeneration ?? 0);
      const current = Number(data?.currentGeneration ?? 0);
      this.cursor = { generation: current, sequence: 0 };
      throw new HpdJournalRebasedError(previous, current);
    }
    if (!message.id) return 'live';
    const [generationText, sequenceText] = message.id.split(':', 2);
    const generation = Number(generationText);
    const cursorSequence = Number(sequenceText);
    if (sequence !== undefined && sequence !== cursorSequence) {
      throw new HpdStreamError(`event sequence ${sequence} did not match cursor ${message.id}`);
    }
    if (generation === this.cursor.generation && cursorSequence === this.cursor.sequence) return 'duplicate';
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
  outcome: 'Completed' | 'Failed';
  content?: Array<Record<string, unknown>>;
  errorMessage?: string;
}

export type ClientToolHandler = (invocation: ClientToolInvocation) =>
  ClientToolResult | Promise<ClientToolResult>;

export class ClientToolRegistry {
  private readonly handlers = new Map<string, ClientToolHandler>();
  private fallback?: ClientToolHandler;

  public register(name: string, handler: ClientToolHandler): this {
    this.handlers.set(normalizeClientToolName(name), handler);
    return this;
  }

  public registerFallback(handler: ClientToolHandler): this {
    this.fallback = handler;
    return this;
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
      return await handler(invocation);
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

export class HpdTransport {
  private readonly baseUrl: string;
  private readonly fetchImpl: typeof globalThis.fetch;
  private readonly headers: Record<string, string>;
  private readonly maxRetries: number;
  private readonly timeoutMs: number;

  public constructor(baseUrl: string, options: ClientOptions = {}) {
    this.baseUrl = baseUrl.replace(/\/+$/, '') + '/';
    this.fetchImpl = options.fetch ?? globalThis.fetch.bind(globalThis);
    this.headers = { Accept: 'application/json', ...(options.headers ?? {}) };
    this.maxRetries = Math.max(0, options.maxRetries ?? 3);
    this.timeoutMs = options.timeoutMs ?? 60_000;
  }

  public async requestJson<T>(
    method: string,
    path: string,
    query: URLSearchParams,
    body: unknown,
    replaySafe: boolean,
  ): Promise<T> {
    const response = await this.request(method, path, query, body, replaySafe, 'application/json');
    const text = await response.text();
    return text.length === 0 ? undefined as T : JSON.parse(text) as T;
  }

  public async requestBytes(
    method: string,
    path: string,
    query: URLSearchParams,
    body: unknown,
    replaySafe: boolean,
  ): Promise<ArrayBuffer> {
    const response = await this.request(method, path, query, body, replaySafe, 'application/octet-stream');
    return response.arrayBuffer();
  }

  public async *streamSse(
    method: string,
    path: string,
    query: URLSearchParams,
    lastEventId?: string,
  ): AsyncGenerator<HpdSseMessage> {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), this.timeoutMs);
    try {
      const headers: Record<string, string> = {
        ...this.headers,
        Accept: 'text/event-stream',
        'Cache-Control': 'no-cache',
      };
      if (lastEventId) headers['Last-Event-ID'] = lastEventId;
      const url = this.url(path, query);
      const response = await this.fetchImpl(url, { method, headers, signal: controller.signal });
      if (!response.ok) throw await this.apiError(response);
      if (!response.body) return;
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
        } catch {
          return undefined;
        } finally {
          eventName = 'message';
          eventId = undefined;
        }
      };
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
      clearTimeout(timeout);
    }
  }

  private async request(
    method: string,
    path: string,
    query: URLSearchParams,
    body: unknown,
    replaySafe: boolean,
    accept: string,
  ): Promise<Response> {
    const idempotent = ['GET', 'HEAD', 'OPTIONS', 'PUT', 'DELETE'].includes(method);
    const attempts = idempotent || replaySafe ? this.maxRetries + 1 : 1;
    const headers = {
      ...this.headers,
      Accept: accept,
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
    };
    for (let attempt = 0; attempt < attempts; attempt++) {
      try {
        const response = await this.fetchImpl(this.url(path, query), {
          method,
          headers,
          body: body === undefined ? undefined : JSON.stringify(body),
        });
        if (response.ok) return response;
        if (attempt + 1 < attempts && [409, 429, 502, 503, 504].includes(response.status)) {
          await delay(response.headers.get('Retry-After'), attempt);
          continue;
        }
        throw await this.apiError(response);
      } catch (error) {
        if (error instanceof HpdApiError) throw error;
        if (attempt + 1 < attempts) {
          await delay(null, attempt);
          continue;
        }
        throw new HpdApiError(0, 'transport-error', error instanceof Error ? error.message : String(error));
      }
    }
    throw new HpdApiError(0, 'retry-exhausted', 'request failed');
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

async function delay(retryAfter: string | null, attempt: number): Promise<void> {
  const seconds = retryAfter ? Number(retryAfter) : Number.NaN;
  const milliseconds = Number.isFinite(seconds)
    ? seconds * 1_000
    : Math.min(250 * 2 ** attempt, 4_000);
  await new Promise(resolve => setTimeout(resolve, milliseconds));
}