/**
 * Unit tests for AgentClient.uploadAsset() and runConfig threading.
 *
 * What these tests cover:
 *   1. uploadAsset() — SseTransport: correct URL, method, multipart body, return value,
 *      error handling.
 *   2. uploadAsset() — WebSocketTransport: uses HTTP base URL, not the ws:// URL.
 *   3. uploadAsset() — AgentClient: delegates to transport.
 *   4. runConfig threading: StreamOptions.runConfig is forwarded to ConnectOptions.runConfig
 *      when AgentClient.stream() connects.
 *   5. SseTransport: runConfig included/omitted from POST body based on options.
 *
 * Test type: unit — all network I/O is replaced by vi.spyOn(globalThis, 'fetch').
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { AgentClient } from '../src/client.js';
import { SseTransport } from '../src/transports/sse.js';
import { WebSocketTransport } from '../src/transports/websocket.js';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const BASE = 'http://localhost:5135';

const ASSET_REFERENCE = {
  assetId: 'asset-abc-123',
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

describe('AgentClient.uploadAsset() — SseTransport', () => {
  let client: AgentClient;

  beforeEach(() => {
    vi.resetAllMocks();
    client = new AgentClient(BASE);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('calls POST /sessions/{sid}/assets', async () => {
    const spy = mockFetchJson(ASSET_REFERENCE, 200);
    await client.uploadAsset('sess-1', makeFile());

    expect(vi.mocked(fetch)).toHaveBeenCalledOnce();
    const [url, init] = vi.mocked(fetch).mock.calls[0];
    expect(String(url)).toBe(`${BASE}/sessions/sess-1/assets`);
    expect(init?.method).toBe('POST');
  });

  it('sends a FormData body (no Content-Type header — browser sets boundary)', async () => {
    mockFetchJson(ASSET_REFERENCE);
    await client.uploadAsset('sess-1', makeFile());

    const [, init] = vi.mocked(fetch).mock.calls[0];
    expect(init?.body).toBeInstanceOf(FormData);
    // Content-Type must NOT be set manually — the browser sets it with the boundary
    const headers = init?.headers as Record<string, string> | undefined;
    expect(headers?.['Content-Type']).toBeUndefined();
  });

  it('returns the parsed AssetReference from the response', async () => {
    mockFetchJson(ASSET_REFERENCE);
    const result = await client.uploadAsset('sess-1', makeFile());

    expect(result).toEqual(ASSET_REFERENCE);
  });

  it('uses the File name as the form field filename by default', async () => {
    mockFetchJson(ASSET_REFERENCE);
    const file = makeFile('my-screenshot.png');
    await client.uploadAsset('sess-1', file);

    const [, init] = vi.mocked(fetch).mock.calls[0];
    const form = init?.body as FormData;
    const entry = form.get('file') as File;
    expect(entry.name).toBe('my-screenshot.png');
  });

  it('uses "upload" as filename for a plain Blob', async () => {
    mockFetchJson(ASSET_REFERENCE);
    await client.uploadAsset('sess-1', makeBlob());

    const [, init] = vi.mocked(fetch).mock.calls[0];
    const form = init?.body as FormData;
    const entry = form.get('file') as File;
    expect(entry.name).toBe('upload');
  });

  it('uses the name param to override the filename', async () => {
    mockFetchJson(ASSET_REFERENCE);
    await client.uploadAsset('sess-1', makeFile('original.png'), 'override.png');

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

    await expect(client.uploadAsset('sess-1', makeFile())).rejects.toThrow('413');
  });
});

// ---------------------------------------------------------------------------

describe('WebSocketTransport.uploadAsset() — uses HTTP base URL', () => {
  beforeEach(() => vi.resetAllMocks());
  afterEach(() => vi.restoreAllMocks());

  it('calls POST on the HTTP URL, not a ws:// URL', async () => {
    const transport = new WebSocketTransport('ws://localhost:5135');
    mockFetchJson(ASSET_REFERENCE);

    await transport.uploadAsset('sess-1', makeFile());

    const [url] = vi.mocked(fetch).mock.calls[0];
    expect(String(url)).toMatch(/^http:\/\//);
    expect(String(url)).toContain('/sessions/sess-1/assets');
  });
});

// ---------------------------------------------------------------------------

describe('AgentClient.stream() — runConfig threading', () => {
  let client: AgentClient;

  beforeEach(() => {
    vi.resetAllMocks();
    client = new AgentClient(BASE);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('forwards runConfig from StreamOptions to the POST body', async () => {
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

    await client.stream('sess-1', 'main', [{ content: 'hi' }], {}, { runConfig, signal }).catch(() => {});

    const [, init] = vi.mocked(fetch).mock.calls[0];
    const body = JSON.parse(init?.body as string);
    expect(body.runConfig).toEqual(runConfig);
  });

  it('omits runConfig from POST body when not provided in StreamOptions', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      status: 200,
      body: new ReadableStream({ start(c) { c.close(); } }),
      headers: new Headers({ 'content-type': 'text/event-stream' }),
      text: async () => '',
      json: async () => ({}),
    } as unknown as Response);

    const signal = new AbortController().signal;
    await client.stream('sess-1', 'main', [{ content: 'hi' }], {}, { signal }).catch(() => {});

    const [, init] = vi.mocked(fetch).mock.calls[0];
    const body = JSON.parse(init?.body as string);
    expect(body.runConfig).toBeUndefined();
    expect('runConfig' in body).toBe(false);
  });
});

// ---------------------------------------------------------------------------

describe('SseTransport — runConfig in POST body', () => {
  beforeEach(() => vi.resetAllMocks());
  afterEach(() => vi.restoreAllMocks());

  it('includes runConfig key when provided in ConnectOptions', async () => {
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
    await transport.connect({
      sessionId: 'sess-1',
      messages: [{ content: 'hi' }],
      runConfig,
    }).catch(() => {});

    const [, init] = vi.mocked(fetch).mock.calls[0];
    const body = JSON.parse(init?.body as string);
    expect(body.runConfig).toEqual(runConfig);
  });

  it('omits runConfig key when not provided in ConnectOptions', async () => {
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

    await transport.connect({
      sessionId: 'sess-1',
      messages: [{ content: 'hi' }],
    }).catch(() => {});

    const [, init] = vi.mocked(fetch).mock.calls[0];
    const body = JSON.parse(init?.body as string);
    expect(body.runConfig).toBeUndefined();
    expect('runConfig' in body).toBe(false);
  });
});
