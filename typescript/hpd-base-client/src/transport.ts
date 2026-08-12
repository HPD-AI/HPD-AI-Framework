import type { BaseClientError, BaseResult, BaseRetryClassification, BaseSuccessStatus } from "./result.js";

export interface BaseTransportOptions {
  readonly url: string;
  readonly accessToken?: () => string | undefined | Promise<string | undefined>;
  readonly fetch?: typeof globalThis.fetch;
  readonly credentials?: RequestCredentials;
  readonly maximumResponseBytes?: number;
}

export class BaseHttpTransport {
  readonly #url: URL;
  readonly #token: (() => string | undefined | Promise<string | undefined>) | undefined;
  readonly #fetch: typeof globalThis.fetch;
  readonly #credentials: RequestCredentials | undefined;
  readonly #maximumResponseBytes: number;

  public constructor(options: BaseTransportOptions) {
    this.#url = new URL(options.url, globalThis.location?.href ?? "http://localhost");
    this.#token = options.accessToken;
    this.#fetch = options.fetch ?? globalThis.fetch;
    this.#credentials = options.credentials;
    this.#maximumResponseBytes = options.maximumResponseBytes ?? 4 * 1024 * 1024;
    if (this.#token !== undefined && this.#credentials !== undefined) throw new TypeError("Conflicting authentication mechanisms.");
  }

  public async json<T>(method: string, route: string, body: Uint8Array | undefined, signal?: AbortSignal, idempotencyKey?: string, requestedCorrelationId?: string): Promise<BaseResult<T>> {
    const response = await this.request(method, route, body === undefined ? undefined : new Uint8Array(body).buffer as ArrayBuffer, body === undefined ? undefined : "application/json", signal, idempotencyKey, undefined, requestedCorrelationId);
    if (!response.ok) return response.result;
    const { bytes, correlationId: responseCorrelationId, status } = response;
    try {
      const value = parseBaseJson(new TextDecoder("utf-8", { fatal: true }).decode(bytes));
      return { ok: true, value: value as T, status: successStatus(status), correlationId: responseCorrelationId, warnings: [] };
    } catch {
      return failure("base.client.responseInvalid", "unexpected", "The BASE response was invalid.", "never", responseCorrelationId);
    }
  }

  public async binary(method: string, route: string, body: BodyInit | undefined, contentType: string | undefined, signal?: AbortSignal, headers?: Readonly<Record<string, string>>): Promise<BaseResult<Uint8Array>> {
    const response = await this.request(method, route, body, contentType, signal, undefined, headers);
    return response.ok
      ? { ok: true, value: response.bytes, status: successStatus(response.status), correlationId: response.correlationId, warnings: [] }
      : response.result;
  }

  public async empty(method: string, route: string, signal?: AbortSignal): Promise<BaseResult<undefined>> {
    const response = await this.request(method, route, undefined, undefined, signal);
    if (!response.ok) return response.result;
    if (response.bytes.length !== 0) return failure("base.client.responseInvalid", "unexpected", "The BASE response was invalid.", "never", response.correlationId);
    return { ok: true, value: undefined, status: successStatus(response.status), correlationId: response.correlationId, warnings: [] };
  }

  public async stream(method: string, route: string, signal?: AbortSignal): Promise<BaseResult<ReadableStream<Uint8Array>>> {
    const token = await this.#token?.(); const requestedCorrelation = crypto.randomUUID(); const headers = new Headers({ Accept: "application/octet-stream", "X-Correlation-ID": requestedCorrelation });
    if (token !== undefined) headers.set("Authorization", `Bearer ${token}`);
    let response: Response;
    try { response = await this.#fetch(new URL(route.replace(/^\//u, ""), ensureSlash(this.#url)), { method, headers, ...(signal === undefined ? {} : { signal }), ...(this.#credentials === undefined ? {} : { credentials: this.#credentials }), redirect: "error" }); }
    catch (cause: unknown) { if (signal?.aborted === true) throw cause; return failure("base.client.transportFailed", "unexpected", "The BASE transport failed.", "safe"); }
    const correlationId = response.headers.get("X-Correlation-ID") ?? requestedCorrelation;
    if (!response.ok) { let bytes: Uint8Array; try { bytes = await boundedBody(response, this.#maximumResponseBytes); } catch { return failure("base.client.responseTooLarge", "validation", "The BASE response exceeded the configured limit.", "never", correlationId); } return parseProblem(bytes, correlationId, response.status); }
    if (response.body === null) return failure("base.client.responseInvalid", "unexpected", "The BASE response was invalid.", "never", correlationId);
    return { ok: true, value: response.body, status: successStatus(response.status), correlationId, warnings: [] };
  }

  public async raw(method: string, route: string, body: BodyInit | undefined, contentType: string | undefined, accept: string, signal?: AbortSignal): Promise<BaseResult<Response>> {
    const token = await this.#token?.(); const requestedCorrelation = crypto.randomUUID();
    const headers = new Headers({ Accept: accept, "X-Correlation-ID": requestedCorrelation });
    if (contentType !== undefined) headers.set("Content-Type", contentType);
    if (token !== undefined) headers.set("Authorization", `Bearer ${token}`);
    let response: Response;
    try { const init: RequestInit & { duplex?: "half" } = { method, headers, ...(body === undefined ? {} : { body }), ...(signal === undefined ? {} : { signal }), ...(this.#credentials === undefined ? {} : { credentials: this.#credentials }), redirect: "error" }; if (body instanceof ReadableStream) init.duplex = "half"; response = await this.#fetch(new URL(route.replace(/^\//u, ""), ensureSlash(this.#url)), init); }
    catch (cause: unknown) { if (signal?.aborted === true) throw cause; return failure("base.client.transportFailed", "unexpected", "The BASE transport failed.", "safe"); }
    const correlationId = response.headers.get("X-Correlation-ID") ?? requestedCorrelation;
    if (!response.ok) { let bytes: Uint8Array; try { bytes = await boundedBody(response, this.#maximumResponseBytes); } catch { return failure("base.client.responseTooLarge", "validation", "The BASE response exceeded the configured limit.", "never", correlationId); } return parseProblem(bytes, correlationId, response.status); }
    return { ok: true, value: response, status: successStatus(response.status), correlationId, warnings: [] };
  }

  private async request(method: string, route: string, body: BodyInit | undefined, contentType: string | undefined, signal?: AbortSignal, idempotencyKey?: string, additional?: Readonly<Record<string, string>>, requestedCorrelationId?: string): Promise<{ readonly ok: true; readonly bytes: Uint8Array; readonly correlationId: string; readonly status: number } | { readonly ok: false; readonly result: BaseResult<never> }> {
    const token = await this.#token?.();
    const headers = new Headers({ Accept: "application/json", "X-Correlation-ID": requestedCorrelationId ?? crypto.randomUUID() });
    if (contentType !== undefined) headers.set("Content-Type", contentType);
    if (token !== undefined) headers.set("Authorization", `Bearer ${token}`);
    if (idempotencyKey !== undefined) headers.set("Idempotency-Key", idempotencyKey);
    for (const [name, value] of Object.entries(additional ?? {})) headers.set(name, value);
    let response: Response;
    try {
      const init: RequestInit & { duplex?: "half" } = {
        method,
        headers,
        ...(body === undefined ? {} : { body }),
        ...(signal === undefined ? {} : { signal }),
        ...(this.#credentials === undefined ? {} : { credentials: this.#credentials }),
        redirect: "error"
      }; if (body instanceof ReadableStream) init.duplex = "half";
      response = await this.#fetch(new URL(route.replace(/^\//u, ""), ensureSlash(this.#url)), init);
    } catch (cause: unknown) {
      if (signal?.aborted === true) throw cause;
      return { ok: false, result: failure("base.client.transportFailed", "unexpected", "The BASE transport failed.", "safe") };
    }
    let bytes: Uint8Array;
    try { bytes = await boundedBody(response, this.#maximumResponseBytes); }
    catch { return { ok: false, result: failure("base.client.responseTooLarge", "validation", "The BASE response exceeded the configured limit.", "never") }; }
    const correlationId = response.headers.get("X-Correlation-ID") ?? headers.get("X-Correlation-ID") ?? "";
    if (!response.ok) return { ok: false, result: parseProblem(bytes, correlationId, response.status) };
    return { ok: true, bytes, correlationId, status: response.status };
  }
}

async function boundedBody(response: Response, maximum: number): Promise<Uint8Array> {
  const declared = response.headers.get("Content-Length");
  if (declared !== null && Number(declared) > maximum) throw new RangeError("base.client.responseTooLarge");
  const reader = response.body?.getReader();
  if (reader === undefined) return new Uint8Array();
  const chunks: Uint8Array[] = [];
  let length = 0;
  for (;;) {
    const next = await reader.read();
    if (next.done) break;
    length += next.value.byteLength;
    if (length > maximum) { await reader.cancel(); throw new RangeError("base.client.responseTooLarge"); }
    chunks.push(next.value);
  }
  const result = new Uint8Array(length);
  let offset = 0;
  for (const chunk of chunks) { result.set(chunk, offset); offset += chunk.byteLength; }
  return result;
}

function parseProblem(bytes: Uint8Array, correlationId: string, status: number): BaseResult<never> {
  let code = status === 401 ? "base.http.authenticationRequired" : status === 403 ? "base.http.authorizationDenied" : "base.client.responseInvalid";
  try {
    const parsed = parseBaseJson(new TextDecoder("utf-8", { fatal: true }).decode(bytes)) as { code?: unknown; extensions?: { code?: unknown } };
    const candidate = parsed.code ?? parsed.extensions?.code;
    if (typeof candidate === "string" && candidate.length <= 128) code = candidate;
  } catch { /* fixed safe result */ }
  const category: BaseClientError["category"] = status === 401 ? "authentication" : status === 403 ? "authorization" : status === 404 ? "notFound" : status === 409 ? "conflict" : status >= 500 ? "store" : "validation";
  return failure(code, category, "The BASE operation failed.", status >= 500 ? "safe" : "never", correlationId);
}

function failure<T>(code: string, category: BaseClientError["category"], message: string, retry: BaseRetryClassification, correlationId?: string): BaseResult<T> {
  return { ok: false, error: { code, category, message }, ...(correlationId === undefined ? {} : { correlationId }), retry };
}

function successStatus(status: number): BaseSuccessStatus { return status === 201 ? "created" : status === 202 ? "accepted" : status === 204 ? "noContent" : "ok"; }
function ensureSlash(url: URL): URL { const value = new URL(url); if (!value.pathname.endsWith("/")) value.pathname += "/"; return value; }

/** Parses bounded BASE JSON while retaining the numeric lexemes needed to reject lossy JavaScript materialization. */
export function parseBaseJson(json: string): unknown {
  let index = 0; const whitespace = (): void => { while (index < json.length && /[\t\n\r ]/u.test(json[index]!)) index++; };
  const string = (): string => { const start = index++; while (index < json.length) { const character = json[index++]!; if (character === "\\") index++; else if (character === '"') return JSON.parse(json.slice(start, index)) as string; } throw new SyntaxError(); };
  const value = (): unknown => { whitespace(); const character = json[index]; if (character === "{") { index++; whitespace(); const result: Record<string, unknown> = {}; const keys = new Set<string>(); if (json[index] === "}") { index++; return result; } while (true) { whitespace(); if (json[index] !== '"') throw new SyntaxError(); const key = string(); if (keys.has(key)) throw new SyntaxError(); keys.add(key); whitespace(); if (json[index++] !== ":") throw new SyntaxError(); result[key] = value(); whitespace(); const separator = json[index++]; if (separator === "}") return result; if (separator !== ",") throw new SyntaxError(); } } if (character === "[") { index++; whitespace(); const result: unknown[] = []; if (json[index] === "]") { index++; return result; } while (true) { result.push(value()); whitespace(); const separator = json[index++]; if (separator === "]") return result; if (separator !== ",") throw new SyntaxError(); } } if (character === '"') return string(); const match = /^(?:true|false|null|-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?)/u.exec(json.slice(index)); if (match === null) throw new SyntaxError(); const token = match[0]; index += token.length; if (token === "true") return true; if (token === "false") return false; if (token === "null") return null; validateNumberToken(token); return Number(token); };
  const result = value(); whitespace(); if (index !== json.length) throw new SyntaxError(); return result;
}
function validateNumberToken(token: string): void { const numeric = Number(token); if (!Number.isFinite(numeric) || Object.is(numeric, -0)) throw new SyntaxError(); if (!token.includes(".") && !/[eE]/u.test(token) && !Number.isSafeInteger(numeric)) throw new SyntaxError(); if (numeric === 0 && /[1-9]/u.test(token.replace(/^[+-]?0*(?:\.0*)?/u, "").split(/[eE]/u)[0] ?? "")) throw new SyntaxError(); }
