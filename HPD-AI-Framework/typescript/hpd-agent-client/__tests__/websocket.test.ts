import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { WebSocketTransport } from '../src/transports/websocket.js';
import { EventTypes } from '../src/types/events.js';

class MockWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;

  readyState = MockWebSocket.CONNECTING;
  onopen?: () => void;
  onmessage?: (event: { data: string }) => void;
  onerror?: () => void;
  onclose?: () => void;
  sentMessages: string[] = [];

  constructor(readonly url: string) {
    setTimeout(() => {
      this.readyState = MockWebSocket.OPEN;
      this.onopen?.();
    }, 0);
  }

  send(data: string) {
    this.sentMessages.push(data);
  }

  close() {
    this.readyState = MockWebSocket.CLOSED;
    this.onclose?.();
  }

  simulateMessage(data: string) {
    this.onmessage?.({ data });
  }
}

describe('WebSocketTransport runtime', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    (globalThis as { WebSocket?: typeof MockWebSocket }).WebSocket = MockWebSocket;
  });

  afterEach(() => vi.restoreAllMocks());

  it('connects to the scoped websocket runtime URL', async () => {
    const transport = new WebSocketTransport('http://localhost:5135');
    await transport.connect({ agentId: 'a1', sessionId: 's1', branchId: 'main' });
    expect(((transport as unknown as { ws: MockWebSocket }).ws).url)
      .toBe('ws://localhost:5135/agents/a1/sessions/s1/branches/main/ws');
  });

  it('receives parsed events', async () => {
    const events: unknown[] = [];
    const transport = new WebSocketTransport('http://localhost:5135');
    transport.onEvent((event) => events.push(event));

    await transport.connect({ agentId: 'a1', sessionId: 's1', branchId: 'main' });
    ((transport as unknown as { ws: MockWebSocket }).ws).simulateMessage(
      JSON.stringify({ type: EventTypes.TEXT_DELTA, text: 'Hello', messageId: 'm1' }),
    );

    expect(events).toEqual([{ type: EventTypes.TEXT_DELTA, text: 'Hello', messageId: 'm1' }]);
  });

  it('sends inputs with scoped session, branch, and agent IDs', async () => {
    const transport = new WebSocketTransport('http://localhost:5135');
    await transport.connect({ agentId: 'a1', sessionId: 's1', branchId: 'main' });
    await transport.submitInput({
      type: EventTypes.PERMISSION_RESPONSE,
      permissionId: 'p1',
      sourceName: 'permission',
      approved: true,
    });

    expect(((transport as unknown as { ws: MockWebSocket }).ws).sentMessages.map((message) => JSON.parse(message))).toEqual([
      {
        type: EventTypes.PERMISSION_RESPONSE,
        permissionId: 'p1',
        sourceName: 'permission',
        approved: true,
        agentId: 'a1',
        sessionId: 's1',
        branchId: 'main',
      },
    ]);
  });

  it('throws when sending without a connection', async () => {
    const transport = new WebSocketTransport('http://localhost:5135');
    await expect(transport.submitInput({
      type: EventTypes.PERMISSION_RESPONSE,
      permissionId: 'p1',
      sourceName: 'permission',
      approved: true,
    })).rejects.toThrow('WebSocket not connected');
  });

  it('disconnects and reports connection state', async () => {
    const transport = new WebSocketTransport('http://localhost:5135');
    await transport.connect({ agentId: 'a1', sessionId: 's1', branchId: 'main' });
    expect(transport.connected).toBe(true);
    transport.disconnect();
    expect(transport.connected).toBe(false);
  });
});
