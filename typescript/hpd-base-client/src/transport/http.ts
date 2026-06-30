import { failureResult, makeTransportError, successStatus, type OperationContextKind } from "../result.js";
import { parseFailureResponse } from "./problem-details.js";
import type { BaseResult, BaseResponseHeaders } from "../types/results.js";

export interface HttpTransportConfig {
  baseUrl: string;
  fetch?: typeof globalThis.fetch;
  headers?: HeadersInit | (() => HeadersInit | Promise<HeadersInit>);
  credentials?: RequestCredentials;
  clientName?: string;
  clientVersion?: string;
  defaultSignal?: AbortSignal;
}

export interface HttpRequestOptions {
  method?: string;
  path: string;
  query?: URLSearchParams;
  body?: unknown;
  headers?: HeadersInit;
  signal?: AbortSignal;
  correlationId?: string;
  context?: OperationContextKind;
}

export interface HttpHeaderOptions {
  headers?: HeadersInit;
  hasBody?: boolean;
  contentType?: string | false;
  accept?: string | false;
  correlationId?: string;
}

export class HttpTransport {
  readonly baseUrl: string;
  readonly fetch: typeof globalThis.fetch;
  readonly credentials?: RequestCredentials;
  readonly defaultSignal?: AbortSignal;
  private readonly config: HttpTransportConfig;

  constructor(config: HttpTransportConfig) {
    this.baseUrl = normalizeBaseUrl(config.baseUrl);
    this.fetch = config.fetch ?? globalThis.fetch;
    if (!this.fetch) {
      throw new Error("HPD.BASE client requires global fetch or config.fetch.");
    }
    this.credentials = config.credentials;
    this.defaultSignal = config.defaultSignal;
    this.config = config;
  }

  async request<T>(options: HttpRequestOptions): Promise<BaseResult<T>> {
    try {
      const hasBody = options.body !== undefined;
      const response = await this.fetch(this.url(options.path, options.query), {
        method: options.method ?? "GET",
        credentials: this.config.credentials,
        signal: options.signal ?? this.config.defaultSignal,
        headers: await this.headers({ headers: options.headers, hasBody, correlationId: options.correlationId }),
        ...(hasBody ? { body: JSON.stringify(options.body) } : null)
      });
      const headers = extractResponseHeaders(response.headers);
      if (!response.ok) {
        const parsed = await parseFailureResponse(response, headers);
        return failureResult<T>(parsed.error, { httpStatus: response.status, headers, problem: parsed.problem, warnings: parsed.error.warnings });
      }
      const value = response.status === 204 ? undefined as T : await readSuccess<T>(response);
      return {
        ok: true,
        status: successStatus(options.context ?? "read", response.status),
        value,
        httpStatus: response.status,
        headers
      };
    } catch (cause) {
      return failureResult<T>(makeTransportError(cause instanceof Error ? cause.message : "BASE request failed.", cause));
    }
  }

  url(path: string, query?: URLSearchParams): string {
    const joined = `${this.baseUrl}${path.startsWith("/") ? path : `/${path}`}`;
    const qs = query?.toString();
    return qs ? `${joined}?${qs}` : joined;
  }

  async headers(options: HttpHeaderOptions = {}): Promise<Headers> {
    const headers = new Headers(await resolveHeaders(this.config.headers));
    if (options.accept !== false) headers.set("Accept", options.accept ?? headers.get("Accept") ?? "application/json");
    if (options.hasBody && options.contentType !== false && !headers.has("Content-Type")) headers.set("Content-Type", options.contentType ?? "application/json");
    if (this.config.clientName) headers.set("X-HPD-Client", this.config.clientName);
    if (this.config.clientVersion) headers.set("X-HPD-Client-Version", this.config.clientVersion);
    if (options.correlationId) headers.set("X-Correlation-ID", options.correlationId);
    if (options.headers) {
      new Headers(options.headers).forEach((value, key) => headers.set(key, value));
    }
    return headers;
  }
}

export function normalizeBaseUrl(baseUrl: string): string {
  const trimmed = baseUrl.trim().replace(/\/+$/u, "");
  return trimmed.length > 0 ? trimmed : "";
}

export function encodePathSegment(value: string): string {
  return encodeURIComponent(value);
}

export function extractResponseHeaders(headers: Headers): BaseResponseHeaders {
  return {
    etag: optionalHeader(headers, "etag"),
    revision: optionalHeader(headers, "hpd-base-revision"),
    lastModified: optionalHeader(headers, "last-modified"),
    location: optionalHeader(headers, "location"),
    correlationId: optionalHeader(headers, "x-correlation-id"),
    eventIds: splitHeader(headers, "hpd-base-event-ids"),
    retryAfter: optionalHeader(headers, "retry-after"),
    preferenceApplied: splitHeader(headers, "preference-applied")
  };
}

async function resolveHeaders(headers: HttpTransportConfig["headers"]): Promise<HeadersInit | undefined> {
  return typeof headers === "function" ? headers() : headers;
}

async function readSuccess<T>(response: Response): Promise<T> {
  const contentType = response.headers.get("content-type") ?? "";
  if (!contentType.includes("json")) return await response.text() as T;
  return await response.json() as T;
}

function optionalHeader(headers: Headers, name: string): string | undefined {
  return headers.get(name) ?? undefined;
}

function splitHeader(headers: Headers, name: string): string[] | undefined {
  const value = headers.get(name);
  if (!value) return undefined;
  return value.split(",").map(part => part.trim()).filter(Boolean);
}
