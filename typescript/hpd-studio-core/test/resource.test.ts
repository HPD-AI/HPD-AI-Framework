import assert from 'node:assert/strict';
import test from 'node:test';
import { studioSha256 } from '../src/module-abi.ts';
import { studioOutwardResourceChecksum, validateStudioOutwardResource } from '../src/resource.ts';

const installedCollectionChecksum = studioSha256('a'.repeat(64));

test('installed collection authority is mandatory and checksum-bound', () => {
  const base = { kind: 'collection' as const, applicationId: 'sample.application', collectionId: 'users', installedCollectionChecksum };
  const resource = { ...base, authorityChecksum: studioOutwardResourceChecksum(base) };
  assert.deepEqual(validateStudioOutwardResource(resource), resource);
  assert.throws(() => validateStudioOutwardResource({ ...resource, collectionId: 'roles' }));
  assert.throws(() => validateStudioOutwardResource({ ...resource, installedCollectionChecksum: studioSha256('b'.repeat(64)) }));
  assert.throws(() => validateStudioOutwardResource({ ...resource, collectionVersion: 1 } as never));
});

test('record authority binds collection installation and record identity', () => {
  const base = { kind: 'record' as const, applicationId: 'sample.application', collectionId: 'users', installedCollectionChecksum, recordId: 'user-1' };
  const resource = { ...base, authorityChecksum: studioOutwardResourceChecksum(base) };
  assert.deepEqual(validateStudioOutwardResource(resource), resource);
  assert.throws(() => validateStudioOutwardResource({ ...resource, recordId: 'user-2' }));
});

test('graph authority binds the canonical string graph version', () => {
  const base = { kind: 'graphExecution' as const, applicationId: 'sample.application', graphId: 'checkout', graphVersion: '2026.08', executionId: 'run-1' };
  const resource = { ...base, authorityChecksum: studioOutwardResourceChecksum(base) };
  assert.deepEqual(validateStudioOutwardResource(resource), resource);
  assert.throws(() => validateStudioOutwardResource({ ...resource, graphVersion: '2026.09' }));
  assert.throws(() => validateStudioOutwardResource({ ...resource, version: 1 } as never));
});
