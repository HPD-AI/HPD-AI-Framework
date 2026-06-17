/**
 * createMockWorkspace() - Mock Workspace for Testing & Development
 *
 * Implements the full Workspace interface without a real HPD backend.
 * Drives AgentState directly to simulate streaming responses.
 *
 * Features:
 * - Simulated character-by-character streaming
 * - In-memory sessions and threads
 * - Thread switching, forking, sibling navigation
 * - Session switching with per-session thread state isolation
 */

import { AgentState } from '../agent/agent.svelte.ts';
import type {
	Thread,
	Session,
	CreateSessionRequest,
	CreateThreadRequest,
	PermissionChoice,
	AgentSummaryDto,
	StoredAgentDto,
	CreateAgentRequest,
	UpdateAgentRequest,
	UpdateSessionRequest,
	AgentRunInputEvent
} from '@hpd-research/hpd-agent-client';
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

function makeMockThread(overrides: Partial<Thread> & { id: string; sessionId: string }): Thread {
	return {
		name: overrides.id,
		description: '',
		forkedFrom: undefined,
		forkedAtMessageId: undefined,
		forkedAtMessageIndex: undefined,
		ancestors: undefined,
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
	// Level 2: Thread registry ($state)
	// ==========================================

	#threads = $state<Map<string, Thread>>(new Map());
	#activeThreadId = $state<string | null>(null);

	// ==========================================
	// Internal: per-session thread maps + state cache
	// ==========================================

	// sessionId → Map<threadId, Thread>
	readonly #sessionThreads = new Map<string, Map<string, Thread>>();

	// `${sessionId}:${threadId}` → AgentState
	readonly #threadStates = new Map<string, AgentState>();

	// ==========================================
	// Derived state
	// ==========================================

	readonly state = $derived.by((): AgentState | null => {
		const sid = this.#activeSessionId;
		const bid = this.#activeThreadId;
		if (!sid || !bid) return null;
		return this.#threadStates.get(`${sid}:${bid}`) ?? null;
	});

	readonly activeThread = $derived.by((): Thread | null => {
		if (!this.#activeThreadId) return null;
		return this.#threads.get(this.#activeThreadId) ?? null;
	});

	readonly activeSiblings = $derived.by((): Thread[] => {
		const thread = this.activeThread;
		if (!thread) return [];
		// Include peer forks (same ForkedFrom + ForkedAtMessageId) AND the source thread (slot 0).
		const peers = Array.from(this.#threads.values()).filter(
			(b) =>
				b.forkedFrom === thread.forkedFrom &&
				b.forkedAtMessageId === thread.forkedAtMessageId
		);
		// For a fork thread: also include its source (ForkedFrom)
		if (!thread.isOriginal && thread.forkedFrom) {
			const source = this.#threads.get(thread.forkedFrom);
			if (source && !peers.some((p) => p.id === source.id)) {
				peers.push(source);
			}
		}
		return peers.sort((a, b) => a.siblingIndex - b.siblingIndex);
	});

	readonly canGoNext = $derived.by(() => this.activeThread?.nextSiblingId != null);
	readonly canGoPrevious = $derived.by(() => this.activeThread?.previousSiblingId != null);

	readonly currentSiblingPosition = $derived.by(() => {
		if (!this.activeThread) return { current: 0, total: 0 };
		return {
			current: this.activeThread.siblingIndex + 1,
			total: this.activeThread.totalSiblings
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
	get threads() {
		return this.#threads;
	}
	get activeThreadId() {
		return this.#activeThreadId;
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

		// Each session starts with a 'main' thread and an empty AgentState
		for (const session of sessions) {
			const mainThread = makeMockThread({ id: 'main', sessionId: session.id });
			const threadMap = new Map<string, Thread>();
			threadMap.set('main', mainThread);
			this.#sessionThreads.set(session.id, threadMap);
			this.#threadStates.set(`${session.id}:main`, new AgentState());
		}

		// Activate first session (synchronous — no async needed for mock init)
		this.#syncActivateSession(sessions[0].id, 'main');
	}

	// ==========================================
	// Internal helpers
	// ==========================================

	#syncActivateSession(sessionId: string, threadId: string): void {
		const threadMap = this.#sessionThreads.get(sessionId) ?? new Map();
		this.#threads = new Map(threadMap);
		this.#activeSessionId = sessionId;
		this.#activeThreadId = threadId;
	}

	async #asyncActivateThread(sessionId: string, threadId: string): Promise<void> {
		this.#loading = true;
		await sleep(80); // simulate network

		const cacheKey = `${sessionId}:${threadId}`;
		if (!this.#threadStates.has(cacheKey)) {
			this.#threadStates.set(cacheKey, new AgentState());
		}
		this.#activeThreadId = threadId;
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

	#syncSessionThreads(sessionId: string): void {
		const threadMap = this.#sessionThreads.get(sessionId);
		if (threadMap && sessionId === this.#activeSessionId) {
			this.#threads = new Map(threadMap);
		}
	}

	#updateThread(sessionId: string, thread: Thread): void {
		const threadMap = this.#sessionThreads.get(sessionId) ?? new Map<string, Thread>();
		threadMap.set(thread.id, thread);
		this.#sessionThreads.set(sessionId, threadMap);
		this.#syncSessionThreads(sessionId);
	}

	// ==========================================
	// Level 1: Session operations
	// ==========================================

	async selectSession(sessionId: string): Promise<void> {
		if (sessionId === this.#activeSessionId) return;
		this.#loading = true;
		await sleep(100);

		const threadMap = this.#sessionThreads.get(sessionId) ?? new Map();
		this.#threads = new Map(threadMap);
		this.#activeSessionId = sessionId;
		this.#activeThreadId = null;

		// Activate 'main' thread (or first available)
		const firstThreadId = threadMap.has('main') ? 'main' : [...threadMap.keys()][0] ?? null;
		if (firstThreadId) {
			const cacheKey = `${sessionId}:${firstThreadId}`;
			if (!this.#threadStates.has(cacheKey)) {
				this.#threadStates.set(cacheKey, new AgentState());
			}
			this.#activeThreadId = firstThreadId;
		}

		this.#loading = false;
	}

	async createSession(options?: CreateSessionRequest): Promise<void> {
		const session = makeMockSession(options?.sessionId);
		const mainThread = makeMockThread({ id: 'main', sessionId: session.id });
		const threadMap = new Map<string, Thread>();
		threadMap.set('main', mainThread);
		this.#sessionThreads.set(session.id, threadMap);
		this.#threadStates.set(`${session.id}:main`, new AgentState());
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
				this.#activeThreadId = null;
				this.#threads = new Map();
			}
		}

		this.#sessions = this.#sessions.filter((s) => s.id !== sessionId);
		this.#sessionThreads.delete(sessionId);

		for (const key of this.#threadStates.keys()) {
			if (key.startsWith(`${sessionId}:`)) {
				this.#threadStates.delete(key);
			}
		}
	}

	// ==========================================
	// Level 2: Thread operations
	// ==========================================

	async switchThread(threadId: string): Promise<void> {
		if (threadId === this.#activeThreadId) return;
		const sessionId = this.#activeSessionId;
		if (!sessionId) throw new Error('No active session');
		if (!this.#threads.has(threadId)) throw new Error(`Thread ${threadId} not found`);
		await this.#asyncActivateThread(sessionId, threadId);
	}

	async goToNextSibling(): Promise<void> {
		const next = this.activeThread?.nextSiblingId;
		if (!next) throw new Error('No next sibling');
		await this.switchThread(next);
	}

	async goToPreviousSibling(): Promise<void> {
		const prev = this.activeThread?.previousSiblingId;
		if (!prev) throw new Error('No previous sibling');
		await this.switchThread(prev);
	}

	async goToSiblingByIndex(index: number): Promise<void> {
		const sibling = this.activeSiblings[index];
		if (!sibling) throw new Error(`No sibling at index ${index}`);
		await this.switchThread(sibling.id);
	}

	async editMessage(messageIndex: number, newContent: string): Promise<void> {
		const sessionId = this.#activeSessionId;
		const threadId = this.#activeThreadId;
		const activeState = this.state;

		if (!sessionId || !threadId || !activeState) throw new Error('No active thread');

		const messages = activeState.messages;
		if (messageIndex < 0 || messageIndex >= messages.length) {
			throw new Error('Invalid message index');
		}
		if (messages[messageIndex].role !== 'user') {
			throw new Error('Can only edit user messages');
		}

		const forkId = `fork-${generateId()}`;
		const sourceThread = this.#threads.get(threadId);
		const forkAtIndex = Math.max(0, messageIndex - 1);
		const fromMessageId = messages[forkAtIndex]?.id;
		if (!fromMessageId) {
			throw new Error('Cannot fork because the fork point message has no id');
		}

		// Existing forks at this message id (not including source)
		const existingForks = Array.from(this.#threads.values())
			.filter((b) => b.forkedFrom === threadId && b.forkedAtMessageId === fromMessageId)
			.sort((a, b) => a.siblingIndex - b.siblingIndex);

		// Source is always slot 0; new fork is appended after all existing forks
		// sortedSiblings: [source, ...existingForks, newFork]
		const newForkSiblingIndex = existingForks.length + 1; // +1 because source is slot 0
		const totalSiblings = newForkSiblingIndex + 1; // source + existingForks + new fork

		// Last existing sibling before the new fork
		const lastBeforeNew = existingForks.length > 0 ? existingForks[existingForks.length - 1] : sourceThread;

		const fork = makeMockThread({
			id: forkId,
			sessionId,
			forkedFrom: threadId,
			forkedAtMessageId: fromMessageId,
			forkedAtMessageIndex: forkAtIndex,
			isOriginal: false,
			originalThreadId: threadId,
			siblingIndex: newForkSiblingIndex,
			totalSiblings,
			previousSiblingId: lastBeforeNew?.id,
			nextSiblingId: undefined,
		});

		// Update all existing siblings' totalSiblings and the last sibling's nextSiblingId
		const allSiblingsToUpdate = sourceThread ? [sourceThread, ...existingForks] : existingForks;
		for (const sibling of allSiblingsToUpdate) {
			const isLast = sibling.id === lastBeforeNew?.id;
			this.#updateThread(sessionId, {
				...sibling,
				totalSiblings,
				nextSiblingId: isLast ? forkId : sibling.nextSiblingId,
				...(sibling.id === threadId
					? { childThreads: [...sibling.childThreads, forkId], totalForks: sibling.totalForks + 1 }
					: {}),
			});
		}

		this.#updateThread(sessionId, fork);

		// Pre-populate fork state with messages through the fork point.
		const forkState = new AgentState();
		forkState.loadHistory(messages.slice(0, forkAtIndex + 1).map((m) => ({ ...m })));
		this.#threadStates.set(`${sessionId}:${forkId}`, forkState);

		await this.switchThread(forkId);
		await this.send(newContent);
	}

	async deleteThread(threadId: string, _options?: { recursive?: boolean }): Promise<void> {
		const sessionId = this.#activeSessionId;
		if (!sessionId) throw new Error('No active session');

		const threadToDelete = this.#threads.get(threadId);
		if (!threadToDelete) throw new Error('Thread not found');

		if (threadToDelete.childThreads.length > 0) {
			throw new Error('Cannot delete thread with children');
		}

		if (this.#activeThreadId === threadId) {
			const targetId =
				threadToDelete.nextSiblingId ??
				threadToDelete.previousSiblingId ??
				threadToDelete.originalThreadId ??
				Array.from(this.#threads.keys()).find((id) => id !== threadId) ??
				null;

			if (!targetId) throw new Error('Cannot delete the only thread');
			await this.switchThread(targetId);
		}

		const threadMap = this.#sessionThreads.get(sessionId);
		threadMap?.delete(threadId);
		this.#syncSessionThreads(sessionId);
		this.#threadStates.delete(`${sessionId}:${threadId}`);
	}

	async createThread(options?: CreateThreadRequest): Promise<Thread> {
		const sessionId = this.#activeSessionId;
		if (!sessionId) throw new Error('No active session');

		const threadId = options?.threadId ?? `thread-${generateId()}`;
		const thread = makeMockThread({
			id: threadId,
			sessionId,
			name: options?.name ?? threadId
		});
		this.#updateThread(sessionId, thread);
		return thread;
	}

	async refreshThread(_threadId: string): Promise<void> {
		// In mock, thread metadata is always current
		await sleep(0);
	}

	invalidateThread(threadId: string): void {
		const sessionId = this.#activeSessionId;
		if (!sessionId) return;
		this.#threadStates.delete(`${sessionId}:${threadId}`);
	}

	// ==========================================
	// Level 3: Streaming
	// ==========================================

	async send(content: string): Promise<void> {
		const activeState = this.state;
		if (!activeState) throw new Error('No active thread');

		activeState.addUserMessage(content);
		await sleep(100); // simulate network latency
		await this.#simulateResponse(activeState);
	}

	async run(_input: AgentRunInputEvent): Promise<void> {
		// Mock runtime accepts event-native inputs without a backend.
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
		run: async () => {},
		on: () => ({ dispose: () => {} }),
		onAny: () => ({ dispose: () => {} }),
		onError: () => ({ dispose: () => {} }),
		abort: () => {},
		listSessions: async () => [],
		getSession: async () => null,
		createSession: async () => ({ id: 'mock', createdAt: new Date().toISOString(), metadata: {}, lastActivity: new Date().toISOString() }),
		updateSession: async (id: string, _req: UpdateSessionRequest) => ({ id, createdAt: new Date().toISOString(), metadata: {}, lastActivity: new Date().toISOString() }),
		deleteSession: async () => {},
		listThreads: async () => [],
		getThread: async () => null,
		createThread: async () => ({ id: 'mock', sessionId: '', name: '', description: '', createdAt: new Date().toISOString(), lastActivity: new Date().toISOString(), messageCount: 0, tags: [], siblingIndex: 0, totalSiblings: 1, isOriginal: true, childThreads: [], totalForks: 0 }),
		forkThread: async () => ({ id: 'mock', sessionId: '', name: '', description: '', createdAt: new Date().toISOString(), lastActivity: new Date().toISOString(), messageCount: 0, tags: [], siblingIndex: 0, totalSiblings: 1, isOriginal: false, childThreads: [], totalForks: 0 }),
		deleteThread: async () => {},
		getThreadMessages: async () => [],
		getThreadSiblings: async () => [],
		getNextSibling: async () => null,
		getPreviousSibling: async () => null,
		listAgents: async () => [],
		getAgent: async () => null,
		createAgent: async () => ({ id: 'mock', name: 'mock', config: {}, createdAt: new Date().toISOString(), updatedAt: new Date().toISOString() }),
		updateAgent: async () => ({ id: 'mock', name: 'mock', config: {}, createdAt: new Date().toISOString(), updatedAt: new Date().toISOString() }),
		deleteAgent: async () => {},
		uploadContent: async () => ({ contentId: 'mock', contentType: 'text/plain' }),
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

	// Session / thread stubs — not needed for permission tests
	readonly sessions: Session[] = [];
	readonly activeSessionId: string | null = null;
	readonly loading = false;
	readonly error: string | null = null;
	readonly threads: Map<string, Thread> = new Map();
	readonly activeThreadId: string | null = null;
	readonly activeThread: Thread | null = null;
	readonly activeSiblings: Thread[] = [];
	readonly canGoNext = false;
	readonly canGoPrevious = false;
	readonly currentSiblingPosition = { current: 0, total: 0 };

	async selectSession(_sessionId: string): Promise<void> {}
	async createSession(): Promise<void> {}
	async deleteSession(_sessionId: string): Promise<void> {}
	async switchThread(_threadId: string): Promise<void> {}
	async goToNextSibling(): Promise<void> {}
	async goToPreviousSibling(): Promise<void> {}
	async goToSiblingByIndex(_index: number): Promise<void> {}
	async editMessage(_messageIndex: number, _newContent: string): Promise<void> {}
	async deleteThread(_threadId: string, _options?: { recursive?: boolean }): Promise<void> {}
	async createThread(): Promise<Thread> {
		throw new Error('Not implemented');
	}
	async refreshThread(_threadId: string): Promise<void> {}
	invalidateThread(_threadId: string): void {}
	async send(_content: string): Promise<void> {}
	async run(_input: AgentRunInputEvent): Promise<void> {}
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
		run: async () => {},
		on: () => ({ dispose: () => {} }),
		onAny: () => ({ dispose: () => {} }),
		onError: () => ({ dispose: () => {} }),
		abort: () => {},
		listSessions: async () => [],
		getSession: async () => null,
		createSession: async () => ({ id: 'mock', createdAt: new Date().toISOString(), metadata: {}, lastActivity: new Date().toISOString() }),
		updateSession: async (id: string, _req: UpdateSessionRequest) => ({ id, createdAt: new Date().toISOString(), metadata: {}, lastActivity: new Date().toISOString() }),
		deleteSession: async () => {},
		listThreads: async () => [],
		getThread: async () => null,
		createThread: async () => ({ id: 'mock', sessionId: '', name: '', description: '', createdAt: new Date().toISOString(), lastActivity: new Date().toISOString(), messageCount: 0, tags: [], siblingIndex: 0, totalSiblings: 1, isOriginal: true, childThreads: [], totalForks: 0 }),
		forkThread: async () => ({ id: 'mock', sessionId: '', name: '', description: '', createdAt: new Date().toISOString(), lastActivity: new Date().toISOString(), messageCount: 0, tags: [], siblingIndex: 0, totalSiblings: 1, isOriginal: false, childThreads: [], totalForks: 0 }),
		deleteThread: async () => {},
		getThreadMessages: async () => [],
		getThreadSiblings: async () => [],
		getNextSibling: async () => null,
		getPreviousSibling: async () => null,
		listAgents: async () => [],
		getAgent: async () => null,
		createAgent: async () => ({ id: 'mock', name: 'mock', config: {}, createdAt: new Date().toISOString(), updatedAt: new Date().toISOString() }),
		updateAgent: async () => ({ id: 'mock', name: 'mock', config: {}, createdAt: new Date().toISOString(), updatedAt: new Date().toISOString() }),
		deleteAgent: async () => {},
		uploadContent: async () => ({ contentId: 'mock', contentType: 'text/plain' }),
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
