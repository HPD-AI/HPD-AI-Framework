import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { SseTransport } from '../src/transports/sse.js';

describe('SseTransport', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('should connect and receive events', async () => {
    const events: any[] = [];
    const transport = new SseTransport('http://localhost:5135');

    // Mock fetch with streaming response
    const mockStream = new ReadableStream({
      start(controller) {
        controller.enqueue(
          new TextEncoder().encode(
            'data: {"version":"1.0","type":"TEXT_DELTA","text":"Hello","messageId":"msg-1"}\n\n'
          )
        );
        controller.close();
      },
    });

    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      body: mockStream,
      text: async () => '',
    } as Response);

    transport.onEvent((event) => events.push(event));

    await transport.connect({
      conversationId: 'test-123',
      messages: [{ content: 'Hi' }],
    });

    // Wait for stream processing
    await new Promise((r) => setTimeout(r, 10));

    expect(events).toHaveLength(1);
    expect(events[0].text).toBe('Hello');
  });

  it('should handle HTTP errors', async () => {
    const transport = new SseTransport('http://localhost:5135');

    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: false,
      status: 404,
      text: async () => 'Not Found',
    } as Response);

    await expect(
      transport.connect({
        conversationId: 'test-123',
        messages: [{ content: 'Hi' }],
      })
    ).rejects.toThrow('HTTP 404: Not Found');
  });

  it('should handle missing response body', async () => {
    const transport = new SseTransport('http://localhost:5135');

    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      body: null,
      text: async () => '',
    } as unknown as Response);

    await expect(
      transport.connect({
        conversationId: 'test-123',
        messages: [{ content: 'Hi' }],
      })
    ).rejects.toThrow('No response body');
  });

  it('should call close handler when stream ends', async () => {
    const transport = new SseTransport('http://localhost:5135');
    const closeHandler = vi.fn();

    const mockStream = new ReadableStream({
      start(controller) {
        controller.close();
      },
    });

    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      body: mockStream,
      text: async () => '',
    } as Response);

    transport.onClose(closeHandler);

    await transport.connect({
      conversationId: 'test-123',
      messages: [{ content: 'Hi' }],
    });

    expect(closeHandler).toHaveBeenCalled();
  });

  it('should disconnect on abort', async () => {
    const transport = new SseTransport('http://localhost:5135');
    const closeHandler = vi.fn();

    // Create a stream that never closes
    let streamController: ReadableStreamDefaultController<Uint8Array>;
    const mockStream = new ReadableStream({
      start(controller) {
        streamController = controller;
      },
    });

    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      body: mockStream,
      text: async () => '',
    } as Response);

    transport.onClose(closeHandler);

    const connectPromise = transport.connect({
      conversationId: 'test-123',
      messages: [{ content: 'Hi' }],
    });

    // Wait for connection to establish
    await new Promise((r) => setTimeout(r, 10));
    expect(transport.connected).toBe(true);

    // Disconnect
    transport.disconnect();

    // Close the stream to let connect promise resolve
    streamController!.close();

    await connectPromise;
    expect(transport.connected).toBe(false);
  });

  it('should send permission response via HTTP', async () => {
    const transport = new SseTransport('http://localhost:5135');
    (transport as any).sessionId = 'test-123';
    (transport as any).branchId = 'main';

    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      text: async () => '',
    } as Response);

    await transport.send({
      type: 'permission_response',
      permissionId: 'perm-1',
      approved: true,
      choice: 'allow_always',
    });

    expect(fetchSpy).toHaveBeenCalledWith(
      'http://localhost:5135/sessions/test-123/branches/main/permissions/respond',
      expect.objectContaining({
        method: 'POST',
        body: expect.any(String),
      })
    );
  });

  it('should send clarification response to correct endpoint', async () => {
    const transport = new SseTransport('http://localhost:5135');
    (transport as any).sessionId = 'test-123';
    (transport as any).branchId = 'main';

    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      text: async () => '',
    } as Response);

    await transport.send({
      type: 'clarification_response',
      clarificationId: 'clar-1',
      response: 'Yes, proceed',
    });

    expect(fetchSpy).toHaveBeenCalledWith(
      'http://localhost:5135/sessions/test-123/branches/main/clarifications/respond',
      expect.objectContaining({
        method: 'POST',
      })
    );
  });

  it('should send continuation response to correct endpoint', async () => {
    const transport = new SseTransport('http://localhost:5135');
    (transport as any).sessionId = 'test-123';
    (transport as any).branchId = 'main';

    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      text: async () => '',
    } as Response);

    await transport.send({
      type: 'continuation_response',
      continuationId: 'cont-1',
      shouldContinue: true,
    });

    expect(fetchSpy).toHaveBeenCalledWith(
      'http://localhost:5135/sessions/test-123/branches/main/continuations/respond',
      expect.objectContaining({
        method: 'POST',
      })
    );
  });

  it('should throw when sending without connection', async () => {
    const transport = new SseTransport('http://localhost:5135');

    await expect(
      transport.send({
        type: 'permission_response',
        permissionId: 'perm-1',
        approved: true,
      })
    ).rejects.toThrow('Not connected');
  });

  it('should remove trailing slash from base URL', () => {
    const transport = new SseTransport('http://localhost:5135/');
    expect((transport as any).baseUrl).toBe('http://localhost:5135');
  });

  // ============================================
  // Session CRUD Tests
  // ============================================

  describe('Session CRUD', () => {
    it('should list sessions with search', async () => {
      const transport = new SseTransport('http://localhost:5135');

      const mockSessions = [
        {
          id: 'session-1',
          createdAt: '2024-01-01T00:00:00Z',
          lastActivity: '2024-01-01T00:10:00Z',
          metadata: { user: 'alice' },
        },
        {
          id: 'session-2',
          createdAt: '2024-01-02T00:00:00Z',
          lastActivity: '2024-01-02T00:10:00Z',
          metadata: { user: 'bob' },
        },
      ];

      const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: true,
        json: async () => mockSessions,
      } as Response);

      const result = await transport.listSessions({ limit: 10, offset: 0 });

      expect(fetchSpy).toHaveBeenCalledWith(
        'http://localhost:5135/sessions?limit=10',
        expect.objectContaining({
          method: 'GET',
        })
      );

      expect(result).toEqual(mockSessions);
    });

    it('should get session by ID', async () => {
      const transport = new SseTransport('http://localhost:5135');

      const mockSession = {
        id: 'session-123',
        createdAt: '2024-01-01T00:00:00Z',
        lastActivity: '2024-01-01T00:10:00Z',
        metadata: { user: 'alice' },
      };

      vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: true,
        json: async () => mockSession,
      } as Response);

      const result = await transport.getSession('session-123');

      expect(result).toEqual(mockSession);
    });

    it('should return null for non-existent session', async () => {
      const transport = new SseTransport('http://localhost:5135');

      vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: false,
        status: 404,
        json: async () => ({}),
      } as Response);

      const result = await transport.getSession('non-existent');

      expect(result).toBeNull();
    });

    it('should create session', async () => {
      const transport = new SseTransport('http://localhost:5135');

      const mockSession = {
        id: 'session-new',
        createdAt: '2024-01-01T00:00:00Z',
        lastActivity: '2024-01-01T00:00:00Z',
        metadata: { user: 'charlie' },
      };

      const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: true,
        json: async () => mockSession,
      } as Response);

      const result = await transport.createSession({
        sessionId: 'session-new',
        metadata: { user: 'charlie' },
      });

      expect(fetchSpy).toHaveBeenCalledWith(
        'http://localhost:5135/sessions',
        expect.objectContaining({
          method: 'POST',
          headers: expect.objectContaining({
            'Content-Type': 'application/json',
          }),
        })
      );

      expect(result).toEqual(mockSession);
    });

    it('should update session metadata', async () => {
      const transport = new SseTransport('http://localhost:5135');

      const updatedSession = {
        id: 'session-123',
        createdAt: '2024-01-01T00:00:00Z',
        lastActivity: '2024-01-01T00:15:00Z',
        metadata: { user: 'alice', updated: true },
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
          headers: expect.objectContaining({
            'Content-Type': 'application/json',
          }),
        })
      );

      expect(result).toEqual(updatedSession);
    });

    it('should delete session', async () => {
      const transport = new SseTransport('http://localhost:5135');

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

    it('should throw on session operation errors', async () => {
      const transport = new SseTransport('http://localhost:5135');

      vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: false,
        status: 500,
      } as Response);

      await expect(transport.listSessions()).rejects.toThrow();
      await expect(transport.getSession('session-123')).rejects.toThrow();
      await expect(transport.createSession()).rejects.toThrow();
      await expect(transport.deleteSession('session-123')).rejects.toThrow();
    });
  });

  // ============================================
  // Branch CRUD Tests
  // ============================================

  describe('Branch CRUD', () => {
    it('should list branches in a session', async () => {
      const transport = new SseTransport('http://localhost:5135');

      const mockBranches = [
        {
          id: 'main',
          sessionId: 'session-123',
          name: 'Main Branch',
          createdAt: '2024-01-01T00:00:00Z',
          lastActivity: '2024-01-01T00:10:00Z',
          messageCount: 5,
          siblingIndex: 0,
          totalSiblings: 1,
          isOriginal: true,
          childBranches: ['branch-1'],
          totalForks: 1,
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

    it('should get branch by ID', async () => {
      const transport = new SseTransport('http://localhost:5135');

      const mockBranch = {
        id: 'main',
        sessionId: 'session-123',
        name: 'Main Branch',
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

    it('should return null for non-existent branch', async () => {
      const transport = new SseTransport('http://localhost:5135');

      vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: false,
        status: 404,
      } as Response);

      const result = await transport.getBranch('session-123', 'non-existent');

      expect(result).toBeNull();
    });

    it('should create branch', async () => {
      const transport = new SseTransport('http://localhost:5135');

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
          headers: expect.objectContaining({
            'Content-Type': 'application/json',
          }),
        })
      );

      expect(result).toEqual(mockBranch);
    });

    it('should fork branch', async () => {
      const transport = new SseTransport('http://localhost:5135');

      const mockForkedBranch = {
        id: 'forked-branch',
        sessionId: 'session-123',
        name: 'Forked Branch',
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
        name: 'Forked Branch',
      });

      expect(fetchSpy).toHaveBeenCalledWith(
        'http://localhost:5135/sessions/session-123/branches/main/fork',
        expect.objectContaining({
          method: 'POST',
          headers: expect.objectContaining({
            'Content-Type': 'application/json',
          }),
        })
      );

      expect(result).toEqual(mockForkedBranch);
    });

    it('should delete branch', async () => {
      const transport = new SseTransport('http://localhost:5135');

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

    it('should get branch messages', async () => {
      const transport = new SseTransport('http://localhost:5135');

      const mockMessages = [
        {
          id: 'msg-1',
          role: 'user',
          content: 'Hello',
          timestamp: '2024-01-01T00:00:00Z',
        },
        {
          id: 'msg-2',
          role: 'assistant',
          content: 'Hi there!',
          timestamp: '2024-01-01T00:00:05Z',
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

    it('should get branch siblings', async () => {
      const transport = new SseTransport('http://localhost:5135');

      const mockSiblings = [
        {
          branchId: 'main',
          name: 'Main',
          siblingIndex: 0,
          totalSiblings: 3,
          isOriginal: true,
          messageCount: 5,
          createdAt: '2024-01-01T00:00:00Z',
          lastActivity: '2024-01-01T00:10:00Z',
        },
        {
          branchId: 'fork-1',
          name: 'Fork 1',
          siblingIndex: 1,
          totalSiblings: 3,
          isOriginal: false,
          messageCount: 5,
          createdAt: '2024-01-01T00:05:00Z',
          lastActivity: '2024-01-01T00:07:00Z',
        },
        {
          branchId: 'fork-2',
          name: 'Fork 2',
          siblingIndex: 2,
          totalSiblings: 3,
          isOriginal: false,
          messageCount: 5,
          createdAt: '2024-01-01T00:06:00Z',
          lastActivity: '2024-01-01T00:08:00Z',
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
      expect(result).toHaveLength(3);
      expect(result[0].isOriginal).toBe(true);
    });

    it('should get next sibling', async () => {
      const transport = new SseTransport('http://localhost:5135');

      const mainBranch = {
        id: 'main',
        sessionId: 'session-123',
        nextSiblingId: 'fork-1',
        childBranches: [],
        totalForks: 0,
      };

      const mockNextSibling = {
        id: 'fork-1',
        sessionId: 'session-123',
        name: 'Fork 1',
        siblingIndex: 1,
        totalSiblings: 2,
        isOriginal: false,
        previousSiblingId: 'main',
        childBranches: [],
        totalForks: 0,
      };

      const fetchSpy = vi.spyOn(globalThis, 'fetch')
        .mockResolvedValueOnce({
          ok: true,
          json: async () => mainBranch,
        } as Response)
        .mockResolvedValueOnce({
          ok: true,
          json: async () => mockNextSibling,
        } as Response);

      const result = await transport.getNextSibling('session-123', 'main');

      expect(fetchSpy).toHaveBeenCalledTimes(2);
      expect(fetchSpy).toHaveBeenNthCalledWith(1, 'http://localhost:5135/sessions/session-123/branches/main', expect.any(Object));
      expect(fetchSpy).toHaveBeenNthCalledWith(2, 'http://localhost:5135/sessions/session-123/branches/fork-1', expect.any(Object));
      expect(result).toEqual(mockNextSibling);
    });

    it('should return null when no next sibling', async () => {
      const transport = new SseTransport('http://localhost:5135');

      vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: false,
        status: 404,
      } as Response);

      const result = await transport.getNextSibling('session-123', 'last-sibling');

      expect(result).toBeNull();
    });

    it('should get previous sibling', async () => {
      const transport = new SseTransport('http://localhost:5135');

      const forkBranch = {
        id: 'fork-1',
        sessionId: 'session-123',
        previousSiblingId: 'main',
        childBranches: [],
        totalForks: 0,
      };

      const mockPrevSibling = {
        id: 'main',
        sessionId: 'session-123',
        name: 'Main',
        siblingIndex: 0,
        totalSiblings: 2,
        isOriginal: true,
        nextSiblingId: 'fork-1',
        childBranches: [],
        totalForks: 0,
      };

      const fetchSpy = vi.spyOn(globalThis, 'fetch')
        .mockResolvedValueOnce({
          ok: true,
          json: async () => forkBranch,
        } as Response)
        .mockResolvedValueOnce({
          ok: true,
          json: async () => mockPrevSibling,
        } as Response);

      const result = await transport.getPreviousSibling('session-123', 'fork-1');

      expect(fetchSpy).toHaveBeenCalledTimes(2);
      expect(fetchSpy).toHaveBeenNthCalledWith(1, 'http://localhost:5135/sessions/session-123/branches/fork-1', expect.any(Object));
      expect(fetchSpy).toHaveBeenNthCalledWith(2, 'http://localhost:5135/sessions/session-123/branches/main', expect.any(Object));
      expect(result).toEqual(mockPrevSibling);
    });

    it('should return null when no previous sibling', async () => {
      const transport = new SseTransport('http://localhost:5135');

      vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: false,
        status: 404,
      } as Response);

      const result = await transport.getPreviousSibling('session-123', 'main');

      expect(result).toBeNull();
    });

    it('should throw on branch operation errors', async () => {
      const transport = new SseTransport('http://localhost:5135');

      vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: false,
        status: 500,
      } as Response);

      await expect(transport.listBranches('session-123')).rejects.toThrow();
      await expect(transport.getBranch('session-123', 'main')).rejects.toThrow();
      await expect(transport.createBranch('session-123')).rejects.toThrow();
      await expect(
        transport.forkBranch('session-123', 'main', { fromMessageIndex: 0 })
      ).rejects.toThrow();
      await expect(transport.deleteBranch('session-123', 'main')).rejects.toThrow();
      await expect(transport.getBranchMessages('session-123', 'main')).rejects.toThrow();
      await expect(transport.getBranchSiblings('session-123', 'main')).rejects.toThrow();
    });
  });
});
