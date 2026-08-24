import { gatewayOperations } from "./generated/contract.js";
import type { GatewayOperationTypes } from "./generated/operations.js";
import type { GatewayAdminError, GatewayCorrelationId } from "./generated/schemas.js";
import type { GatewayOperationResult, GatewayProtocolReason, GatewayResponseHeaders } from "./generated/result.js";
import { validateWireValue } from "./validation.js";

export interface GatewayAuthenticationProvider {
  getAccessToken(signal: AbortSignal): { readonly value: string } | null | Promise<{ readonly value: string } | null>;
}
export interface GatewayClientOptions {
  readonly baseUrl: string | URL;
  readonly apiBasePath?: string;
  readonly authentication: GatewayAuthenticationProvider;
  readonly fetch?: typeof globalThis.fetch;
  readonly defaultSignal?: AbortSignal;
}
export interface GatewayCallOptions { readonly signal?: AbortSignal; }
export type GatewayClient = { readonly [K in keyof GatewayOperationTypes]: (input: GatewayOperationTypes[K]["input"], options?: GatewayCallOptions) => Promise<GatewayOperationTypes[K]["result"]> };

/** Host-only request presented to the sealed Studio transport. It contains no origin or authentication material. */
export interface GatewayStudioTransportRequest {
  readonly operation: keyof GatewayOperationTypes;
  readonly purpose: 'observation' | 'commandPreview' | 'commandExecution';
  readonly method: string;
  readonly relativePathAndQuery: string;
  readonly headers: Readonly<Record<string, string>>;
  readonly body: string | undefined;
  readonly maximumResponseBytes: number;
  readonly deadlineMilliseconds: number;
  readonly signal: AbortSignal;
}
/** Host-owned same-origin authenticated transport admitted by the Gateway generated contract. */
export interface GatewayStudioTransport {
  execute(request: GatewayStudioTransportRequest): Promise<Response>;
}
/** Immutable authority passed only to the Gateway static client activator by the Studio shell. */
export interface GatewayStudioClientHostContext {
  readonly endpointSurfaceId: string;
  readonly principalGeneration: bigint;
  readonly authenticationSessionChecksum: string;
  readonly signal: AbortSignal;
  readonly transport: GatewayStudioTransport;
  readonly limits: Readonly<{ readonly maximumOperations: number; readonly maximumRequestBytes: number;
    readonly maximumResponseBytes: number; readonly maximumConcurrentRequests: number;
    readonly acquisitionDeadlineMilliseconds: number; readonly operationDeadlineMilliseconds: number;
    readonly disposalDeadlineMilliseconds: number }>;
}

type Operation = (typeof gatewayOperations)[number];
type PreparedInput = { readonly body: string | undefined; readonly parameters: ReadonlyMap<string, unknown> };
const encoder = new TextEncoder();
const maximumBodyBytes = 8 * 1024 * 1024;
class JsonBoundExceeded extends Error {}
class RequestBodyBoundExceeded extends Error {}

export function createGatewayClient(options: GatewayClientOptions): GatewayClient {
  const baseUrl = normalizeBaseUrl(options.baseUrl);
  const apiBasePath = normalizeApiBasePath(options.apiBasePath ?? "/management/gateway/v1");
  const authentication = options.authentication;
  if (!authentication || typeof authentication.getAccessToken !== "function") throw new TypeError("Gateway authentication provider is required.");
  const getAccessToken = authentication.getAccessToken.bind(authentication);
  const fetchImplementation = options.fetch ?? globalThis.fetch;
  if (typeof fetchImplementation !== "function") throw new TypeError("A Fetch implementation is required.");
  const defaultSignal = options.defaultSignal;
  return Object.freeze(Object.fromEntries(gatewayOperations.map(operation => [operation.operation,
    (input: unknown, call?: GatewayCallOptions) => execute(baseUrl, apiBasePath, getAccessToken, fetchImplementation, defaultSignal, operation, input, call?.signal)]))) as GatewayClient;
}

/** Creates the generated client over shell-owned transport without disclosing fetch, URLs, or credentials. */
export function createGatewayStudioClient(context: GatewayStudioClientHostContext): GatewayClient {
  if (!context || context.endpointSurfaceId !== 'gateway.admin.v1' || context.principalGeneration < 1n ||
      !/^[a-f0-9]{64}$/u.test(context.authenticationSessionChecksum) || context.signal.aborted ||
      !context.transport || typeof context.transport.execute !== 'function' || !validStudioLimits(context.limits))
    throw new TypeError('Gateway Studio client host authority is invalid.');
  let concurrent = 0;
  return Object.freeze(Object.fromEntries(gatewayOperations.map(operation => [operation.operation,
    async (input: unknown, call?: GatewayCallOptions) => {
      const signal = call?.signal ?? context.signal;
      if (signal.aborted) return canceled();
      if (concurrent >= context.limits.maximumConcurrentRequests) return transport();
      if (!isRecord(input)) return protocol('schema-mismatch', null, null, {});
      let prepared: PreparedInput;
      try { prepared = validateInput(operation, input); }
      catch (error) { return protocol(error instanceof RequestBodyBoundExceeded ? 'request-too-large' : 'schema-mismatch', null, null, {}); }
      if (prepared.body !== undefined && encoder.encode(prepared.body).byteLength > context.limits.maximumRequestBytes)
        return protocol('request-too-large', null, null, {});
      let request: Omit<GatewayStudioTransportRequest, 'maximumResponseBytes' | 'deadlineMilliseconds' | 'signal'>;
      try { request = buildStudioRequest(operation, prepared); }
      catch { return protocol('schema-mismatch', null, null, {}); }
      concurrent++;
      try {
        const response = await context.transport.execute(Object.freeze({ ...request,
          maximumResponseBytes: context.limits.maximumResponseBytes,
          deadlineMilliseconds: context.limits.operationDeadlineMilliseconds, signal }));
        return await decodeResponse(operation, response, signal, context.limits.maximumResponseBytes);
      } catch { return signal.aborted ? canceled() : transport(); }
      finally { concurrent--; }
    }]))) as GatewayClient;
}

function validStudioLimits(limits: GatewayStudioClientHostContext['limits']): boolean {
  return !!limits && Number.isSafeInteger(limits.maximumOperations) && limits.maximumOperations >= gatewayOperations.length && limits.maximumOperations <= 4096 &&
    Number.isSafeInteger(limits.maximumRequestBytes) && limits.maximumRequestBytes >= 1 && limits.maximumRequestBytes <= 67_108_864 &&
    Number.isSafeInteger(limits.maximumResponseBytes) && limits.maximumResponseBytes >= 1 && limits.maximumResponseBytes <= 67_108_864 &&
    Number.isSafeInteger(limits.maximumConcurrentRequests) && limits.maximumConcurrentRequests >= 1 && limits.maximumConcurrentRequests <= 256 &&
    Number.isSafeInteger(limits.acquisitionDeadlineMilliseconds) && limits.acquisitionDeadlineMilliseconds >= 1 && limits.acquisitionDeadlineMilliseconds <= 30_000 &&
    Number.isSafeInteger(limits.operationDeadlineMilliseconds) && limits.operationDeadlineMilliseconds >= 1 && limits.operationDeadlineMilliseconds <= 300_000 &&
    Number.isSafeInteger(limits.disposalDeadlineMilliseconds) && limits.disposalDeadlineMilliseconds >= 1 && limits.disposalDeadlineMilliseconds <= 30_000;
}

async function execute(baseUrl: URL, apiBasePath: string, getAccessToken: GatewayAuthenticationProvider["getAccessToken"], fetchImplementation: typeof fetch,
  defaultSignal: AbortSignal | undefined, operation: Operation, inputValue: unknown, callSignal?: AbortSignal): Promise<GatewayOperationResult<unknown, 200 | 201 | 202, number>> {
  const signal = callSignal ?? defaultSignal ?? new AbortController().signal;
  if (signal.aborted) return canceled();
  if (!isRecord(inputValue)) return protocol("schema-mismatch", null, null, {});
  const input = inputValue;
  let prepared: PreparedInput;
  try { prepared = validateInput(operation, input); }
  catch (error) { return protocol(error instanceof RequestBodyBoundExceeded ? "request-too-large" : "schema-mismatch", null, null, {}); }
  let token: string | undefined;
  try {
    const authenticationResult: unknown = await getAccessToken(signal);
    if (authenticationResult !== null) {
      if (!isRecord(authenticationResult) || !Object.prototype.hasOwnProperty.call(authenticationResult, "value"))
        return protocol("schema-mismatch", null, null, {});
      const value = authenticationResult.value;
      if (typeof value !== "string" || !validToken(value)) return protocol("schema-mismatch", null, null, {});
      token = value;
    }
  } catch { return signal.aborted ? canceled() : transport(); }
  if (signal.aborted) return canceled();
  let request: { url: URL; init: RequestInit };
  try { request = buildRequest(baseUrl, apiBasePath, operation, prepared, token, signal); }
  catch { return protocol("schema-mismatch", null, null, {}); }
  let response: Response;
  try { response = await fetchImplementation(request.url, request.init); }
  catch { return signal.aborted ? canceled() : transport(); }
  return decodeResponse(operation, response, signal, maximumBodyBytes);
}

async function decodeResponse(operation: Operation, response: Response, signal: AbortSignal,
  responseByteLimit: number): Promise<GatewayOperationResult<unknown, 200 | 201 | 202, number>> {
  const headerResult = responseHeaders(response.headers);
  if (!headerResult.ok) { cancelResponse(response); return protocol("response-too-large", response.status, null, {}); }
  const headers = headerResult.value;
  const mediaType = normalizedMediaType(response.headers.get("content-type"));
  if (mediaType === undefined) { cancelResponse(response); return protocol("unexpected-media-type", response.status, null, headers); }
  if (mediaType !== "application/json") { cancelResponse(response); return protocol("unexpected-media-type", response.status, mediaType, headers); }
  const body = await readBody(response, signal, responseByteLimit);
  if (body.kind === "large") return protocol("response-too-large", response.status, mediaType, headers);
  if (body.kind === "canceled") return canceled();
  if (body.kind === "transport") return transport();
  let value: unknown;
  try { value = parseBoundedJson(body.value); }
  catch (error) { return protocol(error instanceof JsonBoundExceeded ? "response-too-large" : "malformed-json", response.status, mediaType, headers); }
  const correlationId = correlation(headers);
  if (isRecord(value) && typeof value.correlationId === "string" && correlationId !== undefined && value.correlationId !== correlationId)
    return protocol("schema-mismatch", response.status, mediaType, headers, correlationId);
  if (response.status === operation.success.status) {
    if (!validateWireValue(operation.success.schemaRef, value)) return protocol("schema-mismatch", response.status, mediaType, headers, correlationId);
    if (!validMutationResponse(operation.mutationResponse, value)) return protocol("schema-mismatch", response.status, mediaType, headers, correlationId);
    return { ok: true, status: operation.success.status, value, correlationId, headers } as GatewayOperationResult<unknown, 200 | 201 | 202, number>;
  }
  if (!operation.documentedErrors.includes(response.status as never)) return protocol("unexpected-status", response.status, mediaType, headers, correlationId);
  const errorRef = "#/components/schemas/HPD_Gateway_Admin_GatewayAdminError";
  if (!validateWireValue(errorRef, value)) return protocol("error-envelope-invalid", response.status, mediaType, headers, correlationId);
  return { ok: false, kind: "http", status: response.status, error: value as GatewayAdminError, correlationId, headers };
}

function buildStudioRequest(operation: Operation, input: PreparedInput): Omit<GatewayStudioTransportRequest,
  'maximumResponseBytes' | 'deadlineMilliseconds' | 'signal'> {
  const contractBasePath = '/management/gateway/v1';
  if (!operation.path.startsWith(`${contractBasePath}/`)) throw new TypeError('Operation path is outside the Gateway Admin contract.');
  const path = operation.path.slice(contractBasePath.length).replace(/\{([^}]+)\}/gu, (_, key: string) => {
    const value = input.parameters.get(parameterKey('path', key));
    if (typeof value !== 'string') throw new TypeError('Missing path parameter.');
    return encodeURIComponent(value);
  });
  const query = new URLSearchParams();
  for (const constraint of operation.parameterConstraints) {
    if (constraint.location !== 'query') continue;
    const value = input.parameters.get(parameterKey('query', constraint.name));
    if (value !== undefined) query.set(constraint.name, String(value));
  }
  const headers: Record<string, string> = { Accept: 'application/json' };
  const correlationId = input.parameters.get(parameterKey('header', 'X-Correlation-ID'));
  const idempotencyKey = input.parameters.get(parameterKey('header', 'Idempotency-Key'));
  const desiredPrecondition = input.parameters.get(parameterKey('header', 'If-Match'));
  if (typeof correlationId === 'string') headers['X-Correlation-ID'] = correlationId;
  if (typeof idempotencyKey === 'string') headers['Idempotency-Key'] = idempotencyKey;
  if (isRecord(desiredPrecondition) && desiredPrecondition.kind === 'replace' && typeof desiredPrecondition.token === 'string')
    headers['If-Match'] = `"${desiredPrecondition.token}"`;
  if (input.body !== undefined) headers['Content-Type'] = (operation.requestBody.mediaTypes as readonly string[]).includes('application/json')
    ? 'application/json' : operation.requestBody.mediaTypes[0] ?? 'application/json';
  const queryText = query.toString();
  const purpose = operation.mutation ? 'commandExecution' : operation.method === 'POST' ? 'commandPreview' : 'observation';
  return Object.freeze({ operation: operation.operation, purpose, method: operation.method,
    relativePathAndQuery: queryText.length === 0 ? path : `${path}?${queryText}`,
    headers: Object.freeze(headers), body: input.body });
}

function validMutationResponse(kind: Operation["mutationResponse"], value: unknown): boolean {
  if (kind === "none") return true;
  if (!isRecord(value) || typeof value.operationId !== "string" || typeof value.revisionId !== "string") return false;
  return kind === "revision-only"
    ? value.activationIntentId === null && value.desiredStateToken === null
    : typeof value.activationIntentId === "string" && typeof value.desiredStateToken === "string";
}

function buildRequest(base: URL, apiBasePath: string, operation: Operation, input: PreparedInput, token: string | undefined, signal: AbortSignal): { url: URL; init: RequestInit } {
  const contractBasePath = "/management/gateway/v1";
  if (!operation.path.startsWith(`${contractBasePath}/`)) throw new TypeError("Operation path is outside the Gateway Admin contract.");
  const path = `${apiBasePath}${operation.path.slice(contractBasePath.length)}`.replace(/\{([^}]+)\}/gu, (_, key: string) => {
    const value = input.parameters.get(parameterKey("path", key)); if (typeof value !== "string") throw new TypeError("Missing path parameter."); return encodeURIComponent(value);
  });
  const prefix = base.pathname === "/" ? "" : base.pathname;
  const url = new URL(`${prefix}${path}`, base.origin);
  for (const constraint of operation.parameterConstraints) {
    if (constraint.location !== "query") continue;
    const value = input.parameters.get(parameterKey("query", constraint.name));
    if (value !== undefined) url.searchParams.set(constraint.name, String(value));
  }
  const headers = new Headers({ Accept: "application/json" });
  if (token !== undefined) headers.set("Authorization", `Bearer ${token}`);
  const correlationId = input.parameters.get(parameterKey("header", "X-Correlation-ID"));
  const idempotencyKey = input.parameters.get(parameterKey("header", "Idempotency-Key"));
  const desiredPrecondition = input.parameters.get(parameterKey("header", "If-Match"));
  if (typeof correlationId === "string") headers.set("X-Correlation-ID", correlationId);
  if (typeof idempotencyKey === "string") headers.set("Idempotency-Key", idempotencyKey);
  if (isRecord(desiredPrecondition) && desiredPrecondition.kind === "replace" && typeof desiredPrecondition.token === "string")
    headers.set("If-Match", `"${desiredPrecondition.token}"`);
  if (input.body !== undefined) {
    headers.set("Content-Type", operation.requestBody.mediaTypes[0] ?? "application/json");
  }
  return { url, init: { method: operation.method, headers, body: input.body, signal, credentials: "omit", redirect: "error" } };
}

function normalizeApiBasePath(value: string): string {
  if (typeof value !== "string" || !value.startsWith("/") || value === "/" || value.endsWith("/") ||
      value.length > 256 || !/^[\x21-\x7e]+$/.test(value) || value.includes("\\") ||
      value.includes("//") || value.includes("..") || value.includes("?") || value.includes("#") ||
      /%(?:2f|5c)/iu.test(value)) {
    throw new TypeError("Gateway API base path is invalid.");
  }
  return value;
}

function validateInput(operation: Operation, input: Record<string, unknown>): PreparedInput {
  const containers = new Map<"path" | "query" | "headers", Set<string>>([
    ["path", new Set()], ["query", new Set()], ["headers", new Set()],
  ]);
  for (const constraint of operation.parameterConstraints) {
    const containerName = constraint.location === "path" ? "path" : constraint.location === "query" ? "query" : "headers";
    containers.get(containerName)!.add(parameterProperty(constraint.name));
  }
  const allowedInput = new Set<string>(["path"]);
  for (const [containerName, properties] of containers) if (properties.size > 0) allowedInput.add(containerName);
  if (operation.requestBody.presence !== "none") allowedInput.add("body");
  if (Object.keys(input).some(key => !allowedInput.has(key))) throw new TypeError("Unknown input member.");
  const capturedContainers = new Map<"path" | "query" | "headers", unknown>([
    ["path", input.path], ["query", input.query], ["headers", input.headers],
  ]);
  if (!isRecord(capturedContainers.get("path"))) throw new TypeError("Path container is required.");
  for (const [containerName, properties] of containers) {
    const candidate = capturedContainers.get(containerName);
    if (candidate === undefined) continue;
    if (!isRecord(candidate) || Object.keys(candidate).some(key => !properties.has(key))) throw new TypeError("Unknown parameter member.");
  }
  const parameterValues = new Map<string, unknown>();
  for (const constraint of operation.parameterConstraints) {
    const containerName = constraint.location === "path" ? "path" : constraint.location === "query" ? "query" : "headers";
    const candidate = capturedContainers.get(containerName);
    const container = isRecord(candidate) ? candidate : {};
    const property = parameterProperty(constraint.name);
    const value = container[property];
    if (value === undefined) { if (constraint.required) throw new TypeError("Required parameter is missing."); continue; }
    if (constraint.name === "If-Match") {
      if (!isRecord(value)) throw new TypeError("Invalid desired precondition.");
      const kind = value.kind;
      const token = value.token;
      if (!(kind === "create-only" || kind === "replace" && validEntityTagPayload(token))) throw new TypeError("Invalid desired precondition.");
      parameterValues.set(parameterKey(constraint.location, constraint.name), kind === "replace" ? Object.freeze({ kind, token }) : Object.freeze({ kind }));
      continue;
    }
    if (typeof value === "number") {
      if (!Number.isInteger(value) || constraint.rules.collectionMinimum !== null || operation.pagination.kind !== "opaque-cursor" ||
          value < operation.pagination.minimumMaximum! || value > operation.pagination.maximumMaximum!) throw new TypeError("Invalid numeric parameter.");
      parameterValues.set(parameterKey(constraint.location, constraint.name), value);
      continue;
    }
    if (typeof value !== "string" || !validStringRule(value, constraint.rules)) throw new TypeError("Invalid string parameter.");
    parameterValues.set(parameterKey(constraint.location, constraint.name), value);
  }
  const bodyValue = input.body;
  if (bodyValue === undefined) {
    if (operation.requestBody.presence === "required") throw new TypeError("Required body is missing.");
    return { body: undefined, parameters: parameterValues };
  }
  if (operation.requestBody.schemaRef === null) throw new TypeError("Unexpected body.");
  const serialized = JSON.stringify(bodyValue);
  if (serialized === undefined) throw new TypeError("Invalid body.");
  if (operation.requestBody.maximumUtf8Bytes === null || encoder.encode(serialized).byteLength > operation.requestBody.maximumUtf8Bytes)
    throw new RequestBodyBoundExceeded("Request body exceeds the operation limit.");
  const materialized: unknown = JSON.parse(serialized);
  if (!validateWireValue(operation.requestBody.schemaRef, materialized)) throw new TypeError("Invalid body.");
  return { body: serialized, parameters: parameterValues };
}

async function readBody(response: Response, signal: AbortSignal, limit = maximumBodyBytes): Promise<{ kind: "value"; value: Uint8Array } | { kind: "large" } | { kind: "transport" } | { kind: "canceled" }> {
  const length = response.headers.get("content-length");
  if (length !== null && /^\d+$/u.test(length) && Number(length) > limit) { cancelResponse(response); return { kind: "large" }; }
  const reader = response.body?.getReader();
  if (!reader) return { kind: "value", value: new Uint8Array() };
  const chunks: Uint8Array[] = []; let total = 0;
  try {
    while (true) {
      const next = await reader.read(); if (next.done) break;
      total += next.value.byteLength;
      if (total > limit) { cancelReader(reader); return { kind: "large" }; }
      chunks.push(next.value);
    }
  } catch { return { kind: signal.aborted ? "canceled" : "transport" }; }
  const result = new Uint8Array(total); let offset = 0;
  for (const chunk of chunks) { result.set(chunk, offset); offset += chunk.byteLength; }
  return { kind: "value", value: result };
}

function cancelResponse(response: Response): void {
  try { consumeCancellation(response.body?.cancel()); } catch { /* Cancellation is best-effort and never changes the closed result. */ }
}
function cancelReader(reader: ReadableStreamDefaultReader<Uint8Array>): void {
  try { consumeCancellation(reader.cancel()); } catch { /* Cancellation is best-effort and never changes the closed result. */ }
}
function consumeCancellation(cancellation: Promise<void> | undefined): void { void cancellation?.catch(() => undefined); }

function parseBoundedJson(bytes: Uint8Array): unknown {
  if (bytes.byteLength === 0) throw new Error("empty");
  const text = new TextDecoder("utf-8", { fatal: true }).decode(bytes);
  scanJson(text);
  const value: unknown = JSON.parse(text);
  checkGraph(value, 0, { tokens: 0 });
  return value;
}
function scanJson(text: string): void {
  const stack: Array<{ kind: "object"; names: Set<string>; count: number } | { kind: "array"; count: number }> = [];
  let index = 0; let tokens = 0;
  const token = (): void => { if (++tokens > 750_000) throw new JsonBoundExceeded("tokens"); };
  const arrayValue = (): void => { const frame = stack.at(-1); if (frame?.kind === "array" && ++frame.count > 10_000) throw new JsonBoundExceeded("array"); };
  while (index < text.length) {
    const current = text[index]!;
    if (/\s/u.test(current)) { index++; continue; }
    if (current === "{") { token(); arrayValue(); stack.push({ kind: "object", names: new Set(), count: 0 }); if (stack.length > 64) throw new JsonBoundExceeded("depth"); index++; continue; }
    if (current === "[") { token(); arrayValue(); stack.push({ kind: "array", count: 0 }); if (stack.length > 64) throw new JsonBoundExceeded("depth"); index++; continue; }
    if (current === "}" || current === "]") { token(); stack.pop(); index++; continue; }
    if (current !== '"') {
      if (current === "," || current === ":") { index++; continue; }
      token(); arrayValue();
      while (index < text.length && !/[\s,\]}]/u.test(text[index]!)) index++;
      continue;
    }
    token();
    const start = index++;
    while (index < text.length) { if (text[index] === "\\") { index += 2; continue; } if (text[index++] === '"') break; }
    let probe = index; while (probe < text.length && /\s/u.test(text[probe]!)) probe++;
    const frame = stack.at(-1);
    if (text[probe] === ":" && frame?.kind === "object") {
      const name = JSON.parse(text.slice(start, index)) as string;
      if (frame.names.has(name)) throw new Error("duplicate");
      frame.names.add(name); if (++frame.count > 256) throw new JsonBoundExceeded("properties");
    } else arrayValue();
  }
}
function checkGraph(value: unknown, depth: number, state: { tokens: number }): void {
  if (depth > 64) throw new JsonBoundExceeded("depth");
  if (++state.tokens > 750_000) throw new JsonBoundExceeded("tokens");
  if (Array.isArray(value)) { if (value.length > 10_000) throw new JsonBoundExceeded("array"); for (const child of value) checkGraph(child, depth + 1, state); return; }
  if (isRecord(value)) { const entries = Object.entries(value); if (entries.length > 256) throw new JsonBoundExceeded("properties"); state.tokens += entries.length; if (state.tokens > 750_000) throw new JsonBoundExceeded("tokens"); for (const [, child] of entries) checkGraph(child, depth + 1, state); }
}

function responseHeaders(source: Headers): { ok: true; value: GatewayResponseHeaders } | { ok: false } {
  const result: Record<string, string> = {}; let count = 0;
  for (const [name, value] of source) {
    if (++count > 128 || !/^[!-~]{1,128}$/u.test(name) || encoder.encode(value).byteLength > 4_096) return { ok: false };
    result[name.toLowerCase()] = value;
  }
  return { ok: true, value: Object.freeze(result) };
}
function normalizedMediaType(value: string | null): string | null | undefined {
  if (value === null) return null;
  if (encoder.encode(value).byteLength > 256 || value.includes(",")) return undefined;
  const media = value.split(";", 1)[0]!.trim().toLowerCase();
  return /^[a-z0-9!#$&^_.+-]+\/[a-z0-9!#$&^_.+-]+$/u.test(media) ? media : undefined;
}
function normalizeBaseUrl(value: string | URL): URL {
  const url = new URL(value.toString());
  if (!(url.protocol === "http:" || url.protocol === "https:") || url.username || url.password || url.search || url.hash) throw new TypeError("Invalid Gateway base URL.");
  url.pathname = url.pathname === "/" ? "" : url.pathname.replace(/\/+$/u, ""); return url;
}
function validToken(value: unknown): value is string { return typeof value === "string" && wellFormed(value) && encoder.encode(value).byteLength >= 1 && encoder.encode(value).byteLength <= 16_384 && !/[\r\n\0]/u.test(value); }
function validEntityTagPayload(value: unknown): value is string { return typeof value === "string" && /^[!-~]{1,512}$/u.test(value) && !/[",]/u.test(value); }
function parameterProperty(name: string): string {
  return name === "X-Correlation-ID" ? "correlationId" : name === "Idempotency-Key" ? "idempotencyKey" : name === "If-Match" ? "desiredPrecondition" : name;
}
function parameterKey(location: "path" | "query" | "header", name: string): string { return `${location}:${name}`; }
function validStringRule(value: string, rules: Operation["parameterConstraints"][number]["rules"]): boolean {
  if (!wellFormed(value)) return false;
  const size = encoder.encode(value).byteLength;
  if (rules.minimumUtf8Bytes !== null && size < rules.minimumUtf8Bytes || rules.maximumUtf8Bytes !== null && size > rules.maximumUtf8Bytes) return false;
  if (rules.normalization === "NFC" && value !== value.normalize("NFC")) return false;
  if (rules.rejectUnicodeControls && /[\u0000-\u001F\u007F-\u009F]/u.test(value)) return false;
  if (rules.characterSet === "visible-ascii" && !/^[!-~]+$/u.test(value)) return false;
  return true;
}
function wellFormed(value: string): boolean {
  for (let index = 0; index < value.length; index++) {
    const code = value.charCodeAt(index);
    if (code >= 0xD800 && code <= 0xDBFF) { const next = value.charCodeAt(++index); if (!(next >= 0xDC00 && next <= 0xDFFF)) return false; }
    else if (code >= 0xDC00 && code <= 0xDFFF) return false;
  }
  return true;
}
function correlation(headers: GatewayResponseHeaders): GatewayCorrelationId | undefined { const value = headers["x-correlation-id"]; return value as GatewayCorrelationId | undefined; }
function protocol(reason: GatewayProtocolReason, actualStatus: number | null, mediaType: string | null, headers: GatewayResponseHeaders, correlationId?: GatewayCorrelationId) { return { ok: false as const, kind: "protocol" as const, reason, actualStatus, mediaType, correlationId, headers }; }
function transport() { return { ok: false as const, kind: "transport" as const, reason: "network-failure" as const }; }
function canceled() { return { ok: false as const, kind: "canceled" as const, reason: "caller-canceled" as const }; }
function isRecord(value: unknown): value is Record<string, unknown> { return value !== null && typeof value === "object" && !Array.isArray(value); }
