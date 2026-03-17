/**
 * Unit tests for MauiTransport
 * Tests WebView bridge communication, event parsing, and CRUD operations
 */

import { describe, it, expect, beforeEach, vi } from 'vitest';
import { MauiTransport } from '../src/transports/maui';
import type { AgentEvent } from '../src/types/events';

// Mock HybridWebView
const createMockHybridWebView = () => {
  const listeners: Array<(event: CustomEvent) => void> = [];

  return {
    InvokeDotNet: vi.fn(),
    SendRawMessage: vi.fn(),
    addEventListener: (type: string, listener: (event: Event) => void) => {
      if (type === 'HybridWebViewMessageReceived') {
        listeners.push(listener as (event: CustomEvent) => void);
      }
    },
    removeEventListener: (type: string, listener: (event: Event) => void) => {
      const index = listeners.indexOf(listener as (event: CustomEvent) => void);
      if (index > -1) listeners.splice(index, 1);
    },
    triggerMessage: (message: string) => {
      const event = new CustomEvent('HybridWebViewMessageReceived', {
        detail: { message }
      });
      listeners.forEach(l => l(event));
    },
    listeners
  };
};

describe('MauiTransport', () => {
  let transport: MauiTransport;
  let mockHybridWebView: ReturnType<typeof createMockHybridWebView>;

  beforeEach(() => {
    mockHybridWebView = createMockHybridWebView();
    (global as any).window = {
      HybridWebView: mockHybridWebView,
      addEventListener: mockHybridWebView.addEventListener,
      removeEventListener: mockHybridWebView.removeEventListener
    };
    transport = new MauiTransport();
  });

  describe('connect()', () => {
    it('throws error when HybridWebView not available', async () => {
      delete (global as any).window.HybridWebView;
      await expect(transport.connect({
        messages: [{ content: 'Test', role: 'user' }],
        sessionId: 's1',
        branchId: 'main'
      })).rejects.toThrow('MAUI HybridWebView not available');
    });

    it('calls InvokeDotNet with correct parameters', async () => {
      mockHybridWebView.InvokeDotNet.mockResolvedValue('stream-123');

      await transport.connect({
        messages: [{ content: 'Hello', role: 'user' }],
        sessionId: 'session-1',
        branchId: 'main'
      });

      expect(mockHybridWebView.InvokeDotNet).toHaveBeenCalledWith(
        'StartStream',
        ['Hello', 'session-1', 'main', undefined, undefined]
      );
    });

    it('sets connected to true on success', async () => {
      mockHybridWebView.InvokeDotNet.mockResolvedValue('stream-123');

      await transport.connect({
        messages: [{ content: 'Test', role: 'user' }],
        sessionId: 's1',
        branchId: 'main'
      });

      expect(transport.connected).toBe(true);
    });

    it('registers message listener', async () => {
      mockHybridWebView.InvokeDotNet.mockResolvedValue('stream-123');

      await transport.connect({
        messages: [{ content: 'Test', role: 'user' }],
        sessionId: 's1',
        branchId: 'main'
      });

      expect(mockHybridWebView.listeners.length).toBeGreaterThan(0);
    });

    it('passes first message content', async () => {
      mockHybridWebView.InvokeDotNet.mockResolvedValue('stream-123');

      await transport.connect({
        messages: [
          { content: 'First message', role: 'user' },
          { content: 'Second message', role: 'user' }
        ],
        sessionId: 's1',
        branchId: 'main'
      });

      expect(mockHybridWebView.InvokeDotNet).toHaveBeenCalledWith(
        'StartStream',
        expect.arrayContaining(['First message'])
      );
    });

    it('passes sessionId and branchId', async () => {
      mockHybridWebView.InvokeDotNet.mockResolvedValue('stream-123');

      await transport.connect({
        messages: [{ content: 'Test', role: 'user' }],
        sessionId: 'my-session',
        branchId: 'my-branch'
      });

      expect(mockHybridWebView.InvokeDotNet).toHaveBeenCalledWith(
        'StartStream',
        expect.arrayContaining(['my-session', 'my-branch'])
      );
    });

    it('passes runConfig when provided', async () => {
      mockHybridWebView.InvokeDotNet.mockResolvedValue('stream-123');

      await transport.connect({
        messages: [{ content: 'Test', role: 'user' }],
        sessionId: 's1',
        branchId: 'main',
        runConfig: { chat: { temperature: 0.7 } }
      });

      const lastCall = mockHybridWebView.InvokeDotNet.mock.calls[0];
      expect(lastCall[1][3]).toContain('temperature');
    });

    it('serializes runConfig as JSON', async () => {
      mockHybridWebView.InvokeDotNet.mockResolvedValue('stream-123');

      const config = { chat: { temperature: 0.9, maxOutputTokens: 1000 } };
      await transport.connect({
        messages: [{ content: 'Test', role: 'user' }],
        sessionId: 's1',
        branchId: 'main',
        runConfig: config
      });

      const lastCall = mockHybridWebView.InvokeDotNet.mock.calls[0];
      const parsedConfig = JSON.parse(lastCall[1][3]);
      expect(parsedConfig.chat.temperature).toBe(0.9);
    });

    it('stores streamId', async () => {
      mockHybridWebView.InvokeDotNet.mockResolvedValue('my-stream-id');

      await transport.connect({
        messages: [{ content: 'Test', role: 'user' }],
        sessionId: 's1',
        branchId: 'main'
      });

      // StreamId is internal, but we can verify it's used later
      expect(transport.connected).toBe(true);
    });

    it('registers abort signal when provided', async () => {
      mockHybridWebView.InvokeDotNet.mockResolvedValue('stream-123');
      const controller = new AbortController();

      await transport.connect({
        messages: [{ content: 'Test', role: 'user' }],
        sessionId: 's1',
        branchId: 'main',
        signal: controller.signal
      });

      // Verify connection established
      expect(transport.connected).toBe(true);

      // Trigger abort
      controller.abort();
      expect(transport.connected).toBe(false);
    });

    it('cleans up on error', async () => {
      mockHybridWebView.InvokeDotNet.mockRejectedValue(new Error('Failed'));

      await expect(transport.connect({
        messages: [{ content: 'Test', role: 'user' }],
        sessionId: 's1',
        branchId: 'main'
      })).rejects.toThrow();

      expect(transport.connected).toBe(false);
    });
  });

  describe('Message Parsing', () => {
    beforeEach(async () => {
      mockHybridWebView.InvokeDotNet.mockResolvedValue('stream-123');
      await transport.connect({
        messages: [{ content: 'Test', role: 'user' }],
        sessionId: 's1',
        branchId: 'main'
      });
    });

    it('parses agent_event correctly', () => {
      const eventHandler = vi.fn();
      transport.onEvent(eventHandler);

      const event: AgentEvent = { type: 'TEXT_DELTA', delta: 'Hello' };
      mockHybridWebView.triggerMessage(`agent_event:stream-123:${JSON.stringify(event)}`);

      expect(eventHandler).toHaveBeenCalledWith(event);
    });

    it('calls event handler with parsed event', () => {
      const eventHandler = vi.fn();
      transport.onEvent(eventHandler);

      mockHybridWebView.triggerMessage(
        `agent_event:stream-123:${JSON.stringify({ type: 'TEXT_DELTA', delta: 'Test' })}`
      );

      expect(eventHandler).toHaveBeenCalledOnce();
      expect(eventHandler).toHaveBeenCalledWith(
        expect.objectContaining({ type: 'TEXT_DELTA', delta: 'Test' })
      );
    });

    it('handles colons in JSON content', () => {
      const eventHandler = vi.fn();
      transport.onEvent(eventHandler);

      const event = { type: 'TEXT_DELTA', delta: 'Time: 12:30:45' };
      mockHybridWebView.triggerMessage(`agent_event:stream-123:${JSON.stringify(event)}`);

      expect(eventHandler).toHaveBeenCalledWith(
        expect.objectContaining({ delta: 'Time: 12:30:45' })
      );
    });

    it('calls close handler on complete', () => {
      const closeHandler = vi.fn();
      transport.onClose(closeHandler);

      mockHybridWebView.triggerMessage('agent_complete:stream-123');

      expect(closeHandler).toHaveBeenCalledOnce();
      expect(transport.connected).toBe(false);
    });

    it('calls error handler on error', () => {
      const errorHandler = vi.fn();
      transport.onError(errorHandler);

      mockHybridWebView.triggerMessage('agent_error:stream-123:Something went wrong');

      expect(errorHandler).toHaveBeenCalledOnce();
      expect(errorHandler).toHaveBeenCalledWith(
        expect.objectContaining({ message: 'Something went wrong' })
      );
      expect(transport.connected).toBe(false);
    });

    it('sets connected to false on complete', () => {
      mockHybridWebView.triggerMessage('agent_complete:stream-123');
      expect(transport.connected).toBe(false);
    });

    it('sets connected to false on error', () => {
      mockHybridWebView.triggerMessage('agent_error:stream-123:Error occurred');
      expect(transport.connected).toBe(false);
    });

    it('ignores events for different streamId', () => {
      const eventHandler = vi.fn();
      transport.onEvent(eventHandler);

      mockHybridWebView.triggerMessage(
        `agent_event:different-stream:${JSON.stringify({ type: 'TEXT_DELTA' })}`
      );

      expect(eventHandler).not.toHaveBeenCalled();
    });

    it('calls error handler on parse error', () => {
      const errorHandler = vi.fn();
      transport.onError(errorHandler);

      mockHybridWebView.triggerMessage('agent_event:stream-123:{ invalid json');

      expect(errorHandler).toHaveBeenCalled();
    });
  });

  describe('Session CRUD', () => {
    it('listSessions calls InvokeDotNet with SearchSessions', async () => {
      mockHybridWebView.InvokeDotNet.mockResolvedValue(JSON.stringify([]));

      await transport.listSessions();

      expect(mockHybridWebView.InvokeDotNet).toHaveBeenCalledWith(
        'SearchSessions',
        expect.any(Array)
      );
    });

    it('listSessions parses JSON to session array', async () => {
      const sessions = [{ sessionId: 's1' }, { sessionId: 's2' }];
      mockHybridWebView.InvokeDotNet.mockResolvedValue(JSON.stringify(sessions));

      const result = await transport.listSessions();

      expect(result).toHaveLength(2);
      expect(result[0].sessionId).toBe('s1');
    });

    it('getSession calls InvokeDotNet with sessionId', async () => {
      mockHybridWebView.InvokeDotNet.mockResolvedValue(JSON.stringify({ sessionId: 's1' }));

      await transport.getSession('s1');

      expect(mockHybridWebView.InvokeDotNet).toHaveBeenCalledWith('GetSession', ['s1']);
    });

    it('getSession returns null on error', async () => {
      mockHybridWebView.InvokeDotNet.mockRejectedValue(new Error('Not found'));

      const result = await transport.getSession('nonexistent');

      expect(result).toBeNull();
    });

    it('createSession serializes metadata as JSON', async () => {
      mockHybridWebView.InvokeDotNet.mockResolvedValue(JSON.stringify({ sessionId: 's1' }));

      await transport.createSession({ metadata: { key: 'value' } });

      const lastCall = mockHybridWebView.InvokeDotNet.mock.calls[0];
      expect(lastCall[1][1]).toContain('key');
    });

    it('updateSession calls InvokeDotNet with UpdateSession', async () => {
      const updatedSession = {
        id: 's1',
        metadata: { updated: true },
        createdAt: '2024-01-01',
        lastActivity: '2024-01-01'
      };
      mockHybridWebView.InvokeDotNet.mockResolvedValue(JSON.stringify(updatedSession));

      const result = await transport.updateSession('s1', {
        metadata: { updated: true }
      });

      expect(mockHybridWebView.InvokeDotNet).toHaveBeenCalledWith(
        'UpdateSession',
        expect.arrayContaining(['s1'])
      );
      expect(result.metadata.updated).toBe(true);
    });

    it('updateSession serializes metadata as JSON', async () => {
      mockHybridWebView.InvokeDotNet.mockResolvedValue(JSON.stringify({ id: 's1' }));

      await transport.updateSession('s1', {
        metadata: { key: 'value' }
      });

      const lastCall = mockHybridWebView.InvokeDotNet.mock.calls[0];
      const metadataJson = lastCall[1][1];
      expect(metadataJson).toContain('key');
      expect(metadataJson).toContain('value');
    });

    it('deleteSession calls InvokeDotNet with DeleteSession', async () => {
      mockHybridWebView.InvokeDotNet.mockResolvedValue(undefined);

      await transport.deleteSession('s1');

      expect(mockHybridWebView.InvokeDotNet).toHaveBeenCalledWith('DeleteSession', ['s1']);
    });

    it('listSessions passes pagination parameters', async () => {
      mockHybridWebView.InvokeDotNet.mockResolvedValue(JSON.stringify([]));

      await transport.listSessions({ limit: 10, offset: 5 });

      const lastCall = mockHybridWebView.InvokeDotNet.mock.calls[0];
      const params = JSON.parse(lastCall[1][0]);
      expect(params.offset).toBe(5);
      expect(params.limit).toBe(10);
    });

    it('throws error when HybridWebView not available for session operations', async () => {
      delete (global as any).window.HybridWebView;

      await expect(transport.listSessions()).rejects.toThrow(
        'MAUI HybridWebView not available'
      );
      await expect(transport.getSession('s1')).rejects.toThrow(
        'MAUI HybridWebView not available'
      );
      await expect(transport.createSession()).rejects.toThrow(
        'MAUI HybridWebView not available'
      );
      await expect(transport.deleteSession('s1')).rejects.toThrow(
        'MAUI HybridWebView not available'
      );
    });
  });

  // ============================================
  // Branch CRUD Tests
  // ============================================

  describe('Branch CRUD', () => {
    it('listBranches calls InvokeDotNet with ListBranches', async () => {
      const branches = [
        { id: 'main', sessionId: 's1', messageCount: 5 },
        { id: 'fork-1', sessionId: 's1', messageCount: 3 },
      ];
      mockHybridWebView.InvokeDotNet.mockResolvedValue(JSON.stringify(branches));

      const result = await transport.listBranches('s1');

      expect(mockHybridWebView.InvokeDotNet).toHaveBeenCalledWith('ListBranches', ['s1']);
      expect(result).toHaveLength(2);
      expect(result[0].id).toBe('main');
    });

    it('getBranch calls InvokeDotNet with GetBranch', async () => {
      const branch = {
        id: 'main',
        sessionId: 's1',
        name: 'Main',
        createdAt: '2024-01-01',
        lastActivity: '2024-01-01',
        messageCount: 5,
        siblingIndex: 0,
        totalSiblings: 1,
        isOriginal: true,
        childBranches: [],
        totalForks: 0,
      };
      mockHybridWebView.InvokeDotNet.mockResolvedValue(JSON.stringify(branch));

      const result = await transport.getBranch('s1', 'main');

      expect(mockHybridWebView.InvokeDotNet).toHaveBeenCalledWith('GetBranch', ['s1', 'main']);
      expect(result?.id).toBe('main');
      expect(result?.siblingIndex).toBe(0);
    });

    it('getBranch returns null on error', async () => {
      mockHybridWebView.InvokeDotNet.mockRejectedValue(new Error('Not found'));

      const result = await transport.getBranch('s1', 'nonexistent');

      expect(result).toBeNull();
    });

    it('createBranch calls InvokeDotNet with CreateBranch', async () => {
      const newBranch = {
        id: 'new-branch',
        sessionId: 's1',
        name: 'New Branch',
        messageCount: 0,
        siblingIndex: 0,
        totalSiblings: 1,
        isOriginal: true,
        childBranches: [],
        totalForks: 0,
      };
      mockHybridWebView.InvokeDotNet.mockResolvedValue(JSON.stringify(newBranch));

      const result = await transport.createBranch('s1', {
        branchId: 'new-branch',
        name: 'New Branch',
      });

      expect(mockHybridWebView.InvokeDotNet).toHaveBeenCalledWith(
        'CreateBranch',
        expect.arrayContaining(['s1'])
      );
      expect(result.id).toBe('new-branch');
    });

    it('createBranch passes parameters separately', async () => {
      mockHybridWebView.InvokeDotNet.mockResolvedValue(
        JSON.stringify({ id: 'new', sessionId: 's1' })
      );

      await transport.createBranch('s1', {
        branchId: 'new-branch',
        name: 'New Branch',
        description: 'Test branch',
      });

      const lastCall = mockHybridWebView.InvokeDotNet.mock.calls[0];
      expect(lastCall[1]).toEqual(['s1', 'new-branch', 'New Branch', 'Test branch']);
    });

    it('forkBranch calls InvokeDotNet with ForkBranch', async () => {
      const forkedBranch = {
        id: 'forked',
        sessionId: 's1',
        name: 'Forked Branch',
        forkedFrom: 'main',
        forkedAtMessageIndex: 3,
        messageCount: 3,
        siblingIndex: 1,
        totalSiblings: 2,
        isOriginal: false,
        originalBranchId: 'main',
        previousSiblingId: 'main',
        childBranches: [],
        totalForks: 0,
      };
      mockHybridWebView.InvokeDotNet.mockResolvedValue(JSON.stringify(forkedBranch));

      const result = await transport.forkBranch('s1', 'main', {
        fromMessageIndex: 3,
        name: 'Forked Branch',
      });

      expect(mockHybridWebView.InvokeDotNet).toHaveBeenCalledWith(
        'ForkBranch',
        expect.arrayContaining(['s1', 'main'])
      );
      expect(result.forkedFrom).toBe('main');
      expect(result.forkedAtMessageIndex).toBe(3);
      expect(result.siblingIndex).toBe(1);
    });

    it('forkBranch passes parameters separately', async () => {
      mockHybridWebView.InvokeDotNet.mockResolvedValue(
        JSON.stringify({ id: 'forked', sessionId: 's1' })
      );

      await transport.forkBranch('s1', 'main', {
        fromMessageIndex: 5,
        name: 'Fork at 5',
        newBranchId: 'custom-fork-id',
      });

      const lastCall = mockHybridWebView.InvokeDotNet.mock.calls[0];
      expect(lastCall[1]).toEqual(['s1', 'main', 'custom-fork-id', 5, 'Fork at 5', undefined]);
    });

    it('deleteBranch calls InvokeDotNet with DeleteBranch', async () => {
      mockHybridWebView.InvokeDotNet.mockResolvedValue(undefined);

      await transport.deleteBranch('s1', 'branch-to-delete');

      expect(mockHybridWebView.InvokeDotNet).toHaveBeenCalledWith('DeleteBranch', [
        's1',
        'branch-to-delete',
        false,
      ]);
    });

    it('getBranchMessages calls InvokeDotNet with GetBranchMessages', async () => {
      const messages = [
        { id: 'msg-1', role: 'user', content: 'Hello', timestamp: '2024-01-01' },
        { id: 'msg-2', role: 'assistant', content: 'Hi!', timestamp: '2024-01-01' },
      ];
      mockHybridWebView.InvokeDotNet.mockResolvedValue(JSON.stringify(messages));

      const result = await transport.getBranchMessages('s1', 'main');

      expect(mockHybridWebView.InvokeDotNet).toHaveBeenCalledWith('GetBranchMessages', [
        's1',
        'main',
      ]);
      expect(result).toHaveLength(2);
      expect(result[0].role).toBe('user');
    });

    it('getBranchSiblings calls InvokeDotNet with GetBranchSiblings', async () => {
      const siblings = [
        {
          branchId: 'main',
          name: 'Main',
          siblingIndex: 0,
          totalSiblings: 3,
          isOriginal: true,
          messageCount: 5,
          createdAt: '2024-01-01',
          lastActivity: '2024-01-01',
        },
        {
          branchId: 'fork-1',
          name: 'Fork 1',
          siblingIndex: 1,
          totalSiblings: 3,
          isOriginal: false,
          messageCount: 5,
          createdAt: '2024-01-01',
          lastActivity: '2024-01-01',
        },
        {
          branchId: 'fork-2',
          name: 'Fork 2',
          siblingIndex: 2,
          totalSiblings: 3,
          isOriginal: false,
          messageCount: 5,
          createdAt: '2024-01-01',
          lastActivity: '2024-01-01',
        },
      ];
      mockHybridWebView.InvokeDotNet.mockResolvedValue(JSON.stringify(siblings));

      const result = await transport.getBranchSiblings('s1', 'main');

      expect(mockHybridWebView.InvokeDotNet).toHaveBeenCalledWith('GetBranchSiblings', [
        's1',
        'main',
      ]);
      expect(result).toHaveLength(3);
      expect(result[0].isOriginal).toBe(true);
      expect(result[0].siblingIndex).toBe(0);
      expect(result[1].siblingIndex).toBe(1);
      expect(result[2].siblingIndex).toBe(2);
    });

    it('getNextSibling fetches branch then next sibling', async () => {
      const mainBranch = {
        id: 'main',
        sessionId: 's1',
        nextSiblingId: 'fork-1',
        childBranches: [],
        totalForks: 0,
      };
      const nextSibling = {
        id: 'fork-1',
        sessionId: 's1',
        siblingIndex: 1,
        totalSiblings: 2,
        isOriginal: false,
        previousSiblingId: 'main',
        childBranches: [],
        totalForks: 0,
      };

      mockHybridWebView.InvokeDotNet
        .mockResolvedValueOnce(JSON.stringify(mainBranch))
        .mockResolvedValueOnce(JSON.stringify(nextSibling));

      const result = await transport.getNextSibling('s1', 'main');

      expect(mockHybridWebView.InvokeDotNet).toHaveBeenCalledTimes(2);
      expect(mockHybridWebView.InvokeDotNet).toHaveBeenNthCalledWith(1, 'GetBranch', ['s1', 'main']);
      expect(mockHybridWebView.InvokeDotNet).toHaveBeenNthCalledWith(2, 'GetBranch', ['s1', 'fork-1']);
      expect(result?.id).toBe('fork-1');
    });

    it('getNextSibling returns null when no next sibling', async () => {
      mockHybridWebView.InvokeDotNet.mockRejectedValue(new Error('Not found'));

      const result = await transport.getNextSibling('s1', 'last-sibling');

      expect(result).toBeNull();
    });

    it('getPreviousSibling fetches branch then previous sibling', async () => {
      const forkBranch = {
        id: 'fork-1',
        sessionId: 's1',
        previousSiblingId: 'main',
        childBranches: [],
        totalForks: 0,
      };
      const prevSibling = {
        id: 'main',
        sessionId: 's1',
        siblingIndex: 0,
        totalSiblings: 2,
        isOriginal: true,
        nextSiblingId: 'fork-1',
        childBranches: [],
        totalForks: 0,
      };

      mockHybridWebView.InvokeDotNet
        .mockResolvedValueOnce(JSON.stringify(forkBranch))
        .mockResolvedValueOnce(JSON.stringify(prevSibling));

      const result = await transport.getPreviousSibling('s1', 'fork-1');

      expect(mockHybridWebView.InvokeDotNet).toHaveBeenCalledTimes(2);
      expect(mockHybridWebView.InvokeDotNet).toHaveBeenNthCalledWith(1, 'GetBranch', ['s1', 'fork-1']);
      expect(mockHybridWebView.InvokeDotNet).toHaveBeenNthCalledWith(2, 'GetBranch', ['s1', 'main']);
      expect(result?.id).toBe('main');
    });

    it('getPreviousSibling returns null when no previous sibling', async () => {
      mockHybridWebView.InvokeDotNet.mockRejectedValue(new Error('Not found'));

      const result = await transport.getPreviousSibling('s1', 'main');

      expect(result).toBeNull();
    });

    it('throws error when HybridWebView not available for branch operations', async () => {
      delete (global as any).window.HybridWebView;

      await expect(transport.listBranches('s1')).rejects.toThrow(
        'MAUI HybridWebView not available'
      );
      await expect(transport.getBranch('s1', 'main')).rejects.toThrow(
        'MAUI HybridWebView not available'
      );
      await expect(transport.createBranch('s1')).rejects.toThrow(
        'MAUI HybridWebView not available'
      );
      await expect(
        transport.forkBranch('s1', 'main', { fromMessageIndex: 0 })
      ).rejects.toThrow('MAUI HybridWebView not available');
      await expect(transport.deleteBranch('s1', 'main')).rejects.toThrow(
        'MAUI HybridWebView not available'
      );
      await expect(transport.getBranchMessages('s1', 'main')).rejects.toThrow(
        'MAUI HybridWebView not available'
      );
      await expect(transport.getBranchSiblings('s1', 'main')).rejects.toThrow(
        'MAUI HybridWebView not available'
      );
    });
  });

  // ============================================
  // uploadAsset() Tests (plan items 5.1–5.6)
  // ============================================

  describe('uploadAsset()', () => {
    const ASSET_JSON = JSON.stringify({
      assetId: 'asset-maui-1',
      contentType: 'image/png',
      name: 'shot.png',
      sizeBytes: 1024,
    });

    function makeFile(name = 'shot.png', type = 'image/png'): File {
      return new File(['px'], name, { type });
    }

    function makeBlob(type = 'image/jpeg'): Blob {
      return new Blob(['px'], { type });
    }

    it('calls InvokeDotNet("UploadAsset") with [sessionId, fileName, contentType, base64]', async () => {
      mockHybridWebView.InvokeDotNet.mockResolvedValue(ASSET_JSON);

      await transport.uploadAsset('sess-1', makeFile('shot.png', 'image/png'));

      expect(mockHybridWebView.InvokeDotNet).toHaveBeenCalledWith(
        'UploadAsset',
        ['sess-1', 'shot.png', 'image/png', expect.any(String)]
      );
    });

    it('uses "upload" as filename for a plain Blob', async () => {
      mockHybridWebView.InvokeDotNet.mockResolvedValue(ASSET_JSON);

      await transport.uploadAsset('sess-1', makeBlob());

      const [, params] = mockHybridWebView.InvokeDotNet.mock.calls[0];
      expect(params[1]).toBe('upload'); // fileName
    });

    it('uses file.name as default filename for a File', async () => {
      mockHybridWebView.InvokeDotNet.mockResolvedValue(ASSET_JSON);

      await transport.uploadAsset('sess-1', makeFile('document.pdf', 'application/pdf'));

      const [, params] = mockHybridWebView.InvokeDotNet.mock.calls[0];
      expect(params[1]).toBe('document.pdf');
    });

    it('name param overrides the file.name', async () => {
      mockHybridWebView.InvokeDotNet.mockResolvedValue(ASSET_JSON);

      await transport.uploadAsset('sess-1', makeFile('original.png'), 'renamed.png');

      const [, params] = mockHybridWebView.InvokeDotNet.mock.calls[0];
      expect(params[1]).toBe('renamed.png');
    });

    it('returns the parsed AssetReference from the JSON response', async () => {
      mockHybridWebView.InvokeDotNet.mockResolvedValue(ASSET_JSON);

      const result = await transport.uploadAsset('sess-1', makeFile());

      expect(result).toEqual({
        assetId: 'asset-maui-1',
        contentType: 'image/png',
        name: 'shot.png',
        sizeBytes: 1024,
      });
    });

    it('throws when HybridWebView is not available', async () => {
      delete (global as any).window.HybridWebView;

      await expect(transport.uploadAsset('sess-1', makeFile())).rejects.toThrow(
        'MAUI HybridWebView not available'
      );
    });
  });

  describe('disconnect()', () => {
    beforeEach(async () => {
      mockHybridWebView.InvokeDotNet.mockResolvedValue('stream-123');
      await transport.connect({
        messages: [{ content: 'Test', role: 'user' }],
        sessionId: 's1',
        branchId: 'main'
      });
    });

    it('calls StopStream with streamId', () => {
      transport.disconnect();

      expect(mockHybridWebView.InvokeDotNet).toHaveBeenCalledWith('StopStream', ['stream-123']);
    });

    it('removes event listener', () => {
      const initialListeners = mockHybridWebView.listeners.length;
      transport.disconnect();

      expect(mockHybridWebView.listeners.length).toBeLessThan(initialListeners);
    });

    it('sets connected to false', () => {
      transport.disconnect();
      expect(transport.connected).toBe(false);
    });

    it('clears streamId', () => {
      transport.disconnect();
      // Verify by trying to trigger message - should be ignored
      const eventHandler = vi.fn();
      transport.onEvent(eventHandler);
      mockHybridWebView.triggerMessage('agent_event:stream-123:{}');
      expect(eventHandler).not.toHaveBeenCalled();
    });

    it('works when HybridWebView not available', () => {
      delete (global as any).window.HybridWebView;

      expect(() => transport.disconnect()).not.toThrow();
      expect(transport.connected).toBe(false);
    });
  });
});
