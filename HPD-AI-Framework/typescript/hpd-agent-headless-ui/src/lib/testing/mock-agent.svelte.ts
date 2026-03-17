/**
 * createMockWorkspace() - Mock Workspace for Testing & Development
 *
 * Implements the full Workspace interface without a real HPD backend.
 * Drives AgentState directly to simulate streaming responses.
 *
 * Features:
 * - Simulated character-by-character streaming
 * - In-memory sessions and branches
 * - Branch switching, forking, sibling navigation
 * - Session switching with per-session branch state isolation
 */

import { AgentState } from '../agent/agent.svelte.ts';
import type {
	Branch,
	Session,
	CreateSessionRequest,
	CreateBranchRequest,
	PermissionChoice,
	AgentSummaryDto,
	StoredAgentDto,
	CreateAgentRequest,
	UpdateAgentRequest,
	UpdateSessionRequest
} from '@hpd/hpd-agent-client';
import type { Workspace, AgentClientLike } from '../workspace/types.ts';

// ============================================
// Options
// ============================================

export interface MockWorkspaceOptions {
	/** Delay between text chunks (ms). Default: 30 */
	typingDelay?: number;

	/** Response templates (cycles through). */
	responses?: string[];

	/** Simulate thinking/reasoning before responses. Default: false */
	enableReasoning?: boolean;

	/** Number of mock sessions to bootstrap. Default: 2 */
	initialSessionCount?: number;
}

// ============================================
// Helpers
// ============================================

function sleep(ms: number): Promise<void> {
	return new Promise((resolve) => setTimeout(resolve, ms));
}

function generateId(): string {
	return `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
}

function makeMockBranch(overrides: Partial<Branch> & { id: string; sessionId: string }): Branch {
	return {
		name: overrides.id,
		description: '',
		forkedFrom: undefined,
		forkedAtMessageIndex: undefined,
		ancestors: undefined,
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

function makeMockSession(id?: string): Session {
	const sid = id ?? `session-${generateId()}`;
	return {
		id: sid,
		createdAt: new Date().toISOString(),
		lastActivity: new Date().toISOString(),
		metadata: {}
	};
}

// ============================================
// MockWorkspace implementation
// ============================================

class MockWorkspaceImpl implements Workspace {
	readonly #options: Required<MockWorkspaceOptions>;
	#responseIndex = 0;

	// ==========================================
	// Level 1: Session list ($state)
	// ==========================================

	#sessions = $state<Session[]>([]);
	#activeSessionId = $state<string | null>(null);
	#loading = $state(false);
	#error = $state<string | null>(null);

	// ==========================================
	// Level 2: Branch registry ($state)
	// ==========================================

	#branches = $state<Map<string, Branch>>(new Map());
	#activeBranchId = $state<string | null>(null);

	// ==========================================
	// Internal: per-session branch maps + state cache
	// ==========================================

	// sessionId → Map<branchId, Branch>
	readonly #sessionBranches = new Map<string, Map<string, Branch>>();

	// `${sessionId}:${branchId}` → AgentState
	readonly #branchStates = new Map<string, AgentState>();

	// ==========================================
	// Derived state
	// ==========================================

	readonly state = $derived.by((): AgentState | null => {
		const sid = this.#activeSessionId;
		const bid = this.#activeBranchId;
		if (!sid || !bid) return null;
		return this.#branchStates.get(`${sid}:${bid}`) ?? null;
	});

	readonly activeBranch = $derived.by((): Branch | null => {
		if (!this.#activeBranchId) return null;
		return this.#branches.get(this.#activeBranchId) ?? null;
	});

	readonly activeSiblings = $derived.by((): Branch[] => {
		const branch = this.activeBranch;
		if (!branch) return [];
		// Include peer forks (same ForkedFrom + ForkedAtMessageIndex) AND the source branch (slot 0).
		const peers = Array.from(this.#branches.values()).filter(
			(b) =>
				b.forkedFrom === branch.forkedFrom &&
				b.forkedAtMessageIndex === branch.forkedAtMessageIndex
		);
		// For a fork branch: also include its source (ForkedFrom)
		if (!branch.isOriginal && branch.forkedFrom) {
			const source = this.#branches.get(branch.forkedFrom);
			if (source && !peers.some((p) => p.id === source.id)) {
				peers.push(source);
			}
		}
		return peers.sort((a, b) => a.siblingIndex - b.siblingIndex);
	});

	readonly canGoNext = $derived.by(() => this.activeBranch?.nextSiblingId != null);
	readonly canGoPrevious = $derived.by(() => this.activeBranch?.previousSiblingId != null);

	readonly currentSiblingPosition = $derived.by(() => {
		if (!this.activeBranch) return { current: 0, total: 0 };
		return {
			current: this.activeBranch.siblingIndex + 1,
			total: this.activeBranch.totalSiblings
		};
	});

	// ==========================================
	// Public getters
	// ==========================================

	get sessions() {
		return this.#sessions;
	}
	get activeSessionId() {
		return this.#activeSessionId;
	}
	get loading() {
		return this.#loading;
	}
	get error() {
		return this.#error;
	}
	get branches() {
		return this.#branches;
	}
	get activeBranchId() {
		return this.#activeBranchId;
	}

	// ==========================================
	// Constructor
	// ==========================================

	constructor(options: MockWorkspaceOptions = {}) {
		this.#options = {
			typingDelay: options.typingDelay ?? 30,
			responses: options.responses ?? [
				'Hello! I am a mock assistant. How can I help you today?',
				'That sounds interesting! Tell me more.',
				'I understand. Let me think about that for a moment...',
				'Great question! Here is what I think about that.',
				'I am a mock workspace, so my responses are simulated.'
			],
			enableReasoning: options.enableReasoning ?? false,
			initialSessionCount: options.initialSessionCount ?? 2
		};

		// Bootstrap N mock sessions
		const count = Math.max(1, this.#options.initialSessionCount);
		const sessions = Array.from({ length: count }, (_, i) =>
			makeMockSession(`mock-session-${i + 1}`)
		);
		this.#sessions = sessions;

		// Each session starts with a 'main' branch and an empty AgentState
		for (const session of sessions) {
			const mainBranch = makeMockBranch({ id: 'main', sessionId: session.id });
			const branchMap = new Map<string, Branch>();
			branchMap.set('main', mainBranch);
			this.#sessionBranches.set(session.id, branchMap);
			this.#branchStates.set(`${session.id}:main`, new AgentState());
		}

		// Activate first session (synchronous — no async needed for mock init)
		this.#syncActivateSession(sessions[0].id, 'main');
	}

	// ==========================================
	// Internal helpers
	// ==========================================

	#syncActivateSession(sessionId: string, branchId: string): void {
		const branchMap = this.#sessionBranches.get(sessionId) ?? new Map();
		this.#branches = new Map(branchMap);
		this.#activeSessionId = sessionId;
		this.#activeBranchId = branchId;
	}

	async #asyncActivateBranch(sessionId: string, branchId: string): Promise<void> {
		this.#loading = true;
		await sleep(80); // simulate network

		const cacheKey = `${sessionId}:${branchId}`;
		if (!this.#branchStates.has(cacheKey)) {
			this.#branchStates.set(cacheKey, new AgentState());
		}
		this.#activeBranchId = branchId;
		this.#loading = false;
	}

	#nextResponse(): string {
		const response = this.#options.responses[this.#responseIndex];
		this.#responseIndex = (this.#responseIndex + 1) % this.#options.responses.length;
		return response;
	}

	async #simulateResponse(state: AgentState): Promise<void> {
		const messageId = `msg-${generateId()}`;
		const response = this.#nextResponse();

		if (this.#options.enableReasoning) {
			const reasoning = 'Analyzing the request...';
			for (const char of reasoning) {
				state.onReasoningDelta(char, messageId);
				await sleep(this.#options.typingDelay);
			}
			await sleep(300);
		}

		state.onTextMessageStart(messageId, 'assistant');
		for (const char of response) {
			state.onTextDelta(char, messageId);
			await sleep(this.#options.typingDelay);
		}
		state.onTextMessageEnd(messageId);
	}

	#syncSessionBranches(sessionId: string): void {
		const branchMap = this.#sessionBranches.get(sessionId);
		if (branchMap && sessionId === this.#activeSessionId) {
			this.#branches = new Map(branchMap);
		}
	}

	#updateBranch(sessionId: string, branch: Branch): void {
		const branchMap = this.#sessionBranches.get(sessionId) ?? new Map<string, Branch>();
		branchMap.set(branch.id, branch);
		this.#sessionBranches.set(sessionId, branchMap);
		this.#syncSessionBranches(sessionId);
	}

	// ==========================================
	// Level 1: Session operations
	// ==========================================

	async selectSession(sessionId: string): Promise<void> {
		if (sessionId === this.#activeSessionId) return;
		this.#loading = true;
		await sleep(100);

		const branchMap = this.#sessionBranches.get(sessionId) ?? new Map();
		this.#branches = new Map(branchMap);
		this.#activeSessionId = sessionId;
		this.#activeBranchId = null;

		// Activate 'main' branch (or first available)
		const firstBranchId = branchMap.has('main') ? 'main' : [...branchMap.keys()][0] ?? null;
		if (firstBranchId) {
			const cacheKey = `${sessionId}:${firstBranchId}`;
			if (!this.#branchStates.has(cacheKey)) {
				this.#branchStates.set(cacheKey, new AgentState());
			}
			this.#activeBranchId = firstBranchId;
		}

		this.#loading = false;
	}

	async createSession(options?: CreateSessionRequest): Promise<void> {
		const session = makeMockSession(options?.sessionId);
		const mainBranch = makeMockBranch({ id: 'main', sessionId: session.id });
		const branchMap = new Map<string, Branch>();
		branchMap.set('main', mainBranch);
		this.#sessionBranches.set(session.id, branchMap);
		this.#branchStates.set(`${session.id}:main`, new AgentState());
		this.#sessions = [...this.#sessions, session];
		await this.selectSession(session.id);
	}

	async deleteSession(sessionId: string): Promise<void> {
		if (sessionId === this.#activeSessionId) {
			const other = this.#sessions.find((s) => s.id !== sessionId);
			if (other) {
				await this.selectSession(other.id);
			} else {
				this.#activeSessionId = null;
				this.#activeBranchId = null;
				this.#branches = new Map();
			}
		}

		this.#sessions = this.#sessions.filter((s) => s.id !== sessionId);
		this.#sessionBranches.delete(sessionId);

		for (const key of this.#branchStates.keys()) {
			if (key.startsWith(`${sessionId}:`)) {
				this.#branchStates.delete(key);
			}
		}
	}

	// ==========================================
	// Level 2: Branch operations
	// ==========================================

	async switchBranch(branchId: string): Promise<void> {
		if (branchId === this.#activeBranchId) return;
		const sessionId = this.#activeSessionId;
		if (!sessionId) throw new Error('No active session');
		if (!this.#branches.has(branchId)) throw new Error(`Branch ${branchId} not found`);
		await this.#asyncActivateBranch(sessionId, branchId);
	}

	async goToNextSibling(): Promise<void> {
		const next = this.activeBranch?.nextSiblingId;
		if (!next) throw new Error('No next sibling');
		await this.switchBranch(next);
	}

	async goToPreviousSibling(): Promise<void> {
		const prev = this.activeBranch?.previousSiblingId;
		if (!prev) throw new Error('No previous sibling');
		await this.switchBranch(prev);
	}

	async goToSiblingByIndex(index: number): Promise<void> {
		const sibling = this.activeSiblings[index];
		if (!sibling) throw new Error(`No sibling at index ${index}`);
		await this.switchBranch(sibling.id);
	}

	async editMessage(messageIndex: number, newContent: string): Promise<void> {
		const sessionId = this.#activeSessionId;
		const branchId = this.#activeBranchId;
		const activeState = this.state;

		if (!sessionId || !branchId || !activeState) throw new Error('No active branch');

		const messages = activeState.messages;
		if (messageIndex < 0 || messageIndex >= messages.length) {
			throw new Error('Invalid message index');
		}
		if (messages[messageIndex].role !== 'user') {
			throw new Error('Can only edit user messages');
		}

		const forkId = `fork-${generateId()}`;
		const sourceBranch = this.#branches.get(branchId);

		// Existing forks at this message index (not including source)
		const existingForks = Array.from(this.#branches.values())
			.filter((b) => b.forkedFrom === branchId && b.forkedAtMessageIndex === messageIndex)
			.sort((a, b) => a.siblingIndex - b.siblingIndex);

		// Source is always slot 0; new fork is appended after all existing forks
		// sortedSiblings: [source, ...existingForks, newFork]
		const newForkSiblingIndex = existingForks.length + 1; // +1 because source is slot 0
		const totalSiblings = newForkSiblingIndex + 1; // source + existingForks + new fork

		// Last existing sibling before the new fork
		const lastBeforeNew = existingForks.length > 0 ? existingForks[existingForks.length - 1] : sourceBranch;

		const fork = makeMockBranch({
			id: forkId,
			sessionId,
			forkedFrom: branchId,
			forkedAtMessageIndex: messageIndex,
			isOriginal: false,
			originalBranchId: branchId,
			siblingIndex: newForkSiblingIndex,
			totalSiblings,
			previousSiblingId: lastBeforeNew?.id,
			nextSiblingId: undefined,
		});

		// Update all existing siblings' totalSiblings and the last sibling's nextSiblingId
		const allSiblingsToUpdate = sourceBranch ? [sourceBranch, ...existingForks] : existingForks;
		for (const sibling of allSiblingsToUpdate) {
			const isLast = sibling.id === lastBeforeNew?.id;
			this.#updateBranch(sessionId, {
				...sibling,
				totalSiblings,
				nextSiblingId: isLast ? forkId : sibling.nextSiblingId,
				...(sibling.id === branchId
					? { childBranches: [...sibling.childBranches, forkId], totalForks: sibling.totalForks + 1 }
					: {}),
			});
		}

		this.#updateBranch(sessionId, fork);

		// Pre-populate fork state with messages up to messageIndex (exclusive)
		const forkState = new AgentState();
		forkState.loadHistory(messages.slice(0, messageIndex).map((m) => ({ ...m })));
		this.#branchStates.set(`${sessionId}:${forkId}`, forkState);

		await this.switchBranch(forkId);
		await this.send(newContent);
	}

	async deleteBranch(branchId: string, _options?: { recursive?: boolean }): Promise<void> {
		const sessionId = this.#activeSessionId;
		if (!sessionId) throw new Error('No active session');

		const branchToDelete = this.#branches.get(branchId);
		if (!branchToDelete) throw new Error('Branch not found');

		if (branchToDelete.childBranches.length > 0) {
			throw new Error('Cannot delete branch with children');
		}

		if (this.#activeBranchId === branchId) {
			const targetId =
				branchToDelete.nextSiblingId ??
				branchToDelete.previousSiblingId ??
				branchToDelete.originalBranchId ??
				Array.from(this.#branches.keys()).find((id) => id !== branchId) ??
				null;

			if (!targetId) throw new Error('Cannot delete the only branch');
			await this.switchBranch(targetId);
		}

		const branchMap = this.#sessionBranches.get(sessionId);
		branchMap?.delete(branchId);
		this.#syncSessionBranches(sessionId);
		this.#branchStates.delete(`${sessionId}:${branchId}`);
	}

	async createBranch(options?: CreateBranchRequest): Promise<Branch> {
		const sessionId = this.#activeSessionId;
		if (!sessionId) throw new Error('No active session');

		const branchId = options?.branchId ?? `branch-${generateId()}`;
		const branch = makeMockBranch({
			id: branchId,
			sessionId,
			name: options?.name ?? branchId
		});
		this.#updateBranch(sessionId, branch);
		return branch;
	}

	async refreshBranch(_branchId: string): Promise<void> {
		// In mock, branch metadata is always current
		await sleep(0);
	}

	invalidateBranch(branchId: string): void {
		const sessionId = this.#activeSessionId;
		if (!sessionId) return;
		this.#branchStates.delete(`${sessionId}:${branchId}`);
	}

	// ==========================================
	// Level 3: Streaming
	// ==========================================

	async send(content: string): Promise<void> {
		const activeState = this.state;
		if (!activeState) throw new Error('No active branch');

		activeState.addUserMessage(content);
		await sleep(100); // simulate network latency
		await this.#simulateResponse(activeState);
	}

	abort(): void {
		// No-op — mock streams run to completion
	}

	async approve(_permissionId: string, _choice?: PermissionChoice): Promise<void> {
		// No-op — mock streams don't pause for permissions
	}

	async deny(_permissionId: string, _reason?: string): Promise<void> {
		// No-op — mock streams don't pause for permissions
	}

	async clarify(_clarificationId: string, _answer: string): Promise<void> {
		// No-op — mock streams don't pause for clarifications
	}

	clear(): void {
		this.state?.clearMessages();
	}

	// ==========================================
	// Agent management
	// ==========================================

	readonly agents: AgentSummaryDto[] = [];
	activeAgentId: string | null = null;

	selectAgent(_agentId: string | null): void {
		// No-op — mock doesn't actually select agents
	}

	async listAgents(): Promise<AgentSummaryDto[]> {
		return [];
	}

	readonly client: AgentClientLike = {
		stream: async () => {},
		abort: () => {},
		listSessions: async () => [],
		getSession: async () => null,
		createSession: async () => ({ id: 'mock', createdAt: new Date().toISOString(), metadata: {}, lastActivity: new Date().toISOString() }),
		updateSession: async (id: string, _req: UpdateSessionRequest) => ({ id, createdAt: new Date().toISOString(), metadata: {}, lastActivity: new Date().toISOString() }),
		deleteSession: async () => {},
		listBranches: async () => [],
		getBranch: async () => null,
		createBranch: async () => ({ id: 'mock', sessionId: '', name: '', description: '', createdAt: new Date().toISOString(), lastActivity: new Date().toISOString(), messageCount: 0, tags: [], siblingIndex: 0, totalSiblings: 1, isOriginal: true, childBranches: [], totalForks: 0 }),
		forkBranch: async () => ({ id: 'mock', sessionId: '', name: '', description: '', createdAt: new Date().toISOString(), lastActivity: new Date().toISOString(), messageCount: 0, tags: [], siblingIndex: 0, totalSiblings: 1, isOriginal: false, childBranches: [], totalForks: 0 }),
		deleteBranch: async () => {},
		getBranchMessages: async () => [],
		getBranchSiblings: async () => [],
		getNextSibling: async () => null,
		getPreviousSibling: async () => null,
		listAgents: async () => [],
		getAgent: async () => null,
		createAgent: async () => ({ id: 'mock', name: 'mock', config: {}, createdAt: new Date().toISOString(), updatedAt: new Date().toISOString() }),
		updateAgent: async () => ({ id: 'mock', name: 'mock', config: {}, createdAt: new Date().toISOString(), updatedAt: new Date().toISOString() }),
		deleteAgent: async () => {},
		uploadAsset: async () => ({ assetId: 'mock', contentType: 'text/plain' }),
	} as any;
}

// ============================================
// Factory
// ============================================

/**
 * Create a mock workspace for development and testing.
 * Implements the full Workspace interface without a real HPD backend.
 */
export function createMockWorkspace(options?: MockWorkspaceOptions): Workspace {
	let instance: MockWorkspaceImpl | undefined;
	$effect.root(() => {
		instance = new MockWorkspaceImpl(options);
	});
	return (instance ?? new MockWorkspaceImpl(options));
}

// ============================================
// MockAgent — lightweight Workspace stub for permission-dialog tests
// ============================================

class MockAgentImpl implements Workspace {
	readonly state = new AgentState();

	// Session / branch stubs — not needed for permission tests
	readonly sessions: Session[] = [];
	readonly activeSessionId: string | null = null;
	readonly loading = false;
	readonly error: string | null = null;
	readonly branches: Map<string, Branch> = new Map();
	readonly activeBranchId: string | null = null;
	readonly activeBranch: Branch | null = null;
	readonly activeSiblings: Branch[] = [];
	readonly canGoNext = false;
	readonly canGoPrevious = false;
	readonly currentSiblingPosition = { current: 0, total: 0 };

	async selectSession(_sessionId: string): Promise<void> {}
	async createSession(): Promise<void> {}
	async deleteSession(_sessionId: string): Promise<void> {}
	async switchBranch(_branchId: string): Promise<void> {}
	async goToNextSibling(): Promise<void> {}
	async goToPreviousSibling(): Promise<void> {}
	async goToSiblingByIndex(_index: number): Promise<void> {}
	async editMessage(_messageIndex: number, _newContent: string): Promise<void> {}
	async deleteBranch(_branchId: string, _options?: { recursive?: boolean }): Promise<void> {}
	async createBranch(): Promise<Branch> {
		throw new Error('Not implemented');
	}
	async refreshBranch(_branchId: string): Promise<void> {}
	invalidateBranch(_branchId: string): void {}
	async send(_content: string): Promise<void> {}
	abort(): void {}

	async approve(permissionId: string, _choice?: PermissionChoice): Promise<void> {
		this.state.onPermissionApproved(permissionId, '');
	}

	async deny(permissionId: string, reason?: string): Promise<void> {
		this.state.onPermissionDenied(permissionId, '', reason ?? '');
	}

	async clarify(_clarificationId: string, _answer: string): Promise<void> {}

	clear(): void {
		this.state.clearMessages();
	}

	// Agent management
	readonly agents: AgentSummaryDto[] = [];
	readonly activeAgentId: string | null = null;
	selectAgent(_agentId: string | null): void {}
	async listAgents(): Promise<AgentSummaryDto[]> { return []; }
	readonly client: AgentClientLike = {
		stream: async () => {},
		abort: () => {},
		listSessions: async () => [],
		getSession: async () => null,
		createSession: async () => ({ id: 'mock', createdAt: new Date().toISOString(), metadata: {}, lastActivity: new Date().toISOString() }),
		updateSession: async (id: string, _req: UpdateSessionRequest) => ({ id, createdAt: new Date().toISOString(), metadata: {}, lastActivity: new Date().toISOString() }),
		deleteSession: async () => {},
		listBranches: async () => [],
		getBranch: async () => null,
		createBranch: async () => ({ id: 'mock', sessionId: '', name: '', description: '', createdAt: new Date().toISOString(), lastActivity: new Date().toISOString(), messageCount: 0, tags: [], siblingIndex: 0, totalSiblings: 1, isOriginal: true, childBranches: [], totalForks: 0 }),
		forkBranch: async () => ({ id: 'mock', sessionId: '', name: '', description: '', createdAt: new Date().toISOString(), lastActivity: new Date().toISOString(), messageCount: 0, tags: [], siblingIndex: 0, totalSiblings: 1, isOriginal: false, childBranches: [], totalForks: 0 }),
		deleteBranch: async () => {},
		getBranchMessages: async () => [],
		getBranchSiblings: async () => [],
		getNextSibling: async () => null,
		getPreviousSibling: async () => null,
		listAgents: async () => [],
		getAgent: async () => null,
		createAgent: async () => ({ id: 'mock', name: 'mock', config: {}, createdAt: new Date().toISOString(), updatedAt: new Date().toISOString() }),
		updateAgent: async () => ({ id: 'mock', name: 'mock', config: {}, createdAt: new Date().toISOString(), updatedAt: new Date().toISOString() }),
		deleteAgent: async () => {},
		uploadAsset: async () => ({ assetId: 'mock', contentType: 'text/plain' }),
	} as any;
}

/**
 * Create a minimal mock agent for testing permission-dialog and other
 * components that accept a Workspace. Has a real AgentState so you can
 * drive onPermissionRequest() / onPermissionApproved() directly.
 */
export function createMockAgent(): MockAgentImpl {
	let instance: MockAgentImpl | undefined;
	$effect.root(() => {
		instance = new MockAgentImpl();
	});
	return (instance ?? new MockAgentImpl());
}
