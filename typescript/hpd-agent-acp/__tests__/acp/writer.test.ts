/**
 * Unit tests for AcpWriter — newline-delimited JSON-RPC stdout serializer.
 *
 * What these tests cover:
 *   AcpWriter emits well-formed JSON-RPC frames (responses, errors, notifications,
 *   and agent→client requests) to a writable stream. Each write is one JSON line
 *   terminated by \n. Request IDs auto-increment and are returned by the sender.
 *
 * Test type: unit — uses an in-memory PassThrough stream as stdout substitute.
 * No network I/O, no HPD server.
 */

import { describe, it, expect } from 'vitest';
import { PassThrough } from 'node:stream';
import { AcpWriter } from '../../src/acp/writer.js';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeWriter() {
  const out = new PassThrough();
  const writer = new AcpWriter(out);
  const lines: unknown[] = [];

  out.on('data', (chunk: Buffer) => {
    const raw = chunk.toString();
    for (const line of raw.split('\n')) {
      const trimmed = line.trim();
      if (trimmed) lines.push(JSON.parse(trimmed));
    }
  });

  return { writer, lines };
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('AcpWriter', () => {
  it('respond — emits a valid JSON-RPC response frame', () => {
    const { writer, lines } = makeWriter();
    writer.respond(1, { foo: 'bar' });

    expect(lines).toHaveLength(1);
    expect(lines[0]).toEqual({ jsonrpc: '2.0', id: 1, result: { foo: 'bar' } });
  });

  it('respondError — emits a JSON-RPC error frame', () => {
    const { writer, lines } = makeWriter();
    writer.respondError(2, -32601, 'Method not found');

    expect(lines).toHaveLength(1);
    expect(lines[0]).toEqual({
      jsonrpc: '2.0',
      id: 2,
      error: { code: -32601, message: 'Method not found' },
    });
  });

  it('respondInitialize — emits response with agentCapabilities', () => {
    const { writer, lines } = makeWriter();
    writer.respondInitialize(1, {
      protocolVersion: 1,
      agentCapabilities: { loadSession: true },
    });

    const msg = lines[0] as any;
    expect(msg.id).toBe(1);
    expect(msg.result.agentCapabilities.loadSession).toBe(true);
  });

  it('respondAuthenticate — emits response with empty result object', () => {
    const { writer, lines } = makeWriter();
    writer.respondAuthenticate(3);

    expect(lines[0]).toEqual({ jsonrpc: '2.0', id: 3, result: {} });
  });

  it('respondSessionNew — emits sessionId in result', () => {
    const { writer, lines } = makeWriter();
    writer.respondSessionNew(4, { sessionId: 'sess-1' });

    expect((lines[0] as any).result.sessionId).toBe('sess-1');
  });

  it('respondSessionLoad — emits null result', () => {
    const { writer, lines } = makeWriter();
    writer.respondSessionLoad(5);

    expect((lines[0] as any).result).toBeNull();
  });

  it('respondSessionPrompt — emits stopReason in result', () => {
    const { writer, lines } = makeWriter();
    writer.respondSessionPrompt(6, 'end_turn');

    expect((lines[0] as any).result.stopReason).toBe('end_turn');
  });

  it('respondSessionSetMode — emits null result', () => {
    const { writer, lines } = makeWriter();
    writer.respondSessionSetMode(7);

    expect((lines[0] as any).result).toBeNull();
  });

  it('notifySessionUpdate — emits notification with no id field', () => {
    const { writer, lines } = makeWriter();
    writer.notifySessionUpdate('sid-1', {
      sessionUpdate: 'agent_message_chunk',
      content: { type: 'text', text: 'hello' },
    });

    const msg = lines[0] as any;
    expect(msg.method).toBe('session/update');
    expect(msg.id).toBeUndefined();
    expect(msg.params.sessionId).toBe('sid-1');
    expect(msg.params.update.sessionUpdate).toBe('agent_message_chunk');
    expect(msg.params.update.content.text).toBe('hello');
  });

  it('requestPermission — returns incrementing id, emits correct method', () => {
    const { writer, lines } = makeWriter();
    const id1 = writer.requestPermission('sid', { toolCallId: 'tc-1', status: 'pending' }, []);
    const id2 = writer.requestPermission('sid', { toolCallId: 'tc-2', status: 'pending' }, []);

    expect(id1).toBe(1);
    expect(id2).toBe(2);
    expect((lines[0] as any).method).toBe('session/request_permission');
    expect((lines[1] as any).method).toBe('session/request_permission');
    expect((lines[0] as any).id).toBe(1);
    expect((lines[1] as any).id).toBe(2);
  });

  it('requestFsRead — emits correct method with optional line and limit', () => {
    const { writer, lines } = makeWriter();
    writer.requestFsRead('sid', '/foo.ts', 10, 50);

    const msg = lines[0] as any;
    expect(msg.method).toBe('fs/read_text_file');
    expect(msg.params.path).toBe('/foo.ts');
    expect(msg.params.line).toBe(10);
    expect(msg.params.limit).toBe(50);
  });

  it('requestFsRead — omits line and limit when not provided', () => {
    const { writer, lines } = makeWriter();
    writer.requestFsRead('sid', '/bar.ts');

    const msg = lines[0] as any;
    expect(msg.params.line).toBeUndefined();
    expect(msg.params.limit).toBeUndefined();
  });

  it('requestFsWrite — emits correct method, path, and content', () => {
    const { writer, lines } = makeWriter();
    writer.requestFsWrite('sid', '/out.ts', 'file content');

    const msg = lines[0] as any;
    expect(msg.method).toBe('fs/write_text_file');
    expect(msg.params.path).toBe('/out.ts');
    expect(msg.params.content).toBe('file content');
  });

  it('requestTerminalCreate — passes cwd through params', () => {
    const { writer, lines } = makeWriter();
    writer.requestTerminalCreate('sid', 'npm test', undefined, undefined, '/project');

    const msg = lines[0] as any;
    expect(msg.method).toBe('terminal/create');
    expect(msg.params.command).toBe('npm test');
    expect(msg.params.cwd).toBe('/project');
  });

  it('each message is terminated by a newline', () => {
    const out = new PassThrough();
    const writer = new AcpWriter(out);
    const chunks: string[] = [];
    out.on('data', (c: Buffer) => chunks.push(c.toString()));

    writer.respond(1, {});

    expect(chunks.join('')).toMatch(/\n$/);
  });

  it('request counter is independent per AcpWriter instance', () => {
    const { writer: w1 } = makeWriter();
    const { writer: w2 } = makeWriter();

    expect(w1.requestPermission('sid', { toolCallId: 'a', status: 'pending' })).toBe(1);
    expect(w1.requestPermission('sid', { toolCallId: 'b', status: 'pending' })).toBe(2);
    expect(w2.requestPermission('sid', { toolCallId: 'c', status: 'pending' })).toBe(1);
  });
});
