/**
 * workspace-send.svelte.test.ts
 *
 * Tests for WorkspaceImpl.send() and the workspace.client getter introduced
 * in proposal 014:
 *   - send() threads runConfig through to USER_TEXT_INPUT events
 *   - send() injects asset:// URIs into message content when attachments provided
 *   - workspace.client exposes the injected AgentClientLike
 *
 * Strategy: inject a FakeAgentClient via the _client option. After init,
 * call send() and inspect the event captured by the run() spy.
 *
 * Test type: integration (svelte project — browser environment).
 */

import { describe, it, expect, vi } from 'vitest';
import { createWorkspace } from '../workspace.svelte.ts';
import type { AgentClientLike, CreateWorkspaceOptions } from '../types.ts';
import { EventTypes } from '@hpd/hpd-agent-client';
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
	AgentRunInputEvent,
	EventSubscription,
} from '@hpd/hpd-agent-client';

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
	runImpl?: () => Promise<void>
): AgentClientLike {
	const subscription = (): EventSubscription => ({ dispose: vi.fn() });
	return {
		// Event runtime — resolves immediately by default so send() completes
		run: vi.fn(runImpl ?? (async () => {})),
		on: vi.fn(() => subscription()),
		onAny: vi.fn(() => subscription()),
		onError: vi.fn(() => subscription()),
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

function capturedRunInput(client: AgentClientLike): AgentRunInputEvent | undefined {
	const spy = vi.mocked(client.run);
	const lastCall = spy.mock.calls[spy.mock.calls.length - 1];
	return lastCall?.[0];
}

function capturedTextInput(client: AgentClientLike) {
	const input = capturedRunInput(client);
	if (input?.type !== EventTypes.USER_TEXT_INPUT) {
		throw new Error(`Expected USER_TEXT_INPUT, got ${input?.type ?? 'none'}`);
	}
	return input;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('workspace.send() — runConfig threading', () => {
	it('passes runConfig to the input event when provided', async () => {
		const sessions = [makeSession('s1')];
		const branches = new Map([['s1', [makeBranch('main', 's1')]]]);
		const client = makeFakeClient(sessions, branches);
		const ws = await buildWorkspace(client);

		const runConfig = { providerKey: 'anthropic', modelId: 'claude-sonnet-4-6', chat: { temperature: 0.7 } };
		await ws.send('hello', { runConfig });

		const input = capturedTextInput(client);
		expect(input.runConfig).toEqual(runConfig);
	});

	it('passes undefined runConfig when send() called with no options', async () => {
		const sessions = [makeSession('s1')];
		const branches = new Map([['s1', [makeBranch('main', 's1')]]]);
		const client = makeFakeClient(sessions, branches);
		const ws = await buildWorkspace(client);

		await ws.send('hello');

		const input = capturedTextInput(client);
		expect(input.runConfig).toBeUndefined();
	});

	it('passes undefined runConfig when SendOptions has no runConfig field', async () => {
		const sessions = [makeSession('s1')];
		const branches = new Map([['s1', [makeBranch('main', 's1')]]]);
		const client = makeFakeClient(sessions, branches);
		const ws = await buildWorkspace(client);

		await ws.send('hello', {});

		const input = capturedTextInput(client);
		expect(input.runConfig).toBeUndefined();
	});
});

describe('workspace.send() — attachment injection', () => {
	it('message content is unchanged when no attachments provided', async () => {
		const sessions = [makeSession('s1')];
		const branches = new Map([['s1', [makeBranch('main', 's1')]]]);
		const client = makeFakeClient(sessions, branches);
		const ws = await buildWorkspace(client);

		await ws.send('hello there');

		const input = capturedTextInput(client);
		expect(input.text).toBe('hello there');
	});

	it('message content is unchanged when attachments is an empty array', async () => {
		const sessions = [makeSession('s1')];
		const branches = new Map([['s1', [makeBranch('main', 's1')]]]);
		const client = makeFakeClient(sessions, branches);
		const ws = await buildWorkspace(client);

		await ws.send('hello there', { attachments: [] });

		const input = capturedTextInput(client);
		expect(input.text).toBe('hello there');
	});

	it('injects asset:// URI for a single attachment', async () => {
		const sessions = [makeSession('s1')];
		const branches = new Map([['s1', [makeBranch('main', 's1')]]]);
		const client = makeFakeClient(sessions, branches);
		const ws = await buildWorkspace(client);

		await ws.send('look at this', { attachments: [ASSET] });

		const input = capturedTextInput(client);
		expect(input.text).toContain('asset://asset-abc');
	});

	it('injects asset:// URIs for multiple attachments', async () => {
		const sessions = [makeSession('s1')];
		const branches = new Map([['s1', [makeBranch('main', 's1')]]]);
		const client = makeFakeClient(sessions, branches);
		const ws = await buildWorkspace(client);

		await ws.send('see both', { attachments: [ASSET, ASSET2] });

		const input = capturedTextInput(client);
		expect(input.text).toContain('asset://asset-abc');
		expect(input.text).toContain('asset://asset-xyz');
	});

	it('message content starts with the original text', async () => {
		const sessions = [makeSession('s1')];
		const branches = new Map([['s1', [makeBranch('main', 's1')]]]);
		const client = makeFakeClient(sessions, branches);
		const ws = await buildWorkspace(client);

		await ws.send('my message', { attachments: [ASSET] });

		const input = capturedTextInput(client);
		expect(input.text).toMatch(/^my message/);
	});
});

describe('workspace.send() — event scope', () => {
	it('stamps active session and branch onto the input event', async () => {
		const sessions = [makeSession('s1')];
		const branches = new Map([['s1', [makeBranch('main', 's1')]]]);
		const client = makeFakeClient(sessions, branches);
		const ws = await buildWorkspace(client);

		await ws.send('hi');

		const input = capturedTextInput(client);
		expect(input.sessionId).toBe('s1');
		expect(input.branchId).toBe('main');
	});

	it('stamps the active agent id when configured', async () => {
		const sessions = [makeSession('s1')];
		const branches = new Map([['s1', [makeBranch('main', 's1')]]]);
		const client = makeFakeClient(sessions, branches);
		const ws = await buildWorkspace(client, { agentId: 'research-agent' });

		await ws.send('hi');

		const input = capturedTextInput(client);
		expect(input.agentId).toBe('research-agent');
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
