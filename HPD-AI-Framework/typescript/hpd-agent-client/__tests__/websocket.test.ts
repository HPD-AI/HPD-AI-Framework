import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { WebSocketTransport } from '../src/transports/websocket.js';

// Mock WebSocket
class MockWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;

  url: string;
  readyState = MockWebSocket.CONNECTING;
  onopen?: () => void;
  onmessage?: (event: { data: string }) => void;
  onerror?: () => void;
  onclose?: () => void;
  sentMessages: string[] = [];

  constructor(url: string) {
    this.url = url;
    // Simulate async connection
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

  // Test helper to simulate receiving a message
  simulateMessage(data: string) {
    this.onmessage?.({ data });
  }

  // Test helper to simulate error
  simulateError() {
    this.onerror?.();
  }
}

describe('WebSocketTransport', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    (globalThis as any).WebSocket = MockWebSocket;
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('should convert http to ws URL', () => {
    const transport = new WebSocketTransport('http://localhost:5135');
    expect((transport as any).baseUrl).toBe('ws://localhost:5135');
  });

  it('should convert https to wss URL', () => {
    const transport = new WebSocketTransport('https://example.com');
    expect((transport as any).baseUrl).toBe('wss://example.com');
  });

  it('should remove trailing slash', () => {
    const transport = new WebSocketTransport('http://localhost:5135/');
    expect((transport as any).baseUrl).toBe('ws://localhost:5135');
  });

  it('should connect and send initial messages', async () => {
    const transport = new WebSocketTransport('http://localhost:5135');

    await transport.connect({
      sessionId: 'test-123',
      branchId: 'main',
      messages: [{ content: 'Hello' }],
    });

    const ws = (transport as any).ws as MockWebSocket;
    expect(ws.url).toBe('ws://localhost:5135/sessions/test-123/branches/main/ws');
    expect(ws.sentMessages).toContainEqual(JSON.stringify({ messages: [{ content: 'Hello' }] }));
  });

  it('should receive and parse events', async () => {
    const events: any[] = [];
    const transport = new WebSocketTransport('http://localhost:5135');

    transport.onEvent((event) => events.push(event));

    await transport.connect({
      conversationId: 'test-123',
      messages: [{ content: 'Hi' }],
    });

    const ws = (transport as any).ws as MockWebSocket;
    ws.simulateMessage('{"version":"1.0","type":"TEXT_DELTA","text":"Hello","messageId":"msg-1"}');

    expect(events).toHaveLength(1);
    expect(events[0].text).toBe('Hello');
  });

  it('should ignore invalid JSON messages', async () => {
    const events: any[] = [];
    const transport = new WebSocketTransport('http://localhost:5135');

    transport.onEvent((event) => events.push(event));

    await transport.connect({
      conversationId: 'test-123',
      messages: [{ content: 'Hi' }],
    });

    const ws = (transport as any).ws as MockWebSocket;
    ws.simulateMessage('not valid json');

    expect(events).toHaveLength(0);
  });

  it('should send messages directly over WebSocket', async () => {
    const transport = new WebSocketTransport('http://localhost:5135');

    await transport.connect({
      conversationId: 'test-123',
      messages: [{ content: 'Hi' }],
    });

    await transport.send({
      type: 'permission_response',
      permissionId: 'perm-1',
      approved: true,
    });

    const ws = (transport as any).ws as MockWebSocket;
    expect(ws.sentMessages).toContainEqual(
      JSON.stringify({
        type: 'permission_response',
        permissionId: 'perm-1',
        approved: true,
      })
    );
  });

  it('should call close handler on disconnect', async () => {
    const transport = new WebSocketTransport('http://localhost:5135');
    const closeHandler = vi.fn();

    transport.onClose(closeHandler);

    await transport.connect({
      conversationId: 'test-123',
      messages: [{ content: 'Hi' }],
    });

    transport.disconnect();

    expect(closeHandler).toHaveBeenCalled();
  });

  it('should throw when sending without connection', async () => {
    const transport = new WebSocketTransport('http://localhost:5135');

    await expect(
      transport.send({
        type: 'permission_response',
        permissionId: 'perm-1',
        approved: true,
      })
    ).rejects.toThrow('WebSocket not connected');
  });

  it('should report connected state correctly', async () => {
    const transport = new WebSocketTransport('http://localhost:5135');

    expect(transport.connected).toBe(false);

    await transport.connect({
      conversationId: 'test-123',
      messages: [{ content: 'Hi' }],
    });

    expect(transport.connected).toBe(true);

    transport.disconnect();

    expect(transport.connected).toBe(false);
  });

  it('should handle abort signal during connection', async () => {
    const transport = new WebSocketTransport('http://localhost:5135');
    const controller = new AbortController();

    // Abort immediately
    controller.abort();

    await expect(
      transport.connect({
        conversationId: 'test-123',
        messages: [{ content: 'Hi' }],
        signal: controller.signal,
      })
    ).rejects.toThrow('Aborted');
  });

  it('should call error handler on WebSocket error', async () => {
    // Create a WebSocket that errors on connect (without calling onopen)
    class ErrorWebSocket {
      static CONNECTING = 0;
      static OPEN = 1;
      static CLOSING = 2;
      static CLOSED = 3;

      url: string;
      readyState = ErrorWebSocket.CONNECTING;
      onopen?: () => void;
      onmessage?: (event: { data: string }) => void;
      onerror?: () => void;
      onclose?: () => void;

      constructor(url: string) {
        this.url = url;
        // Fire error instead of onopen
        setTimeout(() => {
          this.onerror?.();
        }, 0);
      }

      send(_data: string) {}
      close() {
        this.readyState = ErrorWebSocket.CLOSED;
        this.onclose?.();
      }
    }

    (globalThis as any).WebSocket = ErrorWebSocket;

    const transport = new WebSocketTransport('http://localhost:5135');
    const errorHandler = vi.fn();

    transport.onError(errorHandler);

    await expect(
      transport.connect({
        conversationId: 'test-123',
        messages: [{ content: 'Hi' }],
      })
    ).rejects.toThrow('WebSocket error');

    expect(errorHandler).toHaveBeenCalled();
  });

  // ============================================
  // Session CRUD Tests (via HTTP)
  // ============================================

  describe('Session CRUD (HTTP)', () => {
    it('should list sessions via HTTP POST', async () => {
      const transport = new WebSocketTransport('http://localhost:5135');

      const mockSessions = [
        {
          id: 'session-1',
          createdAt: '2024-01-01T00:00:00Z',
          lastActivity: '2024-01-01T00:10:00Z',
          metadata: {},
        },
      ];

      const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: true,
        json: async () => mockSessions,
      } as Response);

      const result = await transport.listSessions({ limit: 10 });

      expect(fetchSpy).toHaveBeenCalledWith(
        'http://localhost:5135/sessions?limit=10',
        expect.objectContaining({
          method: 'GET',
        })
      );

      expect(result).toEqual(mockSessions);
    });

    it('should get session by ID via HTTP GET', async () => {
      const transport = new WebSocketTransport('http://localhost:5135');

      const mockSession = {
        id: 'session-123',
        createdAt: '2024-01-01T00:00:00Z',
        lastActivity: '2024-01-01T00:10:00Z',
        metadata: {},
      };

      vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: true,
        json: async () => mockSession,
      } as Response);

      const result = await transport.getSession('session-123');

      expect(result).toEqual(mockSession);
    });

    it('should return null for 404 on getSession', async () => {
      const transport = new WebSocketTransport('http://localhost:5135');

      vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: false,
        status: 404,
      } as Response);

      const result = await transport.getSession('non-existent');

      expect(result).toBeNull();
    });

    it('should create session via HTTP POST', async () => {
      const transport = new WebSocketTransport('http://localhost:5135');

      const mockSession = {
        id: 'new-session',
        createdAt: '2024-01-01T00:00:00Z',
        lastActivity: '2024-01-01T00:00:00Z',
        metadata: { test: true },
      };

      const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: true,
        json: async () => mockSession,
      } as Response);

      const result = await transport.createSession({
        sessionId: 'new-session',
        metadata: { test: true },
      });

      expect(fetchSpy).toHaveBeenCalledWith(
        'http://localhost:5135/sessions',
        expect.objectContaining({
          method: 'POST',
        })
      );

      expect(result).toEqual(mockSession);
    });

    it('should update session via HTTP PATCH', async () => {
      const transport = new WebSocketTransport('http://localhost:5135');

      const updatedSession = {
        id: 'session-123',
        createdAt: '2024-01-01T00:00:00Z',
        lastActivity: '2024-01-01T00:15:00Z',
        metadata: { updated: true },
      };

      const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: true,
        json: async () => updatedSession,
      } as Response);

      const result = await transport.updateSession('session-123', {
        metadata: { updated: true },
      });

      expect(fetchSpy).toHaveBeenCalledWith(
        'http://localhost:5135/sessions/session-123',
        expect.objectContaining({
          method: 'PUT',
        })
      );

      expect(result).toEqual(updatedSession);
    });

    it('should delete session via HTTP DELETE', async () => {
      const transport = new WebSocketTransport('http://localhost:5135');

      const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: true,
      } as Response);

      await transport.deleteSession('session-123');

      expect(fetchSpy).toHaveBeenCalledWith(
        'http://localhost:5135/sessions/session-123',
        expect.objectContaining({
          method: 'DELETE',
        })
      );
    });
  });

  // ============================================
  // Branch CRUD Tests (via HTTP)
  // ============================================

  describe('Branch CRUD (HTTP)', () => {
    it('should list branches via HTTP GET', async () => {
      const transport = new WebSocketTransport('http://localhost:5135');

      const mockBranches = [
        {
          id: 'main',
          sessionId: 'session-123',
          name: 'Main',
          createdAt: '2024-01-01T00:00:00Z',
          lastActivity: '2024-01-01T00:10:00Z',
          messageCount: 5,
          siblingIndex: 0,
          totalSiblings: 1,
          isOriginal: true,
          childBranches: [],
          totalForks: 0,
        },
      ];

      const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: true,
        json: async () => mockBranches,
      } as Response);

      const result = await transport.listBranches('session-123');

      expect(fetchSpy).toHaveBeenCalledWith(
        'http://localhost:5135/sessions/session-123/branches',
        expect.any(Object)
      );

      expect(result).toEqual(mockBranches);
    });

    it('should get branch by ID via HTTP GET', async () => {
      const transport = new WebSocketTransport('http://localhost:5135');

      const mockBranch = {
        id: 'main',
        sessionId: 'session-123',
        name: 'Main',
        createdAt: '2024-01-01T00:00:00Z',
        lastActivity: '2024-01-01T00:10:00Z',
        messageCount: 5,
        siblingIndex: 0,
        totalSiblings: 1,
        isOriginal: true,
        childBranches: [],
        totalForks: 0,
      };

      vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: true,
        json: async () => mockBranch,
      } as Response);

      const result = await transport.getBranch('session-123', 'main');

      expect(result).toEqual(mockBranch);
    });

    it('should return null for 404 on getBranch', async () => {
      const transport = new WebSocketTransport('http://localhost:5135');

      vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: false,
        status: 404,
      } as Response);

      const result = await transport.getBranch('session-123', 'non-existent');

      expect(result).toBeNull();
    });

    it('should create branch via HTTP POST', async () => {
      const transport = new WebSocketTransport('http://localhost:5135');

      const mockBranch = {
        id: 'new-branch',
        sessionId: 'session-123',
        name: 'New Branch',
        createdAt: '2024-01-01T00:00:00Z',
        lastActivity: '2024-01-01T00:00:00Z',
        messageCount: 0,
        siblingIndex: 0,
        totalSiblings: 1,
        isOriginal: true,
        childBranches: [],
        totalForks: 0,
      };

      const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: true,
        json: async () => mockBranch,
      } as Response);

      const result = await transport.createBranch('session-123', {
        branchId: 'new-branch',
        name: 'New Branch',
      });

      expect(fetchSpy).toHaveBeenCalledWith(
        'http://localhost:5135/sessions/session-123/branches',
        expect.objectContaining({
          method: 'POST',
        })
      );

      expect(result).toEqual(mockBranch);
    });

    it('should fork branch via HTTP POST', async () => {
      const transport = new WebSocketTransport('http://localhost:5135');

      const mockForkedBranch = {
        id: 'forked-branch',
        sessionId: 'session-123',
        name: 'Forked',
        forkedFrom: 'main',
        forkedAtMessageIndex: 3,
        createdAt: '2024-01-01T00:05:00Z',
        lastActivity: '2024-01-01T00:05:00Z',
        messageCount: 3,
        siblingIndex: 1,
        totalSiblings: 2,
        isOriginal: false,
        originalBranchId: 'main',
        previousSiblingId: 'main',
        childBranches: [],
        totalForks: 0,
      };

      const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: true,
        json: async () => mockForkedBranch,
      } as Response);

      const result = await transport.forkBranch('session-123', 'main', {
        fromMessageIndex: 3,
        name: 'Forked',
      });

      expect(fetchSpy).toHaveBeenCalledWith(
        'http://localhost:5135/sessions/session-123/branches/main/fork',
        expect.objectContaining({
          method: 'POST',
        })
      );

      expect(result).toEqual(mockForkedBranch);
    });

    it('should delete branch via HTTP DELETE', async () => {
      const transport = new WebSocketTransport('http://localhost:5135');

      const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: true,
      } as Response);

      await transport.deleteBranch('session-123', 'branch-to-delete');

      expect(fetchSpy).toHaveBeenCalledWith(
        'http://localhost:5135/sessions/session-123/branches/branch-to-delete',
        expect.objectContaining({
          method: 'DELETE',
        })
      );
    });

    it('should get branch messages via HTTP GET', async () => {
      const transport = new WebSocketTransport('http://localhost:5135');

      const mockMessages = [
        {
          id: 'msg-1',
          role: 'user',
          content: 'Hello',
          timestamp: '2024-01-01T00:00:00Z',
        },
      ];

      const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: true,
        json: async () => mockMessages,
      } as Response);

      const result = await transport.getBranchMessages('session-123', 'main');

      expect(fetchSpy).toHaveBeenCalledWith(
        'http://localhost:5135/sessions/session-123/branches/main/messages',
        expect.any(Object)
      );

      expect(result).toEqual(mockMessages);
    });

    it('should get branch siblings via HTTP GET', async () => {
      const transport = new WebSocketTransport('http://localhost:5135');

      const mockSiblings = [
        {
          branchId: 'main',
          name: 'Main',
          siblingIndex: 0,
          totalSiblings: 2,
          isOriginal: true,
          messageCount: 5,
          createdAt: '2024-01-01T00:00:00Z',
          lastActivity: '2024-01-01T00:10:00Z',
        },
        {
          branchId: 'fork-1',
          name: 'Fork 1',
          siblingIndex: 1,
          totalSiblings: 2,
          isOriginal: false,
          messageCount: 5,
          createdAt: '2024-01-01T00:05:00Z',
          lastActivity: '2024-01-01T00:07:00Z',
        },
      ];

      const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: true,
        json: async () => mockSiblings,
      } as Response);

      const result = await transport.getBranchSiblings('session-123', 'main');

      expect(fetchSpy).toHaveBeenCalledWith(
        'http://localhost:5135/sessions/session-123/branches/main/siblings',
        expect.any(Object)
      );

      expect(result).toEqual(mockSiblings);
      expect(result).toHaveLength(2);
    });

    it('should get next sibling via HTTP GET', async () => {
      const transport = new WebSocketTransport('http://localhost:5135');

      const mainBranch = {
        id: 'main',
        sessionId: 'session-123',
        nextSiblingId: 'fork-1',
        childBranches: [],
        totalForks: 0,
      };

      const mockNext = {
        id: 'fork-1',
        sessionId: 'session-123',
        siblingIndex: 1,
        totalSiblings: 2,
        isOriginal: false,
        childBranches: [],
        totalForks: 0,
      };

      vi.spyOn(globalThis, 'fetch')
        .mockResolvedValueOnce({
          ok: true,
          json: async () => mainBranch,
        } as Response)
        .mockResolvedValueOnce({
          ok: true,
          json: async () => mockNext,
        } as Response);

      const result = await transport.getNextSibling('session-123', 'main');

      expect(result).toEqual(mockNext);
    });

    it('should return null when no next sibling', async () => {
      const transport = new WebSocketTransport('http://localhost:5135');

      vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: false,
        status: 404,
      } as Response);

      const result = await transport.getNextSibling('session-123', 'last');

      expect(result).toBeNull();
    });

    it('should get previous sibling via HTTP GET', async () => {
      const transport = new WebSocketTransport('http://localhost:5135');

      const forkBranch = {
        id: 'fork-1',
        sessionId: 'session-123',
        previousSiblingId: 'main',
        childBranches: [],
        totalForks: 0,
      };

      const mockPrev = {
        id: 'main',
        sessionId: 'session-123',
        siblingIndex: 0,
        totalSiblings: 2,
        isOriginal: true,
        childBranches: [],
        totalForks: 0,
      };

      vi.spyOn(globalThis, 'fetch')
        .mockResolvedValueOnce({
          ok: true,
          json: async () => forkBranch,
        } as Response)
        .mockResolvedValueOnce({
          ok: true,
          json: async () => mockPrev,
        } as Response);

      const result = await transport.getPreviousSibling('session-123', 'fork-1');

      expect(result).toEqual(mockPrev);
    });

    it('should return null when no previous sibling', async () => {
      const transport = new WebSocketTransport('http://localhost:5135');

      vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: false,
        status: 404,
      } as Response);

      const result = await transport.getPreviousSibling('session-123', 'main');

      expect(result).toBeNull();
    });
  });
});
