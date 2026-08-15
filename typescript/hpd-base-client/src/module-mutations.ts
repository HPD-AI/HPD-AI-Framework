import type { BaseTypeGraph } from "./codec.js";
import { decodeBaseWireValue, encodeBaseJson } from "./codec.js";
import type { BaseResult } from "./result.js";
import type { BaseHttpTransport } from "./transport.js";

declare const moduleGenerationBrand: unique symbol;
/** Opaque positive module generation transported as canonical decimal text. */
export type BaseModuleGeneration = string & { readonly [moduleGenerationBrand]: true };

export interface BaseModuleMutationDefinition<TRequest = unknown, TResult = unknown> {
  readonly route: string;
  readonly maximumRequestBytes: number;
  readonly audience: "service" | "system";
  readonly requestTypeId: string;
  readonly resultTypeId: string;
  readonly typeGraph: BaseTypeGraph;
  readonly __request?: TRequest;
  readonly __result?: TResult;
}
export interface BaseModuleMutationOptions { readonly idempotencyKey: string; readonly signal?: AbortSignal; }
export interface BaseModuleMutationResult<TResult> { readonly disposition: "new" | "duplicate"; readonly outcome: "committed" | "duplicate"; readonly result: TResult; }

/** Creates one immutable generated Service/System module-mutation descriptor. */
export function moduleMutation<TRequest, TResult>(definition: BaseModuleMutationDefinition<TRequest, TResult>): BaseModuleMutationDefinition<TRequest, TResult> {
  return Object.freeze(definition);
}

/** Executes one graph-owned module mutation through its owning client transport. */
export async function executeModuleMutation<TRequest, TResult>(transport: BaseHttpTransport, definition: BaseModuleMutationDefinition<TRequest, TResult>, request: TRequest, options: BaseModuleMutationOptions): Promise<BaseResult<BaseModuleMutationResult<TResult>>> {
  if (request === null || typeof request !== "object" || Array.isArray(request) || options.idempotencyKey.length === 0) throw new TypeError("base.moduleMutation.invalid");
  const bytes = new TextEncoder().encode(encodeBaseJson(request, definition.requestTypeId, definition.typeGraph));
  if (bytes.byteLength > definition.maximumRequestBytes) throw new RangeError("base.moduleMutation.limitExceeded");
  const response = await transport.jsonDocument("POST", definition.route, bytes, options.signal, options.idempotencyKey);
  if (!response.ok) return response;
  if (response.value === null || typeof response.value !== "object" || Array.isArray(response.value)) throw new TypeError("base.client.responseInvalid");
  const envelope = response.value as Record<string, unknown>;
  if (Object.keys(envelope).length !== 3 || !["new", "duplicate"].includes(envelope.disposition as string)
      || !["committed", "duplicate"].includes(envelope.outcome as string) || !("result" in envelope)) throw new TypeError("base.client.responseInvalid");
  const result = decodeBaseWireValue<TResult>(envelope.result, definition.resultTypeId, definition.typeGraph);
  return { ...response, value: { disposition: envelope.disposition as "new" | "duplicate", outcome: envelope.outcome as "committed" | "duplicate", result } };
}
