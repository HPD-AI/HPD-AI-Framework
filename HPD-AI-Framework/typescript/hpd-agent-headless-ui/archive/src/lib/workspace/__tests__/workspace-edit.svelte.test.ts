/**
 * workspace-edit.svelte.test.ts
 *
 * Tests for WorkspaceImpl.editMessage() — specifically the sibling-flattening
 * behaviour introduced to fix the "linear chain of forks-of-forks" bug.
 *
 * Key invariants under test:
 *
 * 1. First edit from the original thread → fork from original (main).
 * 2. Second edit from a fork that shares the same forkAtIndex → fork from
 *    the ORIGINAL thread (not from the current fork).  This is the fix: all
 *    edits of the same user message become flat siblings of the original
 *    thread rather than a linear chain.
 * 3. Edit from a fork at a DIFFERENT forkAtIndex → fork from the current
 *    thread (no ancestor walk needed; different fork group).
 * 4. Edit from the original thread again → still forks from original.
 *
 * Test type: integration (svelte project — browser environment).
 */

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { createWorkspace } from '../workspace.svelte.ts';
import type { AgentClientLike, CreateWorkspaceOptions } from '../types.ts';
import type {
	Thread,
	ThreadMessage,
	Session,
	SiblingThread,
	CreateSessionRequest,
	UpdateSessionRequest,
	ListSessionsOptions,
	CreateThreadRequest,
	ForkThreadRequest,
	AgentSummaryDto,
	StoredAgentDto,
	CreateAgentRequest,
	UpdateAgentRequest,
	ContentReference,
} from '@hpd-research/hpd-agent-client';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

async function tick(ms = 200): Promise<void> {
	await new Promise((r) => setTimeout(r, ms));
}

function makeSession(id: string): Session {
	return { id, createdAt: new Date().toISOString(), lastActivity: new Date().toISOString(), metadata: {} };
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
		...overrides,
	};
}

function makeUserMessage(text: string, idx: number): ThreadMessage {
	return { id: `msg-${idx}`, role: 'user', contents: [{ $type: 'text', text }], timestamp: new Date().toISOString() };
}

function makeAssistantMessage(text: string, idx: number): ThreadMessage {
	return { id: `msg-a-${idx}`, role: 'assistant', contents: [{ $type: 'text', text }], timestamp: new Date().toISOString() };
}

// ---------------------------------------------------------------------------
// Fake client builder
//
// Maintains internal state:
//   bySession: Map<sessionId, Thread[]>   — used by listThreads
//   byId:      Map<threadId, Thread>      — used by getThread / forkThread
//   messages:  Map<threadId, ThreadMessage[]>
// ---------------------------------------------------------------------------

function makeFakeClient(
	sessions: Session[],
	initialThreads: Thread[],
	initialMessages: Map<string, ThreadMessage[]> = new Map()
) {
	const sessionId = sessions[0]?.id ?? 's1';
	const byId = new Map<string, Thread>(initialThreads.map(b => [b.id, b]));
	const messages = new Map<string, ThreadMessage[]>(initialMessages);

	// Ensure all initial threads are in messages map
	for (const b of initialThreads) {
		if (!messages.has(b.id)) messages.set(b.id, []);
	}

	/** Re-index all siblings at a given fork point, updating navigation pointers. */
	function reindexSiblings(sourceId: string, forkAtMessageId: string) {
		const forks = Array.from(byId.values())
			.filter(b => b.forkedFrom === sourceId && b.forkedAtMessageId === forkAtMessageId)
			.sort((a, b) => a.createdAt.localeCompare(b.createdAt));

		const source = byId.get(sourceId)!;
		const all = [source, ...forks];
		const total = all.length;
		all.forEach((b, i) => {
			b.siblingIndex = i;
			b.totalSiblings = total;
			b.previousSiblingId = i > 0 ? all[i - 1].id : undefined;
			b.nextSiblingId = i < total - 1 ? all[i + 1].id : undefined;
		});
	}

	const client: AgentClientLike = {
		run: vi.fn(async () => {}),
		on: vi.fn(() => ({ dispose: vi.fn() })),
		onAny: vi.fn(() => ({ dispose: vi.fn() })),
		onError: vi.fn(() => ({ dispose: vi.fn() })),
		abort: vi.fn(),

		listSessions: vi.fn(async () => sessions),
		getSession: vi.fn(async (id) => sessions.find(s => s.id === id) ?? null),
		createSession: vi.fn(async (opts?: CreateSessionRequest) => makeSession(opts?.sessionId ?? 'new')),
		updateSession: vi.fn(async (id, _req: UpdateSessionRequest) => sessions.find(s => s.id === id)!),
		deleteSession: vi.fn(async () => {}),

		// listThreads returns all threads for the session
		listThreads: vi.fn(async () => Array.from(byId.values()).filter(b => b.sessionId === sessionId)),
		getThread: vi.fn(async (_sid, bid) => byId.get(bid) ?? null),

		createThread: vi.fn(async (_sid, opts?: CreateThreadRequest) => {
			const b = makeThread(opts?.threadId ?? 'new-thread', sessionId);
			byId.set(b.id, b);
			messages.set(b.id, []);
			return b;
		}),

		forkThread: vi.fn(async (_sid, sourceThreadId: string, opts: ForkThreadRequest) => {
			const source = byId.get(sourceThreadId)!;
			const srcMsgs = messages.get(sourceThreadId) ?? [];
			const forkAtIndex = srcMsgs.findIndex(message => message.id === opts.fromMessageId);
			if (forkAtIndex < 0) throw new Error(`Missing fork message ${opts.fromMessageId}`);

			const existingForks = Array.from(byId.values()).filter(
				b => b.forkedFrom === sourceThreadId && b.forkedAtMessageId === opts.fromMessageId
			);

			const newThread = makeThread(opts.newThreadId ?? `fork-${byId.size}`, sessionId, {
				isOriginal: false,
				forkedFrom: sourceThreadId,
				forkedAtMessageId: opts.fromMessageId,
				forkedAtMessageIndex: forkAtIndex,
				siblingIndex: existingForks.length + 1,
				totalSiblings: existingForks.length + 2,
				// Slightly offset timestamps to get stable ordering
				createdAt: new Date(Date.now() + existingForks.length * 10).toISOString(),
			});

			byId.set(newThread.id, newThread);

			// Copy messages up to and including forkAtIndex
			messages.set(newThread.id, srcMsgs.slice(0, forkAtIndex + 1));

			reindexSiblings(sourceThreadId, opts.fromMessageId);

			return newThread;
		}),

		deleteThread: vi.fn(async () => {}),
		getThreadMessages: vi.fn(async (_sid, bid): Promise<ThreadMessage[]> => messages.get(bid) ?? []),

		getThreadSiblings: vi.fn(async (): Promise<SiblingThread[]> => []),
		getNextSibling: vi.fn(async (): Promise<Thread | null> => null),
		getPreviousSibling: vi.fn(async (): Promise<Thread | null> => null),

		listAgents: vi.fn(async (): Promise<AgentSummaryDto[]> => []),
		getAgent: vi.fn(async (): Promise<StoredAgentDto | null> => null),
		createAgent: vi.fn(async (_req: CreateAgentRequest): Promise<StoredAgentDto> => { throw new Error('not implemented'); }),
		updateAgent: vi.fn(async (_id: string, _req: UpdateAgentRequest): Promise<StoredAgentDto> => { throw new Error('not implemented'); }),
		deleteAgent: vi.fn(async () => {}),
		uploadContent: vi.fn(async (): Promise<ContentReference> => ({ contentId: 'a', version: 'rev:1', contentType: 'image/png', name: 'x.png' })),
	};

	return { client, byId, messages };
}

async function buildWorkspace(client: AgentClientLike, overrides: Partial<CreateWorkspaceOptions> = {}) {
	const ws = createWorkspace({ baseUrl: 'http://fake', _client: client, ...overrides });
	await tick();
	return ws;
}

function capturedForkCalls(client: AgentClientLike) {
	return vi.mocked(client.forkThread).mock.calls;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('editMessage() — source thread selection', () => {
	const SID = 's1';

	// 5 messages: user(0), asst(1), user(2), asst(3), user(4)
	// We'll edit the user message at index 4 (forkAtIndex = 3)
	const baseMsgs: ThreadMessage[] = [
		makeUserMessage('hi', 0),
		makeAssistantMessage('hello', 1),
		makeUserMessage('who are you', 2),
		makeAssistantMessage('I am an AI', 3),
		makeUserMessage('edit me', 4),
	];

	function setup() {
		const sessions = [makeSession(SID)];
		const mainThread = makeThread('main', SID);
		const msgMap = new Map([['main', [...baseMsgs]]]);
		const { client, byId, messages } = makeFakeClient(sessions, [mainThread], msgMap);
		return { sessions, client, byId, messages };
	}

	it('first edit from original thread forks from the original thread', async () => {
		const { client } = setup();
		const ws = await buildWorkspace(client);

		await ws.editMessage(4, 'new content');

		const forkCalls = capturedForkCalls(client);
		expect(forkCalls).toHaveLength(1);
		expect(forkCalls[0][1]).toBe('main');                // sourceThreadId
		expect(forkCalls[0][2].fromMessageId).toBe('msg-a-3'); // forkAtIndex = messageIndex - 1
	});

	it('second edit from a fork at the same forkAtIndex forks from the ORIGINAL thread, not the current fork', async () => {
		const { client } = setup();
		const ws = await buildWorkspace(client);

		// First edit: creates fork1 from main at index 3
		await ws.editMessage(4, 'first edit');
		const fork1Id = capturedForkCalls(client)[0][2].newThreadId!;

		// Now on fork1. Edit again at same messageIndex.
		await ws.editMessage(4, 'second edit');

		const forkCalls = capturedForkCalls(client);
		expect(forkCalls).toHaveLength(2);

		// Second fork must come from 'main', not fork1
		expect(forkCalls[1][1]).toBe('main');
		expect(forkCalls[1][1]).not.toBe(fork1Id);
		expect(forkCalls[1][2].fromMessageId).toBe('msg-a-3');
	});

	it('third and fourth edits still fork from the original thread (flat siblings)', async () => {
		const { client } = setup();
		const ws = await buildWorkspace(client);

		await ws.editMessage(4, 'first edit');
		await ws.editMessage(4, 'second edit');
		await ws.editMessage(4, 'third edit');

		const forkCalls = capturedForkCalls(client);
		expect(forkCalls).toHaveLength(3);
		for (const call of forkCalls) {
			expect(call[1]).toBe('main');
		}
	});

	it('after three edits all forks are flat siblings with totalSiblings=4', async () => {
		const { client, byId } = setup();
		const ws = await buildWorkspace(client);

		await ws.editMessage(4, 'first edit');
		await ws.editMessage(4, 'second edit');
		await ws.editMessage(4, 'third edit');

		const forkThreads = Array.from(byId.values()).filter(b => !b.isOriginal);
		expect(forkThreads).toHaveLength(3);
		for (const b of forkThreads) {
			expect(b.totalSiblings).toBe(4);
			expect(b.forkedFrom).toBe('main');
		}
		expect(byId.get('main')!.totalSiblings).toBe(4);
	});

	it('edit from a fork at a DIFFERENT forkAtIndex forks from the current fork (different group)', async () => {
		const { client } = setup();
		const ws = await buildWorkspace(client);

		// First edit at messageIndex=4 (forkAtIndex=3) → fork1 from main
		await ws.editMessage(4, 'edit msg 4');
		const fork1Id = capturedForkCalls(client)[0][2].newThreadId!;

		// On fork1, edit an EARLIER message at messageIndex=2 (forkAtIndex=1)
		// fork1.forkedAtMessageId=msg-a-3 !== msg-a-1, so should fork from fork1
		await ws.editMessage(2, 'edit msg 2');

		const forkCalls = capturedForkCalls(client);
		expect(forkCalls).toHaveLength(2);
		expect(forkCalls[1][1]).toBe(fork1Id);             // forks from current fork
		expect(forkCalls[1][2].fromMessageId).toBe('msg-a-1'); // forkAtIndex = 2 - 1
	});

	it('retry (re-edit with same content) creates a flat sibling, not a fork-of-fork', async () => {
		const { client } = setup();
		const ws = await buildWorkspace(client);

		// First edit: creates fork1 from main at index 3
		await ws.editMessage(4, 'same content');

		// Retry (re-edit with the same content from the fork) — simulates what RetryButton does
		await ws.editMessage(4, 'same content');

		const forkCalls = capturedForkCalls(client);
		expect(forkCalls).toHaveLength(2);
		// Both must fork from main, not from fork1
		expect(forkCalls[0][1]).toBe('main');
		expect(forkCalls[1][1]).toBe('main');
	});

	it('three retries all fork from original (flat siblings, totalSiblings=4)', async () => {
		const { client, byId } = setup();
		const ws = await buildWorkspace(client);

		await ws.editMessage(4, 'same content');
		await ws.editMessage(4, 'same content');
		await ws.editMessage(4, 'same content');

		const forkThreads = Array.from(byId.values()).filter(b => !b.isOriginal);
		expect(forkThreads).toHaveLength(3);
		for (const b of forkThreads) {
			expect(b.forkedFrom).toBe('main');
			expect(b.totalSiblings).toBe(4);
		}
		expect(byId.get('main')!.totalSiblings).toBe(4);
	});

	it('navigating back to original and editing again still forks from original', async () => {
		const { client } = setup();
		const ws = await buildWorkspace(client);

		// Edit from main → fork1
		await ws.editMessage(4, 'from main');

		// Navigate back to main
		await ws.switchThread('main');
		await tick();

		// Edit again from main
		await ws.editMessage(4, 'from main again');

		const forkCalls = capturedForkCalls(client);
		expect(forkCalls).toHaveLength(2);
		expect(forkCalls[0][1]).toBe('main');
		expect(forkCalls[1][1]).toBe('main');
	});
});
