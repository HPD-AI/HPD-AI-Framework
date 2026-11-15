/**
 * Integration tests for bridge.ts — full ACP dispatch path.
 *
 * What these tests cover:
 *   createBridge() wires AcpReader → dispatch → bridge modules → AcpWriter.
 *   Tests feed JSON-RPC messages into a mock stdin stream, capture stdout lines,
 *   and assert the correct sequence of responses and notifications. AgentClient
 *   is replaced by a typed mock object — no real HPD server required.
 *
 * Test type: integration — real reader/writer on in-memory PassThrough streams;
 * AgentClient replaced by vi.fn() mocks.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { PassThrough } from 'node:stream';
import { AcpReader } from '../src/acp/reader.js';
import { AcpWriter } from '../src/acp/writer.js';
import { SessionRegistry } from '../src/bridge/session.js';
import { createBridge } from '../src/bridge.js';
import type { BridgeConfig } from '../src/config.js';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const DEFAULT_CONFIG: BridgeConfig = {
  serverUrl: 'http://localhost:5000',
  agentName: 'test-agent',
};

/** Build a bridge with in-memory streams and a mock AgentClient. */
function makeBridge(clientOverrides: Partial<MockClient> = {}) {
  const stdin  = new PassThrough();
  const stdout = new PassThrough();
  const reader  = new AcpReader(stdin);
  const writer  = new AcpWriter(stdout);
  const sessions = new SessionRegistry();

  const client = makeMockClient(clientOverrides);
  createBridge(client as any, reader, writer, sessions, DEFAULT_CONFIG);

  // Collect stdout lines as parsed JSON
  const output: unknown[] = [];
  stdout.on('data', (chunk: Buffer) => {
    for (const line of chunk.toString().split('\n')) {
      const t = line.trim();
      if (t) output.push(JSON.parse(t));
    }
  });

  /** Send a JSON-RPC message into the bridge */
  function send(msg: unknown) {
    stdin.write(JSON.stringify(msg) + '\n');
  }

  /** Wait for N output messages to arrive */
  async function waitFor(n: number, timeoutMs = 100): Promise<unknown[]> {
    const deadline = Date.now() + timeoutMs;
    while (output.length < n && Date.now() < deadline) {
      await new Promise((r) => setTimeout(r, 5));
    }
    return output;
  }

  return { send, output, waitFor, client, sessions, writer, reader };
}

// ---------------------------------------------------------------------------
// Minimal mock AgentClient
// ---------------------------------------------------------------------------

type MockClient = {
  listThreads: ReturnType<typeof vi.fn>;
  getThread: ReturnType<typeof vi.fn>;
  stream?: ReturnType<typeof vi.fn>;
};

function makeMockClient(overrides: Partial<MockClient> = {}) {
  let options: any;
  let rejectObservation!: (error: unknown) => void;
  const listThreads = overrides.listThreads ?? vi.fn(async () => [{ id: 'hpd-thread-1' }]);
  const getThread = overrides.getThread ?? vi.fn(async () => ({ messages: [] }));
  const emit = async (event: any, cursor?: { generation: number; sequence: number }) => {
    const delivery = { event, cursor };
    await options.handlers?.[event.type]?.(delivery);
    await options.onEvent?.(delivery);
  };
  const complete = () => emit({ type: 'THREAD_EXECUTION_FINISHED', threadExecutionId: 'e', outcome: 'succeeded' });
  const thread = {
    id: 'hpd-thread-1',
    state: async () => ({ observedCursor: {
      generation: 1, sequenceNumber: (await getThread()).messages.length * 2,
    } }),
    observe: vi.fn(async (observeOptions: any) => {
      options = observeOptions;
      const done = new Promise<void>((resolve, reject) => {
        rejectObservation = reject;
        options.signal.addEventListener('abort', () => resolve(), { once: true });
      });
      void done.catch(() => {});
      if (!options.handlers) {
        let sequence = 0;
        for (const message of (await getThread()).messages) {
          await emit({ type: 'TEXT_MESSAGE_START', messageId: message.id, role: message.role },
            { generation: 1, sequence: ++sequence });
          await emit({ type: 'TEXT_DELTA', messageId: message.id, text: message.contents[0].text },
            { generation: 1, sequence: ++sequence });
        }
      }
      return { done, close: vi.fn(async () => {}) };
    }),
    send: vi.fn(async (text: string, { runConfig }: any) => {
      if (overrides.stream) {
        void Promise.resolve(overrides.stream('hpd-sess-1', 'hpd-thread-1', [{ role: 'user', content: text }], {
          onEvent: emit,
          onClarificationRequest: emit,
          onComplete: complete,
          onError: rejectObservation,
        }, { signal: options.signal, runConfig })).catch(rejectObservation);
      } else await complete();
      return { status: 'queued', execution: { id: 'e', cancel: vi.fn(async () => {}) } };
    }),
    respond: vi.fn(async () => ({ accepted: true })),
  };
  const session = { id: 'hpd-sess-1', threads: { list: listThreads, get: vi.fn(async () => thread) } };
  const sessions = {
    create: vi.fn(async () => ({ session, thread })),
    get: vi.fn(async () => session),
  };
  return { sessions, thread, listThreads };
}

// ---------------------------------------------------------------------------
// Message factories
// ---------------------------------------------------------------------------

const initMsg = (id = 1, caps = {}) => ({
  jsonrpc: '2.0', id, method: 'initialize',
  params: { protocolVersion: 1, clientCapabilities: caps },
});

const sessionNewMsg = (id = 2) => ({
  jsonrpc: '2.0', id, method: 'session/new',
  params: { cwd: '/workspace' },
});

const sessionLoadMsg = (id = 3, sessionId = 'hpd-sess-1') => ({
  jsonrpc: '2.0', id, method: 'session/load',
  params: { sessionId, cwd: '/workspace' },
});

const promptMsg = (id = 4, sessionId = 'hpd-sess-1', text = 'hello') => ({
  jsonrpc: '2.0', id, method: 'session/prompt',
  params: { sessionId, prompt: [{ type: 'text', text }] },
});

const cancelMsg = (sessionId = 'hpd-sess-1') => ({
  jsonrpc: '2.0', method: 'session/cancel',
  params: { sessionId },
});

const setModeMsg = (id = 5, sessionId = 'hpd-sess-1') => ({
  jsonrpc: '2.0', id, method: 'session/set_mode',
  params: { sessionId, modeId: 'auto' },
});

const authenticateMsg = (id = 6) => ({
  jsonrpc: '2.0', id, method: 'authenticate',
  params: { methodId: 'api_key' },
});

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('createBridge — initialize', () => {
  it('echoes protocolVersion and advertises agent capabilities', async () => {
    const { send, waitFor } = makeBridge();
    send(initMsg(1));

    const [msg] = await waitFor(1) as any[];
    expect(msg.id).toBe(1);
    expect(msg.result.protocolVersion).toBe(1);
    expect(msg.result.agentCapabilities.loadSession).toBe(true);
    expect(msg.result.agentCapabilities.promptCapabilities.image).toBe(false);
    expect(msg.result.agentCapabilities.promptCapabilities.embeddedContext).toBe(true);
    expect(msg.result.agentCapabilities.mcpCapabilities.http).toBe(true);
  });

  it('includes agentInfo with name from config', async () => {
    const { send, waitFor } = makeBridge();
    send(initMsg(1));

    const [msg] = await waitFor(1) as any[];
    expect(msg.result.agentInfo.name).toBe('test-agent');
  });
});

describe('createBridge — authenticate', () => {
  it('responds with empty result', async () => {
    const { send, waitFor } = makeBridge();
    send(authenticateMsg(6));

    const [msg] = await waitFor(1) as any[];
    expect(msg.id).toBe(6);
    expect(msg.result).toBeDefined();
  });
});

describe('createBridge — session/new', () => {
  it('creates HPD session+thread and returns stable sessionId', async () => {
    const { send, waitFor, client } = makeBridge();
    send(sessionNewMsg(2));

    const [msg] = await waitFor(1) as any[];
    expect(msg.id).toBe(2);
    expect(msg.result.sessionId).toBe('hpd-sess-1');
    expect(client.sessions.create).toHaveBeenCalledWith({ agentId: 'test-agent' });
  });

  it('acpSessionId equals hpdSessionId in registry', async () => {
    const { send, waitFor, sessions } = makeBridge();
    send(sessionNewMsg(2));
    await waitFor(1);

    expect(sessions.get('hpd-sess-1')).toBeDefined();
  });
});

describe('createBridge — session/load', () => {
  it('responds with null result for known session', async () => {
    const { send, waitFor } = makeBridge();
    // First create the session
    send(sessionNewMsg(2));
    await waitFor(1);

    send(sessionLoadMsg(3, 'hpd-sess-1'));
    const msgs = await waitFor(2) as any[];
    const loadResp = msgs[1];
    expect(loadResp.id).toBe(3);
    expect(loadResp.result).toBeNull();
  });

  it('replays history as session/update notifications before responding', async () => {
    const client = {
      getThread: vi.fn().mockResolvedValue({
        messages: [
          { id: 'm1', role: 'user',      contents: [{ $type: 'text', text: 'hi' }],    timestamp: '' },
          { id: 'm2', role: 'assistant', contents: [{ $type: 'text', text: 'hello' }], timestamp: '' },
        ],
      }),
    };
    const { send, waitFor } = makeBridge(client);
    send(sessionNewMsg(2));
    await waitFor(1);

    send(sessionLoadMsg(3, 'hpd-sess-1'));
    // 2 notifications + 1 response = 3 total (after the session/new response)
    const msgs = await waitFor(4) as any[];

    const notifications = msgs.filter((m: any) => m.method === 'session/update');
    expect(notifications).toHaveLength(2);
    expect(notifications[0].params.update.sessionUpdate).toBe('user_message_chunk');
    expect(notifications[0].params.update.content.text).toBe('hi');
    expect(notifications[1].params.update.sessionUpdate).toBe('agent_message_chunk');
    expect(notifications[1].params.update.content.text).toBe('hello');
  });

  it('fetches threads from HPD on bridge restart (unknown session)', async () => {
    const { send, waitFor, client } = makeBridge();
    send(sessionLoadMsg(3, 'hpd-sess-1'));

    const msgs = await waitFor(1) as any[];
    expect(client.listThreads).toHaveBeenCalledOnce();
    expect(msgs[0].result).toBeNull();
  });

  it('returns ResourceNotFound error when listThreads returns empty', async () => {
    const { send, waitFor } = makeBridge({
      listThreads: vi.fn().mockResolvedValue([]),
    });
    send(sessionLoadMsg(3, 'unknown-sess'));

    const [msg] = await waitFor(1) as any[];
    expect(msg.error.code).toBe(-32002);
  });
});

describe('createBridge — session/prompt', () => {
  it('returns ResourceNotFound for unknown session', async () => {
    const { send, waitFor } = makeBridge();
    send(promptMsg(4, 'no-such-session'));

    const [msg] = await waitFor(1) as any[];
    expect(msg.error.code).toBe(-32002);
  });

  it('calls client.run with resetClientState in runConfig.clientToolInput', async () => {
    const { send, waitFor, client } = makeBridge();
    send(sessionNewMsg(2));
    await waitFor(1);

    send(promptMsg(4, 'hpd-sess-1'));
    await waitFor(2);

    const runCall = client.thread.send.mock.calls[0];
    expect(runCall[1].runConfig.tools.clientInput.resetClientState).toBe(true);
  });

  it('responds with end_turn when onComplete fires', async () => {
    const stream = vi.fn().mockImplementation(
      (_sid: string, _bid: string, _msgs: unknown[], handlers: any) => {
        handlers.onComplete();
        return Promise.resolve();
      },
    );
    const { send, waitFor, client } = makeBridge({ stream });
    send(sessionNewMsg(2));
    await waitFor(1);

    send(promptMsg(4, 'hpd-sess-1'));
    const msgs = await waitFor(2) as any[];
    const promptResp = msgs[1] as any;
    expect(promptResp.id).toBe(4);
    expect(promptResp.result.stopReason).toBe('end_turn');
  });

  it('forwards TEXT_DELTA events as session/update notifications', async () => {
    const stream = vi.fn().mockImplementation(
      (_sid: string, _bid: string, _msgs: unknown[], handlers: any) => {
        handlers.onEvent({ type: 'TEXT_DELTA', text: 'chunk1', messageId: 'm1', version: '1.0' });
        handlers.onEvent({ type: 'TEXT_DELTA', text: 'chunk2', messageId: 'm1', version: '1.0' });
        handlers.onComplete();
        return Promise.resolve();
      },
    );
    const { send, waitFor, client } = makeBridge({ stream });
    send(sessionNewMsg(2));
    await waitFor(1);

    send(promptMsg(4, 'hpd-sess-1'));
    // session/new response + 2 notifications + prompt response = 4
    const msgs = await waitFor(4) as any[];
    const notifications = msgs.filter((m: any) => m.method === 'session/update');

    expect(notifications).toHaveLength(2);
    expect(notifications[0].params.update.content.text).toBe('chunk1');
    expect(notifications[1].params.update.content.text).toBe('chunk2');
  });

  it('responds with InternalError when onError fires', async () => {
    const stream = vi.fn().mockImplementation(
      (_sid: string, _bid: string, _msgs: unknown[], handlers: any) => {
        handlers.onError('something exploded');
        return Promise.resolve();
      },
    );
    const { send, waitFor } = makeBridge({ stream });
    send(sessionNewMsg(2));
    await waitFor(1);

    send(promptMsg(4, 'hpd-sess-1'));
    const msgs = await waitFor(2) as any[];
    const promptResp = msgs[1] as any;
    expect(promptResp.error.code).toBe(-32603);
  });

  it('responds with cancelled when stream throws AbortError', async () => {
    const stream = vi.fn().mockImplementation(() => {
      const e = new Error('aborted');
      e.name = 'AbortError';
      return Promise.reject(e);
    });
    const { send, waitFor } = makeBridge({ stream });
    send(sessionNewMsg(2));
    await waitFor(1);

    send(promptMsg(4, 'hpd-sess-1'));
    const msgs = await waitFor(2) as any[];
    const promptResp = msgs[1] as any;
    expect(promptResp.result.stopReason).toBe('cancelled');
  });

  it('clarification prompt does not start a new stream', async () => {
    // First prompt triggers clarification; second prompt should resolve it, not call stream again
    const stream = vi.fn().mockImplementationOnce(
      (_sid: string, _bid: string, _msgs: unknown[], handlers: any) => {
        // Trigger a clarification — park a promise
        handlers.onClarificationRequest({
          type: 'USER_QUESTION_REQUEST',
          requestId: 'clr-1',
          sourceName: 'questions',
          questions: [{ id: 'q1', text: 'Which?', options: [{ id: 'a', label: 'A' }] }],
          version: '1.0',
        });
        // Never calls onComplete — stream is still running
        return new Promise(() => {}); // unresolved
      },
    );
    const { send, waitFor } = makeBridge({ stream });
    send(sessionNewMsg(2));
    await waitFor(1);

    // First prompt — starts stream, stream emits clarification notification
    send(promptMsg(4, 'hpd-sess-1', 'do something'));
    await waitFor(2); // session/new response + clarification notification

    // Second prompt — should resolve clarification, NOT call stream again
    send(promptMsg(5, 'hpd-sess-1', 'Option A'));
    await new Promise((r) => setTimeout(r, 30));

    expect(stream).toHaveBeenCalledOnce(); // still only one stream call
  });

  it('passes clientToolHarnesses derived from stored clientCapabilities', async () => {
    const stream = vi.fn().mockImplementation(
      (_sid: string, _bid: string, _msgs: unknown[], handlers: any) => {
        handlers.onComplete();
        return Promise.resolve();
      },
    );
    const { send, waitFor, client } = makeBridge({ stream });

    // Initialize with fs capability
    send(initMsg(1, { fs: { readTextFile: true } }));
    await waitFor(1);

    send(sessionNewMsg(2));
    await waitFor(2);

    send(promptMsg(4, 'hpd-sess-1'));
    await waitFor(3);

    const runCall = client.thread.send.mock.calls[0];
    const HARNESS = runCall[1].runConfig.tools.clientInput.clientToolHarnesses;
    expect(HARNESS[0].tools.some((t: any) => t.name === 'editor_read_file')).toBe(true);
  });
});

describe('createBridge — session/cancel', () => {
  it('is a notification — no response emitted', async () => {
    const { send, output } = makeBridge();
    send(cancelMsg('hpd-sess-1'));
    await new Promise((r) => setTimeout(r, 30));

    expect(output).toHaveLength(0);
  });

  it('aborts the active stream abort controller', async () => {
    let capturedSignal: AbortSignal | undefined;
    const stream = vi.fn().mockImplementation(
      (_sid: string, _bid: string, _msgs: unknown[], _handlers: any, opts: any) => {
        capturedSignal = opts.signal;
        return new Promise(() => {}); // never resolves
      },
    );
    const { send, waitFor } = makeBridge({ stream });
    send(sessionNewMsg(2));
    await waitFor(1);

    send(promptMsg(4, 'hpd-sess-1'));
    await new Promise((r) => setTimeout(r, 20));

    send(cancelMsg('hpd-sess-1'));
    await new Promise((r) => setTimeout(r, 20));

    expect(capturedSignal?.aborted).toBe(true);
  });
});

describe('createBridge — session/set_mode', () => {
  it('responds with null result', async () => {
    const { send, waitFor } = makeBridge();
    send(setModeMsg(5));

    const [msg] = await waitFor(1) as any[];
    expect(msg.id).toBe(5);
    expect(msg.result).toBeNull();
  });
});

describe('createBridge — unknown method', () => {
  it('returns MethodNotFound error for unknown request', async () => {
    const { send, waitFor } = makeBridge();
    send({ jsonrpc: '2.0', id: 99, method: 'no/such/method', params: {} });

    const [msg] = await waitFor(1) as any[];
    expect(msg.error.code).toBe(-32601);
  });

  it('silently ignores unknown notification (no id)', async () => {
    const { send, output } = makeBridge();
    send({ jsonrpc: '2.0', method: 'no/such/notification', params: {} });
    await new Promise((r) => setTimeout(r, 30));

    expect(output).toHaveLength(0);
  });
});

describe('createBridge — inbound response routing', () => {
  it('routes editor responses to resolveOutboundRequest', async () => {
    const { send, waitFor, sessions } = makeBridge();
    send(sessionNewMsg(2));
    await waitFor(1);

    // Plant a fake pending permission resolver
    const session = sessions.get('hpd-sess-1')!;
    let resolved = false;
    session.pendingPermissions.set('10', {
      resolve: () => { resolved = true; },
      reject: () => {},
    });

    // Send the editor's response to outbound request id=10
    send({ jsonrpc: '2.0', id: 10, result: { outcome: { outcome: 'selected', optionId: 'allow_once' } } });
    await new Promise((r) => setTimeout(r, 20));

    expect(resolved).toBe(true);
    expect(session.pendingPermissions.size).toBe(0);
  });
});
