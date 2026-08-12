import type { BaseResult } from "./result.js";
import type { BaseTypeGraph } from "./codec.js";
import { decodeBaseWireValue, encodeBaseJson } from "./codec.js";
import type { BaseHttpTransport } from "./transport.js";

export interface BaseSelectionMutationResult {
  readonly selectedCount: number;
  readonly mutatedCount: number;
  readonly outcome: "committed" | "rolledBack" | "partiallyCommitted";
  readonly requestDisposition: "committed" | "duplicate";
}

export type BaseSelectionQueryValue = null | boolean | number | string | readonly BaseSelectionQueryValue[];
export type BaseSelectionFilter =
  | { readonly kind: "true" | "false" }
  | { readonly kind: "compare"; readonly field: string; readonly operator: string; readonly value: BaseSelectionQueryValue }
  | { readonly kind: "in" | "between"; readonly field: string; readonly values: readonly BaseSelectionQueryValue[] }
  | { readonly kind: "isNull" | "isDefined"; readonly field: string }
  | { readonly kind: "not"; readonly children: readonly [BaseSelectionFilter] }
  | { readonly kind: "and" | "or"; readonly children: readonly BaseSelectionFilter[] };
export interface BaseSelectionHttpQuery { readonly filter?: BaseSelectionFilter; readonly sort: readonly { readonly field: string; readonly direction: "asc" | "desc" }[]; readonly take: number; }
export interface BaseSelectionPreviousState { readonly revision: Readonly<Record<string, unknown>>; readonly fields: readonly Readonly<Record<string, unknown>>[]; }
export interface BaseSelectionRequestIdentity { readonly scope: string; readonly operation: string; readonly idempotencyKey: string; readonly fingerprint: string; }

export interface BaseSelectionMutationOptions {
  readonly mutationId?: string;
  readonly signal?: AbortSignal;
}

/** Creates one generated, executable transaction-bound selection mutation. */
export interface BaseSelectionMutationDefinition<TRequest = unknown> {
  readonly route: string;
  readonly mutationKind: "mergePatch" | "delete";
  readonly maximumRequestBodyBytes: number;
  readonly requestTypeId: string;
  readonly resultTypeId: string;
  readonly typeGraph: BaseTypeGraph;
  readonly __request?: TRequest;
}

/** Creates one immutable generated selection-mutation descriptor. */
export function selectionMutation<TRequest>(definition: BaseSelectionMutationDefinition<TRequest>): BaseSelectionMutationDefinition<TRequest> {
  return Object.freeze(definition);
}

/** Executes one generated descriptor through its owning configured client transport. */
export async function executeSelectionMutation<TRequest>(transport: BaseHttpTransport, definition: BaseSelectionMutationDefinition<TRequest>, request: TRequest, options: BaseSelectionMutationOptions = {}): Promise<BaseResult<BaseSelectionMutationResult>> {
    if (request === null || typeof request !== "object" || Array.isArray(request)) throw new TypeError("base.selection.contractInvalid");
    const bytes = new TextEncoder().encode(encodeBaseJson(request, definition.requestTypeId, definition.typeGraph));
    if (bytes.byteLength > definition.maximumRequestBodyBytes) throw new RangeError("base.selection.limitExceeded");
    const result = await transport.jsonDocument("POST", definition.route, bytes, options.signal, options.mutationId);
    if (!result.ok) return result;
    const value = decodeBaseWireValue<unknown>(result.value, definition.resultTypeId, definition.typeGraph);
    if (value === null || typeof value !== "object" || Array.isArray(value)) throw new TypeError("base.client.responseInvalid");
    const item = value as Record<string, unknown>;
    if (Object.keys(item).some(key => !["selectedCount", "mutatedCount", "outcome", "requestDisposition"].includes(key))
      || Object.keys(item).length !== 4 || !Number.isSafeInteger(item.selectedCount) || !Number.isSafeInteger(item.mutatedCount)
      || !["committed", "rolledBack", "partiallyCommitted"].includes(item.outcome as string)
      || !["committed", "duplicate"].includes(item.requestDisposition as string))
      throw new TypeError("base.client.responseInvalid");
    return { ...result, value: { selectedCount: item.selectedCount as number, mutatedCount: item.mutatedCount as number, outcome: item.outcome as BaseSelectionMutationResult["outcome"], requestDisposition: item.requestDisposition as BaseSelectionMutationResult["requestDisposition"] } };
}
