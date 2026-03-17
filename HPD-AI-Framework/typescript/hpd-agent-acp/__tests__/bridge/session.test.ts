/**
 * Unit tests for bridge/session.ts — ACP session state registry.
 *
 * What these tests cover:
 *   SessionRegistry.create/get/delete lifecycle, resolveOutboundRequest routing
 *   (permissions and client tools), cancelAll (abort + reject all pending
 *   operations), and multi-session lookup correctness.
 *
 * Test type: unit — no I/O, no network, all in-memory.
 */

import { describe, it, expect } from 'vitest';
import { SessionRegistry } from '../../src/bridge/session.js';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeRegistry() {
  return new SessionRegistry();
}

function makeSession(registry: SessionRegistry, id = 'hpd-1') {
  return registry.create(id, 'branch-1', '/workspace');
}

// Register a fake pending permission and return { key, resolve, reject, promise }
function addPendingPermission(registry: SessionRegistry, sessionId: string, key: string) {
  let resolve!: (v: { approved: boolean; optionId?: string }) => void;
  let reject!: (e: Error) => void;
  const promise = new Promise<{ approved: boolean; optionId?: string }>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  const session = registry.get(sessionId)!;
  session.pendingPermissions.set(key, { resolve, reject });
  return { promise };
}

// Register a fake pending client tool and return { key, resolve, reject, promise }
function addPendingClientTool(registry: SessionRegistry, sessionId: string, key: string) {
  let resolve!: (v: { success: boolean; content: string }) => void;
  let reject!: (e: Error) => void;
  const promise = new Promise<{ success: boolean; content: string }>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  const session = registry.get(sessionId)!;
  session.pendingClientTools.set(key, { resolve, reject } as any);
  return { promise };
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('SessionRegistry', () => {
  // ── create ───────────────────────────────────────────────────────────────

  it('create — acpSessionId equals hpdSessionId', () => {
    const registry = makeRegistry();
    const state = makeSession(registry);

    expect(state.acpSessionId).toBe('hpd-1');
    expect(state.hpdSessionId).toBe('hpd-1');
  });

  it('create — initialises all fields to empty/null', () => {
    const registry = makeRegistry();
    const state = makeSession(registry);

    expect(state.hpdBranchId).toBe('branch-1');
    expect(state.cwd).toBe('/workspace');
    expect(state.pendingPromptRequestId).toBeNull();
    expect(state.streamAbort).toBeNull();
    expect(state.pendingPermissions.size).toBe(0);
    expect(state.pendingClientTools.size).toBe(0);
    expect(state.pendingClarification).toBeNull();
  });

  // ── get ──────────────────────────────────────────────────────────────────

  it('get — returns the session after create', () => {
    const registry = makeRegistry();
    const state = makeSession(registry);

    expect(registry.get('hpd-1')).toBe(state);
  });

  it('get — returns undefined for unknown id', () => {
    const registry = makeRegistry();

    expect(registry.get('nope')).toBeUndefined();
  });

  // ── delete ───────────────────────────────────────────────────────────────

  it('delete — removes the session', () => {
    const registry = makeRegistry();
    makeSession(registry);
    registry.delete('hpd-1');

    expect(registry.get('hpd-1')).toBeUndefined();
  });

  it('delete — no-op for unknown id', () => {
    const registry = makeRegistry();
    expect(() => registry.delete('nope')).not.toThrow();
  });

  // ── resolveOutboundRequest — permissions ─────────────────────────────────

  it('resolveOutboundRequest — resolves a pending permission', async () => {
    const registry = makeRegistry();
    makeSession(registry);
    const { promise } = addPendingPermission(registry, 'hpd-1', '42');

    const resolved = registry.resolveOutboundRequest(42, {
      outcome: { outcome: 'selected', optionId: 'allow_once' },
    });

    expect(resolved).toBe(true);
    const result = await promise;
    expect(result.approved).toBe(true);
    expect(result.optionId).toBe('allow_once');
  });

  it('resolveOutboundRequest — rejects a pending permission on error', async () => {
    const registry = makeRegistry();
    makeSession(registry);
    const { promise } = addPendingPermission(registry, 'hpd-1', '99');

    registry.resolveOutboundRequest(99, undefined, { code: -32603, message: 'fail' });

    await expect(promise).rejects.toThrow('-32603: fail');
  });

  it('resolveOutboundRequest — removes entry after resolution', async () => {
    const registry = makeRegistry();
    makeSession(registry);
    addPendingPermission(registry, 'hpd-1', '1');

    registry.resolveOutboundRequest(1, { outcome: { outcome: 'cancelled' } });

    expect(registry.get('hpd-1')!.pendingPermissions.size).toBe(0);
  });

  // ── resolveOutboundRequest — client tools ────────────────────────────────

  it('resolveOutboundRequest — resolves a pending client tool', async () => {
    const registry = makeRegistry();
    makeSession(registry);
    const { promise } = addPendingClientTool(registry, 'hpd-1', '7');

    registry.resolveOutboundRequest(7, { success: true, content: 'data' });

    const result = await promise;
    expect(result.content).toBe('data');
  });

  it('resolveOutboundRequest — rejects a pending client tool on error', async () => {
    const registry = makeRegistry();
    makeSession(registry);
    const { promise } = addPendingClientTool(registry, 'hpd-1', '8');

    registry.resolveOutboundRequest(8, undefined, { code: -1, message: 'editor error' });

    await expect(promise).rejects.toThrow('editor error');
  });

  it('resolveOutboundRequest — removes client tool entry after resolution', async () => {
    const registry = makeRegistry();
    makeSession(registry);
    addPendingClientTool(registry, 'hpd-1', '5');

    registry.resolveOutboundRequest(5, { success: true, content: '' });

    expect(registry.get('hpd-1')!.pendingClientTools.size).toBe(0);
  });

  it('resolveOutboundRequest — returns false for unknown id', () => {
    const registry = makeRegistry();
    makeSession(registry);

    expect(registry.resolveOutboundRequest(999, {})).toBe(false);
  });

  // ── resolveOutboundRequest — multi-session ───────────────────────────────

  it('resolveOutboundRequest — finds request registered on the second session', async () => {
    const registry = makeRegistry();
    registry.create('hpd-1', 'b1', '/a');
    registry.create('hpd-2', 'b2', '/b');
    const { promise } = addPendingClientTool(registry, 'hpd-2', '20');

    registry.resolveOutboundRequest(20, { success: true, content: 'from-second' });

    const result = await promise;
    expect(result.content).toBe('from-second');
  });

  // ── cancelAll ────────────────────────────────────────────────────────────

  it('cancelAll — aborts the streamAbort controller', () => {
    const registry = makeRegistry();
    const state = makeSession(registry);
    state.streamAbort = new AbortController();

    registry.cancelAll('hpd-1');

    expect(state.streamAbort).toBeNull(); // cleared by cancelAll
  });

  it('cancelAll — the abort signal was fired before clearing', () => {
    const registry = makeRegistry();
    const state = makeSession(registry);
    const ctrl = new AbortController();
    state.streamAbort = ctrl;

    registry.cancelAll('hpd-1');

    expect(ctrl.signal.aborted).toBe(true);
  });

  it('cancelAll — rejects all pending permissions', async () => {
    const registry = makeRegistry();
    makeSession(registry);
    const { promise: p1 } = addPendingPermission(registry, 'hpd-1', '1');
    const { promise: p2 } = addPendingPermission(registry, 'hpd-1', '2');

    registry.cancelAll('hpd-1');

    await expect(p1).rejects.toThrow('Session cancelled');
    await expect(p2).rejects.toThrow('Session cancelled');
  });

  it('cancelAll — rejects all pending client tools', async () => {
    const registry = makeRegistry();
    makeSession(registry);
    const { promise: p1 } = addPendingClientTool(registry, 'hpd-1', '3');
    const { promise: p2 } = addPendingClientTool(registry, 'hpd-1', '4');

    registry.cancelAll('hpd-1');

    await expect(p1).rejects.toThrow('Session cancelled');
    await expect(p2).rejects.toThrow('Session cancelled');
  });

  it('cancelAll — rejects pending clarification', async () => {
    const registry = makeRegistry();
    const state = makeSession(registry);
    let rejectCalled = false;
    state.pendingClarification = {
      hpdRequestId: 'r1',
      resolve: () => {},
      reject: () => { rejectCalled = true; },
    };

    registry.cancelAll('hpd-1');

    expect(rejectCalled).toBe(true);
    expect(state.pendingClarification).toBeNull();
  });

  it('cancelAll — clears all maps and nulls state', () => {
    const registry = makeRegistry();
    const state = makeSession(registry);
    addPendingPermission(registry, 'hpd-1', '10');
    addPendingClientTool(registry, 'hpd-1', '11');

    // suppress unhandled rejection warnings from the rejected promises
    registry.get('hpd-1')!.pendingPermissions.get('10')!.reject = () => {};
    registry.get('hpd-1')!.pendingClientTools.get('11')!.reject = () => {};

    registry.cancelAll('hpd-1');

    expect(state.pendingPermissions.size).toBe(0);
    expect(state.pendingClientTools.size).toBe(0);
    expect(state.pendingClarification).toBeNull();
    expect(state.streamAbort).toBeNull();
  });

  it('cancelAll — no-op for unknown session id', () => {
    const registry = makeRegistry();
    expect(() => registry.cancelAll('nope')).not.toThrow();
  });
});
