/**
 * workspace-permissions.svelte.test.ts
 *
 * Tests for permission and clarification round-trips through WorkspaceImpl.
 *
 * Strategy: inject a FakeAgentClient via `_client` option. The fake captures
 * the AgentClient event handlers registered by WorkspaceImpl and exposes test helpers to
 * fire synthetic events (permission requests, clarification requests, completion).
 *
 * This exercises the full workspace → AgentClient.on(...) → AgentState
 * pipeline without a real server.
 */

import { describe, it, expect, vi } from 'vitest';
import { createWorkspace } from '../workspace.svelte.ts';
import type { AgentClientLike, CreateWorkspaceOptions } from '../types.ts';
import type {
	AgentEvent,
	AgentRunInputEvent,
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
	EventSubscription,
	PermissionRequestEvent,
	ClarificationRequestEvent,
} from '@hpd/hpd-agent-client';
import { EventTypes } from '@hpd/hpd-agent-client';

// ============================================
// Helpers
// ============================================

async function tick(ms = 50): Promise<void> {
	await new Promise((r) => setTimeout(r, ms));
}

// ============================================
// FakeAgentClient
//
// Captures AgentClient handlers and exposes helpers to drive synthetic events.
// run(USER_TEXT_INPUT) returns a Promise that resolves when complete() is called
// (simulating MESSAGE_TURN_FINISHED).
// ============================================

class FakeAgentClient implements AgentClientLike {
	#typedHandlers = new Map<string, Array<(event: AgentEvent) => void | Promise<void>>>();
	#anyHandlers: Array<(event: AgentEvent) => void | Promise<void>> = [];
	#errorHandlers: Array<(error: Error) => void | Promise<void>> = [];
	#resolveRun: (() => void) | null = null;
	#runCallCount = 0;
	#lastSessionId: string | null = null;
	#lastBranchId: string | undefined = undefined;
	#lastRunInput: AgentRunInputEvent | null = null;
	#runInputs: AgentRunInputEvent[] = [];

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

	get runCallCount() { return this.#runCallCount; }
	get lastSessionId() { return this.#lastSessionId; }
	get lastBranchId() { return this.#lastBranchId; }
	get lastRunInput() { return this.#lastRunInput; }
	get runInputs() { return this.#runInputs; }

	// ---- Event runtime ----

	on<TType extends AgentEvent['type']>(
		type: TType,
		handler: (event: Extract<AgentEvent, { type: TType }>) => void | Promise<void>
	): EventSubscription {
		const handlers = this.#typedHandlers.get(type) ?? [];
		const stored = handler as (event: AgentEvent) => void | Promise<void>;
		handlers.push(stored);
		this.#typedHandlers.set(type, handlers);
		return {
			dispose: () => {
				const next = (this.#typedHandlers.get(type) ?? []).filter((h) => h !== stored);
				this.#typedHandlers.set(type, next);
			}
		};
	}

	onAny(handler: (event: AgentEvent) => void | Promise<void>): EventSubscription {
		this.#anyHandlers.push(handler);
		return {
			dispose: () => {
				this.#anyHandlers = this.#anyHandlers.filter((h) => h !== handler);
			}
		};
	}

	onError(handler: (error: Error) => void | Promise<void>): EventSubscription {
		this.#errorHandlers.push(handler);
		return {
			dispose: () => {
				this.#errorHandlers = this.#errorHandlers.filter((h) => h !== handler);
			}
		};
	}

	async run(input: AgentRunInputEvent): Promise<void> {
		this.#runCallCount++;
		this.#lastRunInput = input;
		this.#runInputs.push(input);

		if (input.type !== EventTypes.USER_TEXT_INPUT) {
			return;
		}

		this.#lastSessionId = input.sessionId ?? null;
		this.#lastBranchId = input.branchId;

		return new Promise<void>((resolve) => {
			this.#resolveRun = resolve;
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

	async #emit(event: AgentEvent): Promise<void> {
		for (const handler of this.#typedHandlers.get(event.type) ?? []) {
			await handler(event);
		}
		for (const handler of this.#anyHandlers) {
			await handler(event);
		}
	}

	/** Fire a PERMISSION_REQUEST event. */
	async firePermissionRequest(permissionId: string): Promise<void> {
		await this.#emit({
			type: EventTypes.PERMISSION_REQUEST,
			version: '1',
			permissionId,
			sourceName: 'test-tool',
			functionName: 'testFunc',
			description: 'Test permission',
			callId: `call-${permissionId}`,
			arguments: {}
		} satisfies PermissionRequestEvent);
	}

	/** Fire a CLARIFICATION_REQUEST event. */
	async fireClarificationRequest(requestId: string): Promise<void> {
		await this.#emit({
			type: EventTypes.CLARIFICATION_REQUEST,
			version: '1',
			requestId,
			sourceName: 'test-source',
			question: 'What do you mean?',
			agentName: 'TestAgent',
			options: ['Option A', 'Option B']
		} satisfies ClarificationRequestEvent);
	}

	/** Complete the run (simulates MESSAGE_TURN_FINISHED). */
	complete(): void {
		void this.#emit({
			type: EventTypes.MESSAGE_TURN_FINISHED,
			version: '1',
			messageTurnId: 'turn-1',
			conversationId: 's1',
			agentName: 'TestAgent',
			duration: '00:00:00',
			timestamp: new Date().toISOString()
		});
		this.#resolveRun?.();
		this.#resolveRun = null;
	}

	/** Fail the run with an error message. */
	fail(message: string): void {
		void this.#emit({ type: EventTypes.MESSAGE_TURN_ERROR, version: '1', message });
		this.#resolveRun?.();
		this.#resolveRun = null;
	}

	/** True if a run is currently in progress. */
	get isStreaming(): boolean {
		return this.#resolveRun !== null;
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

		// Start a run (non-awaited — run is held open by FakeAgentClient)
		const sendPromise = ws.send('hello');

		// Fire a permission request into the workspace's event handlers
		await client.firePermissionRequest('perm-1');
		await tick(50);

		expect(ws.state?.pendingPermissions).toHaveLength(1);
		expect(ws.state?.pendingPermissions[0].permissionId).toBe('perm-1');

		// Resolve so test doesn't hang
		client.complete();
		await sendPromise;
	});

	it('canSend is false while permission is pending', async () => {
		const client = new FakeAgentClient();
		const ws = await buildWorkspace(client);

		const sendPromise = ws.send('hello');
		await client.firePermissionRequest('perm-1');
		await tick(50);

		expect(ws.state?.canSend).toBe(false);

		client.complete();
		await sendPromise;
	});

	it('approve() sends a permission response event and removes it from pendingPermissions', async () => {
		const client = new FakeAgentClient();
		const ws = await buildWorkspace(client);

		const sendPromise = ws.send('hello');
		await client.firePermissionRequest('perm-1');
		await tick(50);

		expect(ws.state?.pendingPermissions).toHaveLength(1);

		await ws.approve('perm-1', 'ask');
		await tick(50);

		expect(ws.state?.pendingPermissions).toHaveLength(0);
		expect(client.lastRunInput).toMatchObject({
			type: EventTypes.PERMISSION_RESPONSE,
			permissionId: 'perm-1',
			sourceName: 'test-tool',
			approved: true,
			choice: 'ask'
		});

		client.complete();
		await sendPromise;
	});

	it('deny() sends a permission response event with approved: false', async () => {
		const client = new FakeAgentClient();
		const ws = await buildWorkspace(client);

		const sendPromise = ws.send('hello');
		await client.firePermissionRequest('perm-1');
		await tick(50);

		await ws.deny('perm-1', 'not allowed');
		await tick(50);

		expect(ws.state?.pendingPermissions).toHaveLength(0);
		expect(client.lastRunInput).toMatchObject({
			type: EventTypes.PERMISSION_RESPONSE,
			permissionId: 'perm-1',
			sourceName: 'test-tool',
			approved: false,
			reason: 'not allowed'
		});

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

		await client.firePermissionRequest('perm-1');
		await tick(30);
		await client.firePermissionRequest('perm-2');
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

		expect(client.runInputs).toEqual(
			expect.arrayContaining([
				expect.objectContaining({ type: EventTypes.PERMISSION_RESPONSE, permissionId: 'perm-1', approved: true }),
				expect.objectContaining({ type: EventTypes.PERMISSION_RESPONSE, permissionId: 'perm-2', approved: true })
			])
		);

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
		await client.fireClarificationRequest('clarif-1');
		await tick(50);

		expect(ws.state?.pendingClarifications).toHaveLength(1);
		expect(ws.state?.pendingClarifications[0].requestId).toBe('clarif-1');

		client.complete();
		await sendPromise;
	});

	it('canSend is false while clarification is pending', async () => {
		const client = new FakeAgentClient();
		const ws = await buildWorkspace(client);

		const sendPromise = ws.send('hello');
		await client.fireClarificationRequest('clarif-1');
		await tick(50);

		expect(ws.state?.canSend).toBe(false);

		client.complete();
		await sendPromise;
	});

	it('clarify() sends a clarification response event', async () => {
		const client = new FakeAgentClient();
		const ws = await buildWorkspace(client);

		const sendPromise = ws.send('hello');
		await client.fireClarificationRequest('clarif-1');
		await tick(50);

		expect(ws.state?.pendingClarifications).toHaveLength(1);

		await ws.clarify('clarif-1', 'my answer');
		await tick(50);

		expect(client.lastRunInput).toMatchObject({
			type: EventTypes.CLARIFICATION_RESPONSE,
			requestId: 'clarif-1',
			sourceName: 'test-source',
			question: 'What do you mean?',
			answer: 'my answer'
		});

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
		await client.fireClarificationRequest('clarif-1');
		await tick(50);

		const pending = ws.state?.pendingClarifications[0];
		expect(pending?.question).toBe('What do you mean?');
		expect(pending?.options).toEqual(['Option A', 'Option B']);

		client.complete();
		await sendPromise;
	});
});

// ============================================
// Group C: run() is called with correct session + branch
// ============================================

describe('createWorkspace — send() targets correct session and branch', () => {
	it('send() calls client.run with activeSessionId and activeBranchId', async () => {
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

		expect(client.runCallCount).toBe(2);
	});
});

// ============================================
// Group D: run error path
// ============================================

describe('createWorkspace — run error handling', () => {
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

		expect(client.runCallCount).toBe(2);
	});
});
