/**
 * workspace-reactive-owner.svelte.test.ts
 *
 * Regression tests for the reactive-owner pitfall with createWorkspace().
 *
 * Root cause: `WorkspaceImpl` uses `$state`/`$derived` class fields.  When
 * `new WorkspaceImpl(options)` is called outside any Svelte reactive context
 * (e.g. at module level: `export const ws = createWorkspace(...)`) those
 * fields are orphaned — state changes never propagate to derived values or
 * template renders.
 *
 * The fix wraps the constructor in `$effect.root(...)` inside `createWorkspace`
 * so the instance always has a live reactive owner regardless of call site.
 *
 * These tests simulate the module-level usage pattern by calling
 * `createWorkspace` outside of any component — if the fix were removed, the
 * `state` derived would remain null even after init completes and messages
 * would never be visible.
 *
 * Test type: browser (svelte project) — runes must be live.
 */

import { describe, it, expect, vi } from 'vitest';
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
// Helpers (copied from workspace-send pattern)
// ---------------------------------------------------------------------------

async function tick(ms = 150): Promise<void> {
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

function makeThreadMessage(id: string, role: 'user' | 'assistant', text: string): ThreadMessage {
	return {
		id,
		role,
		contents: [{ $type: 'text', text }],
		timestamp: new Date().toISOString(),
	} as ThreadMessage;
}

const DUMMY_CONTENT: ContentReference = { contentId: 'a', version: 'rev:1', contentType: 'image/png', name: 'a.png' };

function makeFakeClient(
	sessions: Session[],
	threads: Map<string, Thread[]>,
	messages: Map<string, ThreadMessage[]> = new Map(),
): AgentClientLike {
	return {
		run: vi.fn(async () => {}),
		on: vi.fn(() => ({ dispose: vi.fn() })),
		onAny: vi.fn(() => ({ dispose: vi.fn() })),
		onError: vi.fn(() => ({ dispose: vi.fn() })),
		abort: vi.fn(),
		listSessions: vi.fn(async (_opts?: ListSessionsOptions) => sessions),
		getSession: vi.fn(async (id: string) => sessions.find((s) => s.id === id) ?? null),
		createSession: vi.fn(async (opts?: CreateSessionRequest) => makeSession(opts?.sessionId ?? 'new')),
		updateSession: vi.fn(async (id: string, _req: UpdateSessionRequest) => sessions.find((s) => s.id === id)!),
		deleteSession: vi.fn(async () => {}),
		listThreads: vi.fn(async (sid: string) => threads.get(sid) ?? []),
		getThread: vi.fn(async (sid: string, bid: string) => (threads.get(sid) ?? []).find((b) => b.id === bid) ?? null),
		createThread: vi.fn(async (sid: string, opts?: CreateThreadRequest) => makeThread(opts?.threadId ?? 'new', sid)),
		forkThread: vi.fn(async (sid: string, _bid: string, opts: ForkThreadRequest) => makeThread(opts.newThreadId ?? 'fork', sid, { isOriginal: false })),
		deleteThread: vi.fn(async () => {}),
		getThreadMessages: vi.fn(async (sid: string, bid: string): Promise<ThreadMessage[]> => messages.get(`${sid}:${bid}`) ?? []),
		getThreadSiblings: vi.fn(async (): Promise<SiblingThread[]> => []),
		getNextSibling: vi.fn(async (): Promise<Thread | null> => null),
		getPreviousSibling: vi.fn(async (): Promise<Thread | null> => null),
		listAgents: vi.fn(async (): Promise<AgentSummaryDto[]> => []),
		getAgent: vi.fn(async (): Promise<StoredAgentDto | null> => null),
		createAgent: vi.fn(async (_req: CreateAgentRequest): Promise<StoredAgentDto> => { throw new Error('not implemented'); }),
		updateAgent: vi.fn(async (_id: string, _req: UpdateAgentRequest): Promise<StoredAgentDto> => { throw new Error('not implemented'); }),
		deleteAgent: vi.fn(async () => {}),
		uploadContent: vi.fn(async (): Promise<ContentReference> => DUMMY_CONTENT),
	};
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('createWorkspace — reactive owner safety (module-level usage)', () => {
	/**
	 * The core regression: createWorkspace called outside any component must
	 * still produce a workspace whose reactive state propagates correctly.
	 * Without $effect.root, workspace.state stays null even after init.
	 */
	it('state becomes non-null after init when called outside a component', async () => {
		const session = makeSession('s1');
		const thread = makeThread('main', 's1');
		const client = makeFakeClient([session], new Map([['s1', [thread]]]));

		// Simulate module-level call — no component context at all
		const ws = createWorkspace({ baseUrl: 'http://fake', _client: client });
		await tick();

		expect(ws.state).not.toBeNull();
	});

	it('activeSessionId is set after init', async () => {
		const session = makeSession('s1');
		const thread = makeThread('main', 's1');
		const client = makeFakeClient([session], new Map([['s1', [thread]]]));

		const ws = createWorkspace({ baseUrl: 'http://fake', _client: client });
		await tick();

		expect(ws.activeSessionId).toBe('s1');
		expect(ws.activeThreadId).toBe('main');
	});

	it('loads history messages into state.messages', async () => {
		const session = makeSession('s1');
		const thread = makeThread('main', 's1');
		const rawMessages = [
			makeThreadMessage('m1', 'user', 'Hello'),
			makeThreadMessage('m2', 'assistant', 'Hi there'),
		];
		const client = makeFakeClient(
			[session],
			new Map([['s1', [thread]]]),
			new Map([['s1:main', rawMessages]])
		);

		const ws = createWorkspace({ baseUrl: 'http://fake', _client: client });
		await tick();

		expect(ws.state).not.toBeNull();
		expect(ws.state!.messages.length).toBe(2);
		expect(ws.state!.messages[0].content).toBe('Hello');
		expect(ws.state!.messages[1].content).toBe('Hi there');
	});

	it('state updates reactively when selectSession is called', async () => {
		const s1 = makeSession('s1');
		const s2 = makeSession('s2');
		const b1 = makeThread('main', 's1');
		const b2 = makeThread('main', 's2');
		const rawMessages = [makeThreadMessage('m1', 'user', 'From session 2')];
		const client = makeFakeClient(
			[s1, s2],
			new Map([['s1', [b1]], ['s2', [b2]]]),
			new Map([['s2:main', rawMessages]])
		);

		const ws = createWorkspace({ baseUrl: 'http://fake', _client: client });
		await tick();

		expect(ws.activeSessionId).toBe('s1');
		expect(ws.state!.messages.length).toBe(0);

		await ws.selectSession('s2');
		await tick();

		expect(ws.activeSessionId).toBe('s2');
		expect(ws.state).not.toBeNull();
		expect(ws.state!.messages.length).toBe(1);
		expect(ws.state!.messages[0].content).toBe('From session 2');
	});

	it('state.sessions list is populated after init', async () => {
		const sessions = [makeSession('s1'), makeSession('s2'), makeSession('s3')];
		const client = makeFakeClient(sessions, new Map([['s1', [makeThread('main', 's1')]]]));

		const ws = createWorkspace({ baseUrl: 'http://fake', _client: client });
		await tick();

		expect(ws.sessions.length).toBe(3);
	});

	it('loading flag transitions false → true → false', async () => {
		let resolveInit!: () => void;
		const blocked = new Promise<void>((r) => { resolveInit = r; });

		const session = makeSession('s1');
		const thread = makeThread('main', 's1');
		const client = makeFakeClient([session], new Map([['s1', [thread]]]));
		// Make listSessions block until we release it
		(client.listSessions as ReturnType<typeof vi.fn>).mockImplementation(async () => {
			await blocked;
			return [session];
		});

		const ws = createWorkspace({ baseUrl: 'http://fake', _client: client });

		// loading should be true while init is blocked
		expect(ws.loading).toBe(true);

		resolveInit();
		await tick();

		expect(ws.loading).toBe(false);
	});
});
