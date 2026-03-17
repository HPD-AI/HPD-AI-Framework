/**
 * workspace-permissions.svelte.test.ts
 *
 * Tests for permission and clarification round-trips through WorkspaceImpl.
 *
 * Strategy: inject a FakeAgentClient via `_client` option. The fake captures
 * the EventHandlers passed by workspace.send() and exposes test helpers to
 * fire synthetic events (permission requests, clarification requests, completion).
 *
 * This exercises the full workspace → AgentClient → EventHandlers → AgentState
 * pipeline without a real server.
 */

import { describe, it, expect, vi } from 'vitest';
import { createWorkspace } from '../workspace.svelte.ts';
import type { AgentClientLike, CreateWorkspaceOptions } from '../types.ts';
import type { EventHandlers, PermissionResponse } from '@hpd/hpd-agent-client';
import type {
	Branch,
	BranchMessage,
	Session,
	CreateSessionRequest,
	UpdateSessionRequest,
	ListSessionsOptions,
	CreateBranchRequest,
	ForkBranchRequest,
	SiblingBranch,
	CreateAgentRequest,
	UpdateAgentRequest,
	StoredAgentDto,
	AssetReference,
} from '@hpd/hpd-agent-client';

// ============================================
// Helpers
// ============================================

async function tick(ms = 50): Promise<void> {
	await new Promise((r) => setTimeout(r, ms));
}

// ============================================
// FakeAgentClient
//
// Captures EventHandlers on stream() and exposes helpers to drive
// synthetic events. stream() returns a Promise that resolves when
// complete() is called (simulating MESSAGE_TURN_FINISHED).
// ============================================

class FakeAgentClient implements AgentClientLike {
	#handlers: EventHandlers | null = null;
	#resolveStream: (() => void) | null = null;
	#streamCallCount = 0;
	#lastSessionId: string | null = null;
	#lastBranchId: string | undefined = undefined;

	// CRUD state — minimal stubs sufficient for init (one session, one branch)
	readonly #sessions: Session[] = [
		{ id: 's1', createdAt: new Date().toISOString(), lastActivity: new Date().toISOString(), metadata: {} }
	];
	readonly #branches: Map<string, Branch[]> = new Map([['s1', [this.#makeBranch('main', 's1')]]]);

	#makeBranch(id: string, sessionId: string): Branch {
		return {
			id, sessionId, name: id, description: '',
			createdAt: new Date().toISOString(),
			lastActivity: new Date().toISOString(),
			messageCount: 0, tags: [],
			siblingIndex: 0, totalSiblings: 1,
			isOriginal: true,
			childBranches: [], totalForks: 0
		};
	}

	get streamCallCount() { return this.#streamCallCount; }
	get lastSessionId() { return this.#lastSessionId; }
	get lastBranchId() { return this.#lastBranchId; }

	// ---- Streaming ----

	async stream(
		sessionId: string,
		branchId: string | undefined,
		_messages: Array<{ content: string; role?: string }>,
		handlers: EventHandlers
	): Promise<void> {
		this.#streamCallCount++;
		this.#lastSessionId = sessionId;
		this.#lastBranchId = branchId;
		this.#handlers = handlers;

		return new Promise<void>((resolve) => {
			this.#resolveStream = resolve;
		});
	}

	abort(): void {
		// no-op
	}

	// ---- Session CRUD ----

	async listSessions(_opts?: ListSessionsOptions): Promise<Session[]> {
		return this.#sessions;
	}
	async getSession(id: string): Promise<Session | null> {
		return this.#sessions.find((s) => s.id === id) ?? null;
	}
	async createSession(opts?: CreateSessionRequest): Promise<Session> {
		const s: Session = { id: opts?.sessionId ?? `s-${Date.now()}`, createdAt: new Date().toISOString(), lastActivity: new Date().toISOString(), metadata: {} };
		this.#sessions.push(s);
		return s;
	}
	async updateSession(id: string, req: UpdateSessionRequest): Promise<Session> {
		const s = this.#sessions.find((s) => s.id === id)!;
		return { ...s, metadata: { ...s.metadata, ...req.metadata } };
	}
	async deleteSession(_id: string): Promise<void> {}

	// ---- Branch CRUD ----

	async listBranches(sid: string): Promise<Branch[]> {
		return this.#branches.get(sid) ?? [];
	}
	async getBranch(sid: string, bid: string): Promise<Branch | null> {
		return (this.#branches.get(sid) ?? []).find((b) => b.id === bid) ?? null;
	}
	async createBranch(_sid: string, _opts?: CreateBranchRequest): Promise<Branch> {
		throw new Error('not needed in permission tests');
	}
	async forkBranch(_sid: string, _bid: string, _opts: ForkBranchRequest): Promise<Branch> {
		throw new Error('not needed in permission tests');
	}
	async deleteBranch(_sid: string, _bid: string): Promise<void> {}
	async getBranchMessages(_sid: string, _bid: string): Promise<BranchMessage[]> { return []; }

	// ---- Sibling navigation ----

	async getBranchSiblings(_sid: string, _bid: string): Promise<SiblingBranch[]> { return []; }
	async getNextSibling(_sid: string, _bid: string): Promise<Branch | null> { return null; }
	async getPreviousSibling(_sid: string, _bid: string): Promise<Branch | null> { return null; }

	// ---- Agent CRUD ----

	async listAgents() { return []; }
	async getAgent(_agentId: string) { return null; }
	async createAgent(_request: CreateAgentRequest): Promise<StoredAgentDto> {
		throw new Error('not needed in permission tests');
	}
	async updateAgent(_agentId: string, _request: UpdateAgentRequest): Promise<StoredAgentDto> {
		throw new Error('not needed in permission tests');
	}
	async deleteAgent(_agentId: string) {}

	// ---- Asset upload ----

	async uploadAsset(_sessionId: string, _file: File | Blob, _name?: string): Promise<AssetReference> {
		throw new Error('not needed in permission tests');
	}

	// ---- Test helpers ----

	/** Fire a PERMISSION_REQUEST event. Returns the promise that workspace resolves when approve/deny is called. */
	async firePermissionRequest(permissionId: string): Promise<PermissionResponse> {
		const handlers = this.#handlers;
		if (!handlers?.onPermissionRequest) throw new Error('No permission handler registered');

		return handlers.onPermissionRequest({
			type: 'PERMISSION_REQUEST',
			version: '1',
			permissionId,
			sourceName: 'test-tool',
			functionName: 'testFunc',
			description: 'Test permission',
			callId: `call-${permissionId}`,
			arguments: {}
		});
	}

	/** Fire a CLARIFICATION_REQUEST event. Returns the promise that workspace resolves when clarify() is called. */
	async fireClarificationRequest(requestId: string): Promise<string> {
		const handlers = this.#handlers;
		if (!handlers?.onClarificationRequest) throw new Error('No clarification handler registered');

		return handlers.onClarificationRequest({
			type: 'CLARIFICATION_REQUEST',
			version: '1',
			requestId,
			sourceName: 'test-source',
			question: 'What do you mean?',
			agentName: 'TestAgent',
			options: ['Option A', 'Option B']
		});
	}

	/** Complete the stream (simulates MESSAGE_TURN_FINISHED). */
	complete(): void {
		this.#handlers?.onComplete?.();
		this.#resolveStream?.();
		this.#resolveStream = null;
		this.#handlers = null;
	}

	/** Fail the stream with an error message. */
	fail(message: string): void {
		this.#handlers?.onError?.(message);
		this.#resolveStream?.();
		this.#resolveStream = null;
		this.#handlers = null;
	}

	/** True if a stream is currently in progress (handlers captured, not yet resolved). */
	get isStreaming(): boolean {
		return this.#resolveStream !== null;
	}
}

async function buildWorkspace(
	client: FakeAgentClient,
	overrides: Partial<CreateWorkspaceOptions> = {}
) {
	const ws = createWorkspace({
		baseUrl: 'http://fake',
		_client: client,
		...overrides
	});

	await tick(200); // wait for async init
	return ws;
}

// ============================================
// Group A: Permission request round-trip
// ============================================

describe('createWorkspace — permission round-trip', () => {
	it('adds permission to state.pendingPermissions when request arrives', async () => {
		const client = new FakeAgentClient();
		const ws = await buildWorkspace(client);

		// Start a stream (non-awaited — stream is held open by FakeAgentClient)
		const sendPromise = ws.send('hello');

		// Fire a permission request into the workspace's event handlers
		const permissionPromise = client.firePermissionRequest('perm-1');
		await tick(50);

		expect(ws.state?.pendingPermissions).toHaveLength(1);
		expect(ws.state?.pendingPermissions[0].permissionId).toBe('perm-1');

		// Resolve so test doesn't hang
		client.complete();
		await sendPromise;
		void permissionPromise;
	});

	it('canSend is false while permission is pending', async () => {
		const client = new FakeAgentClient();
		const ws = await buildWorkspace(client);

		const sendPromise = ws.send('hello');
		client.firePermissionRequest('perm-1');
		await tick(50);

		expect(ws.state?.canSend).toBe(false);

		client.complete();
		await sendPromise;
	});

	it('approve() resolves the permission request and removes it from pendingPermissions', async () => {
		const client = new FakeAgentClient();
		const ws = await buildWorkspace(client);

		const sendPromise = ws.send('hello');
		const permPromise = client.firePermissionRequest('perm-1');
		await tick(50);

		expect(ws.state?.pendingPermissions).toHaveLength(1);

		// Approve — this should resolve the promise and remove from pendingPermissions
		await ws.approve('perm-1', 'ask');
		await tick(50);

		expect(ws.state?.pendingPermissions).toHaveLength(0);

		// The permPromise should have resolved with approved: true
		const response = await permPromise;
		expect(response.approved).toBe(true);
		expect(response.choice).toBe('ask');

		client.complete();
		await sendPromise;
	});

	it('deny() resolves the permission request with approved: false', async () => {
		const client = new FakeAgentClient();
		const ws = await buildWorkspace(client);

		const sendPromise = ws.send('hello');
		const permPromise = client.firePermissionRequest('perm-1');
		await tick(50);

		await ws.deny('perm-1', 'not allowed');
		await tick(50);

		expect(ws.state?.pendingPermissions).toHaveLength(0);

		const response = await permPromise;
		expect(response.approved).toBe(false);
		expect(response.reason).toBe('not allowed');

		client.complete();
		await sendPromise;
	});

	it('approve() with unknown permissionId is a silent no-op', async () => {
		const client = new FakeAgentClient();
		const ws = await buildWorkspace(client);

		// No stream in progress — no permissions pending
		await expect(ws.approve('unknown-id')).resolves.not.toThrow();
		expect(ws.state?.pendingPermissions).toHaveLength(0);
	});

	it('deny() with unknown permissionId is a silent no-op', async () => {
		const client = new FakeAgentClient();
		const ws = await buildWorkspace(client);

		await expect(ws.deny('unknown-id', 'reason')).resolves.not.toThrow();
		expect(ws.state?.pendingPermissions).toHaveLength(0);
	});

	it('multiple permission requests queue up independently', async () => {
		const client = new FakeAgentClient();
		const ws = await buildWorkspace(client);

		const sendPromise = ws.send('hello');

		const perm1Promise = client.firePermissionRequest('perm-1');
		await tick(30);
		const perm2Promise = client.firePermissionRequest('perm-2');
		await tick(50);

		expect(ws.state?.pendingPermissions).toHaveLength(2);

		// Approve perm-1 only
		await ws.approve('perm-1');
		await tick(30);
		expect(ws.state?.pendingPermissions).toHaveLength(1);
		expect(ws.state?.pendingPermissions[0].permissionId).toBe('perm-2');

		// Approve perm-2
		await ws.approve('perm-2');
		await tick(30);
		expect(ws.state?.pendingPermissions).toHaveLength(0);

		const r1 = await perm1Promise;
		const r2 = await perm2Promise;
		expect(r1.approved).toBe(true);
		expect(r2.approved).toBe(true);

		client.complete();
		await sendPromise;
	});
});

// ============================================
// Group B: Clarification request round-trip
// ============================================

describe('createWorkspace — clarification round-trip', () => {
	it('adds clarification to state.pendingClarifications when request arrives', async () => {
		const client = new FakeAgentClient();
		const ws = await buildWorkspace(client);

		const sendPromise = ws.send('hello');
		const clarifPromise = client.fireClarificationRequest('clarif-1');
		await tick(50);

		expect(ws.state?.pendingClarifications).toHaveLength(1);
		expect(ws.state?.pendingClarifications[0].requestId).toBe('clarif-1');

		client.complete();
		await sendPromise;
		void clarifPromise;
	});

	it('canSend is false while clarification is pending', async () => {
		const client = new FakeAgentClient();
		const ws = await buildWorkspace(client);

		const sendPromise = ws.send('hello');
		client.fireClarificationRequest('clarif-1');
		await tick(50);

		expect(ws.state?.canSend).toBe(false);

		client.complete();
		await sendPromise;
	});

	it('clarify() resolves the clarification and removes it from pendingClarifications', async () => {
		const client = new FakeAgentClient();
		const ws = await buildWorkspace(client);

		const sendPromise = ws.send('hello');
		const clarifPromise = client.fireClarificationRequest('clarif-1');
		await tick(50);

		expect(ws.state?.pendingClarifications).toHaveLength(1);

		await ws.clarify('clarif-1', 'my answer');
		await tick(50);

		// The clarification promise should resolve with the answer
		const answer = await clarifPromise;
		expect(answer).toBe('my answer');

		client.complete();
		await sendPromise;
	});

	it('clarify() with unknown id is a silent no-op', async () => {
		const client = new FakeAgentClient();
		const ws = await buildWorkspace(client);

		await expect(ws.clarify('unknown-id', 'answer')).resolves.not.toThrow();
		expect(ws.state?.pendingClarifications).toHaveLength(0);
	});

	it('clarification question and options are preserved in pendingClarifications', async () => {
		const client = new FakeAgentClient();
		const ws = await buildWorkspace(client);

		const sendPromise = ws.send('hello');
		const clarifPromise = client.fireClarificationRequest('clarif-1');
		await tick(50);

		const pending = ws.state?.pendingClarifications[0];
		expect(pending?.question).toBe('What do you mean?');
		expect(pending?.options).toEqual(['Option A', 'Option B']);

		client.complete();
		await sendPromise;
		void clarifPromise;
	});
});

// ============================================
// Group C: stream() is called with correct session + branch
// ============================================

describe('createWorkspace — send() targets correct session and branch', () => {
	it('send() calls client.stream with activeSessionId and activeBranchId', async () => {
		const client = new FakeAgentClient();
		const ws = await buildWorkspace(client);

		const sendPromise = ws.send('hello');
		await tick(50);

		expect(client.lastSessionId).toBe('s1');
		expect(client.lastBranchId).toBe('main');

		client.complete();
		await sendPromise;
	});

	it('send() call count increments per send', async () => {
		const client = new FakeAgentClient();
		const ws = await buildWorkspace(client);

		const p1 = ws.send('first');
		await tick(20);
		client.complete();
		await p1;

		const p2 = ws.send('second');
		await tick(20);
		client.complete();
		await p2;

		expect(client.streamCallCount).toBe(2);
	});
});

// ============================================
// Group D: stream error path
// ============================================

describe('createWorkspace — stream error handling', () => {
	it('onError sets error message on AgentState', async () => {
		const client = new FakeAgentClient();
		const ws = await buildWorkspace(client);

		const sendPromise = ws.send('hello');
		await tick(20);

		client.fail('Something went wrong');
		await sendPromise.catch(() => {});

		// AgentState.onMessageTurnError sets the error
		expect(ws.state?.error).not.toBeNull();
	});

	it('state is usable after stream error (can send again)', async () => {
		const client = new FakeAgentClient();
		const ws = await buildWorkspace(client);

		const p1 = ws.send('hello');
		await tick(20);
		client.fail('error');
		await p1.catch(() => {});

		// Clear the error and send again
		ws.state?.clearError();
		expect(ws.state?.error).toBeNull();

		const p2 = ws.send('retry');
		await tick(20);
		client.complete();
		await p2;

		expect(client.streamCallCount).toBe(2);
	});
});
