/**
 * Unit tests for bridge/clarification.ts — CLARIFICATION_REQUEST handling.
 *
 * What these tests cover:
 *   handleClarificationRequest emits the question as an agent_message_chunk with
 *   hpd.dev _meta extension and parks the promise resolver on the session.
 *   tryResolveClarification routes the next session/prompt as the answer,
 *   resolves the promise, clears pendingClarification, and returns true.
 *   When no clarification is pending, tryResolveClarification returns false.
 *
 * Test type: unit — AcpWriter replaced by a mock that captures calls;
 * no network or HPD server involved.
 */

import { describe, it, expect, vi } from 'vitest';
import { PassThrough } from 'node:stream';
import { handleClarificationRequest, tryResolveClarification } from '../../src/bridge/clarification.js';
import { AcpWriter } from '../../src/acp/writer.js';
import { SessionRegistry } from '../../src/bridge/session.js';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeWriter() {
  const out = new PassThrough();
  return new AcpWriter(out);
}

function makeSession() {
  const registry = new SessionRegistry();
  return registry.create('hpd-1', 'branch-1', '/cwd');
}

function makeClarificationEvent(overrides?: Partial<{ question: string; requestId: string; options: string[] }>) {
  return {
    type: 'CLARIFICATION_REQUEST' as const,
    requestId: overrides?.requestId ?? 'clr-1',
    question: overrides?.question ?? 'Which approach?',
    options: overrides?.options ?? ['Option A', 'Option B'],
    version: '1.0',
  };
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('handleClarificationRequest', () => {
  it('emits agent_message_chunk with the question text', () => {
    const writer = makeWriter();
    const session = makeSession();
    const spy = vi.spyOn(writer, 'notifySessionUpdate');

    handleClarificationRequest(makeClarificationEvent({ question: 'Which approach?' }), session, writer);

    expect(spy).toHaveBeenCalledOnce();
    const update = spy.mock.calls[0]![1] as any;
    expect(update.sessionUpdate).toBe('agent_message_chunk');
    expect(update.content.text).toBe('Which approach?');
  });

  it('includes hpd.dev _meta extension with clarification_request type', () => {
    const writer = makeWriter();
    const session = makeSession();
    const spy = vi.spyOn(writer, 'notifySessionUpdate');

    handleClarificationRequest(makeClarificationEvent({ requestId: 'clr-42' }), session, writer);

    const update = spy.mock.calls[0]![1] as any;
    const meta = update._meta?.['hpd.dev'];
    expect(meta?.type).toBe('clarification_request');
    expect(meta?.requestId).toBe('clr-42');
  });

  it('includes options array in the _meta extension', () => {
    const writer = makeWriter();
    const session = makeSession();
    const spy = vi.spyOn(writer, 'notifySessionUpdate');

    handleClarificationRequest(
      makeClarificationEvent({ options: ['A', 'B', 'C'] }),
      session,
      writer,
    );

    const update = spy.mock.calls[0]![1] as any;
    expect(update._meta?.['hpd.dev']?.options).toEqual(['A', 'B', 'C']);
  });

  it('sets pendingClarification on the session', () => {
    const writer = makeWriter();
    const session = makeSession();

    handleClarificationRequest(makeClarificationEvent({ requestId: 'clr-1' }), session, writer);

    expect(session.pendingClarification).not.toBeNull();
    expect(session.pendingClarification!.hpdRequestId).toBe('clr-1');
  });

  it('notifies with the correct sessionId', () => {
    const writer = makeWriter();
    const session = makeSession();
    const spy = vi.spyOn(writer, 'notifySessionUpdate');

    handleClarificationRequest(makeClarificationEvent(), session, writer);

    expect(spy.mock.calls[0]![0]).toBe('hpd-1');
  });
});

describe('tryResolveClarification', () => {
  it('returns true and resolves the promise when a clarification is pending', async () => {
    const writer = makeWriter();
    const session = makeSession();

    const promise = handleClarificationRequest(makeClarificationEvent(), session, writer);
    const resolved = tryResolveClarification(session, 'my answer');

    expect(resolved).toBe(true);
    const answer = await promise;
    expect(answer).toBe('my answer');
  });

  it('clears pendingClarification after resolving', async () => {
    const writer = makeWriter();
    const session = makeSession();

    const promise = handleClarificationRequest(makeClarificationEvent(), session, writer);
    tryResolveClarification(session, 'answer');
    await promise;

    expect(session.pendingClarification).toBeNull();
  });

  it('returns false when no clarification is pending', () => {
    const session = makeSession();

    const resolved = tryResolveClarification(session, 'anything');

    expect(resolved).toBe(false);
  });

  it('does not call resolve when no clarification is pending', () => {
    const session = makeSession();
    // pendingClarification is null by default — should not throw
    expect(() => tryResolveClarification(session, 'text')).not.toThrow();
  });
});
