/**
 * workspace-edit.svelte.test.ts
 *
 * Tests for WorkspaceImpl.editMessage() — specifically the sibling-flattening
 * behaviour introduced to fix the "linear chain of forks-of-forks" bug.
 *
 * Key invariants under test:
 *
 * 1. First edit from the original branch → fork from original (main).
 * 2. Second edit from a fork that shares the same forkAtIndex → fork from
 *    the ORIGINAL branch (not from the current fork).  This is the fix: all
 *    edits of the same user message become flat siblings of the original
 *    branch rather than a linear chain.
 * 3. Edit from a fork at a DIFFERENT forkAtIndex → fork from the current
 *    branch (no ancestor walk needed; different fork group).
 * 4. Edit from the original branch again → still forks from original.
 *
 * Test type: integration (svelte project — browser environment).
 */

import { describe, it, expect, vi, beforeEach } from 'vitest';
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

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

async function tick(ms = 200): Promise<void> {
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

function makeUserMessage(text: string, idx: number): BranchMessage {
	return { id: `msg-${idx}`, role: 'user', contents: [{ $type: 'text', text }], timestamp: new Date().toISOString() };
}

function makeAssistantMessage(text: string, idx: number): BranchMessage {
	return { id: `msg-a-${idx}`, role: 'assistant', contents: [{ $type: 'text', text }], timestamp: new Date().toISOString() };
}

// ---------------------------------------------------------------------------
// Fake client builder
//
// Maintains internal state:
//   bySession: Map<sessionId, Branch[]>   — used by listBranches
//   byId:      Map<branchId, Branch>      — used by getBranch / forkBranch
//   messages:  Map<branchId, BranchMessage[]>
// ---------------------------------------------------------------------------

function makeFakeClient(
	sessions: Session[],
	initialBranches: Branch[],
	initialMessages: Map<string, BranchMessage[]> = new Map()
) {
	const sessionId = sessions[0]?.id ?? 's1';
	const byId = new Map<string, Branch>(initialBranches.map(b => [b.id, b]));
	const messages = new Map<string, BranchMessage[]>(initialMessages);

	// Ensure all initial branches are in messages map
	for (const b of initialBranches) {
		if (!messages.has(b.id)) messages.set(b.id, []);
	}

	/** Re-index all siblings at a given fork point, updating navigation pointers. */
	function reindexSiblings(sourceId: string, forkAtIndex: number) {
		const forks = Array.from(byId.values())
			.filter(b => b.forkedFrom === sourceId && b.forkedAtMessageIndex === forkAtIndex)
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
		stream: vi.fn(async () => {}),
		abort: vi.fn(),

		listSessions: vi.fn(async () => sessions),
		getSession: vi.fn(async (id) => sessions.find(s => s.id === id) ?? null),
		createSession: vi.fn(async (opts?: CreateSessionRequest) => makeSession(opts?.sessionId ?? 'new')),
		updateSession: vi.fn(async (id, _req: UpdateSessionRequest) => sessions.find(s => s.id === id)!),
		deleteSession: vi.fn(async () => {}),

		// listBranches returns all branches for the session
		listBranches: vi.fn(async () => Array.from(byId.values()).filter(b => b.sessionId === sessionId)),
		getBranch: vi.fn(async (_sid, bid) => byId.get(bid) ?? null),

		createBranch: vi.fn(async (_sid, opts?: CreateBranchRequest) => {
			const b = makeBranch(opts?.branchId ?? 'new-branch', sessionId);
			byId.set(b.id, b);
			messages.set(b.id, []);
			return b;
		}),

		forkBranch: vi.fn(async (_sid, sourceBranchId: string, opts: ForkBranchRequest) => {
			const source = byId.get(sourceBranchId)!;
			const forkAtIndex = opts.fromMessageIndex;

			const existingForks = Array.from(byId.values()).filter(
				b => b.forkedFrom === sourceBranchId && b.forkedAtMessageIndex === forkAtIndex
			);

			const newBranch = makeBranch(opts.newBranchId ?? `fork-${byId.size}`, sessionId, {
				isOriginal: false,
				forkedFrom: sourceBranchId,
				forkedAtMessageIndex: forkAtIndex,
				siblingIndex: existingForks.length + 1,
				totalSiblings: existingForks.length + 2,
				// Slightly offset timestamps to get stable ordering
				createdAt: new Date(Date.now() + existingForks.length * 10).toISOString(),
			});

			byId.set(newBranch.id, newBranch);

			// Copy messages up to and including forkAtIndex
			const srcMsgs = messages.get(sourceBranchId) ?? [];
			messages.set(newBranch.id, srcMsgs.slice(0, forkAtIndex + 1));

			reindexSiblings(sourceBranchId, forkAtIndex);

			return newBranch;
		}),

		deleteBranch: vi.fn(async () => {}),
		getBranchMessages: vi.fn(async (_sid, bid): Promise<BranchMessage[]> => messages.get(bid) ?? []),

		getBranchSiblings: vi.fn(async (): Promise<SiblingBranch[]> => []),
		getNextSibling: vi.fn(async (): Promise<Branch | null> => null),
		getPreviousSibling: vi.fn(async (): Promise<Branch | null> => null),

		listAgents: vi.fn(async (): Promise<AgentSummaryDto[]> => []),
		getAgent: vi.fn(async (): Promise<StoredAgentDto | null> => null),
		createAgent: vi.fn(async (_req: CreateAgentRequest): Promise<StoredAgentDto> => { throw new Error('not implemented'); }),
		updateAgent: vi.fn(async (_id: string, _req: UpdateAgentRequest): Promise<StoredAgentDto> => { throw new Error('not implemented'); }),
		deleteAgent: vi.fn(async () => {}),
		uploadAsset: vi.fn(async (): Promise<AssetReference> => ({ assetId: 'a', contentType: 'image/png', name: 'x.png' })),
	};

	return { client, byId, messages };
}

async function buildWorkspace(client: AgentClientLike, overrides: Partial<CreateWorkspaceOptions> = {}) {
	const ws = createWorkspace({ baseUrl: 'http://fake', _client: client, ...overrides });
	await tick();
	return ws;
}

function capturedForkCalls(client: AgentClientLike) {
	return vi.mocked(client.forkBranch).mock.calls;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('editMessage() — source branch selection', () => {
	const SID = 's1';

	// 5 messages: user(0), asst(1), user(2), asst(3), user(4)
	// We'll edit the user message at index 4 (forkAtIndex = 3)
	const baseMsgs: BranchMessage[] = [
		makeUserMessage('hi', 0),
		makeAssistantMessage('hello', 1),
		makeUserMessage('who are you', 2),
		makeAssistantMessage('I am an AI', 3),
		makeUserMessage('edit me', 4),
	];

	function setup() {
		const sessions = [makeSession(SID)];
		const mainBranch = makeBranch('main', SID);
		const msgMap = new Map([['main', [...baseMsgs]]]);
		const { client, byId, messages } = makeFakeClient(sessions, [mainBranch], msgMap);
		return { sessions, client, byId, messages };
	}

	it('first edit from original branch forks from the original branch', async () => {
		const { client } = setup();
		const ws = await buildWorkspace(client);

		await ws.editMessage(4, 'new content');

		const forkCalls = capturedForkCalls(client);
		expect(forkCalls).toHaveLength(1);
		expect(forkCalls[0][1]).toBe('main');                // sourceBranchId
		expect(forkCalls[0][2].fromMessageIndex).toBe(3);   // forkAtIndex = messageIndex - 1
	});

	it('second edit from a fork at the same forkAtIndex forks from the ORIGINAL branch, not the current fork', async () => {
		const { client } = setup();
		const ws = await buildWorkspace(client);

		// First edit: creates fork1 from main at index 3
		await ws.editMessage(4, 'first edit');
		const fork1Id = capturedForkCalls(client)[0][2].newBranchId!;

		// Now on fork1. Edit again at same messageIndex.
		await ws.editMessage(4, 'second edit');

		const forkCalls = capturedForkCalls(client);
		expect(forkCalls).toHaveLength(2);

		// Second fork must come from 'main', not fork1
		expect(forkCalls[1][1]).toBe('main');
		expect(forkCalls[1][1]).not.toBe(fork1Id);
		expect(forkCalls[1][2].fromMessageIndex).toBe(3);
	});

	it('third and fourth edits still fork from the original branch (flat siblings)', async () => {
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

		const forkBranches = Array.from(byId.values()).filter(b => !b.isOriginal);
		expect(forkBranches).toHaveLength(3);
		for (const b of forkBranches) {
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
		const fork1Id = capturedForkCalls(client)[0][2].newBranchId!;

		// On fork1, edit an EARLIER message at messageIndex=2 (forkAtIndex=1)
		// fork1.forkedAtMessageIndex=3 !== 1, so should fork from fork1
		await ws.editMessage(2, 'edit msg 2');

		const forkCalls = capturedForkCalls(client);
		expect(forkCalls).toHaveLength(2);
		expect(forkCalls[1][1]).toBe(fork1Id);             // forks from current fork
		expect(forkCalls[1][2].fromMessageIndex).toBe(1);  // forkAtIndex = 2 - 1
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

		const forkBranches = Array.from(byId.values()).filter(b => !b.isOriginal);
		expect(forkBranches).toHaveLength(3);
		for (const b of forkBranches) {
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
		await ws.switchBranch('main');
		await tick();

		// Edit again from main
		await ws.editMessage(4, 'from main again');

		const forkCalls = capturedForkCalls(client);
		expect(forkCalls).toHaveLength(2);
		expect(forkCalls[0][1]).toBe('main');
		expect(forkCalls[1][1]).toBe('main');
	});
});
