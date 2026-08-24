import { studioCanonicalHash } from './canonical.ts';
import { studioSha256, type StudioSha256 } from './module-abi.ts';

export const STUDIO_RESOURCE_KINDS = Object.freeze([
  'application', 'module', 'collection', 'record', 'relation', 'fileBucket', 'file', 'registeredRead',
  'selectionOperation', 'moduleMutation', 'operationExecution', 'receipt', 'activationDefinition',
  'activation', 'schedule', 'occurrence', 'activationAttempt', 'effect', 'executor', 'subjectContract',
  'subject', 'lifecycleConsumer', 'lifecycleCheckpoint', 'retirementBarrier', 'textIndex', 'vectorIndex',
  'searchRebuild', 'certificationReceipt', 'policy', 'grant', 'store', 'provider', 'schema', 'migration',
  'backup', 'restore', 'maintenance', 'health', 'diagnostic', 'quarantineItem', 'graphDefinition',
  'graphExecution', 'graphNode', 'graphChannel', 'graphCheckpoint'
] as const);
export type StudioResourceKind = typeof STUDIO_RESOURCE_KINDS[number];
type MemberKind = 'text' | 'int' | 'long' | 'checksum';
type ResourceValue = string | number;
export type StudioOutwardResourceAuthority = Readonly<{ readonly kind: StudioResourceKind; readonly applicationId: string;
  readonly authorityChecksum: StudioSha256 } & Record<string, ResourceValue>>;

const members: Readonly<Record<StudioResourceKind, readonly (readonly [string, MemberKind])[]>> = Object.freeze({
  application: [], module: [['moduleId','text'],['moduleVersion','int']], collection: [['collectionId','text'],['installedCollectionChecksum','checksum']],
  record: [['collectionId','text'],['installedCollectionChecksum','checksum'],['recordId','text']], relation: [['sourceCollectionId','text'],['sourceRecordId','text'],['fieldEdgeId','text'],['targetCollectionId','text'],['targetRecordId','text']],
  fileBucket: [['bucketId','text']], file: [['bucketId','text'],['objectId','text']], registeredRead: [['readId','text'],['version','int']],
  selectionOperation: [['profileId','text'],['version','int']], moduleMutation: [['operationId','text'],['version','int']],
  operationExecution: [['operationKind','text'],['operationId','text'],['requestIdentity','text']], receipt: [['receiptKind','text'],['operationId','text'],['requestIdentity','text']],
  activationDefinition: [['definitionId','text'],['version','int']], activation: [['definitionId','text'],['version','int'],['activationId','text']],
  schedule: [['scheduleId','text'],['version','int']], occurrence: [['scheduleId','text'],['version','int'],['occurrenceId','text']],
  activationAttempt: [['activationId','text'],['positiveAttemptNumber','int']], effect: [['activationId','text'],['attemptNumber','int'],['effectId','text']],
  executor: [['hostId','text'],['processIncarnationId','text'],['executorGeneration','long']], subjectContract: [['contractId','text'],['contractVersion','int']],
  subject: [['contractId','text'],['contractVersion','int'],['protectedSubjectIdentity','text']], lifecycleConsumer: [['consumerId','text'],['version','int'],['contractId','text'],['contractVersion','int']],
  lifecycleCheckpoint: [['consumerId','text'],['consumerVersion','int'],['contractId','text'],['contractVersion','int'],['protectedScopeIdentity','text']],
  retirementBarrier: [['contractId','text'],['contractVersion','int'],['protectedSubjectIdentity','text'],['epoch','long'],['incarnation','long']],
  textIndex: [['collectionId','text'],['indexId','text'],['indexVersion','int']], vectorIndex: [['collectionId','text'],['indexId','text'],['indexVersion','int']],
  searchRebuild: [['searchKind','text'],['collectionId','text'],['indexId','text'],['indexVersion','int'],['rebuildIdentity','text']],
  certificationReceipt: [['certificationKind','text'],['providerId','text'],['providerVersion','int'],['contractChecksum','checksum']],
  policy: [['policyId','text'],['version','int']], grant: [['grantId','text'],['version','int']], store: [['storeIdentity','text']],
  provider: [['storeIdentity','text'],['providerId','text'],['providerVersion','int']], schema: [['storeIdentity','text'],['schemaGeneration','long']],
  migration: [['storeIdentity','text'],['migrationId','text']], backup: [['storeIdentity','text'],['artifactId','text']],
  restore: [['storeIdentity','text'],['restoreRequestIdentity','text']], maintenance: [['storeIdentity','text'],['maintenanceKind','text'],['operationIdentity','text']],
  health: [['contributorId','text'],['entryId','text']], diagnostic: [['contributorId','text'],['entryId','text']],
  quarantineItem: [['quarantineKind','text'],['owningSubsystemId','text'],['quarantineIdentity','text']],
  graphDefinition: [['graphId','text'],['graphVersion','text']], graphExecution: [['graphId','text'],['graphVersion','text'],['executionId','text']],
  graphNode: [['graphId','text'],['graphVersion','text'],['executionId','text'],['nodeId','text']], graphChannel: [['graphId','text'],['graphVersion','text'],['executionId','text'],['channelId','text']],
  graphCheckpoint: [['graphId','text'],['graphVersion','text'],['executionId','text'],['checkpointId','text']]
});

export function isStudioResourceKind(value: unknown): value is StudioResourceKind {
  return typeof value === 'string' && (STUDIO_RESOURCE_KINDS as readonly string[]).includes(value);
}

/** Validates, checksums, and deeply owns one exact server-issued resource variant. */
export function validateStudioOutwardResource(value: StudioOutwardResourceAuthority): StudioOutwardResourceAuthority {
  if (!value || typeof value !== 'object' || !isStudioResourceKind(value.kind) || !validText(value.applicationId)) invalid();
  const shape = members[value.kind]; const keys = ['kind','applicationId',...shape.map(member => member[0]),'authorityChecksum'].sort();
  if (Object.keys(value).sort().some((key, index) => key !== keys[index]) || Object.keys(value).length !== keys.length) invalid();
  for (const [name, kind] of shape) if (!validMember(value[name], kind)) invalid();
  if (studioOutwardResourceChecksum(value) !== value.authorityChecksum) invalid();
  return Object.freeze(structuredClone(value));
}

/** Recomputes the exact Runtime resource-identity checksum. */
export function studioOutwardResourceChecksum(value: Omit<StudioOutwardResourceAuthority, 'authorityChecksum'>): StudioSha256 {
  if (!isStudioResourceKind(value.kind) || !validText(value.applicationId)) invalid(); const shape = members[value.kind];
  return studioSha256(studioCanonicalHash('base.studio.resource-identity.v1', writer => {
    writer.discriminator(STUDIO_RESOURCE_KINDS.indexOf(value.kind as StudioResourceKind) + 1); writer.string(value.applicationId as string); writer.count(shape.length);
    for (const [name, kind] of shape) { const member = value[name]; if (!validMember(member, kind)) invalid(); writer.string(name);
      writer.discriminator(kind === 'text' ? 1 : kind === 'int' ? 2 : kind === 'long' ? 3 : 4);
      if (kind === 'text') writer.string(member as string); else if (kind === 'int') writer.int32(member as number);
      else if (kind === 'long') writer.int64(member as string); else writer.checksum(member as string); }
  }));
}
function validMember(value: ResourceValue, kind: MemberKind): boolean { if (kind === 'int') return Number.isSafeInteger(value) && (value as number) > 0;
  if (kind === 'long') return typeof value === 'string' && /^(?:0|[1-9][0-9]{0,18})$/u.test(value);
  if (kind === 'checksum') return typeof value === 'string' && /^[a-f0-9]{64}$/u.test(value); return validText(value); }
function validText(value: unknown): value is string { return typeof value === 'string' && value.length > 0 && new TextEncoder().encode(value).length <= 512 && !/[\u0000-\u001f\u007f]/u.test(value); }
function invalid(): never { throw new TypeError('Studio outward resource authority is invalid.'); }
