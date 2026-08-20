import type { BaseResult } from "./result.js";
import type { BaseHttpTransport } from "./transport.js";
import { decodeSubjectLifecycleDelivery } from "./subject-lifecycle.js";
import type { BaseSubjectLifecycleConsumerDefinition, BaseSubjectLifecycleCursor, BaseSubjectLifecycleDelivery, BaseSubjectLifecycleMutationIdentity, BaseSubjectLifecycleReadOptions } from "./subject-lifecycle.js";

declare const advisoryEvidenceBrand: unique symbol;
declare const requiredEvidenceBrand: unique symbol;
/** Opaque authenticated evidence that can attest advisory handling only. */
export type BaseSubjectAdvisoryAcknowledgementEvidence = string & { readonly [advisoryEvidenceBrand]: true };
/** Opaque authenticated evidence bound to one current required barrier. */
export type BaseSubjectRequiredAcknowledgementEvidence = string & { readonly [requiredEvidenceBrand]: true };
export type BaseSubjectAcknowledgementDisposition = "completed" | "retainedByPolicy";

export interface BaseSubjectAdvisoryLifecycleDelivery {
  readonly lifecycle: BaseSubjectLifecycleDelivery;
  readonly acknowledgement: BaseSubjectAdvisoryAcknowledgementEvidence;
  readonly acknowledgementIdentity: BaseSubjectLifecycleMutationIdentity;
}
export interface BaseSubjectRequiredLifecycleDelivery {
  readonly lifecycle: BaseSubjectLifecycleDelivery;
  readonly acknowledgement: BaseSubjectRequiredAcknowledgementEvidence;
  readonly acknowledgementIdentity: BaseSubjectLifecycleMutationIdentity;
}
export interface BaseSubjectAcknowledgementResult {
  readonly outcome: "applied" | "duplicate" | "obsolete";
  readonly throughSubjectSequence: string;
  readonly barrierState?: "pending" | "satisfied" | "timedOut" | "quarantined" | "overridden";
  readonly barrierGeneration?: string;
  readonly barrierChecksum?: string;
}
export interface BaseSubjectRetirementConsumerDefinition {
  readonly id: string;
  readonly version: number;
  readonly checksum: string;
  readonly participation: "advisory" | "required";
  readonly acknowledgementRoute: string;
}

/** Enumerates participation-specific retirement deliveries without advancing the L47 checkpoint. */
export async function* iterateSubjectRetirement(
  transport:BaseHttpTransport,definition:BaseSubjectLifecycleConsumerDefinition,options:BaseSubjectLifecycleReadOptions={}
):AsyncGenerator<BaseSubjectAdvisoryLifecycleDelivery|BaseSubjectRequiredLifecycleDelivery,void,void>{
  if(definition.retirementParticipation==="observeOnly"||!definition.acknowledgementRoute||!definition.retirementChecksum)invalid();let cursor:BaseSubjectLifecycleCursor|undefined;
  while(true){const body=new TextEncoder().encode(JSON.stringify({consumerId:definition.id,consumerVersion:definition.version,contractId:definition.contractId,contractVersion:definition.contractVersion,projectId:options.projectId??null,take:1,cursor:cursor??null}));const response=await transport.json("POST",definition.readRoute,body,options.signal);if(!response.ok)throw new Error(response.error.code);const page=object(response.value);if(!Array.isArray(page.deliveries)||page.deliveries.length>1||page.next!==null&&typeof page.next!=="string"||typeof page.checkpoint!=="string"||Object.keys(page).some(key=>!["deliveries","next","checkpoint"].includes(key)))invalid();if(page.deliveries.length===0)return;const row=object(page.deliveries[0]);if(Object.keys(row).length!==3||!Object.hasOwn(row,"lifecycle")||!Object.hasOwn(row,"acknowledgement")||!Object.hasOwn(row,"acknowledgementIdentity")||!opaque(row.acknowledgement,2048))invalid();const lifecycle=decodeSubjectLifecycleDelivery(row.lifecycle,definition);const identity=decodeIdentity(row.acknowledgementIdentity);yield Object.freeze({lifecycle,acknowledgement:row.acknowledgement as BaseSubjectAdvisoryAcknowledgementEvidence,acknowledgementIdentity:identity}) as BaseSubjectAdvisoryLifecycleDelivery|BaseSubjectRequiredLifecycleDelivery;if(page.next===null)return;cursor=page.next as BaseSubjectLifecycleCursor;}
}

/** Submits one generated participation-specific acknowledgement. */
export async function acknowledgeSubjectRetirement(
  transport: BaseHttpTransport,
  definition: BaseSubjectRetirementConsumerDefinition,
  evidence: BaseSubjectAdvisoryAcknowledgementEvidence | BaseSubjectRequiredAcknowledgementEvidence,
  disposition: BaseSubjectAcknowledgementDisposition,
  identity: BaseSubjectLifecycleMutationIdentity,
  projectId?: string,
  signal?: AbortSignal): Promise<BaseResult<BaseSubjectAcknowledgementResult>> {
  validateDefinition(definition);
  if (!opaque(evidence, 2_048) || !["completed", "retainedByPolicy"].includes(disposition)
    || identity.operation !== "subjectRetirement.acknowledge" || !/^[0-9a-f]{64}$/u.test(identity.idempotencyKey)
    || !fingerprint(identity.fingerprint) || projectId !== undefined && (projectId.length < 1 || projectId.length > 256))
    invalid();
  const body = new TextEncoder().encode(JSON.stringify({
    consumerId: definition.id, consumerVersion: definition.version, participation: definition.participation,
    evidence, disposition, identity, projectId: projectId ?? null,
  }));
  const response = await transport.json("POST", definition.acknowledgementRoute, body, signal, identity.idempotencyKey);
  if (!response.ok) return response;
  const value = object(response.value); const allowed = ["outcome", "throughSubjectSequence", "barrierState", "barrierGeneration", "barrierChecksum"];
  if (Object.keys(value).some(key => !allowed.includes(key)) || !["applied", "duplicate", "obsolete"].includes(value.outcome as string)
    || !positive(value.throughSubjectSequence)) invalid();
  const hasBarrier = value.barrierState !== undefined || value.barrierGeneration !== undefined || value.barrierChecksum !== undefined;
  if (hasBarrier && (! ["pending", "satisfied", "timedOut", "quarantined", "overridden"].includes(value.barrierState as string)
    || !positive(value.barrierGeneration) || typeof value.barrierChecksum !== "string" || !/^[0-9a-f]{64}$/u.test(value.barrierChecksum))) invalid();
  return { ...response, value: Object.freeze({ outcome: value.outcome, throughSubjectSequence: value.throughSubjectSequence,
    ...(hasBarrier ? { barrierState: value.barrierState, barrierGeneration: value.barrierGeneration, barrierChecksum: value.barrierChecksum } : {}) }) as BaseSubjectAcknowledgementResult };
}

function validateDefinition(value: BaseSubjectRetirementConsumerDefinition): void {
  if (!Number.isSafeInteger(value.version) || value.version < 1 || !/^[0-9a-f]{64}$/u.test(value.checksum)
    || !["advisory", "required"].includes(value.participation) || !value.acknowledgementRoute.startsWith("/")) invalid();
}
function decodeIdentity(input:unknown):BaseSubjectLifecycleMutationIdentity{const value=object(input);if(Object.keys(value).length!==4||value.operation!=="subjectRetirement.acknowledge"||typeof value.scope!=="string"||typeof value.idempotencyKey!=="string"||!/^[0-9a-f]{64}$/u.test(value.idempotencyKey)||!fingerprint(value.fingerprint))invalid();return Object.freeze(value as unknown as BaseSubjectLifecycleMutationIdentity);}
function object(value: unknown): Record<string, unknown> { if (value === null || typeof value !== "object" || Array.isArray(value)) invalid(); return value as Record<string, unknown>; }
function positive(value: unknown): value is string { return typeof value === "string" && /^[1-9][0-9]*$/u.test(value); }
function opaque(value: unknown, maximumBytes: number): value is string { if (typeof value !== "string" || !/^[A-Za-z0-9_-]+$/u.test(value)) return false; try { const text=value.replace(/-/gu,"+").replace(/_/gu,"/"); return Uint8Array.from(atob(text.padEnd(text.length+(4-text.length%4)%4,"=")),c=>c.charCodeAt(0)).length<=maximumBytes; } catch { return false; } }
function fingerprint(value: unknown): value is string { if (typeof value !== "string" || !/^[A-Za-z0-9+/]{43}=$/u.test(value)) return false; try { return Uint8Array.from(atob(value),c=>c.charCodeAt(0)).length===32; } catch { return false; } }
function invalid(): never { throw new TypeError("base.client.responseInvalid"); }
