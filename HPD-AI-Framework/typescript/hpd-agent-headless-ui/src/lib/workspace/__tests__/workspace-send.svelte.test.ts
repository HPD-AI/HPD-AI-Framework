/**
 * workspace-send.svelte.test.ts
 *
 * Tests for WorkspaceImpl.send() and the workspace.client getter introduced
 * in proposal 014:
 *   - send() threads runConfig through to stream() options
 *   - send() injects asset:// URIs into message content when attachments provided
 *   - send() always passes resetClientState: true
 *   - send() forwards workspace-level clientToolKits
 *   - workspace.client exposes the injected AgentClientLike
 *
 * Strategy: inject a FakeAgentClient via the _client option. After init,
 * call send() and inspect the arguments captured by the stream() spy.
 *
 * Test type: integration (svelte project — browser environment).
 */

import { describe, it, expect, vi } from 'vitest';
import { createWorkspace } from '../workspace.svelte.ts';
import type { AgentClientLike, CreateWorkspaceOptions } from '../types.ts';
import type {
	Branch,
	BranchMessage,
	Session,
	SiblingBranch,
	CreateSessionRequest,
	UpdateSessionRequest,
	ListSessionsOptions,
	CreateBranchRequest,
	ForkBranchRequest,
	AgentSummaryDto,
	StoredAgentDto,
	CreateAgentRequest,
	UpdateAgentRequest,
	AssetReference,
} from '@hpd/hpd-agent-client';
import type { StreamOptions, EventHandlers } from '@hpd/hpd-agent-client';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

async function tick(ms = 150): Promise<void> {
	await new Promise((r) => setTimeout(r, ms));
}

function makeSession(id: string): Session {
	return { id, createdAt: new Date().toISOString(), lastActivity: new Date().toISOString(), metadata: {} };
}

function makeBranch(id: string, sessionId: string, overrides: Partial<Branch> = {}): Branch {
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
		originalBranchId: undefined,
		previousSiblingId: undefined,
		nextSiblingId: undefined,
		childBranches: [],
		totalForks: 0,
		...overrides,
	};
}

const ASSET: AssetReference = { assetId: 'asset-abc', contentType: 'image/png', name: 'shot.png' };
const ASSET2: AssetReference = { assetId: 'asset-xyz', contentType: 'text/plain', name: 'doc.txt' };

function makeFakeClient(
	sessions: Session[],
	branches: Map<string, Branch[]>,
	streamImpl?: () => Promise<void>
): AgentClientLike {
	return {
		// Streaming — resolves immediately by default so send() completes
		stream: vi.fn(streamImpl ?? (async () => {})),
		abort: vi.fn(),

		// Session CRUD
		listSessions: vi.fn(async (_opts?: ListSessionsOptions) => sessions),
		getSession: vi.fn(async (id: string) => sessions.find((s) => s.id === id) ?? null),
		createSession: vi.fn(async (opts?: CreateSessionRequest) => makeSession(opts?.sessionId ?? 'new')),
		updateSession: vi.fn(async (id: string, _req: UpdateSessionRequest) =>
			sessions.find((s) => s.id === id)!
		),
		deleteSession: vi.fn(async () => {}),

		// Branch CRUD
		listBranches: vi.fn(async (sid: string) => branches.get(sid) ?? []),
		getBranch: vi.fn(async (sid: string, bid: string) =>
			(branches.get(sid) ?? []).find((b) => b.id === bid) ?? null
		),
		createBranch: vi.fn(async (sid: string, opts?: CreateBranchRequest) =>
			makeBranch(opts?.branchId ?? 'new-branch', sid)
		),
		forkBranch: vi.fn(async (sid: string, _bid: string, opts: ForkBranchRequest) =>
			makeBranch(opts.newBranchId ?? 'fork', sid, { isOriginal: false })
		),
		deleteBranch: vi.fn(async () => {}),
		getBranchMessages: vi.fn(async (): Promise<BranchMessage[]> => []),

		// Sibling navigation
		getBranchSiblings: vi.fn(async (): Promise<SiblingBranch[]> => []),
		getNextSibling: vi.fn(async (): Promise<Branch | null> => null),
		getPreviousSibling: vi.fn(async (): Promise<Branch | null> => null),

		// Agent CRUD
		listAgents: vi.fn(async (): Promise<AgentSummaryDto[]> => []),
		getAgent: vi.fn(async (): Promise<StoredAgentDto | null> => null),
		createAgent: vi.fn(async (_req: CreateAgentRequest): Promise<StoredAgentDto> => {
			throw new Error('not implemented');
		}),
		updateAgent: vi.fn(async (_id: string, _req: UpdateAgentRequest): Promise<StoredAgentDto> => {
			throw new Error('not implemented');
		}),
		deleteAgent: vi.fn(async () => {}),

		// Asset upload
		uploadAsset: vi.fn(async (): Promise<AssetReference> => ASSET),
	};
}

async function buildWorkspace(client: AgentClientLike, overrides: Partial<CreateWorkspaceOptions> = {}) {
	const ws = createWorkspace({ baseUrl: 'http://fake', _client: client, ...overrides });
	await tick();
	return ws;
}

// Extracts the StreamOptions argument from the stream() spy
function capturedStreamOptions(client: AgentClientLike): StreamOptions | undefined {
	const spy = vi.mocked(client.stream);
	const lastCall = spy.mock.calls[spy.mock.calls.length - 1];
	return lastCall?.[4]; // stream(sessionId, branchId, messages, handlers, options)
}

function capturedMessages(client: AgentClientLike): Array<{ content: string; role?: string }> {
	const spy = vi.mocked(client.stream);
	const lastCall = spy.mock.calls[spy.mock.calls.length - 1];
	return lastCall?.[2] ?? [];
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('workspace.send() — runConfig threading', () => {
	it('passes runConfig to stream() options when provided', async () => {
		const sessions = [makeSession('s1')];
		const branches = new Map([['s1', [makeBranch('main', 's1')]]]);
		const client = makeFakeClient(sessions, branches);
		const ws = await buildWorkspace(client);

		const runConfig = { providerKey: 'anthropic', modelId: 'claude-sonnet-4-6', chat: { temperature: 0.7 } };
		await ws.send('hello', { runConfig });

		const opts = capturedStreamOptions(client);
		expect(opts?.runConfig).toEqual(runConfig);
	});

	it('passes undefined runConfig when send() called with no options', async () => {
		const sessions = [makeSession('s1')];
		const branches = new Map([['s1', [makeBranch('main', 's1')]]]);
		const client = makeFakeClient(sessions, branches);
		const ws = await buildWorkspace(client);

		await ws.send('hello');

		const opts = capturedStreamOptions(client);
		expect(opts?.runConfig).toBeUndefined();
	});

	it('passes undefined runConfig when SendOptions has no runConfig field', async () => {
		const sessions = [makeSession('s1')];
		const branches = new Map([['s1', [makeBranch('main', 's1')]]]);
		const client = makeFakeClient(sessions, branches);
		const ws = await buildWorkspace(client);

		await ws.send('hello', {});

		const opts = capturedStreamOptions(client);
		expect(opts?.runConfig).toBeUndefined();
	});
});

describe('workspace.send() — attachment injection', () => {
	it('message content is unchanged when no attachments provided', async () => {
		const sessions = [makeSession('s1')];
		const branches = new Map([['s1', [makeBranch('main', 's1')]]]);
		const client = makeFakeClient(sessions, branches);
		const ws = await buildWorkspace(client);

		await ws.send('hello there');

		const messages = capturedMessages(client);
		expect(messages).toHaveLength(1);
		expect(messages[0].content).toBe('hello there');
	});

	it('message content is unchanged when attachments is an empty array', async () => {
		const sessions = [makeSession('s1')];
		const branches = new Map([['s1', [makeBranch('main', 's1')]]]);
		const client = makeFakeClient(sessions, branches);
		const ws = await buildWorkspace(client);

		await ws.send('hello there', { attachments: [] });

		const messages = capturedMessages(client);
		expect(messages[0].content).toBe('hello there');
	});

	it('injects asset:// URI for a single attachment', async () => {
		const sessions = [makeSession('s1')];
		const branches = new Map([['s1', [makeBranch('main', 's1')]]]);
		const client = makeFakeClient(sessions, branches);
		const ws = await buildWorkspace(client);

		await ws.send('look at this', { attachments: [ASSET] });

		const messages = capturedMessages(client);
		expect(messages[0].content).toContain('asset://asset-abc');
	});

	it('injects asset:// URIs for multiple attachments', async () => {
		const sessions = [makeSession('s1')];
		const branches = new Map([['s1', [makeBranch('main', 's1')]]]);
		const client = makeFakeClient(sessions, branches);
		const ws = await buildWorkspace(client);

		await ws.send('see both', { attachments: [ASSET, ASSET2] });

		const messages = capturedMessages(client);
		expect(messages[0].content).toContain('asset://asset-abc');
		expect(messages[0].content).toContain('asset://asset-xyz');
	});

	it('message content starts with the original text', async () => {
		const sessions = [makeSession('s1')];
		const branches = new Map([['s1', [makeBranch('main', 's1')]]]);
		const client = makeFakeClient(sessions, branches);
		const ws = await buildWorkspace(client);

		await ws.send('my message', { attachments: [ASSET] });

		const messages = capturedMessages(client);
		expect(messages[0].content).toMatch(/^my message/);
	});
});

describe('workspace.send() — always-on stream options', () => {
	it('always passes resetClientState: true', async () => {
		const sessions = [makeSession('s1')];
		const branches = new Map([['s1', [makeBranch('main', 's1')]]]);
		const client = makeFakeClient(sessions, branches);
		const ws = await buildWorkspace(client);

		await ws.send('hi');

		const opts = capturedStreamOptions(client);
		expect(opts?.resetClientState).toBe(true);
	});

	it('forwards clientToolKits from CreateWorkspaceOptions', async () => {
		const sessions = [makeSession('s1')];
		const branches = new Map([['s1', [makeBranch('main', 's1')]]]);
		const client = makeFakeClient(sessions, branches);
		const toolkit = { name: 'my-tools', startCollapsed: false, tools: [], description: 'test' };
		const ws = await buildWorkspace(client, { clientToolKits: [toolkit] });

		await ws.send('hi');

		const opts = capturedStreamOptions(client);
		expect(opts?.clientToolKits).toContainEqual(toolkit);
	});

	it('passes undefined clientToolKits when none configured', async () => {
		const sessions = [makeSession('s1')];
		const branches = new Map([['s1', [makeBranch('main', 's1')]]]);
		const client = makeFakeClient(sessions, branches);
		const ws = await buildWorkspace(client);

		await ws.send('hi');

		const opts = capturedStreamOptions(client);
		// May be undefined or empty — must not contain anything meaningful
		expect(opts?.clientToolKits ?? []).toHaveLength(0);
	});
});

describe('workspace.client getter', () => {
	it('exposes the injected AgentClientLike', async () => {
		const sessions = [makeSession('s1')];
		const branches = new Map([['s1', [makeBranch('main', 's1')]]]);
		const client = makeFakeClient(sessions, branches);
		const ws = await buildWorkspace(client);

		expect(ws.client).toBe(client);
	});
});
