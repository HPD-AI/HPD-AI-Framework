/**
 * createWorkspace() - Unified Session/Thread/Streaming Factory
 *
 * Single factory that owns all three levels of the HPD Agent hierarchy:
 *   Level 1: Session list (select, create, delete sessions)
 *   Level 2: Thread view (switch, fork, navigate siblings)
 *   Level 3: Thread streaming state (send, approve, clarify, abort)
 *
 * Always routes streaming through AgentClient (correct event queue,
 * bidirectional handling). Never exposes raw transport to callers.
 *
 * @example
 * ```ts
 * const workspace = createWorkspace({ baseUrl: 'http://localhost:5135' });
 *
 * // Level 1
 * workspace.sessions       // reactive list
 * workspace.selectSession(id)
 *
 * // Level 2
 * workspace.activeThread   // reactive metadata
 * workspace.switchThread(id)
 * workspace.goToNextSibling()
 *
 * // Level 3
 * workspace.state.messages // reactive messages
 * workspace.send('hello')
 * workspace.approve(permId)
 * ```
 */

import {
	AgentClient,
	EventTypes,
	type AgentRunInputEvent,
	type PermissionChoice,
	type Thread,
	type ThreadMessage,
	type ContentReference,
	type CreateThreadRequest,
	type CreateSessionRequest,
	type Session,
	type AgentSummaryDto,
} from '@hpd-research/hpd-agent-client';
import { AgentState } from '../agent/agent.svelte.ts';
import type { Message, MessageRole, ToolCall } from '../agent/types.ts';
import type { AgentClientLike, CreateWorkspaceOptions, SendOptions, Workspace } from './types.ts';

// ============================================
// History Loading
// ============================================

/**
 * Map raw ThreadMessage[] to UI Message[].
 * Extracts text, reasoning, and tool calls from the full AIContent list.
 * All fields are set to their fully-settled defaults — no streaming side effects.
 */
function mapToUIMessages(raw: ThreadMessage[]): Message[] {
	return raw
		// 'tool' role messages are function result containers — internal plumbing, not user-visible
		.filter((msg) => msg.role !== 'tool')
		.map((msg) => {
		let content = '';
		let reasoning: string | undefined;
		const toolCalls: ToolCall[] = [];

		for (const item of msg.contents) {
			if (item.$type === 'text') {
				const tc = item as import('@hpd-research/hpd-agent-client').AiTextContent;
				content += tc.text;
			} else if (item.$type === 'reasoning') {
				const rc = item as import('@hpd-research/hpd-agent-client').AiTextReasoningContent;
				reasoning = (reasoning ?? '') + rc.text;
			} else if (item.$type === 'functionCall') {
				const fc = item as import('@hpd-research/hpd-agent-client').AiFunctionCallContent;
				toolCalls.push({
					callId: fc.callId,
					name: fc.name,
					messageId: msg.id,
					status: 'complete',
					args: fc.arguments,
					startTime: new Date(msg.timestamp)
				});
			} else if (item.$type === 'functionResult') {
				const fr = item as import('@hpd-research/hpd-agent-client').AiFunctionResultContent;
				const match = toolCalls.find((tc) => tc.callId === fr.callId);
				if (match) {
					match.resultText =
						typeof fr.result === 'string' ? fr.result : JSON.stringify(fr.result);
				}
			}
		}

		return {
			id: msg.id,
			role: msg.role as MessageRole,
			content,
			streaming: false,
			thinking: false,
			timestamp: new Date(msg.timestamp),
			toolCalls,
			reasoning
		};
	});
}

// ============================================
// WorkspaceImpl
// ============================================

class WorkspaceImpl implements Workspace {
	// ==========================================
	// Dependencies
	// ==========================================

	readonly #client: AgentClientLike;
	readonly #options: CreateWorkspaceOptions;
	readonly #maxCachedThreads: number;

	get client(): AgentClientLike { return this.#client; }

	// ==========================================
	// Level 1: Session list ($state)
	// ==========================================

	#sessions = $state<Session[]>([]);
	#activeSessionId = $state<string | null>(null);
	#loading = $state(false);
	#error = $state<string | null>(null);

	// ==========================================
	// Agent selection ($state)
	// ==========================================

	#agents = $state<AgentSummaryDto[]>([]);
	#activeAgentId = $state<string | null>(null);

	// ==========================================
	// Level 2: Thread registry ($state)
	// ==========================================

	#threads = $state<Map<string, Thread>>(new Map());
	#activeThreadId = $state<string | null>(null);

	// ==========================================
	// Level 2+3: Thread state cache (plain Maps, LRU managed manually)
	// Key format: `${sessionId}:${threadId}`
	// Two sessions can both have a thread named 'main' — compound key prevents collision.
	// ==========================================

	readonly #threadStates = new Map<string, AgentState>();
	readonly #threadAccessTimestamps = new Map<string, number>();

	// ==========================================
	// Level 3: Bidirectional resolvers
	// ==========================================

	readonly #pendingPermissionResolvers = new Map<
		string,
		{ sourceName: string }
	>();
	readonly #pendingClarificationResolvers = new Map<string, { sourceName: string; question: string }>();

	// ==========================================
	// Derived state ($derived.by)
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
		return Array.from(this.#threads.values())
			.filter(
				(b) =>
					b.forkedFrom === thread.forkedFrom &&
					b.forkedAtMessageId === thread.forkedAtMessageId
			)
			.sort((a, b) => a.siblingIndex - b.siblingIndex);
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
	// Public getters (expose $state)
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
	get agents() {
		return this.#agents;
	}
	get activeAgentId() {
		return this.#activeAgentId;
	}

	// ==========================================
	// Constructor
	// ==========================================

	constructor(options: CreateWorkspaceOptions) {
		this.#options = options;
		this.#maxCachedThreads = options.maxCachedThreads ?? 10;
		this.#activeAgentId = options.agentId ?? null;

		// Create AgentClient (or use injected one for tests)
		this.#client = options._client ?? new AgentClient({
			baseUrl: options.baseUrl,
			transport: options.transport ?? 'sse',
			headers: options.headers
		});
		if (options.onClientToolInvoke) {
			this.#client.tools?.registerFallback(options.onClientToolInvoke);
		}
		this.#registerClientHandlers();

		// Kick off async init (loading flag covers UI during this)
		void this.#init();
	}

	// ==========================================
	// Initialization
	// ==========================================

	async #init(): Promise<void> {
		this.#loading = true;
		this.#error = null;

		try {
			// Load session list and agent definitions concurrently
			const [sessions] = await Promise.all([
				this.#client.listSessions(),
				this.#client.listAgents().then(
					(agents) => { this.#agents = agents; },
					() => { /* agents are optional — swallow if store not registered */ }
				),
			]);
			this.#sessions = sessions;

			// Activate initial session
			const targetSessionId = this.#options.sessionId ?? sessions[0]?.id ?? null;
			if (targetSessionId) {
				await this.#loadSession(targetSessionId, this.#options.initialThreadId);
			}
		} catch (err) {
			this.#error = err instanceof Error ? err.message : 'Failed to initialize';
		} finally {
			this.#loading = false;
		}
	}

	// ==========================================
	// Internal: load session threads + switch to thread
	// Does NOT set #loading (callers manage that).
	// ==========================================

	async #loadSession(sessionId: string, preferredThreadId?: string): Promise<void> {
		// Clear thread view immediately while loading
		this.#activeThreadId = null;
		this.#threads = new Map();

		// Load all threads for this session
		const threadList = await this.#client.listThreads(sessionId);
		const threadMap = new Map<string, Thread>();
		for (const thread of threadList) {
			threadMap.set(thread.id, thread);
		}
		this.#threads = threadMap;
		this.#activeSessionId = sessionId;

		// Determine which thread to activate
		const targetThreadId =
			preferredThreadId ??
			(threadMap.has('main') ? 'main' : threadList[0]?.id ?? null);

		if (targetThreadId) {
			await this.#loadThread(sessionId, targetThreadId);
		}
	}

	// ==========================================
	// Internal: load thread state into cache + activate
	// ==========================================

	async #loadThread(sessionId: string, threadId: string): Promise<void> {
		const cacheKey = `${sessionId}:${threadId}`;
		const existing = this.#threadStates.get(cacheKey);
		if (existing) {
			this.#threadAccessTimestamps.set(cacheKey, Date.now());
			this.#activeThreadId = threadId;
			return;
		} else {
			const rawMessages = await this.#client.getThreadMessages(sessionId, threadId);
			const mapped = mapToUIMessages(rawMessages);
			const state = new AgentState();
			state.loadHistory(mapped);
			this.#threadStates.set(cacheKey, state);
			this.#threadAccessTimestamps.set(cacheKey, Date.now());
		}

		this.#activeThreadId = threadId;
		this.#evictOldThreadStates();
	}

	// ==========================================
	// Internal: LRU eviction
	// ==========================================

	#evictOldThreadStates(): void {
		if (this.#threadStates.size <= this.#maxCachedThreads) return;

		const sorted = Array.from(this.#threadAccessTimestamps.entries()).sort(
			(a, b) => a[1] - b[1]
		);

		const activeCacheKey = this.#activeSessionId && this.#activeThreadId
			? `${this.#activeSessionId}:${this.#activeThreadId}`
			: null;

		for (const [key] of sorted) {
			if (this.#threadStates.size <= this.#maxCachedThreads) break;
			if (key !== activeCacheKey) {
				this.#threadStates.delete(key);
				this.#threadAccessTimestamps.delete(key);
			}
		}
	}

	// ==========================================
	// Internal: active thread state (for event handlers)
	// ==========================================

	#activeState(): AgentState | null {
		const sid = this.#activeSessionId;
		const bid = this.#activeThreadId;
		if (!sid || !bid) return null;
		return this.#threadStates.get(`${sid}:${bid}`) ?? null;
	}

	// ==========================================
	// Internal: register client event handlers
	// ==========================================

	#registerClientHandlers(): void {
		this.#client.onAny((event) => {
			this.#activeState()?.dispatch(event);
		});

		this.#client.on(EventTypes.PERMISSION_REQUEST, (request) => {
			this.#activeState()?.onPermissionRequest({
				permissionId: request.permissionId,
				sourceName: request.sourceName,
				functionName: request.functionName,
				description: request.description,
				callId: request.callId,
				arguments: request.arguments
			});
			this.#pendingPermissionResolvers.set(request.permissionId, {
				sourceName: request.sourceName
			});
		});

		this.#client.on(EventTypes.CLARIFICATION_REQUEST, (request) => {
			this.#activeState()?.onClarificationRequest({
				requestId: request.requestId,
				sourceName: request.sourceName,
				question: request.question,
				agentName: request.agentName,
				options: request.options
			});
			this.#pendingClarificationResolvers.set(request.requestId, {
				sourceName: request.sourceName,
				question: request.question
			});
		});

		this.#client.on(EventTypes.CONTINUATION_REQUEST, (request) => {
			void this.#client.run({
				type: EventTypes.CONTINUATION_RESPONSE,
				continuationId: request.continuationId,
				sourceName: request.sourceName,
				approved: true
			}).catch((error) => this.#options.onError?.(error.message));
		});

		this.#client.on(EventTypes.CLIENT_TOOL_GROUPS_REGISTERED, (event) => {
			this.#activeState()?.onclientToolHarnessesRegistered(
				event.registeredToolHarnesses,
				event.totalTools,
				event.timestamp
			);
		});

		this.#client.on(EventTypes.MESSAGE_TURN_FINISHED, () => {
			this.#options.onComplete?.();
		});

		this.#client.on(EventTypes.MESSAGE_TURN_ERROR, (event) => {
			this.#activeState()?.onMessageTurnError(event.message);
			this.#options.onError?.(event.message);
		});

		this.#client.onError((error) => {
			this.#options.onError?.(error.message);
		});

	}

	// ==========================================
	// Level 1: Session operations
	// ==========================================

	async selectSession(sessionId: string): Promise<void> {
		if (sessionId === this.#activeSessionId) return;

		this.#loading = true;
		this.#error = null;
		try {
			await this.#loadSession(sessionId);
		} catch (err) {
			this.#error = err instanceof Error ? err.message : 'Failed to switch session';
			throw err;
		} finally {
			this.#loading = false;
		}
	}

	async createSession(options?: CreateSessionRequest): Promise<void> {
		this.#loading = true;
		this.#error = null;
		try {
			const session = await this.#client.createSession(options);
			this.#sessions = [...this.#sessions, session];
			await this.#loadSession(session.id);
		} catch (err) {
			this.#error = err instanceof Error ? err.message : 'Failed to create session';
			throw err;
		} finally {
			this.#loading = false;
		}
	}

	async deleteSession(sessionId: string): Promise<void> {
		// Navigate away if deleting active session
		if (sessionId === this.#activeSessionId) {
			const other = this.#sessions.find((s) => s.id !== sessionId);
			if (other) {
				await this.selectSession(other.id);
			} else {
				// No other session — reset to empty state
				this.#activeSessionId = null;
				this.#activeThreadId = null;
				this.#threads = new Map();
			}
		}

		await this.#client.deleteSession(sessionId);
		this.#sessions = this.#sessions.filter((s) => s.id !== sessionId);

		// Evict all cached thread states for this session
		for (const key of this.#threadStates.keys()) {
			if (key.startsWith(`${sessionId}:`)) {
				this.#threadStates.delete(key);
				this.#threadAccessTimestamps.delete(key);
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

		if (!this.#threads.has(threadId)) {
			throw new Error(`Thread ${threadId} not found in active session`);
		}

		this.#loading = true;
		this.#error = null;
		try {
			await this.#loadThread(sessionId, threadId);
			// Refresh thread metadata so sibling navigation fields are current
			const fresh = await this.#client.getThread(sessionId, threadId);
			if (fresh) {
				const updated = new Map(this.#threads);
				updated.set(threadId, fresh);
				this.#threads = updated;
			}
		} catch (err) {
			this.#error = err instanceof Error ? err.message : 'Failed to switch thread';
			throw err;
		} finally {
			this.#loading = false;
		}
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
		const activeState = this.#activeState();

		if (!sessionId || !threadId || !activeState) throw new Error('No active thread');

		const messages = activeState.messages;
		if (messageIndex < 0 || messageIndex >= messages.length) {
			throw new Error('Invalid message index');
		}
		if (messages[messageIndex].role !== 'user') {
			throw new Error('Can only edit user messages');
		}

		// Fork at the last assistant turn before the edited message so the fork contains
		// everything up to (but not including) the user message being replaced.
		const forkAtIndex = Math.max(0, messageIndex - 1);
		const fromMessageId = messages[forkAtIndex]?.id;
		if (!fromMessageId) {
			throw new Error('Cannot fork because the fork point message has no id');
		}

		// Always fork from the thread that OWNS the shared context at forkAtIndex.
		// If the current thread is itself a fork at this same message (forkedAtMessageId === fromMessageId),
		// then its parent already owns the shared context — fork from the parent instead.
		// This ensures all edits of the same message become flat siblings of the original thread
		// rather than a linear chain of forks-of-forks.
		const activeThread = this.#threads.get(threadId)!;
		const sourceThreadId =
			!activeThread.isOriginal && activeThread.forkedAtMessageId === fromMessageId
				? activeThread.forkedFrom!
				: threadId;

		const fork = await this.#client.forkThread(sessionId, sourceThreadId, {
			newThreadId: crypto.randomUUID(),
			fromMessageId,
			name: `Edit: ${newContent.slice(0, 30)}${newContent.length > 30 ? '...' : ''}`,
			agentId: this.#activeAgentId ?? undefined
		});
		// Register fork in thread map
		const newThreads = new Map(this.#threads);
		newThreads.set(fork.id, fork);
		this.#threads = newThreads;

		// Refresh source thread metadata (it gained a new sibling group member)
		const updatedSource = await this.#client.getThread(sessionId, sourceThreadId);
		if (updatedSource) {
			const refreshed = new Map(this.#threads);
			refreshed.set(sourceThreadId, updatedSource);
			this.#threads = refreshed;
		}

		// Also refresh all existing siblings at this fork point so their totalSiblings is current
		const allThreads = Array.from(this.#threads.values());
		const siblingsToRefresh = allThreads.filter(
			b => b.id !== fork.id && b.id !== sourceThreadId &&
				b.forkedFrom === sourceThreadId && b.forkedAtMessageId === fromMessageId
		);
		if (siblingsToRefresh.length > 0) {
			const refreshed = new Map(this.#threads);
			await Promise.all(siblingsToRefresh.map(async sib => {
				const fresh = await this.#client.getThread(sessionId, sib.id);
				if (fresh) refreshed.set(sib.id, fresh);
			}));
			this.#threads = refreshed;
		}

		// The fork has messages up to forkAtIndex (messageIndex - 1), not including the edited message.
		// Switch to fork and send the edited content as a fresh message.
		await this.switchThread(fork.id);
		await this.send(newContent);
	}

	async deleteThread(threadId: string, options?: { recursive?: boolean }): Promise<void> {
		const sessionId = this.#activeSessionId;
		if (!sessionId) throw new Error('No active session');

		const threadToDelete = this.#threads.get(threadId);
		if (!threadToDelete) throw new Error('Thread not found');

		// Capture siblings BEFORE any navigation (activeSiblings is $derived — it changes after switchThread)
		const siblingsToRefresh = threadToDelete.forkedFrom
			? Array.from(this.#threads.values()).filter(
					(b) =>
						b.id !== threadId &&
						b.forkedFrom === threadToDelete.forkedFrom &&
						b.forkedAtMessageId === threadToDelete.forkedAtMessageId
				)
			: [];

		// Navigate away if the active thread is the deleted thread OR is a descendant of it.
		// Use the ancestors chain — every thread stores its full lineage.
		const activeIsInsideSubtree =
			this.#activeThreadId === threadId ||
			(this.#activeThreadId !== null &&
				this.activeThread?.ancestors != null &&
				Object.values(this.activeThread.ancestors).includes(threadId));

		if (activeIsInsideSubtree) {
			let targetId: string | null = null;

			if (threadToDelete.nextSiblingId) {
				targetId = threadToDelete.nextSiblingId;
			} else if (threadToDelete.previousSiblingId) {
				targetId = threadToDelete.previousSiblingId;
			} else if (threadToDelete.originalThreadId) {
				targetId = threadToDelete.originalThreadId;
			} else {
				targetId = Array.from(this.#threads.keys()).find((id) => id !== threadId) ?? null;
			}

			if (!targetId) throw new Error('Cannot delete the only thread');
			await this.switchThread(targetId);
		}

		await this.#client.deleteThread(sessionId, threadId, options);

		// Remove the deleted thread and all its descendants from the local thread map and state cache
		const deletedIds = this.#collectSubtreeIds(threadId);
		const newThreads = new Map(this.#threads);
		for (const id of deletedIds) {
			newThreads.delete(id);
			this.#threadStates.delete(`${sessionId}:${id}`);
			this.#threadAccessTimestamps.delete(`${sessionId}:${id}`);
		}
		this.#threads = newThreads;

		// Refresh sibling metadata (backend reindexed siblingIndex, totalSiblings, navigation pointers)
		for (const sibling of siblingsToRefresh) {
			await this.refreshThread(sibling.id);
		}
	}

	/** Collect the IDs of a thread and all its descendants from the local thread map. */
	#collectSubtreeIds(threadId: string): string[] {
		const result: string[] = [threadId];
		const thread = this.#threads.get(threadId);
		if (thread) {
			for (const childId of thread.childThreads) {
				result.push(...this.#collectSubtreeIds(childId));
			}
		}
		return result;
	}

	async createThread(options?: CreateThreadRequest): Promise<Thread> {
		const sessionId = this.#activeSessionId;
		if (!sessionId) throw new Error('No active session');

		const thread = await this.#client.createThread(sessionId, options);
		const newThreads = new Map(this.#threads);
		newThreads.set(thread.id, thread);
		this.#threads = newThreads;
		return thread;
	}

	async refreshThread(threadId: string): Promise<void> {
		const sessionId = this.#activeSessionId;
		if (!sessionId) return;

		const thread = await this.#client.getThread(sessionId, threadId);
		if (thread) {
			const newThreads = new Map(this.#threads);
			newThreads.set(threadId, thread);
			this.#threads = newThreads;
		}
	}

	invalidateThread(threadId: string): void {
		const sessionId = this.#activeSessionId;
		if (!sessionId) return;
		this.#threadStates.delete(`${sessionId}:${threadId}`);
		this.#threadAccessTimestamps.delete(`${sessionId}:${threadId}`);
	}

	// ==========================================
	// Agent selection
	// ==========================================

	selectAgent(agentId: string | null): void {
		this.#activeAgentId = agentId;
	}

	async listAgents(): Promise<AgentSummaryDto[]> {
		const agents = await this.#client.listAgents();
		this.#agents = agents;
		return agents;
	}

	// ==========================================
	// Level 3: Streaming
	// ==========================================

	async send(content: string, options?: SendOptions): Promise<void> {
		const sessionId = this.#activeSessionId;
		const threadId = this.#activeThreadId;
		const activeState = this.#activeState();

		if (!sessionId || !threadId || !activeState) throw new Error('No active thread');

		activeState.addUserMessage(content);

		const effectiveAgentId = this.#activeAgentId ?? undefined;
		const messages = this.#buildMessages(content, options?.attachments);
		await this.#client.run({
			type: EventTypes.USER_TEXT_INPUT,
			text: messages[0]?.content ?? content,
			sessionId,
			threadId,
			agentId: effectiveAgentId,
			runConfig: options?.runConfig
		});
	}

	async run(input: AgentRunInputEvent): Promise<void> {
		await this.#client.run(this.#stampInputScope(input));
	}

	#stampInputScope(input: AgentRunInputEvent): AgentRunInputEvent {
		if (input.type === EventTypes.USER_TEXT_INPUT || input.type === EventTypes.USER_MESSAGES_INPUT) {
			return {
				...input,
				sessionId: input.sessionId ?? this.#activeSessionId ?? undefined,
				threadId: input.threadId ?? this.#activeThreadId ?? undefined,
				agentId: input.agentId ?? this.#activeAgentId ?? undefined
			};
		}
		return input;
	}

	#buildMessages(content: string, attachments?: ContentReference[]): Array<{ content: string; role?: string }> {
		// The transport wire format uses { content: string } messages.
		// Attachments are injected as hpd-content:// URIs appended to the text content.
		if (!attachments || attachments.length === 0) {
			return [{ content }];
		}
		const contentRefs = attachments
			.map((a) => `hpd-content://${a.contentId}`)
			.join(' ');
		return [{ content: `${content}\n${contentRefs}`.trimStart() }];
	}

	abort(): void {
		this.#client.abort();
	}

	async approve(permissionId: string, choice: PermissionChoice = 'ask'): Promise<void> {
		const pending = this.#pendingPermissionResolvers.get(permissionId);
		if (pending) {
			await this.#client.run({
				type: EventTypes.PERMISSION_RESPONSE,
				permissionId,
				sourceName: pending.sourceName,
				approved: true,
				choice
			});
			this.#pendingPermissionResolvers.delete(permissionId);
			this.#activeState()?.onPermissionApproved(permissionId, pending.sourceName);
		}
	}

	async deny(permissionId: string, reason?: string): Promise<void> {
		const pending = this.#pendingPermissionResolvers.get(permissionId);
		if (pending) {
			await this.#client.run({
				type: EventTypes.PERMISSION_RESPONSE,
				permissionId,
				sourceName: pending.sourceName,
				approved: false,
				reason
			});
			this.#pendingPermissionResolvers.delete(permissionId);
			this.#activeState()?.onPermissionDenied(permissionId, pending.sourceName, reason ?? 'User denied');
		}
	}

	async clarify(clarificationId: string, answer: string): Promise<void> {
		const pending = this.#pendingClarificationResolvers.get(clarificationId);
		if (pending) {
			await this.#client.run({
				type: EventTypes.CLARIFICATION_RESPONSE,
				requestId: clarificationId,
				sourceName: pending.sourceName,
				question: pending.question,
				answer
			});
			this.#pendingClarificationResolvers.delete(clarificationId);
			this.#activeState()?.onClarificationResolved(clarificationId, pending.sourceName);
		}
	}

	clear(): void {
		this.#activeState()?.clearMessages();
	}
}

// ============================================
// Factory function
// ============================================

/**
 * Create a workspace that owns session list, thread management, and streaming.
 *
 * Internally uses AgentClient for all streaming (correct sequential event queue,
 * bidirectional permission/clarification handling). The transport is never exposed.
 */
export function createWorkspace(options: CreateWorkspaceOptions): Workspace {
	// $effect.root gives the instance a reactive owner when called outside a
	// component (e.g. module-level singletons). On the server (SSR) $effect.root
	// is a no-op, so we fall back to a plain new — SSR only needs a single
	// synchronous render and does not require reactive signal tracking.
	let instance: WorkspaceImpl | undefined;
	$effect.root(() => {
		instance = new WorkspaceImpl(options);
	});
	return (instance ?? new WorkspaceImpl(options));
}
