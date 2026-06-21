/**
 * Unit tests for AcpReader — newline-delimited JSON-RPC stdin parser.
 *
 * What these tests cover:
 *   AcpReader wraps Node readline and calls onMessage for each valid JSON line,
 *   onError for malformed JSON, and ignores blank/whitespace lines.
 *
 * Test type: unit — uses in-memory PassThrough streams as stdin substitutes.
 * No network I/O, no HPD server.
 */

import { describe, it, expect, vi } from 'vitest';
import { PassThrough } from 'node:stream';
import { AcpReader } from '../../src/acp/reader.js';

function makeReader() {
  const stream = new PassThrough();
  const reader = new AcpReader(stream);
  return { stream, reader };
}

function sendLine(stream: PassThrough, line: string) {
  stream.write(line + '\n');
}

describe('AcpReader', () => {
  it('parses a valid JSON-RPC line and calls onMessage', async () => {
    const { stream, reader } = makeReader();
    const messages: unknown[] = [];
    reader.onMessage((m) => messages.push(m));

    sendLine(stream, '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}');
    await new Promise((r) => setTimeout(r, 10));

    expect(messages).toHaveLength(1);
    expect((messages[0] as any).method).toBe('initialize');
    expect((messages[0] as any).id).toBe(1);
  });

  it('ignores blank lines', async () => {
    const { stream, reader } = makeReader();
    const messages: unknown[] = [];
    reader.onMessage((m) => messages.push(m));

    stream.write('\n');
    stream.write('   \n');
    stream.write('\t\n');
    await new Promise((r) => setTimeout(r, 10));

    expect(messages).toHaveLength(0);
  });

  it('calls onError for malformed JSON', async () => {
    const { stream, reader } = makeReader();
    const messages: unknown[] = [];
    const errors: Error[] = [];
    reader.onMessage((m) => messages.push(m));
    reader.onError((e) => errors.push(e));

    sendLine(stream, 'not-valid-json');
    await new Promise((r) => setTimeout(r, 10));

    expect(messages).toHaveLength(0);
    expect(errors).toHaveLength(1);
    expect(errors[0]).toBeInstanceOf(Error);
  });

  it('parses multiple sequential lines in order', async () => {
    const { stream, reader } = makeReader();
    const methods: string[] = [];
    reader.onMessage((m) => methods.push((m as any).method ?? 'response'));

    sendLine(stream, '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}');
    sendLine(stream, '{"jsonrpc":"2.0","id":2,"method":"session/new","params":{"cwd":"/"}}');
    sendLine(stream, '{"jsonrpc":"2.0","id":3,"result":{"sessionId":"s1"}}');
    await new Promise((r) => setTimeout(r, 10));

    expect(methods).toEqual(['initialize', 'session/new', 'response']);
  });

  it('truncates long lines in error messages to 200 chars', async () => {
    const { stream, reader } = makeReader();
    const errors: Error[] = [];
    reader.onError((e) => errors.push(e));

    const longGarbage = 'x'.repeat(500);
    sendLine(stream, longGarbage);
    await new Promise((r) => setTimeout(r, 10));

    expect(errors).toHaveLength(1);
    // The error message includes a substring of the bad line — capped at 200 chars
    const msgLen = errors[0]!.message.length;
    expect(msgLen).toBeLessThanOrEqual(300); // message prefix + 200 chars of content
  });

  it('does not call onMessage when no handler is registered', async () => {
    const { stream } = makeReader();
    // No onMessage registered — should not throw
    sendLine(stream, '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}');
    await new Promise((r) => setTimeout(r, 10));
    // If we get here without throwing, the test passes
    expect(true).toBe(true);
  });

  it('does not call onMessage for a line without a trailing newline (partial)', async () => {
    const { stream, reader } = makeReader();
    const messages: unknown[] = [];
    reader.onMessage((m) => messages.push(m));

    // Write without newline — readline won't emit until newline or close
    stream.write('{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}');
    await new Promise((r) => setTimeout(r, 10));

    expect(messages).toHaveLength(0);
  });
});
