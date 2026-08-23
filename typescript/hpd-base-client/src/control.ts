import type { BaseResult } from "./result.js";
import { BaseHttpTransport, parseBaseJson } from "./transport.js";

export interface BasePurgeRequest { readonly collectionId: string; readonly recordIds: readonly string[]; readonly reasonCode: string; readonly auditReference: string; readonly evaluatedAt: string; readonly expectedPurgeGeneration?: number; }
export interface BaseBackupCreateRequest { readonly storeId: string; readonly expectedStoreIdentityDigest?: string; }
export interface BaseBackupValidationRequest { readonly storeId: string; readonly expectedArtifactStoreIdentityDigest?: string; }
export interface BaseRestoreRequest { readonly storeId: string; readonly expectedCurrentStoreIdentityDigest: string; readonly expectedArtifactStoreIdentityDigest: string; readonly identityMode: "requireCurrentStoreIdentity" | "adoptArtifactStoreIdentity"; readonly recoveryImageRetention: "deleteAfterSuccessfulRestore" | "retainUntilHostRemoves"; readonly confirmDestructiveReplacement: true; }
export interface BaseBackupManifest { readonly envelopeVersion: number; readonly providerKind: string; readonly providerVersion: string; readonly nativeSqliteVersion: string; readonly baseContractVersion: string; readonly storeIdentityDigest: string; readonly schemaGeneration: number; readonly schemaBaselineId: string; readonly schemaChecksum: string; readonly restoreEpoch: number; readonly createdAt: string; readonly providerPayloadLength: number; readonly providerPayloadSha256: string; readonly logicalPartitions: readonly string[]; readonly receiptFormatVersion: number; readonly journalFormatVersion: number; readonly collectionHistoryFormatVersion: number; readonly payloadEncryptedAtRest: boolean; readonly externalKeyReferenceKind: string | null; }
export interface BasePurgeResult { readonly collectionId: string; readonly requestedCount: number; readonly purgedCount: number; readonly purgeGeneration: number; readonly committedAt: string; }
export interface BaseRestoreResult { readonly storeId: string; readonly status: "restored"; readonly installedStoreIdentityDigest: string; readonly restoreEpoch: number; readonly recoveryImageRetained: boolean; }
export interface BaseSubjectEpochRotationRequest { readonly storeId: string; readonly contractId: string; readonly contractVersion: number; readonly expectedStateGeneration: string; readonly destructiveIntent: "rotate-subject-authority-epoch"; }
export interface BaseSubjectEpochRotationResult { readonly contractId: string; readonly contractVersion: number; readonly previousStateGeneration: string; readonly publishedStateGeneration: string; readonly publicationPosition: string; readonly examinedRecords: string; readonly rewrittenReferences: string; }
declare const semanticInspectionTokenBrand: unique symbol;
/** Opaque ControlPlane-only continuation or sanitized semantic-slot reference. */
export type BaseSemanticActivationInspectionToken = string & { readonly [semanticInspectionTokenBrand]: true };
export interface BaseSemanticActivationInspectionRequest { readonly storeId: string; readonly definitionId: string; readonly definitionVersion: number; readonly definitionChecksum: string; readonly state?: 1 | 2 | 3 | null; readonly after?: BaseSemanticActivationInspectionToken | null; readonly take: number; }
export interface BaseSemanticActivationInspectionItem { readonly state: 1 | 2 | 3; readonly slotGeneration: string; readonly itemToken: BaseSemanticActivationInspectionToken; readonly retirementPosition: string | null; readonly sanitizedChecksum: string; }
export interface BaseSemanticActivationInspectionPage { readonly items: readonly BaseSemanticActivationInspectionItem[]; readonly next: BaseSemanticActivationInspectionToken | null; readonly capturedAuthorityGeneration: string; readonly checksum: string; }

interface BaseControlPlaneSurface {
  purge(request: BasePurgeRequest, signal?: AbortSignal): Promise<BaseResult<BasePurgeResult>>;
  createBackup(request: BaseBackupCreateRequest, destination: WritableStream<Uint8Array>, signal?: AbortSignal): Promise<BaseResult<BaseBackupManifest>>;
  validateBackup(request: BaseBackupValidationRequest, artifact: Blob, signal?: AbortSignal): Promise<BaseResult<BaseBackupManifest>>;
  restoreBackup(request: BaseRestoreRequest, artifact: Blob, signal?: AbortSignal): Promise<BaseResult<BaseRestoreResult>>;
  rotateSubjectEpoch(request: BaseSubjectEpochRotationRequest, signal?: AbortSignal): Promise<BaseResult<BaseSubjectEpochRotationResult>>;
  inspectSemanticActivations(request: BaseSemanticActivationInspectionRequest, signal?: AbortSignal): Promise<BaseResult<BaseSemanticActivationInspectionPage>>;
}

type BaseControlMethodMap = {
  readonly "base.admin.purge": "purge";
  readonly "base.admin.backup.create": "createBackup";
  readonly "base.admin.backup.validate": "validateBackup";
  readonly "base.admin.backup.restore": "restoreBackup";
  readonly "base.admin.subject.epoch.rotate": "rotateSubjectEpoch";
  readonly "base.semanticActivation.inspect": "inspectSemanticActivations";
};
type InstalledControlMethod<TOperations extends readonly string[]> = { [K in keyof BaseControlMethodMap]: K extends TOperations[number] ? BaseControlMethodMap[K] : never }[keyof BaseControlMethodMap];
export type BaseControlPlaneClient<TOperations extends readonly string[]> = Pick<BaseControlPlaneSurface, InstalledControlMethod<TOperations>>;

export function createControlPlaneClient<const TOperations extends readonly string[]>(transport: BaseHttpTransport, operations: TOperations): BaseControlPlaneClient<TOperations> {
  const implementation = new BaseControlPlaneSurfaceImplementation(transport); const result: Record<string, unknown> = {};
  if (operations.includes("base.admin.purge")) result["purge"] = implementation.purge.bind(implementation);
  if (operations.includes("base.admin.backup.create")) result["createBackup"] = implementation.createBackup.bind(implementation);
  if (operations.includes("base.admin.backup.validate")) result["validateBackup"] = implementation.validateBackup.bind(implementation);
  if (operations.includes("base.admin.backup.restore")) result["restoreBackup"] = implementation.restoreBackup.bind(implementation);
  if (operations.includes("base.admin.subject.epoch.rotate")) result["rotateSubjectEpoch"] = implementation.rotateSubjectEpoch.bind(implementation);
  if (operations.includes("base.semanticActivation.inspect")) result["inspectSemanticActivations"] = implementation.inspectSemanticActivations.bind(implementation);
  return Object.freeze(result) as BaseControlPlaneClient<TOperations>;
}

class BaseControlPlaneSurfaceImplementation implements BaseControlPlaneSurface {
  public constructor(private readonly transport: BaseHttpTransport) {}
  public async purge(request: BasePurgeRequest, signal?: AbortSignal): Promise<BaseResult<BasePurgeResult>> { const result = await this.transport.json("POST", "administration/purge", new Uint8Array(encode(request)), signal); return result.ok ? (isPurgeResult(result.value) ? success(result.value, result.correlationId) : invalid(result.correlationId)) : result; }
  public async createBackup(request: BaseBackupCreateRequest, destination: WritableStream<Uint8Array>, signal?: AbortSignal): Promise<BaseResult<BaseBackupManifest>> { const response = await this.transport.raw("POST", "administration/backups:create", encode(request), "application/json", "multipart/mixed", signal); if (!response.ok) return response; try { return success(await consumeConfirmedBackup(response.value, destination, signal), response.correlationId); } catch (cause: unknown) { try { await destination.abort(cause); } catch { } return invalid(response.correlationId); } }
  public validateBackup(request: BaseBackupValidationRequest, artifact: Blob, signal?: AbortSignal): Promise<BaseResult<BaseBackupManifest>> { return this.multipart("administration/backups:validate", request, artifact, isManifest, signal); }
  public restoreBackup(request: BaseRestoreRequest, artifact: Blob, signal?: AbortSignal): Promise<BaseResult<BaseRestoreResult>> { if (request.confirmDestructiveReplacement !== true) throw new TypeError("base.client.confirmationRequired"); return this.multipart("administration/backups:restore", request, artifact, isRestoreResult, signal); }
  public async rotateSubjectEpoch(request: BaseSubjectEpochRotationRequest, signal?: AbortSignal): Promise<BaseResult<BaseSubjectEpochRotationResult>> { if (!exactObject(request, ["storeId", "contractId", "contractVersion", "expectedStateGeneration", "destructiveIntent"]) || !boundedIdentifier(request.storeId, 256) || !boundedIdentifier(request.contractId, 256) || request.destructiveIntent !== "rotate-subject-authority-epoch" || !positiveDecimal(request.expectedStateGeneration) || !Number.isInteger(request.contractVersion) || request.contractVersion < 1 || request.contractVersion > 2_147_483_647) throw new TypeError("base.client.requestInvalid"); const body = { storeId: request.storeId, contractId: request.contractId, contractVersion: request.contractVersion, expectedStateGeneration: request.expectedStateGeneration, destructiveIntent: request.destructiveIntent }; const result = await this.transport.json("POST", "administration/subjects:rotate-epoch", new Uint8Array(encode(body)), signal); return result.ok ? (isSubjectEpochRotationResult(result.value) ? success(Object.freeze(result.value), result.correlationId) : invalid(result.correlationId)) : result; }
  public async inspectSemanticActivations(request: BaseSemanticActivationInspectionRequest, signal?: AbortSignal): Promise<BaseResult<BaseSemanticActivationInspectionPage>> { if (!exactOptionalObject(request, ["storeId", "definitionId", "definitionVersion", "definitionChecksum", "take"], ["state", "after"]) || !boundedIdentifier(request.storeId, 256) || !boundedIdentifier(request.definitionId, 256) || !Number.isInteger(request.definitionVersion) || request.definitionVersion < 1 || request.definitionVersion > 2_147_483_647 || !base64Bytes(request.definitionChecksum, 32) || request.state !== null && request.state !== undefined && ![1,2,3].includes(request.state) || request.after !== null && request.after !== undefined && !semanticToken(request.after) || !Number.isInteger(request.take) || request.take < 1 || request.take > 256) throw new TypeError("base.client.requestInvalid"); const body = { definitionId: request.definitionId, definitionVersion: request.definitionVersion, definitionChecksum: request.definitionChecksum, state: request.state ?? null, after: request.after ?? null, take: request.take }; const result = await this.transport.json("POST", `control/stores/${encodeURIComponent(request.storeId)}/semantic-activations/query`, new Uint8Array(encode(body)), signal); return result.ok ? (isSemanticInspectionPage(result.value) ? success(freezeSemanticPage(result.value), result.correlationId) : invalid(result.correlationId)) : result; }
  private async multipart<T>(route: string, request: unknown, artifact: Blob, validate: (value: unknown) => value is T, signal?: AbortSignal): Promise<BaseResult<T>> { const form = new FormData(); form.append("request", new Blob([encode(request)], { type: "application/json" }), "request.json"); form.append("artifact", artifact, "artifact.bin"); const response = await this.transport.raw("POST", route, form, undefined, "application/json", signal); if (!response.ok) return response; try { const bytes = await boundedResponse(response.value, 4 * 1024 * 1024); const value = parseBaseJson(new TextDecoder("utf-8", { fatal: true }).decode(bytes)); return validate(value) ? success(value, response.correlationId) : invalid(response.correlationId); } catch { return invalid(response.correlationId); } }
}

async function consumeConfirmedBackup(response: Response, destination: WritableStream<Uint8Array>, signal?: AbortSignal): Promise<BaseBackupManifest> {
  const declaredTotal = strictLength(response.headers.get("Content-Length"));
  const contentType = response.headers.get("Content-Type") ?? "";
  const match = /^multipart\/mixed;\s*boundary=(hpd-base-[0-9a-f]{32})$/u.exec(contentType);
  if (match === null || response.body === null) throw new TypeError();
  const boundary = match[1]!; const input = new ByteReader(response.body.getReader(), declaredTotal, signal);
  await input.expect(`--${boundary}\r\n`);
  const manifestHeaders = await input.headers();
  if (manifestHeaders.contentType !== "application/json" || manifestHeaders.length > 64 * 1024) throw new TypeError();
  const manifestBytes = await input.bytes(manifestHeaders.length);
  await input.expect(`\r\n--${boundary}\r\n`);
  const artifactHeaders = await input.headers();
  if (artifactHeaders.contentType !== "application/octet-stream") throw new TypeError();
  const writer = destination.getWriter();
  try { await input.copy(artifactHeaders.length, writer); await writer.close(); }
  catch (cause: unknown) { try { await writer.abort(cause); } catch { /* preserve original */ } throw cause; }
  await input.expect(`\r\n--${boundary}--\r\n`);
  await input.complete();
  const manifest = parseBaseJson(new TextDecoder("utf-8", { fatal: true }).decode(manifestBytes)) as BaseBackupManifest;
  if (!isManifest(manifest)) throw new TypeError();
  return Object.freeze({ ...manifest, logicalPartitions: Object.freeze([...manifest.logicalPartitions]) });
}

class ByteReader {
  readonly #decoder = new TextEncoder(); private buffered = new Uint8Array(); private consumed = 0;
  public constructor(private readonly reader: ReadableStreamDefaultReader<Uint8Array>, private readonly declared: number, private readonly signal?: AbortSignal) {}
  public async expect(text: string): Promise<void> { const expected = this.#decoder.encode(text); const actual = await this.bytes(expected.length); if (!equal(actual, expected)) throw new TypeError(); }
  public async headers(): Promise<{ readonly contentType: string; readonly length: number }> {
    let raw = ""; for (;;) { if (raw.length > 16 * 1024) throw new TypeError(); const byte = (await this.bytes(1))[0]!; raw += String.fromCharCode(byte); if (raw.endsWith("\r\n\r\n")) break; }
    const lines = raw.slice(0, -4).split("\r\n"); if (lines.some(line => line.length > 4096) || lines.length !== 2) throw new TypeError();
    const values = new Map(lines.map(line => { const separator = line.indexOf(":"); if (separator <= 0) throw new TypeError(); return [line.slice(0, separator).toLowerCase(), line.slice(separator + 1).trim()]; }));
    if (values.size !== 2) throw new TypeError(); return { contentType: values.get("content-type") ?? "", length: strictLength(values.get("content-length") ?? null) };
  }
  public async bytes(length: number): Promise<Uint8Array> { if (!Number.isSafeInteger(length) || length < 0) throw new TypeError(); while (this.buffered.length < length) await this.fill(); const result = this.buffered.slice(0, length); this.buffered = this.buffered.slice(length); this.consumed += length; if (this.consumed > this.declared) throw new TypeError(); return result; }
  public async copy(length: number, writer: WritableStreamDefaultWriter<Uint8Array>): Promise<void> { let remaining = length; while (remaining !== 0) { if (this.buffered.length === 0) await this.fill(); const count = Math.min(remaining, this.buffered.length); await writer.write(this.buffered.slice(0, count)); this.buffered = this.buffered.slice(count); this.consumed += count; remaining -= count; if (this.consumed > this.declared) throw new TypeError(); } }
  public async complete(): Promise<void> { if (this.buffered.length !== 0 || this.consumed !== this.declared) throw new TypeError(); const final = await this.reader.read(); if (!final.done) throw new TypeError(); }
  private async fill(): Promise<void> { this.signal?.throwIfAborted(); const next = await this.reader.read(); if (next.done || next.value.length === 0) throw new TypeError(); const merged = new Uint8Array(this.buffered.length + next.value.length); merged.set(this.buffered); merged.set(next.value, this.buffered.length); this.buffered = merged; }
}

function strictLength(value: string | null): number { if (value === null || !/^(?:0|[1-9][0-9]*)$/u.test(value)) throw new TypeError(); const length = Number(value); if (!Number.isSafeInteger(length)) throw new TypeError(); return length; }
function equal(left: Uint8Array, right: Uint8Array): boolean { return left.length === right.length && left.every((value, index) => value === right[index]); }
function encode(value: unknown): ArrayBuffer { const bytes = new TextEncoder().encode(JSON.stringify(value)); return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer; }
function isManifest(value: unknown): value is BaseBackupManifest { if (!exactObject(value, ["envelopeVersion", "providerKind", "providerVersion", "nativeSqliteVersion", "baseContractVersion", "storeIdentityDigest", "schemaGeneration", "schemaBaselineId", "schemaChecksum", "restoreEpoch", "createdAt", "providerPayloadLength", "providerPayloadSha256", "logicalPartitions", "receiptFormatVersion", "journalFormatVersion", "collectionHistoryFormatVersion", "payloadEncryptedAtRest", "externalKeyReferenceKind"])) return false; const item = value as unknown as BaseBackupManifest; return Number.isSafeInteger(item.envelopeVersion) && strings(item.providerKind, item.providerVersion, item.nativeSqliteVersion, item.baseContractVersion, item.storeIdentityDigest, item.schemaBaselineId, item.schemaChecksum, item.createdAt, item.providerPayloadSha256) && Number.isSafeInteger(item.schemaGeneration) && Number.isSafeInteger(item.restoreEpoch) && Number.isSafeInteger(item.providerPayloadLength) && Array.isArray(item.logicalPartitions) && item.logicalPartitions.every(part => typeof part === "string") && Number.isSafeInteger(item.receiptFormatVersion) && Number.isSafeInteger(item.journalFormatVersion) && Number.isSafeInteger(item.collectionHistoryFormatVersion) && typeof item.payloadEncryptedAtRest === "boolean" && (item.externalKeyReferenceKind === null || typeof item.externalKeyReferenceKind === "string"); }
function isRestoreResult(value: unknown): value is BaseRestoreResult { if (!exactObject(value, ["storeId", "status", "installedStoreIdentityDigest", "restoreEpoch", "recoveryImageRetained"])) return false; const item = value as unknown as BaseRestoreResult; return strings(item.storeId, item.installedStoreIdentityDigest) && item.status === "restored" && Number.isSafeInteger(item.restoreEpoch) && typeof item.recoveryImageRetained === "boolean"; }
function isPurgeResult(value: unknown): value is BasePurgeResult { if (!exactObject(value, ["collectionId", "requestedCount", "purgedCount", "purgeGeneration", "committedAt"])) return false; const item = value as unknown as BasePurgeResult; return strings(item.collectionId, item.committedAt) && Number.isSafeInteger(item.requestedCount) && Number.isSafeInteger(item.purgedCount) && Number.isSafeInteger(item.purgeGeneration); }
function isSubjectEpochRotationResult(value: unknown): value is BaseSubjectEpochRotationResult { if (!exactObject(value, ["contractId", "contractVersion", "previousStateGeneration", "publishedStateGeneration", "publicationPosition", "examinedRecords", "rewrittenReferences"])) return false; const item = value as unknown as BaseSubjectEpochRotationResult; return typeof item.contractId === "string" && Number.isInteger(item.contractVersion) && item.contractVersion > 0 && positiveDecimal(item.previousStateGeneration) && positiveDecimal(item.publishedStateGeneration) && positiveDecimal(item.publicationPosition) && nonnegativeDecimal(item.examinedRecords) && nonnegativeDecimal(item.rewrittenReferences); }
function isSemanticInspectionPage(value: unknown): value is BaseSemanticActivationInspectionPage { if (!exactObject(value,["items","next","capturedAuthorityGeneration","checksum"])) return false; const page=value as unknown as BaseSemanticActivationInspectionPage; return Array.isArray(page.items)&&page.items.length<=256&&page.items.every(item=>exactObject(item,["state","slotGeneration","itemToken","retirementPosition","sanitizedChecksum"])&&typeof item.state==="number"&&[1,2,3].includes(item.state)&&positiveDecimal(item.slotGeneration)&&semanticToken(item.itemToken)&&(item.retirementPosition===null||positiveDecimal(item.retirementPosition))&&base64Bytes(item.sanitizedChecksum,32))&&(page.next===null||semanticToken(page.next))&&positiveDecimal(page.capturedAuthorityGeneration)&&base64Bytes(page.checksum,32); }
function freezeSemanticPage(value: BaseSemanticActivationInspectionPage): BaseSemanticActivationInspectionPage { return Object.freeze({ ...value, items:Object.freeze(value.items.map(item=>Object.freeze({ ...item, itemToken:item.itemToken as BaseSemanticActivationInspectionToken }))), next:value.next===null?null:value.next as BaseSemanticActivationInspectionToken }); }
function semanticToken(value: unknown): value is BaseSemanticActivationInspectionToken { return typeof value==="string"&&value.length>=1&&value.length<=2048&&/^[A-Za-z0-9_-]+$/u.test(value); }
function base64Bytes(value: unknown, bytes: number): value is string { if(typeof value!=="string"||!/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/u.test(value))return false; try{const decoded=atob(value);return decoded.length===bytes&&btoa(decoded)===value;}catch{return false;} }
function positiveDecimal(value: unknown): value is string { return typeof value === "string" && /^[1-9][0-9]*$/u.test(value) && BigInt(value) <= 9_223_372_036_854_775_807n; }
function nonnegativeDecimal(value: unknown): value is string { return value === "0" || positiveDecimal(value); }
function boundedIdentifier(value: unknown, maximumBytes: number): value is string { return typeof value === "string" && value.length !== 0 && new TextEncoder().encode(value).length <= maximumBytes && !/[\u0000-\u001f\u007f]/u.test(value); }
function exactObject(value: unknown, keys: readonly string[]): value is Record<string, unknown> { return typeof value === "object" && value !== null && !Array.isArray(value) && Object.keys(value).length === keys.length && Object.keys(value).every(key => keys.includes(key)); }
function exactOptionalObject(value: unknown, required: readonly string[], optional: readonly string[]): value is Record<string, unknown> { if(typeof value!=="object"||value===null||Array.isArray(value))return false;const keys=Object.keys(value);return required.every(key=>keys.includes(key))&&keys.every(key=>required.includes(key)||optional.includes(key)); }
function strings(...values: readonly unknown[]): boolean { return values.every(value => typeof value === "string"); }
async function boundedResponse(response: Response, maximum: number): Promise<Uint8Array> { const declared = response.headers.get("Content-Length"); if (declared !== null && strictLength(declared) > maximum) throw new TypeError(); const reader = response.body?.getReader(); if (reader === undefined) return new Uint8Array(); const chunks: Uint8Array[] = []; let length = 0; for (;;) { const next = await reader.read(); if (next.done) break; length += next.value.length; if (length > maximum) { await reader.cancel(); throw new TypeError(); } chunks.push(next.value); } const result = new Uint8Array(length); let offset = 0; for (const chunk of chunks) { result.set(chunk, offset); offset += chunk.length; } return result; }
function success<T>(value: T, correlationId = ""): BaseResult<T> { return { ok: true, value, status: "ok", correlationId, warnings: [] }; }
function invalid<T>(correlationId?: string): BaseResult<T> { return { ok: false, error: { code: "base.client.responseInvalid", category: "unexpected", message: "The BASE response was invalid." }, ...(correlationId === undefined ? {} : { correlationId }), retry: "never" }; }
