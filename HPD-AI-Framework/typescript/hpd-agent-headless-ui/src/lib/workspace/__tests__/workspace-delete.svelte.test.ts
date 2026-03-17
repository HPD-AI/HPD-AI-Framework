/**
 * workspace-delete.svelte.test.ts
 *
 * Tests for deleteBranch() — recursive option, descendant-is-active navigation,
 * cache eviction, and the recursive query param being passed through to the transport.
 *
 * Strategy: inject a FakeTransport via _transport so no real server is needed.
 * Branch metadata (childBranches, ancestors, sibling pointers) is set up manually
 * to reflect the tree shapes each test needs.
 */

import { describe, it, expect, vi } from 'vitest';
import { createWorkspace } from '../workspace.svelte.ts';
import type { AgentClientLike, CreateWorkspaceOptions } from '../types.ts';
import type {
	Branch,
	BranchMessage,
	Session,
	CreateSessionRequest,
	UpdateSessionRequest,
	CreateBranchRequest,
	ForkBranchRequest,
	SiblingBranch,
} from '@hpd/hpd-agent-client';

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
		...overrides
	};
}

function makeFakeAgentClient(
	sessions: Session[],
	branchesPerSession: Map<string, Branch[]>
): AgentClientLike {
	const client: AgentClientLike = {
		stream: vi.fn(async () => new Promise<void>(() => {})),
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

		listBranches: vi.fn(async (sid: string) => branchesPerSession.get(sid) ?? []),
		getBranch: vi.fn(async (sid: string, bid: string) => {
			const list = branchesPerSession.get(sid) ?? [];
			return list.find((b) => b.id === bid) ?? null;
		}),
		createBranch: vi.fn(async (sid: string, opts?: CreateBranchRequest) => {
			const b = makeBranch(opts?.branchId ?? `branch-${Date.now()}`, sid);
			const list = branchesPerSession.get(sid) ?? [];
			list.push(b);
			branchesPerSession.set(sid, list);
			return b;
		}),
		forkBranch: vi.fn(async (sid: string, bid: string, opts: ForkBranchRequest) => {
			const b = makeBranch(opts.newBranchId ?? `fork-${Date.now()}`, sid, {
				forkedFrom: bid,
				forkedAtMessageIndex: opts.fromMessageIndex,
				isOriginal: false,
				originalBranchId: bid
			});
			const list = branchesPerSession.get(sid) ?? [];
			list.push(b);
			branchesPerSession.set(sid, list);
			return b;
		}),
		deleteBranch: vi.fn(),
		getBranchMessages: vi.fn(async (): Promise<BranchMessage[]> => []),

		getBranchSiblings: vi.fn(async (): Promise<SiblingBranch[]> => []),
		getNextSibling: vi.fn(async (): Promise<Branch | null> => null),
		getPreviousSibling: vi.fn(async (): Promise<Branch | null> => null),

		listAgents: vi.fn(async () => []),
		getAgent: vi.fn(async () => null),
		createAgent: vi.fn(),
		updateAgent: vi.fn(),
		deleteAgent: vi.fn(),
		uploadAsset: vi.fn(),
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

describe('deleteBranch — transport call with recursive option', () => {
	it('calls client.deleteBranch without recursive when not passed', async () => {
		const sessions = [makeSession('s1')];
		const branches = new Map([
			['s1', [makeBranch('main', 's1'), makeBranch('fork-1', 's1')]]
		]);
		const client = makeFakeAgentClient(sessions, branches);
		const ws = await buildWorkspace(client);

		await ws.deleteBranch('fork-1');

		const spy = client.deleteBranch as ReturnType<typeof vi.fn>;
		expect(spy).toHaveBeenCalledOnce();
		const [, , opts] = spy.mock.calls[0];
		// No recursive option passed — should be undefined or falsy
		expect(opts?.recursive).toBeFalsy();
	});

	it('calls client.deleteBranch with recursive: true when passed', async () => {
		const sessions = [makeSession('s1')];
		// Set up fork-1 with a child so the frontend won't short-circuit
		const fork1 = makeBranch('fork-1', 's1', {
			forkedFrom: 'main',
			childBranches: ['fork-1a']
		});
		const fork1a = makeBranch('fork-1a', 's1', {
			forkedFrom: 'fork-1',
			ancestors: { '0': 'main', '1': 'fork-1' }
		});
		const branches = new Map([['s1', [makeBranch('main', 's1'), fork1, fork1a]]]);
		const client = makeFakeAgentClient(sessions, branches);
		const ws = await buildWorkspace(client);

		// Switch to fork-1a first so fork-1 is not active (no navigation needed)
		await ws.switchBranch('fork-1a');
		// Now delete fork-1 recursively (active is fork-1a, a descendant — will navigate first)
		// Switch to main to avoid descendant navigation complexity in this test
		await ws.switchBranch('main');
		await ws.deleteBranch('fork-1', { recursive: true });

		const spy = client.deleteBranch as ReturnType<typeof vi.fn>;
		const lastCall = spy.mock.calls[spy.mock.calls.length - 1];
		expect(lastCall[2]).toEqual({ recursive: true });
	});

	it('calls client.deleteBranch with recursive: false when explicitly passed false', async () => {
		const sessions = [makeSession('s1')];
		const branches = new Map([['s1', [makeBranch('main', 's1'), makeBranch('fork-1', 's1')]]]);
		const client = makeFakeAgentClient(sessions, branches);
		const ws = await buildWorkspace(client);

		await ws.deleteBranch('fork-1', { recursive: false });

		const spy = client.deleteBranch as ReturnType<typeof vi.fn>;
		const [, , opts] = spy.mock.calls[0];
		expect(opts?.recursive).toBeFalsy();
	});
});

// ============================================
// Group B: local branch map and cache eviction after delete
// ============================================

describe('deleteBranch — local branch map and cache cleanup', () => {
	it('removes the deleted branch from #branches', async () => {
		const sessions = [makeSession('s1')];
		const branches = new Map([['s1', [makeBranch('main', 's1'), makeBranch('fork-1', 's1')]]]);
		const client = makeFakeAgentClient(sessions, branches);
		const ws = await buildWorkspace(client);

		expect(ws.branches.has('fork-1')).toBe(true);
		await ws.deleteBranch('fork-1');
		expect(ws.branches.has('fork-1')).toBe(false);
	});

	it('removes all subtree branches from #branches on recursive delete', async () => {
		const sessions = [makeSession('s1')];
		const fork1 = makeBranch('fork-1', 's1', {
			forkedFrom: 'main',
			childBranches: ['fork-1a', 'fork-1b']
		});
		const fork1a = makeBranch('fork-1a', 's1', {
			forkedFrom: 'fork-1',
			ancestors: { '0': 'main', '1': 'fork-1' }
		});
		const fork1b = makeBranch('fork-1b', 's1', {
			forkedFrom: 'fork-1',
			ancestors: { '0': 'main', '1': 'fork-1' }
		});
		const branches = new Map([['s1', [makeBranch('main', 's1'), fork1, fork1a, fork1b]]]);
		const client = makeFakeAgentClient(sessions, branches);
		const ws = await buildWorkspace(client);

		// Switch to main so none of the subtree branches are active
		// (main is already active after init)
		await ws.deleteBranch('fork-1', { recursive: true });

		expect(ws.branches.has('fork-1')).toBe(false);
		expect(ws.branches.has('fork-1a')).toBe(false);
		expect(ws.branches.has('fork-1b')).toBe(false);
		// main is untouched
		expect(ws.branches.has('main')).toBe(true);
	});

	it('evicts deleted branch from AgentState cache', async () => {
		const sessions = [makeSession('s1')];
		const branches = new Map([['s1', [makeBranch('main', 's1'), makeBranch('fork-1', 's1')]]]);
		const client = makeFakeAgentClient(sessions, branches);
		const ws = await buildWorkspace(client);

		// Warm up fork-1 in the cache
		await ws.switchBranch('fork-1');
		await ws.switchBranch('main'); // navigate away so fork-1 is not active

		await ws.deleteBranch('fork-1');

		// Add fork-1 back to the fake transport's branch list, then refresh the workspace
		// branch map so switchBranch can find it (switchBranch checks #branches, not transport directly).
		const list = branches.get('s1')!;
		list.push(makeBranch('fork-1', 's1'));
		await ws.refreshBranch('fork-1');
		await ws.switchBranch('fork-1');

		// Cache was evicted — should have called getBranchMessages again
		const spy = client.getBranchMessages as ReturnType<typeof vi.fn>;
		const fork1Calls = spy.mock.calls.filter((args: unknown[]) => args[1] === 'fork-1');
		expect(fork1Calls.length).toBe(2); // once on first load, once after eviction
	});
});

// ============================================
// Group C: navigation away from active/descendant branch before delete
// ============================================

describe('deleteBranch — navigation before delete', () => {
	it('navigates to nextSiblingId when active branch is deleted', async () => {
		const sessions = [makeSession('s1')];
		// fork-1 and fork-2 are siblings; fork-1 has nextSiblingId = fork-2
		const fork1 = makeBranch('fork-1', 's1', {
			forkedFrom: 'main',
			siblingIndex: 0,
			totalSiblings: 2,
			nextSiblingId: 'fork-2'
		});
		const fork2 = makeBranch('fork-2', 's1', {
			forkedFrom: 'main',
			siblingIndex: 1,
			totalSiblings: 2,
			previousSiblingId: 'fork-1'
		});
		const branches = new Map([['s1', [makeBranch('main', 's1'), fork1, fork2]]]);
		const client = makeFakeAgentClient(sessions, branches);
		const ws = await buildWorkspace(client);

		await ws.switchBranch('fork-1'); // make fork-1 active
		await ws.deleteBranch('fork-1');

		// Should have navigated to fork-2 before deleting
		expect(ws.activeBranchId).toBe('fork-2');
	});

	it('navigates to previousSiblingId when active branch has no next sibling', async () => {
		const sessions = [makeSession('s1')];
		const fork1 = makeBranch('fork-1', 's1', {
			forkedFrom: 'main',
			siblingIndex: 0,
			totalSiblings: 2,
			nextSiblingId: 'fork-2'
		});
		const fork2 = makeBranch('fork-2', 's1', {
			forkedFrom: 'main',
			siblingIndex: 1,
			totalSiblings: 2,
			previousSiblingId: 'fork-1'
		});
		const branches = new Map([['s1', [makeBranch('main', 's1'), fork1, fork2]]]);
		const client = makeFakeAgentClient(sessions, branches);
		const ws = await buildWorkspace(client);

		await ws.switchBranch('fork-2'); // make fork-2 active (last sibling)
		await ws.deleteBranch('fork-2');

		// Should have navigated to fork-1 (previousSiblingId)
		expect(ws.activeBranchId).toBe('fork-1');
	});

	it('navigates away when active branch is a descendant of the deleted subtree root', async () => {
		const sessions = [makeSession('s1')];
		// Tree: main → fork-1 → fork-1a (active)
		const fork1 = makeBranch('fork-1', 's1', {
			forkedFrom: 'main',
			childBranches: ['fork-1a'],
			siblingIndex: 0,
			totalSiblings: 2,
			nextSiblingId: 'fork-2'
		});
		const fork1a = makeBranch('fork-1a', 's1', {
			forkedFrom: 'fork-1',
			// ancestors includes fork-1 — this is how the descendant check works
			ancestors: { '0': 'main', '1': 'fork-1' }
		});
		const fork2 = makeBranch('fork-2', 's1', {
			forkedFrom: 'main',
			siblingIndex: 1,
			totalSiblings: 2,
			previousSiblingId: 'fork-1'
		});
		const branches = new Map([['s1', [makeBranch('main', 's1'), fork1, fork1a, fork2]]]);
		const client = makeFakeAgentClient(sessions, branches);
		const ws = await buildWorkspace(client);

		// Make fork-1a active (it's a descendant of fork-1)
		await ws.switchBranch('fork-1a');
		expect(ws.activeBranchId).toBe('fork-1a');

		// Delete fork-1 recursively — active branch is inside the subtree
		await ws.deleteBranch('fork-1', { recursive: true });

		// Should have navigated away from the subtree (to fork-2, the next sibling of fork-1)
		expect(ws.activeBranchId).toBe('fork-2');
		expect(ws.activeBranchId).not.toBe('fork-1');
		expect(ws.activeBranchId).not.toBe('fork-1a');
	});

	it('does not navigate when deleting a branch that is not active and not an ancestor', async () => {
		const sessions = [makeSession('s1')];
		const fork1 = makeBranch('fork-1', 's1', { forkedFrom: 'main' });
		const branches = new Map([['s1', [makeBranch('main', 's1'), fork1]]]);
		const client = makeFakeAgentClient(sessions, branches);
		const ws = await buildWorkspace(client);

		// Stay on main, delete fork-1
		expect(ws.activeBranchId).toBe('main');
		await ws.deleteBranch('fork-1');

		// Active branch unchanged
		expect(ws.activeBranchId).toBe('main');
	});
});
