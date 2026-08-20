import type { BaseResult } from "./result.js";
import type { BaseHttpTransport } from "./transport.js";

declare const lifecycleCursorBrand: unique symbol;
declare const lifecycleCheckpointBrand: unique symbol;
declare const lifecycleEpochBrand: unique symbol;
declare const lifecycleIncarnationBrand: unique symbol;
/** Opaque protected lifecycle continuation. */
export type BaseSubjectLifecycleCursor = string & { readonly [lifecycleCursorBrand]: true };
/** Opaque purpose-bound checkpoint evidence. */
export type BaseSubjectLifecycleCheckpoint = string & { readonly [lifecycleCheckpointBrand]: true };
/** Opaque 128-bit authority epoch. */
export type BaseSubjectLifecycleAuthorityEpoch = string & { readonly [lifecycleEpochBrand]: true };
/** Opaque 192-bit subject incarnation. */
export type BaseSubjectLifecycleIncarnation = string & { readonly [lifecycleIncarnationBrand]: true };
export type BaseSubjectLifecycleState = "active" | "inactive" | "tombstoned" | "retired";
export type BaseSubjectLifecycleFactKind = "created" | "transitioned" | "retired";

export interface BaseSubjectLifecycleFact {
  readonly commitPosition: string; readonly contractId: string; readonly contractVersion: number; readonly subjectId: string;
  readonly authorityEpoch: BaseSubjectLifecycleAuthorityEpoch; readonly incarnation: BaseSubjectLifecycleIncarnation;
  readonly subjectSequence: string; readonly contractStateGeneration: string; readonly deliveryEpoch: string;
  readonly kind: BaseSubjectLifecycleFactKind; readonly previousState?: BaseSubjectLifecycleState; readonly currentState?: BaseSubjectLifecycleState;
}
/** Deterministic receipt identity owned by one generated lifecycle delivery. */
export interface BaseSubjectLifecycleMutationIdentity {
  readonly scope: string; readonly operation: string; readonly idempotencyKey: string; readonly fingerprint: string;
}
/** Immutable delivery evidence; enumeration never advances durable ownership. */
export interface BaseSubjectLifecycleDelivery {
  readonly fact: BaseSubjectLifecycleFact; readonly checkpoint: BaseSubjectLifecycleCheckpoint;
  readonly processingIdentity: BaseSubjectLifecycleMutationIdentity; readonly advanceIdentity: BaseSubjectLifecycleMutationIdentity;
}
export interface BaseSubjectLifecyclePage { readonly facts: readonly BaseSubjectLifecycleFact[]; readonly next: BaseSubjectLifecycleCursor | null; readonly checkpoint: BaseSubjectLifecycleCheckpoint; }
export interface BaseSubjectLifecycleCheckpointResult { readonly checkpointGeneration: string; readonly advancedAtUtc: string; readonly duplicate: boolean; }
export interface BaseSubjectLifecycleConsumerDefinition {
  readonly id: string; readonly version: number; readonly checksum: string; readonly audience: "service" | "system"; readonly contractId: string; readonly contractVersion: number;
  readonly observedStates: readonly BaseSubjectLifecycleState[]; readonly readRoute: string; readonly checkpointRoute: string;
  readonly maximumFactsPerPage: number; readonly maximumResultBytes: number;
}
export interface BaseSubjectLifecycleReadOptions { readonly projectId?: string; readonly cursor?: BaseSubjectLifecycleCursor; readonly take?: number; readonly signal?: AbortSignal; }

/** Creates one immutable generated lifecycle-worker descriptor. */
export function subjectLifecycleConsumer(definition: BaseSubjectLifecycleConsumerDefinition): BaseSubjectLifecycleConsumerDefinition {
  if (!/^[0-9a-f]{64}$/u.test(definition.checksum) || !Number.isSafeInteger(definition.version) || definition.version < 1
    || !Number.isSafeInteger(definition.contractVersion) || definition.contractVersion < 1 || definition.observedStates.length === 0
    || new Set(definition.observedStates).size !== definition.observedStates.length || definition.maximumFactsPerPage < 1 || definition.maximumFactsPerPage > 256
    || definition.maximumResultBytes < 1 || definition.maximumResultBytes > 1_048_576) invalid();
  return Object.freeze({ ...definition, observedStates: Object.freeze([...definition.observedStates]) });
}

/** Reads durable facts without advancing provider-owned checkpoint state. */
export async function readSubjectLifecycle(transport: BaseHttpTransport, definition: BaseSubjectLifecycleConsumerDefinition, options: BaseSubjectLifecycleReadOptions = {}): Promise<BaseResult<BaseSubjectLifecyclePage>> {
  const take = options.take ?? definition.maximumFactsPerPage;
  if (!Number.isSafeInteger(take) || take < 1 || take > definition.maximumFactsPerPage) throw new RangeError("base.subjectLifecycle.contractInvalid");
  if (options.projectId !== undefined && (options.projectId.length < 1 || options.projectId.length > 256)) throw new RangeError("base.subjectLifecycle.contractInvalid");
  const body = new TextEncoder().encode(JSON.stringify({ consumerId: definition.id, consumerVersion: definition.version, projectId: options.projectId ?? null, take, cursor: options.cursor ?? null }));
  const response = await transport.json("POST", definition.readRoute, body, options.signal);
  if (!response.ok) return response;
  return { ...response, value: decodePage(response.value, definition) };
}

/** Advances one durable consumer checkpoint through identified receipt authority. */
export async function advanceSubjectLifecycle(transport: BaseHttpTransport, definition: BaseSubjectLifecycleConsumerDefinition, checkpoint: BaseSubjectLifecycleCheckpoint, identity: BaseSubjectLifecycleMutationIdentity, projectId?: string, signal?: AbortSignal): Promise<BaseResult<BaseSubjectLifecycleCheckpointResult>> {
  if (identity.scope !== `subject-lifecycle:${definition.id}` || identity.operation !== "subjectLifecycle.advance"
    || !/^[0-9a-f]{64}$/u.test(identity.idempotencyKey) || !fingerprint(identity.fingerprint))
    throw new TypeError("base.subjectLifecycle.contractInvalid");
  if (projectId !== undefined && (projectId.length < 1 || projectId.length > 256)) throw new RangeError("base.subjectLifecycle.contractInvalid");
  const body = new TextEncoder().encode(JSON.stringify({ consumerId: definition.id, consumerVersion: definition.version, projectId: projectId ?? null, checkpoint, identity }));
  const response = await transport.json("POST", definition.checkpointRoute, body, signal, identity.idempotencyKey);
  if (!response.ok) return response;
  const value = object(response.value); exact(value, ["checkpointGeneration", "advancedAtUtc", "duplicate"]);
  if (!positive(value.checkpointGeneration) || typeof value.advancedAtUtc !== "string" || !utc(value.advancedAtUtc) || typeof value.duplicate !== "boolean") invalid();
  return { ...response, value: { checkpointGeneration: value.checkpointGeneration as string, advancedAtUtc: value.advancedAtUtc, duplicate: value.duplicate } };
}

/** Enumerates one fact at a time as inert evidence without implicitly advancing its checkpoint. */
export async function* iterateSubjectLifecycle(transport: BaseHttpTransport, definition: BaseSubjectLifecycleConsumerDefinition, options: BaseSubjectLifecycleReadOptions = {}): AsyncGenerator<BaseSubjectLifecycleDelivery, void, void> {
  let cursor: BaseSubjectLifecycleCursor | undefined;
  while (true) {
    const page = await readSubjectLifecycle(transport, definition, {
      ...(cursor === undefined ? {} : { cursor }), take: 1, ...(options.projectId === undefined ? {} : { projectId: options.projectId }), ...(options.signal === undefined ? {} : { signal: options.signal })
    });
    if (!page.ok) throw new BaseSubjectLifecycleDeliveryError(page.error.code);
    if (page.value.facts.length === 0) return;
    const fact = page.value.facts[0]!;
    const [processingIdentity, advanceIdentity] = await Promise.all([
      deliveryIdentity(definition, "process", fact), deliveryIdentity(definition, "advance", fact)
    ]);
    yield Object.freeze({ fact, checkpoint: page.value.checkpoint, processingIdentity, advanceIdentity });
    if (page.value.next === null) return;
    cursor = page.value.next;
  }
}

/** Stable terminal error surfaced by the generated lifecycle iterator. */
export class BaseSubjectLifecycleDeliveryError extends Error {
  public constructor(public readonly code: string) { super(code); this.name = "BaseSubjectLifecycleDeliveryError"; }
}

function decodePage(input: unknown, definition: BaseSubjectLifecycleConsumerDefinition): BaseSubjectLifecyclePage {
  const value = object(input); exact(value, ["facts", "next", "checkpoint"]);
  if (!Array.isArray(value.facts) || value.facts.length > definition.maximumFactsPerPage || value.next !== null && !token(value.next) || !token(value.checkpoint)) invalid();
  return { facts: Object.freeze(value.facts.map(item => decodeFact(item, definition))), next: value.next as BaseSubjectLifecycleCursor | null, checkpoint: value.checkpoint as BaseSubjectLifecycleCheckpoint };
}
function decodeFact(input: unknown, definition: BaseSubjectLifecycleConsumerDefinition): BaseSubjectLifecycleFact {
  const value = object(input); const required = ["commitPosition", "contractId", "contractVersion", "subjectId", "authorityEpoch", "incarnation", "subjectSequence", "contractStateGeneration", "deliveryEpoch", "kind"];
  const allowed = [...required, "previousState", "currentState"]; if (required.some(key => !Object.hasOwn(value, key)) || Object.keys(value).some(key => !allowed.includes(key))) invalid();
  if (!positive(value.commitPosition) || value.contractId !== definition.contractId || value.contractVersion !== definition.contractVersion || typeof value.subjectId !== "string" || value.subjectId.length === 0
    || !fixedToken(value.authorityEpoch, 16) || !fixedToken(value.incarnation, 24) || !positive(value.subjectSequence) || !positive(value.contractStateGeneration) || !positive(value.deliveryEpoch)
    || !["created", "transitioned", "retired"].includes(value.kind as string)) invalid();
  const kind = value.kind as BaseSubjectLifecycleFactKind; const previous = value.previousState; const current = value.currentState;
  if (kind === "created" ? previous !== undefined || current !== "active" : kind === "transitioned" ? !state(previous) || !state(current) || previous === "retired" || current === "retired" : previous !== "tombstoned" || current !== undefined) invalid();
  return Object.freeze({ commitPosition: value.commitPosition as string, contractId: value.contractId as string, contractVersion: value.contractVersion as number, subjectId: value.subjectId,
    authorityEpoch: value.authorityEpoch as BaseSubjectLifecycleAuthorityEpoch, incarnation: value.incarnation as BaseSubjectLifecycleIncarnation, subjectSequence: value.subjectSequence as string,
    contractStateGeneration: value.contractStateGeneration as string, deliveryEpoch: value.deliveryEpoch as string, kind,
    ...(previous === undefined ? {} : { previousState: previous as BaseSubjectLifecycleState }), ...(current === undefined ? {} : { currentState: current as BaseSubjectLifecycleState }) });
}
function object(value: unknown): Record<string, unknown> { if (value === null || typeof value !== "object" || Array.isArray(value)) invalid(); return value as Record<string, unknown>; }
function exact(value: Record<string, unknown>, keys: readonly string[]): void { if (Object.keys(value).length !== keys.length || keys.some(key => !Object.hasOwn(value, key))) invalid(); }
function positive(value: unknown): value is string { return typeof value === "string" && /^[1-9][0-9]*$/u.test(value); }
function state(value: unknown): value is BaseSubjectLifecycleState { return typeof value === "string" && ["active", "inactive", "tombstoned", "retired"].includes(value); }
function token(value: unknown): value is string { return typeof value === "string" && value.length >= 16 && value.length <= 16_384 && /^[A-Za-z0-9_-]+$/u.test(value); }
function fixedToken(value: unknown, bytes: number): value is string { if (!token(value)) return false; try { const text = value.replace(/-/gu, "+").replace(/_/gu, "/"); const padded = text.padEnd(text.length + (4 - text.length % 4) % 4, "="); return Uint8Array.from(atob(padded), character => character.charCodeAt(0)).length === bytes; } catch { return false; } }
function fingerprint(value: unknown): value is string { if (typeof value !== "string" || !/^[A-Za-z0-9+/]{43}=$/u.test(value)) return false; try { return Uint8Array.from(atob(value), character => character.charCodeAt(0)).length === 32; } catch { return false; } }
function utc(value: string): boolean { const parsed = Date.parse(value); return Number.isFinite(parsed) && value.endsWith("Z"); }
async function deliveryIdentity(definition: BaseSubjectLifecycleConsumerDefinition, operation: "process" | "advance", fact: BaseSubjectLifecycleFact): Promise<BaseSubjectLifecycleMutationIdentity> {
  const semantic = `base.subjectLifecycle.delivery.${operation}.v1\0${definition.checksum}\0${fact.commitPosition}\0${fact.subjectId}\0${fact.authorityEpoch}\0${fact.incarnation}\0${fact.subjectSequence}`;
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(semantic)));
  const idempotencyKey = [...digest].map(value => value.toString(16).padStart(2, "0")).join("");
  let binary = ""; for (const value of digest) binary += String.fromCharCode(value);
  return Object.freeze({ scope: `subject-lifecycle:${definition.id}`, operation: `subjectLifecycle.${operation}`, idempotencyKey, fingerprint: btoa(binary) });
}
function invalid(): never { throw new TypeError("base.client.responseInvalid"); }
