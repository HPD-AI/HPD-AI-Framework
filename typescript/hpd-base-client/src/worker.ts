import type { BaseTypeGraph } from "./codec.js";
import { decodeBaseWireValue, encodeBaseJson } from "./codec.js";
import type { BaseResult } from "./result.js";
import { BaseHttpTransport, type BaseTransportOptions } from "./transport.js";

declare const activationIdBrand: unique symbol;
declare const occurrenceIdBrand: unique symbol;
declare const claimBrand: unique symbol;
declare const leaseBrand: unique symbol;
declare const effectBrand: unique symbol;
declare const executorBrand: unique symbol;

/** Opaque durable activation identity. */
export type BaseActivationId = string & { readonly [activationIdBrand]: true };
/** Opaque deterministic schedule-occurrence identity. */
export type BaseOccurrenceId = string & { readonly [occurrenceIdBrand]: true };
/** Opaque current activation-claim authority. */
export type BaseActivationClaim = Readonly<Record<string, unknown>> & { readonly [claimBrand]: true };
/** Opaque replaceable activation-lease observation. */
export type BaseActivationLease = Readonly<Record<string, unknown>> & { readonly [leaseBrand]: true };
/** Opaque at-most-once effect authority. */
export type BaseEffectAuthority = Readonly<Record<string, unknown>> & { readonly [effectBrand]: true };
/** Opaque durable executor-incarnation authority. */
export type BaseExecutorAuthority = Readonly<Record<string, unknown>> & { readonly [executorBrand]: true };

/** Describes one generated Service/System activation definition. */
export interface BaseActivationWorkerDefinition<TInput, TResult> {
  readonly id: string;
  readonly version: number;
  readonly inputTypeId: string;
  readonly resultTypeId: string;
  readonly typeGraph: BaseTypeGraph;
  readonly __input?: TInput;
  readonly __result?: TResult;
}

/** Narrows one worker call without exposing provider machinery. */
export interface BaseActivationWorkerOptions {
  readonly idempotencyKey: string;
  readonly signal?: AbortSignal;
}

/** Contains one immutable claimed delivery. */
export interface BaseActivationDelivery<TInput> {
  readonly activationId: BaseActivationId;
  readonly input: TInput;
  readonly claim: BaseActivationClaim;
  readonly lease: BaseActivationLease;
  readonly attempt: Readonly<Record<string, unknown>>;
  readonly occurrenceId?: BaseOccurrenceId;
  readonly requestedDueAt: number;
  readonly effectiveDueAt: number;
}

/** Contains one durable transition result. */
export interface BaseActivationTransitionResult {
  readonly state: string;
  readonly generation: number;
  readonly disposition: string;
  readonly effect?: BaseEffectAuthority;
}

/** Contains one registered executor and its current heartbeat. */
export interface BaseExecutorRegistration {
  readonly executor: BaseExecutorAuthority;
  readonly heartbeat: Readonly<Record<string, unknown>>;
  readonly disposition: string;
}

/** Executes only Service/System activation operations. */
export class BaseActivationWorkerClient<TInput, TResult> {
  readonly #transport: BaseHttpTransport;
  readonly #definition: BaseActivationWorkerDefinition<TInput, TResult>;

  public constructor(transport: BaseHttpTransport, definition: BaseActivationWorkerDefinition<TInput, TResult>) {
    assertWorkerEnvironment();
    if (definition.id.length === 0 || !Number.isSafeInteger(definition.version) || definition.version <= 0)
      throw new TypeError("base.activation.invalid");
    this.#transport = transport;
    this.#definition = Object.freeze({ ...definition });
  }

  /** Creates or resolves one identified durable activation. */
  public async enqueue(input: TInput, options: BaseActivationWorkerOptions & { readonly dueAt?: number }): Promise<BaseResult<{ readonly activationId: BaseActivationId; readonly state: string; readonly disposition: string }>> {
    const payload = encodeGraph(input, this.#definition.inputTypeId, this.#definition.typeGraph);
    const request = { ...this.definition(), payload: JSON.parse(payload) as unknown, ...(options.dueAt === undefined ? {} : { dueAt: integer(options.dueAt) }) };
    return this.call("activations/enqueue", request, "enqueue", options, isEnqueueResult);
  }

  /** Atomically observes and claims the earliest eligible activation. */
  public async claim(options: BaseActivationWorkerOptions): Promise<BaseResult<BaseActivationDelivery<TInput> | undefined>> {
    const result = await this.callUnknown("activations/claims/next", this.definition(), "claim", options);
    if (!result.ok) return result;
    if (!isObject(result.value) || typeof result.value.empty !== "boolean") throw new TypeError("base.client.responseInvalid");
    if (result.value.empty) return { ...result, value: undefined };
    const value = result.value;
    if (typeof value.activationId !== "string" || !isObject(value.claim) || !isObject(value.lease)
        || !isObject(value.attempt) || !Number.isSafeInteger(value.requestedDueAt)
        || !Number.isSafeInteger(value.effectiveDueAt) || !("payload" in value))
      throw new TypeError("base.client.responseInvalid");
    const input = decodeBaseWireValue<TInput>(value.payload, this.#definition.inputTypeId, this.#definition.typeGraph);
    return { ...result, value: {
      activationId: value.activationId as BaseActivationId,
      input,
      claim: value.claim as BaseActivationClaim,
      lease: value.lease as BaseActivationLease,
      attempt: value.attempt,
      ...(typeof value.occurrenceId === "string" ? { occurrenceId: value.occurrenceId as BaseOccurrenceId } : {}),
      requestedDueAt: value.requestedDueAt as number,
      effectiveDueAt: value.effectiveDueAt as number,
    } };
  }

  /** Renews a current claim without changing its stable fence. */
  public renew(delivery: BaseActivationDelivery<TInput>, options: BaseActivationWorkerOptions): Promise<BaseResult<unknown>> {
    return this.callUnknown("activations/claims/renew", { ...this.definition(), claim: delivery.claim, lease: delivery.lease }, "renew", options);
  }

  /** Completes one current claim with its graph-encoded result. */
  public complete(delivery: BaseActivationDelivery<TInput>, result: TResult, options: BaseActivationWorkerOptions): Promise<BaseResult<BaseActivationTransitionResult>> {
    const encoded = encodeGraph(result, this.#definition.resultTypeId, this.#definition.typeGraph);
    return this.call("activations/complete", { ...this.definition(), claim: delivery.claim, result: JSON.parse(encoded) as unknown }, "complete", options, isTransition);
  }

  /** Records a stable failed-attempt result. */
  public fail(delivery: BaseActivationDelivery<TInput>, failureCode: string, retry: boolean, options: BaseActivationWorkerOptions): Promise<BaseResult<BaseActivationTransitionResult>> {
    if (failureCode.length === 0 || failureCode.length > 128) throw new TypeError("base.activation.invalid");
    return this.call("activations/fail", { ...this.definition(), claim: delivery.claim, failureCode, retry }, "fail", options, isTransition);
  }

  /** Cancels one exact activation generation. */
  public cancel(activationId: BaseActivationId, expectedGeneration: number, propagation: "none" | "descendants", options: BaseActivationWorkerOptions): Promise<BaseResult<BaseActivationTransitionResult>> {
    return this.call("activations/cancel", { ...this.definition(), activationId, expectedGeneration: integer(expectedGeneration), propagation }, "cancel", options, isTransition);
  }

  /** Begins one at-most-once external effect before external work starts. */
  public beginEffect(delivery: BaseActivationDelivery<TInput>, options: BaseActivationWorkerOptions): Promise<BaseResult<BaseActivationTransitionResult>> {
    return this.call("activations/effects/begin", { ...this.definition(), claim: delivery.claim }, "effect-begin", options, isTransition);
  }

  /** Renews one current external-effect heartbeat. */
  public heartbeatEffect(effect: BaseEffectAuthority, options: BaseActivationWorkerOptions): Promise<BaseResult<BaseActivationTransitionResult>> {
    return this.call("activations/effects/heartbeat", { ...this.definition(), effect }, "effect-heartbeat", options, isTransition);
  }

  /** Registers one durable worker-process incarnation. */
  public registerExecutor(hostId: string, processIncarnationId: string, heartbeatMilliseconds: number, options: BaseActivationWorkerOptions): Promise<BaseResult<BaseExecutorRegistration>> {
    if (hostId.length === 0 || processIncarnationId.length === 0) throw new TypeError("base.activation.invalid");
    return this.call("activation-executors/register", { ...this.definition(), hostId, processIncarnationId, heartbeatMilliseconds: integer(heartbeatMilliseconds) }, "executor-register", options, isExecutorRegistration);
  }

  /** Renews one durable worker-process heartbeat. */
  public heartbeatExecutor(executor: BaseExecutorAuthority, heartbeat: Readonly<Record<string, unknown>>, extensionMilliseconds: number, options: BaseActivationWorkerOptions): Promise<BaseResult<unknown>> {
    return this.callUnknown("activation-executors/heartbeat", { ...this.definition(), executor, heartbeat, extensionMilliseconds: integer(extensionMilliseconds) }, "executor-heartbeat", options);
  }

  /** Retires one exact worker-process incarnation. */
  public retireExecutor(executor: BaseExecutorAuthority, heartbeat: Readonly<Record<string, unknown>>, options: BaseActivationWorkerOptions): Promise<BaseResult<unknown>> {
    return this.callUnknown("activation-executors/retire", { ...this.definition(), executor, heartbeat }, "executor-retire", options);
  }

  readonly definition = (): { readonly definitionId: string; readonly definitionVersion: number } => ({
    definitionId: this.#definition.id,
    definitionVersion: this.#definition.version,
  });

  async call<T>(route: string, request: Readonly<Record<string, unknown>>, operation: string, options: BaseActivationWorkerOptions, validate: (value: unknown) => value is T): Promise<BaseResult<T>> {
    const response = await this.callUnknown(route, request, operation, options);
    if (!response.ok) return response;
    if (!validate(response.value)) throw new TypeError("base.client.responseInvalid");
    return { ...response, value: response.value };
  }

  async callUnknown(route: string, request: Readonly<Record<string, unknown>>, operation: string, options: BaseActivationWorkerOptions): Promise<BaseResult<unknown>> {
    if (options.idempotencyKey.length === 0) throw new TypeError("base.activation.invalid");
    const semantic = new TextEncoder().encode(canonicalJson(request));
    const identity = await requestIdentity(this.#definition, operation, options.idempotencyKey, semantic);
    const bytes = new TextEncoder().encode(canonicalJson({ ...request, identity }));
    return this.#transport.jsonDocument("POST", route, bytes, options.signal);
  }
}

/** Creates a Service/System activation worker client. Browser runtimes fail closed. */
export function createBaseActivationWorkerClient<TInput, TResult>(options: BaseTransportOptions, definition: BaseActivationWorkerDefinition<TInput, TResult>): BaseActivationWorkerClient<TInput, TResult> {
  assertWorkerEnvironment();
  return new BaseActivationWorkerClient(new BaseHttpTransport(options), definition);
}

function assertWorkerEnvironment(): void {
  if (typeof globalThis.document !== "undefined" || typeof globalThis.window !== "undefined")
    throw new Error("base.activation.browserForbidden");
}

function encodeGraph(value: unknown, typeId: string, graph: BaseTypeGraph): string {
  try { return encodeBaseJson(value, typeId, graph); }
  catch { throw new TypeError("base.activation.invalid"); }
}

async function requestIdentity<TInput, TResult>(definition: BaseActivationWorkerDefinition<TInput, TResult>, operation: string, idempotencyKey: string, semantic: Uint8Array): Promise<Readonly<Record<string, unknown>>> {
  const fingerprint = new Uint8Array(await crypto.subtle.digest(
    "SHA-256",
    semantic.slice().buffer as ArrayBuffer));
  return {
    scope: `activation:${definition.id}:${definition.version}`,
    operation,
    idempotencyKey,
    fingerprint: [...fingerprint],
  };
}

function integer(value: number): number {
  if (!Number.isSafeInteger(value) || value < 0) throw new TypeError("base.activation.invalid");
  return value;
}

function canonicalJson(value: unknown, path: Set<object> = new Set()): string {
  if (value === null) return "null";
  if (typeof value === "string" || typeof value === "boolean") return JSON.stringify(value);
  if (typeof value === "number") {
    if (!Number.isFinite(value)) throw new TypeError("base.activation.invalid");
    return Object.is(value, -0) ? "-0" : String(value);
  }
  if (typeof value !== "object") throw new TypeError("base.activation.invalid");
  if (path.has(value)) throw new TypeError("base.activation.invalid");
  path.add(value);
  try {
    if (Array.isArray(value)) return `[${value.map(item => canonicalJson(item, path)).join(",")}]`;
    const object = value as Readonly<Record<string, unknown>>;
    return `{${Object.keys(object).sort().map(key => `${JSON.stringify(key)}:${canonicalJson(object[key], path)}`).join(",")}}`;
  } finally { path.delete(value); }
}

function isObject(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function isEnqueueResult(value: unknown): value is { readonly activationId: BaseActivationId; readonly state: string; readonly disposition: string } {
  return isObject(value) && typeof value.activationId === "string" && typeof value.state === "string" && typeof value.disposition === "string";
}

function isTransition(value: unknown): value is BaseActivationTransitionResult {
  return isObject(value) && typeof value.state === "string" && Number.isSafeInteger(value.generation) && typeof value.disposition === "string";
}

function isExecutorRegistration(value: unknown): value is BaseExecutorRegistration {
  return isObject(value) && isObject(value.executor) && isObject(value.heartbeat) && typeof value.disposition === "string";
}
