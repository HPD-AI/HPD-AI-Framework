/**
 * Unit tests for AgentClient.uploadContent() and runConfig threading.
 *
 * What these tests cover:
 *   1. uploadContent() — AgentHttpApi via AgentClient: correct URL, method, multipart body, return value,
 *      error handling.
 *   2. runConfig threading: USER_MESSAGES_INPUT.runConfig is forwarded in the event envelope.
 *   3. SseTransport: runConfig included/omitted from POST body based on input events.
 *
 * Test type: unit — all network I/O is replaced by vi.spyOn(globalThis, 'fetch').
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { AgentClient } from '../src/client.js';
import { SseTransport } from '../src/transports/sse.js';
import { EventTypes } from '../src/types/events.js';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const BASE = 'http://localhost:5135';

const CONTENT_REFERENCE = {
  contentId: 'content-abc-123',
  version: 'rev:test',
  contentType: 'image/png',
  name: 'screenshot.png',
  sizeBytes: 4096,
};

function mockFetchJson(body: unknown, status = 200) {
  vi.spyOn(globalThis, 'fetch').mockResolvedValue({
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
    text: async () => JSON.stringify(body),
  } as Response);
}

function makeFile(name = 'test.png', type = 'image/png'): File {
  return new File(['fake-content'], name, { type });
}

function makeBlob(type = 'application/octet-stream'): Blob {
  return new Blob(['fake-content'], { type });
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('AgentClient.uploadContent() — AgentHttpApi', () => {
  let client: AgentClient;

  beforeEach(() => {
    vi.resetAllMocks();
    client = new AgentClient(BASE);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('calls POST /sessions/{sid}/threads/{bid}/content', async () => {
    const spy = mockFetchJson(CONTENT_REFERENCE, 200);
    await client.uploadContent('sess-1', 'main', makeFile());

    expect(vi.mocked(fetch)).toHaveBeenCalledOnce();
    const [url, init] = vi.mocked(fetch).mock.calls[0];
    expect(String(url)).toBe(`${BASE}/sessions/sess-1/threads/main/content`);
    expect(init?.method).toBe('POST');
  });

  it('sends a FormData body (no Content-Type header — browser sets boundary)', async () => {
    mockFetchJson(CONTENT_REFERENCE);
    await client.uploadContent('sess-1', 'main', makeFile());

    const [, init] = vi.mocked(fetch).mock.calls[0];
    expect(init?.body).toBeInstanceOf(FormData);
    // Content-Type must NOT be set manually — the browser sets it with the boundary
    const headers = init?.headers as Record<string, string> | undefined;
    expect(headers?.['Content-Type']).toBeUndefined();
  });

  it('returns the parsed ContentReference from the response', async () => {
    mockFetchJson(CONTENT_REFERENCE);
    const result = await client.uploadContent('sess-1', 'main', makeFile());

    expect(result).toEqual(CONTENT_REFERENCE);
  });

  it('uses the File name as the form field filename by default', async () => {
    mockFetchJson(CONTENT_REFERENCE);
    const file = makeFile('my-screenshot.png');
    await client.uploadContent('sess-1', 'main', file);

    const [, init] = vi.mocked(fetch).mock.calls[0];
    const form = init?.body as FormData;
    const entry = form.get('file') as File;
    expect(entry.name).toBe('my-screenshot.png');
  });

  it('uses "upload" as filename for a plain Blob', async () => {
    mockFetchJson(CONTENT_REFERENCE);
    await client.uploadContent('sess-1', 'main', makeBlob());

    const [, init] = vi.mocked(fetch).mock.calls[0];
    const form = init?.body as FormData;
    const entry = form.get('file') as File;
    expect(entry.name).toBe('upload');
  });

  it('uses the name param to override the filename', async () => {
    mockFetchJson(CONTENT_REFERENCE);
    await client.uploadContent('sess-1', 'main', makeFile('original.png'), 'override.png');

    const [, init] = vi.mocked(fetch).mock.calls[0];
    const form = init?.body as FormData;
    const entry = form.get('file') as File;
    expect(entry.name).toBe('override.png');
  });

  it('throws with HTTP status in the message on non-2xx response', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: false,
      status: 413,
      json: async () => null,
      text: async () => 'Payload Too Large',
    } as Response);

    await expect(client.uploadContent('sess-1', 'main', makeFile())).rejects.toThrow('413');
  });
});

describe('AgentClient.submitInput() — runConfig threading', () => {
  let client: AgentClient;

  beforeEach(() => {
    vi.resetAllMocks();
    client = new AgentClient(BASE);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('forwards runConfig from the input event to the POST body', async () => {
    // Mock the stream response — we only care about what was sent
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      status: 200,
      body: new ReadableStream({ start(c) { c.close(); } }),
      headers: new Headers({ 'content-type': 'text/event-stream' }),
      text: async () => '',
      json: async () => ({}),
    } as unknown as Response);

    const runConfig = { providerKey: 'anthropic', modelId: 'claude-sonnet-4-6', chat: { temperature: 0.7 } };
    const signal = new AbortController().signal;

    await client.submitInput({
      type: EventTypes.USER_MESSAGES_INPUT,
      sessionId: 'sess-1',
      agentId: 'agent-1',
      threadId: 'main',
      messages: [{
        role: 'user',
        contents: [{ $type: 'text', text: 'hi' }],
      }],
      runConfig,
    }, { signal }).catch(() => {});

    const [, init] = vi.mocked(fetch).mock.calls[0];
    const body = JSON.parse(init?.body as string);
    expect(body.runConfig).toEqual(runConfig);
  });

  it('omits runConfig from POST body when not provided on the input event', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      status: 200,
      body: new ReadableStream({ start(c) { c.close(); } }),
      headers: new Headers({ 'content-type': 'text/event-stream' }),
      text: async () => '',
      json: async () => ({}),
    } as unknown as Response);

    const signal = new AbortController().signal;
    await client.submitInput({
      type: EventTypes.USER_MESSAGES_INPUT,
      sessionId: 'sess-1',
      agentId: 'agent-1',
      threadId: 'main',
      messages: [{
        role: 'user',
        contents: [{ $type: 'text', text: 'hi' }],
      }],
    }, { signal }).catch(() => {});

    const [, init] = vi.mocked(fetch).mock.calls[0];
    const body = JSON.parse(init?.body as string);
    expect(body.runConfig).toBeUndefined();
    expect('runConfig' in body).toBe(false);
  });
});

// ---------------------------------------------------------------------------

describe('SseTransport — submitInput runConfig in POST body', () => {
  beforeEach(() => vi.resetAllMocks());
  afterEach(() => vi.restoreAllMocks());

  it('includes runConfig key when provided on the input event', async () => {
    const transport = new SseTransport(BASE);

    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      status: 200,
      body: new ReadableStream({ start(c) { c.close(); } }),
      headers: new Headers({ 'content-type': 'text/event-stream' }),
      text: async () => '',
      json: async () => ({}),
    } as unknown as Response);

    transport.onEvent(() => {});
    transport.onError(() => {});
    transport.onClose(() => {});

    const runConfig = { modelId: 'claude-opus-4-6' };
    await transport.submitInput({
      type: EventTypes.USER_MESSAGES_INPUT,
      sessionId: 'sess-1',
      agentId: 'agent-1',
      threadId: 'main',
      messages: [{
        role: 'user',
        contents: [{ $type: 'text', text: 'hi' }],
      }],
      runConfig,
    }).catch(() => {});

    const [, init] = vi.mocked(fetch).mock.calls[0];
    const body = JSON.parse(init?.body as string);
    expect(body.runConfig).toEqual(runConfig);
  });

  it('omits runConfig key when not provided on the input event', async () => {
    const transport = new SseTransport(BASE);

    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      status: 200,
      body: new ReadableStream({ start(c) { c.close(); } }),
      headers: new Headers({ 'content-type': 'text/event-stream' }),
      text: async () => '',
      json: async () => ({}),
    } as unknown as Response);

    transport.onEvent(() => {});
    transport.onError(() => {});
    transport.onClose(() => {});

    await transport.submitInput({
      type: EventTypes.USER_MESSAGES_INPUT,
      sessionId: 'sess-1',
      agentId: 'agent-1',
      threadId: 'main',
      messages: [{
        role: 'user',
        contents: [{ $type: 'text', text: 'hi' }],
      }],
    }).catch(() => {});

    const [, init] = vi.mocked(fetch).mock.calls[0];
    const body = JSON.parse(init?.body as string);
    expect(body.runConfig).toBeUndefined();
    expect('runConfig' in body).toBe(false);
  });
});
