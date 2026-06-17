/**
 * workspace-delete.svelte.test.ts
 *
 * Tests for deleteThread() — recursive option, descendant-is-active navigation,
 * cache eviction, and the recursive query param being passed through to the transport.
 *
 * Strategy: inject a FakeTransport via _transport so no real server is needed.
 * Thread metadata (childThreads, ancestors, sibling pointers) is set up manually
 * to reflect the tree shapes each test needs.
 */

import { describe, it, expect, vi } from 'vitest';
import { createWorkspace } from '../workspace.svelte.ts';
import type { AgentClientLike, CreateWorkspaceOptions } from '../types.ts';
import type {
	Thread,
	ThreadMessage,
	Session,
	CreateSessionRequest,
	UpdateSessionRequest,
	CreateThreadRequest,
	ForkThreadRequest,
	SiblingThread,
} from '@hpd-research/hpd-agent-client';

// ============================================
// Helpers (same pattern as workspace-transport tests)
// ============================================

async function tick(ms = 100): Promise<void> {
	await new Promise((r) => setTimeout(r, ms));
}

function makeSession(id: string): Session {
	return {
		id,
		createdAt: new Date().toISOString(),
		lastActivity: new Date().toISOString(),
		metadata: {}
	};
}

function makeThread(id: string, sessionId: string, overrides: Partial<Thread> = {}): Thread {
	return {
		id,
		sessionId,
		name: id,
		description: '',
		forkedFrom: undefined,
		forkedAtMessageIndex: undefined,
		ancestors: {},
		createdAt: new Date().toISOString(),
		lastActivity: new Date().toISOString(),
		messageCount: 0,
		tags: [],
		siblingIndex: 0,
		totalSiblings: 1,
		isOriginal: true,
		originalThreadId: undefined,
		previousSiblingId: undefined,
		nextSiblingId: undefined,
		childThreads: [],
		totalForks: 0,
		...overrides
	};
}

function makeFakeAgentClient(
	sessions: Session[],
	threadsPerSession: Map<string, Thread[]>
): AgentClientLike {
	const client: AgentClientLike = {
		run: vi.fn(async () => new Promise<void>(() => {})),
		on: vi.fn(() => ({ dispose: vi.fn() })),
		onAny: vi.fn(() => ({ dispose: vi.fn() })),
		onError: vi.fn(() => ({ dispose: vi.fn() })),
		abort: vi.fn(),

		listSessions: vi.fn(async () => sessions),
		getSession: vi.fn(async (id: string) => sessions.find((s) => s.id === id) ?? null),
		createSession: vi.fn(async (opts?: CreateSessionRequest) => {
			const s = makeSession(opts?.sessionId ?? `session-${Date.now()}`);
			sessions.push(s);
			return s;
		}),
		updateSession: vi.fn(async (id: string, req: UpdateSessionRequest) => {
			const s = sessions.find((s) => s.id === id)!;
			return { ...s, metadata: { ...s.metadata, ...req.metadata } };
		}),
		deleteSession: vi.fn(),

		listThreads: vi.fn(async (sid: string) => threadsPerSession.get(sid) ?? []),
		getThread: vi.fn(async (sid: string, bid: string) => {
			const list = threadsPerSession.get(sid) ?? [];
			return list.find((b) => b.id === bid) ?? null;
		}),
		createThread: vi.fn(async (sid: string, opts?: CreateThreadRequest) => {
			const b = makeThread(opts?.threadId ?? `thread-${Date.now()}`, sid);
			const list = threadsPerSession.get(sid) ?? [];
			list.push(b);
			threadsPerSession.set(sid, list);
			return b;
		}),
		forkThread: vi.fn(async (sid: string, bid: string, opts: ForkThreadRequest) => {
			const b = makeThread(opts.newThreadId ?? `fork-${Date.now()}`, sid, {
				forkedFrom: bid,
				forkedAtMessageId: opts.fromMessageId,
				isOriginal: false,
				originalThreadId: bid
			});
			const list = threadsPerSession.get(sid) ?? [];
			list.push(b);
			threadsPerSession.set(sid, list);
			return b;
		}),
		deleteThread: vi.fn(),
		getThreadMessages: vi.fn(async (): Promise<ThreadMessage[]> => []),

		getThreadSiblings: vi.fn(async (): Promise<SiblingThread[]> => []),
		getNextSibling: vi.fn(async (): Promise<Thread | null> => null),
		getPreviousSibling: vi.fn(async (): Promise<Thread | null> => null),

		listAgents: vi.fn(async () => []),
		getAgent: vi.fn(async () => null),
		createAgent: vi.fn(),
		updateAgent: vi.fn(),
		deleteAgent: vi.fn(),
		uploadContent: vi.fn(),
	};

	return client;
}

async function buildWorkspace(
	client: AgentClientLike,
	overrides: Partial<CreateWorkspaceOptions> = {}
) {
	const ws = createWorkspace({
		baseUrl: 'http://fake',
		_client: client,
		...overrides
	});
	await tick(200);
	return ws;
}

// ============================================
// Group A: recursive query param forwarding
// ============================================

describe('deleteThread — transport call with recursive option', () => {
	it('calls client.deleteThread without recursive when not passed', async () => {
		const sessions = [makeSession('s1')];
		const threads = new Map([
			['s1', [makeThread('main', 's1'), makeThread('fork-1', 's1')]]
		]);
		const client = makeFakeAgentClient(sessions, threads);
		const ws = await buildWorkspace(client);

		await ws.deleteThread('fork-1');

		const spy = client.deleteThread as ReturnType<typeof vi.fn>;
		expect(spy).toHaveBeenCalledOnce();
		const [, , opts] = spy.mock.calls[0];
		// No recursive option passed — should be undefined or falsy
		expect(opts?.recursive).toBeFalsy();
	});

	it('calls client.deleteThread with recursive: true when passed', async () => {
		const sessions = [makeSession('s1')];
		// Set up fork-1 with a child so the frontend won't short-circuit
		const fork1 = makeThread('fork-1', 's1', {
			forkedFrom: 'main',
			childThreads: ['fork-1a']
		});
		const fork1a = makeThread('fork-1a', 's1', {
			forkedFrom: 'fork-1',
			ancestors: { '0': 'main', '1': 'fork-1' }
		});
		const threads = new Map([['s1', [makeThread('main', 's1'), fork1, fork1a]]]);
		const client = makeFakeAgentClient(sessions, threads);
		const ws = await buildWorkspace(client);

		// Switch to fork-1a first so fork-1 is not active (no navigation needed)
		await ws.switchThread('fork-1a');
		// Now delete fork-1 recursively (active is fork-1a, a descendant — will navigate first)
		// Switch to main to avoid descendant navigation complexity in this test
		await ws.switchThread('main');
		await ws.deleteThread('fork-1', { recursive: true });

		const spy = client.deleteThread as ReturnType<typeof vi.fn>;
		const lastCall = spy.mock.calls[spy.mock.calls.length - 1];
		expect(lastCall[2]).toEqual({ recursive: true });
	});

	it('calls client.deleteThread with recursive: false when explicitly passed false', async () => {
		const sessions = [makeSession('s1')];
		const threads = new Map([['s1', [makeThread('main', 's1'), makeThread('fork-1', 's1')]]]);
		const client = makeFakeAgentClient(sessions, threads);
		const ws = await buildWorkspace(client);

		await ws.deleteThread('fork-1', { recursive: false });

		const spy = client.deleteThread as ReturnType<typeof vi.fn>;
		const [, , opts] = spy.mock.calls[0];
		expect(opts?.recursive).toBeFalsy();
	});
});

// ============================================
// Group B: local thread map and cache eviction after delete
// ============================================

describe('deleteThread — local thread map and cache cleanup', () => {
	it('removes the deleted thread from #threads', async () => {
		const sessions = [makeSession('s1')];
		const threads = new Map([['s1', [makeThread('main', 's1'), makeThread('fork-1', 's1')]]]);
		const client = makeFakeAgentClient(sessions, threads);
		const ws = await buildWorkspace(client);

		expect(ws.threads.has('fork-1')).toBe(true);
		await ws.deleteThread('fork-1');
		expect(ws.threads.has('fork-1')).toBe(false);
	});

	it('removes all subtree threads from #threads on recursive delete', async () => {
		const sessions = [makeSession('s1')];
		const fork1 = makeThread('fork-1', 's1', {
			forkedFrom: 'main',
			childThreads: ['fork-1a', 'fork-1b']
		});
		const fork1a = makeThread('fork-1a', 's1', {
			forkedFrom: 'fork-1',
			ancestors: { '0': 'main', '1': 'fork-1' }
		});
		const fork1b = makeThread('fork-1b', 's1', {
			forkedFrom: 'fork-1',
			ancestors: { '0': 'main', '1': 'fork-1' }
		});
		const threads = new Map([['s1', [makeThread('main', 's1'), fork1, fork1a, fork1b]]]);
		const client = makeFakeAgentClient(sessions, threads);
		const ws = await buildWorkspace(client);

		// Switch to main so none of the subtree threads are active
		// (main is already active after init)
		await ws.deleteThread('fork-1', { recursive: true });

		expect(ws.threads.has('fork-1')).toBe(false);
		expect(ws.threads.has('fork-1a')).toBe(false);
		expect(ws.threads.has('fork-1b')).toBe(false);
		// main is untouched
		expect(ws.threads.has('main')).toBe(true);
	});

	it('evicts deleted thread from AgentState cache', async () => {
		const sessions = [makeSession('s1')];
		const threads = new Map([['s1', [makeThread('main', 's1'), makeThread('fork-1', 's1')]]]);
		const client = makeFakeAgentClient(sessions, threads);
		const ws = await buildWorkspace(client);

		// Warm up fork-1 in the cache
		await ws.switchThread('fork-1');
		await ws.switchThread('main'); // navigate away so fork-1 is not active

		await ws.deleteThread('fork-1');

		// Add fork-1 back to the fake transport's thread list, then refresh the workspace
		// thread map so switchThread can find it (switchThread checks #threads, not transport directly).
		const list = threads.get('s1')!;
		list.push(makeThread('fork-1', 's1'));
		await ws.refreshThread('fork-1');
		await ws.switchThread('fork-1');

		// Cache was evicted — should have called getThreadMessages again
		const spy = client.getThreadMessages as ReturnType<typeof vi.fn>;
		const fork1Calls = spy.mock.calls.filter((args: unknown[]) => args[1] === 'fork-1');
		expect(fork1Calls.length).toBe(2); // once on first load, once after eviction
	});
});

// ============================================
// Group C: navigation away from active/descendant thread before delete
// ============================================

describe('deleteThread — navigation before delete', () => {
	it('navigates to nextSiblingId when active thread is deleted', async () => {
		const sessions = [makeSession('s1')];
		// fork-1 and fork-2 are siblings; fork-1 has nextSiblingId = fork-2
		const fork1 = makeThread('fork-1', 's1', {
			forkedFrom: 'main',
			siblingIndex: 0,
			totalSiblings: 2,
			nextSiblingId: 'fork-2'
		});
		const fork2 = makeThread('fork-2', 's1', {
			forkedFrom: 'main',
			siblingIndex: 1,
			totalSiblings: 2,
			previousSiblingId: 'fork-1'
		});
		const threads = new Map([['s1', [makeThread('main', 's1'), fork1, fork2]]]);
		const client = makeFakeAgentClient(sessions, threads);
		const ws = await buildWorkspace(client);

		await ws.switchThread('fork-1'); // make fork-1 active
		await ws.deleteThread('fork-1');

		// Should have navigated to fork-2 before deleting
		expect(ws.activeThreadId).toBe('fork-2');
	});

	it('navigates to previousSiblingId when active thread has no next sibling', async () => {
		const sessions = [makeSession('s1')];
		const fork1 = makeThread('fork-1', 's1', {
			forkedFrom: 'main',
			siblingIndex: 0,
			totalSiblings: 2,
			nextSiblingId: 'fork-2'
		});
		const fork2 = makeThread('fork-2', 's1', {
			forkedFrom: 'main',
			siblingIndex: 1,
			totalSiblings: 2,
			previousSiblingId: 'fork-1'
		});
		const threads = new Map([['s1', [makeThread('main', 's1'), fork1, fork2]]]);
		const client = makeFakeAgentClient(sessions, threads);
		const ws = await buildWorkspace(client);

		await ws.switchThread('fork-2'); // make fork-2 active (last sibling)
		await ws.deleteThread('fork-2');

		// Should have navigated to fork-1 (previousSiblingId)
		expect(ws.activeThreadId).toBe('fork-1');
	});

	it('navigates away when active thread is a descendant of the deleted subtree root', async () => {
		const sessions = [makeSession('s1')];
		// Tree: main → fork-1 → fork-1a (active)
		const fork1 = makeThread('fork-1', 's1', {
			forkedFrom: 'main',
			childThreads: ['fork-1a'],
			siblingIndex: 0,
			totalSiblings: 2,
			nextSiblingId: 'fork-2'
		});
		const fork1a = makeThread('fork-1a', 's1', {
			forkedFrom: 'fork-1',
			// ancestors includes fork-1 — this is how the descendant check works
			ancestors: { '0': 'main', '1': 'fork-1' }
		});
		const fork2 = makeThread('fork-2', 's1', {
			forkedFrom: 'main',
			siblingIndex: 1,
			totalSiblings: 2,
			previousSiblingId: 'fork-1'
		});
		const threads = new Map([['s1', [makeThread('main', 's1'), fork1, fork1a, fork2]]]);
		const client = makeFakeAgentClient(sessions, threads);
		const ws = await buildWorkspace(client);

		// Make fork-1a active (it's a descendant of fork-1)
		await ws.switchThread('fork-1a');
		expect(ws.activeThreadId).toBe('fork-1a');

		// Delete fork-1 recursively — active thread is inside the subtree
		await ws.deleteThread('fork-1', { recursive: true });

		// Should have navigated away from the subtree (to fork-2, the next sibling of fork-1)
		expect(ws.activeThreadId).toBe('fork-2');
		expect(ws.activeThreadId).not.toBe('fork-1');
		expect(ws.activeThreadId).not.toBe('fork-1a');
	});

	it('does not navigate when deleting a thread that is not active and not an ancestor', async () => {
		const sessions = [makeSession('s1')];
		const fork1 = makeThread('fork-1', 's1', { forkedFrom: 'main' });
		const threads = new Map([['s1', [makeThread('main', 's1'), fork1]]]);
		const client = makeFakeAgentClient(sessions, threads);
		const ws = await buildWorkspace(client);

		// Stay on main, delete fork-1
		expect(ws.activeThreadId).toBe('main');
		await ws.deleteThread('fork-1');

		// Active thread unchanged
		expect(ws.activeThreadId).toBe('main');
	});
});
