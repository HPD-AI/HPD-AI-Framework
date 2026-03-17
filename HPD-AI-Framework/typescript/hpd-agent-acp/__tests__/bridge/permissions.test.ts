/**
 * Unit tests for bridge/permissions.ts — PERMISSION_REQUEST → ACP round-trip.
 *
 * What these tests cover:
 *   handlePermissionRequest sends session/request_permission to the editor via
 *   AcpWriter, registers a pending resolver, then resolves the HPD PermissionResponse
 *   promise based on the editor's outcome. ACP option IDs are mapped to HPD choice values.
 *
 * Test type: unit — AcpWriter replaced by a mock that captures calls;
 * no network or HPD server involved.
 */

import { describe, it, expect, vi } from 'vitest';
import { PassThrough } from 'node:stream';
import { handlePermissionRequest } from '../../src/bridge/permissions.js';
import { AcpWriter } from '../../src/acp/writer.js';
import { SessionRegistry } from '../../src/bridge/session.js';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeWriter() {
  const out = new PassThrough();
  const writer = new AcpWriter(out);
  return writer;
}

function makeSession() {
  const registry = new SessionRegistry();
  return registry.create('hpd-1', 'branch-1', '/cwd');
}

function makePermissionEvent(functionName = 'read_file') {
  return {
    type: 'PERMISSION_REQUEST' as const,
    permissionId: 'perm-1',
    callId: 'call-1',
    functionName,
    sourceName: 'agent',
    description: 'Read a file',
    arguments: { path: '/foo.ts' },
    version: '1.0',
  };
}

/** Simulate the editor responding to an outbound request. */
function simulateEditorResponse(
  session: ReturnType<typeof makeSession>,
  outboundId: number,
  outcome: { outcome: string; optionId?: string },
) {
  const registry = new SessionRegistry();
  // Use the session's pendingPermissions map directly
  const key = String(outboundId);
  const pending = session.pendingPermissions.get(key);
  if (!pending) throw new Error(`No pending permission for key ${key}`);
  pending.resolve({ approved: outcome.outcome === 'selected', optionId: outcome.optionId });
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('handlePermissionRequest', () => {
  it('sends session/request_permission to the editor', async () => {
    const writer = makeWriter();
    const session = makeSession();
    const spy = vi.spyOn(writer, 'requestPermission');

    const promise = handlePermissionRequest(makePermissionEvent(), session, writer);

    expect(spy).toHaveBeenCalledOnce();
    expect(spy.mock.calls[0]![0]).toBe('hpd-1'); // sessionId

    // Prevent unhandled rejection — resolve the promise
    const outboundId = spy.mock.results[0]!.value as number;
    session.pendingPermissions.get(String(outboundId))!.resolve({ approved: true });
    await promise;
  });

  it('registers the pending permission in session.pendingPermissions', () => {
    const writer = makeWriter();
    const session = makeSession();
    const spy = vi.spyOn(writer, 'requestPermission');

    handlePermissionRequest(makePermissionEvent(), session, writer).catch(() => {});

    const outboundId = spy.mock.results[0]!.value as number;
    expect(session.pendingPermissions.has(String(outboundId))).toBe(true);
  });

  it('outbound id from writer.requestPermission is the key in pendingPermissions', () => {
    const writer = makeWriter();
    const session = makeSession();
    const spy = vi.spyOn(writer, 'requestPermission');

    handlePermissionRequest(makePermissionEvent(), session, writer).catch(() => {});

    const returnedId = spy.mock.results[0]!.value as number;
    expect(session.pendingPermissions.has(String(returnedId))).toBe(true);
  });

  it('allow_once → HPD approved=true, choice=ask', async () => {
    const writer = makeWriter();
    const session = makeSession();
    const spy = vi.spyOn(writer, 'requestPermission');

    const promise = handlePermissionRequest(makePermissionEvent(), session, writer);
    const id = spy.mock.results[0]!.value as number;
    session.pendingPermissions.get(String(id))!.resolve({ approved: true, optionId: 'allow_once' });

    const result = await promise;
    expect(result.approved).toBe(true);
    expect(result.choice).toBe('ask');
  });

  it('allow_always → HPD approved=true, choice=allow_always', async () => {
    const writer = makeWriter();
    const session = makeSession();
    const spy = vi.spyOn(writer, 'requestPermission');

    const promise = handlePermissionRequest(makePermissionEvent(), session, writer);
    const id = spy.mock.results[0]!.value as number;
    session.pendingPermissions.get(String(id))!.resolve({ approved: true, optionId: 'allow_always' });

    const result = await promise;
    expect(result.approved).toBe(true);
    expect(result.choice).toBe('allow_always');
  });

  it('reject_once → HPD approved=false, choice=deny_always', async () => {
    const writer = makeWriter();
    const session = makeSession();
    const spy = vi.spyOn(writer, 'requestPermission');

    const promise = handlePermissionRequest(makePermissionEvent(), session, writer);
    const id = spy.mock.results[0]!.value as number;
    session.pendingPermissions.get(String(id))!.resolve({ approved: false, optionId: 'reject_once' });

    const result = await promise;
    expect(result.approved).toBe(false);
    expect(result.choice).toBe('deny_always');
  });

  it('cancelled outcome → HPD approved=false', async () => {
    const writer = makeWriter();
    const session = makeSession();
    const spy = vi.spyOn(writer, 'requestPermission');

    const promise = handlePermissionRequest(makePermissionEvent(), session, writer);
    const id = spy.mock.results[0]!.value as number;
    // cancelled: approved=false, no optionId
    session.pendingPermissions.get(String(id))!.resolve({ approved: false });

    const result = await promise;
    expect(result.approved).toBe(false);
  });

  it('tool kind derived from function name — read_file → kind=read', () => {
    const writer = makeWriter();
    const session = makeSession();
    const spy = vi.spyOn(writer, 'requestPermission');

    handlePermissionRequest(makePermissionEvent('read_file'), session, writer).catch(() => {});

    const toolCall = spy.mock.calls[0]![1] as any;
    expect(toolCall.kind).toBe('read');
  });

  it('tool kind derived from function name — bash → kind=execute', () => {
    const writer = makeWriter();
    const session = makeSession();
    const spy = vi.spyOn(writer, 'requestPermission');

    handlePermissionRequest(makePermissionEvent('bash'), session, writer).catch(() => {});

    const toolCall = spy.mock.calls[0]![1] as any;
    expect(toolCall.kind).toBe('execute');
  });
});
