/**
 * createWorkspace() - Unified Session/Branch/Streaming Factory
 *
 * Single factory that owns all three levels of the HPD Agent hierarchy:
 *   Level 1: Session list (select, create, delete sessions)
 *   Level 2: Branch view (switch, fork, navigate siblings)
 *   Level 3: Branch streaming state (send, approve, clarify, abort)
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
 * workspace.activeBranch   // reactive metadata
 * workspace.switchBranch(id)
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
	type Branch,
	type BranchMessage,
	type ContentReference,
	type CreateBranchRequest,
	type CreateSessionRequest,
	type Session,
	type AgentSummaryDto,
} from '@hpd/hpd-agent-client';
import { AgentState } from '../agent/agent.svelte.ts';
import type { Message, MessageRole, ToolCall } from '../agent/types.ts';
import type { AgentClientLike, CreateWorkspaceOptions, SendOptions, Workspace } from './types.ts';

// ============================================
// History Loading
// ============================================

/**
 * Map raw BranchMessage[] to UI Message[].
 * Extracts text, reasoning, and tool calls from the full AIContent list.
 * All fields are set to their fully-settled defaults — no streaming side effects.
 */
function mapToUIMessages(raw: BranchMessage[]): Message[] {
	return raw
		// 'tool' role messages are function result containers — internal plumbing, not user-visible
		.filter((msg) => msg.role !== 'tool')
		.map((msg) => {
		let content = '';
		let reasoning: string | undefined;
		const toolCalls: ToolCall[] = [];

		for (const item of msg.contents) {
			if (item.$type === 'text') {
				const tc = item as import('@hpd/hpd-agent-client').AiTextContent;
				content += tc.text;
			} else if (item.$type === 'reasoning') {
				const rc = item as import('@hpd/hpd-agent-client').AiTextReasoningContent;
				reasoning = (reasoning ?? '') + rc.text;
			} else if (item.$type === 'functionCall') {
				const fc = item as import('@hpd/hpd-agent-client').AiFunctionCallContent;
				toolCalls.push({
					callId: fc.callId,
					name: fc.name,
					messageId: msg.id,
					status: 'complete',
					args: fc.arguments,
					startTime: new Date(msg.timestamp)
				});
			} else if (item.$type === 'functionResult') {
				const fr = item as import('@hpd/hpd-agent-client').AiFunctionResultContent;
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
	readonly #maxCachedBranches: number;

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
	// Level 2: Branch registry ($state)
	// ==========================================

	#branches = $state<Map<string, Branch>>(new Map());
	#activeBranchId = $state<string | null>(null);

	// ==========================================
	// Level 2+3: Branch state cache (plain Maps, LRU managed manually)
	// Key format: `${sessionId}:${branchId}`
	// Two sessions can both have a branch named 'main' — compound key prevents collision.
	// ==========================================

	readonly #branchStates = new Map<string, AgentState>();
	readonly #branchAccessTimestamps = new Map<string, number>();

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
		return Array.from(this.#branches.values())
			.filter(
				(b) =>
					b.forkedFrom === branch.forkedFrom &&
					b.forkedAtMessageId === branch.forkedAtMessageId
			)
			.sort((a, b) => a.siblingIndex - b.siblingIndex);
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
	get branches() {
		return this.#branches;
	}
	get activeBranchId() {
		return this.#activeBranchId;
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
		this.#maxCachedBranches = options.maxCachedBranches ?? 10;
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
				await this.#loadSession(targetSessionId, this.#options.initialBranchId);
			}
		} catch (err) {
			this.#error = err instanceof Error ? err.message : 'Failed to initialize';
		} finally {
			this.#loading = false;
		}
	}

	// ==========================================
	// Internal: load session branches + switch to branch
	// Does NOT set #loading (callers manage that).
	// ==========================================

	async #loadSession(sessionId: string, preferredBranchId?: string): Promise<void> {
		// Clear branch view immediately while loading
		this.#activeBranchId = null;
		this.#branches = new Map();

		// Load all branches for this session
		const branchList = await this.#client.listBranches(sessionId);
		const branchMap = new Map<string, Branch>();
		for (const branch of branchList) {
			branchMap.set(branch.id, branch);
		}
		this.#branches = branchMap;
		this.#activeSessionId = sessionId;

		// Determine which branch to activate
		const targetBranchId =
			preferredBranchId ??
			(branchMap.has('main') ? 'main' : branchList[0]?.id ?? null);

		if (targetBranchId) {
			await this.#loadBranch(sessionId, targetBranchId);
		}
	}

	// ==========================================
	// Internal: load branch state into cache + activate
	// ==========================================

	async #loadBranch(sessionId: string, branchId: string): Promise<void> {
		const cacheKey = `${sessionId}:${branchId}`;
		const existing = this.#branchStates.get(cacheKey);
		if (existing) {
			this.#branchAccessTimestamps.set(cacheKey, Date.now());
			this.#activeBranchId = branchId;
			return;
		} else {
			const rawMessages = await this.#client.getBranchMessages(sessionId, branchId);
			const mapped = mapToUIMessages(rawMessages);
			const state = new AgentState();
			state.loadHistory(mapped);
			this.#branchStates.set(cacheKey, state);
			this.#branchAccessTimestamps.set(cacheKey, Date.now());
		}

		this.#activeBranchId = branchId;
		this.#evictOldBranchStates();
	}

	// ==========================================
	// Internal: LRU eviction
	// ==========================================

	#evictOldBranchStates(): void {
		if (this.#branchStates.size <= this.#maxCachedBranches) return;

		const sorted = Array.from(this.#branchAccessTimestamps.entries()).sort(
			(a, b) => a[1] - b[1]
		);

		const activeCacheKey = this.#activeSessionId && this.#activeBranchId
			? `${this.#activeSessionId}:${this.#activeBranchId}`
			: null;

		for (const [key] of sorted) {
			if (this.#branchStates.size <= this.#maxCachedBranches) break;
			if (key !== activeCacheKey) {
				this.#branchStates.delete(key);
				this.#branchAccessTimestamps.delete(key);
			}
		}
	}

	// ==========================================
	// Internal: active branch state (for event handlers)
	// ==========================================

	#activeState(): AgentState | null {
		const sid = this.#activeSessionId;
		const bid = this.#activeBranchId;
		if (!sid || !bid) return null;
		return this.#branchStates.get(`${sid}:${bid}`) ?? null;
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
			this.#activeState()?.onclientHarnessesRegistered(
				event.registeredHarnesses,
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
				this.#activeBranchId = null;
				this.#branches = new Map();
			}
		}

		await this.#client.deleteSession(sessionId);
		this.#sessions = this.#sessions.filter((s) => s.id !== sessionId);

		// Evict all cached branch states for this session
		for (const key of this.#branchStates.keys()) {
			if (key.startsWith(`${sessionId}:`)) {
				this.#branchStates.delete(key);
				this.#branchAccessTimestamps.delete(key);
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

		if (!this.#branches.has(branchId)) {
			throw new Error(`Branch ${branchId} not found in active session`);
		}

		this.#loading = true;
		this.#error = null;
		try {
			await this.#loadBranch(sessionId, branchId);
			// Refresh branch metadata so sibling navigation fields are current
			const fresh = await this.#client.getBranch(sessionId, branchId);
			if (fresh) {
				const updated = new Map(this.#branches);
				updated.set(branchId, fresh);
				this.#branches = updated;
			}
		} catch (err) {
			this.#error = err instanceof Error ? err.message : 'Failed to switch branch';
			throw err;
		} finally {
			this.#loading = false;
		}
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
		const activeState = this.#activeState();

		if (!sessionId || !branchId || !activeState) throw new Error('No active branch');

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

		// Always fork from the branch that OWNS the shared context at forkAtIndex.
		// If the current branch is itself a fork at this same message (forkedAtMessageId === fromMessageId),
		// then its parent already owns the shared context — fork from the parent instead.
		// This ensures all edits of the same message become flat siblings of the original branch
		// rather than a linear chain of forks-of-forks.
		const activeBranch = this.#branches.get(branchId)!;
		const sourceBranchId =
			!activeBranch.isOriginal && activeBranch.forkedAtMessageId === fromMessageId
				? activeBranch.forkedFrom!
				: branchId;

		const fork = await this.#client.forkBranch(sessionId, sourceBranchId, {
			newBranchId: crypto.randomUUID(),
			fromMessageId,
			name: `Edit: ${newContent.slice(0, 30)}${newContent.length > 30 ? '...' : ''}`,
			agentId: this.#activeAgentId ?? undefined
		});
		// Register fork in branch map
		const newBranches = new Map(this.#branches);
		newBranches.set(fork.id, fork);
		this.#branches = newBranches;

		// Refresh source branch metadata (it gained a new sibling group member)
		const updatedSource = await this.#client.getBranch(sessionId, sourceBranchId);
		if (updatedSource) {
			const refreshed = new Map(this.#branches);
			refreshed.set(sourceBranchId, updatedSource);
			this.#branches = refreshed;
		}

		// Also refresh all existing siblings at this fork point so their totalSiblings is current
		const allBranches = Array.from(this.#branches.values());
		const siblingsToRefresh = allBranches.filter(
			b => b.id !== fork.id && b.id !== sourceBranchId &&
				b.forkedFrom === sourceBranchId && b.forkedAtMessageId === fromMessageId
		);
		if (siblingsToRefresh.length > 0) {
			const refreshed = new Map(this.#branches);
			await Promise.all(siblingsToRefresh.map(async sib => {
				const fresh = await this.#client.getBranch(sessionId, sib.id);
				if (fresh) refreshed.set(sib.id, fresh);
			}));
			this.#branches = refreshed;
		}

		// The fork has messages up to forkAtIndex (messageIndex - 1), not including the edited message.
		// Switch to fork and send the edited content as a fresh message.
		await this.switchBranch(fork.id);
		await this.send(newContent);
	}

	async deleteBranch(branchId: string, options?: { recursive?: boolean }): Promise<void> {
		const sessionId = this.#activeSessionId;
		if (!sessionId) throw new Error('No active session');

		const branchToDelete = this.#branches.get(branchId);
		if (!branchToDelete) throw new Error('Branch not found');

		// Capture siblings BEFORE any navigation (activeSiblings is $derived — it changes after switchBranch)
		const siblingsToRefresh = branchToDelete.forkedFrom
			? Array.from(this.#branches.values()).filter(
					(b) =>
						b.id !== branchId &&
						b.forkedFrom === branchToDelete.forkedFrom &&
						b.forkedAtMessageId === branchToDelete.forkedAtMessageId
				)
			: [];

		// Navigate away if the active branch is the deleted branch OR is a descendant of it.
		// Use the ancestors chain — every branch stores its full lineage.
		const activeIsInsideSubtree =
			this.#activeBranchId === branchId ||
			(this.#activeBranchId !== null &&
				this.activeBranch?.ancestors != null &&
				Object.values(this.activeBranch.ancestors).includes(branchId));

		if (activeIsInsideSubtree) {
			let targetId: string | null = null;

			if (branchToDelete.nextSiblingId) {
				targetId = branchToDelete.nextSiblingId;
			} else if (branchToDelete.previousSiblingId) {
				targetId = branchToDelete.previousSiblingId;
			} else if (branchToDelete.originalBranchId) {
				targetId = branchToDelete.originalBranchId;
			} else {
				targetId = Array.from(this.#branches.keys()).find((id) => id !== branchId) ?? null;
			}

			if (!targetId) throw new Error('Cannot delete the only branch');
			await this.switchBranch(targetId);
		}

		await this.#client.deleteBranch(sessionId, branchId, options);

		// Remove the deleted branch and all its descendants from the local branch map and state cache
		const deletedIds = this.#collectSubtreeIds(branchId);
		const newBranches = new Map(this.#branches);
		for (const id of deletedIds) {
			newBranches.delete(id);
			this.#branchStates.delete(`${sessionId}:${id}`);
			this.#branchAccessTimestamps.delete(`${sessionId}:${id}`);
		}
		this.#branches = newBranches;

		// Refresh sibling metadata (backend reindexed siblingIndex, totalSiblings, navigation pointers)
		for (const sibling of siblingsToRefresh) {
			await this.refreshBranch(sibling.id);
		}
	}

	/** Collect the IDs of a branch and all its descendants from the local branch map. */
	#collectSubtreeIds(branchId: string): string[] {
		const result: string[] = [branchId];
		const branch = this.#branches.get(branchId);
		if (branch) {
			for (const childId of branch.childBranches) {
				result.push(...this.#collectSubtreeIds(childId));
			}
		}
		return result;
	}

	async createBranch(options?: CreateBranchRequest): Promise<Branch> {
		const sessionId = this.#activeSessionId;
		if (!sessionId) throw new Error('No active session');

		const branch = await this.#client.createBranch(sessionId, options);
		const newBranches = new Map(this.#branches);
		newBranches.set(branch.id, branch);
		this.#branches = newBranches;
		return branch;
	}

	async refreshBranch(branchId: string): Promise<void> {
		const sessionId = this.#activeSessionId;
		if (!sessionId) return;

		const branch = await this.#client.getBranch(sessionId, branchId);
		if (branch) {
			const newBranches = new Map(this.#branches);
			newBranches.set(branchId, branch);
			this.#branches = newBranches;
		}
	}

	invalidateBranch(branchId: string): void {
		const sessionId = this.#activeSessionId;
		if (!sessionId) return;
		this.#branchStates.delete(`${sessionId}:${branchId}`);
		this.#branchAccessTimestamps.delete(`${sessionId}:${branchId}`);
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
		const branchId = this.#activeBranchId;
		const activeState = this.#activeState();

		if (!sessionId || !branchId || !activeState) throw new Error('No active branch');

		activeState.addUserMessage(content);

		const effectiveAgentId = this.#activeAgentId ?? undefined;
		const messages = this.#buildMessages(content, options?.attachments);
		await this.#client.run({
			type: EventTypes.USER_TEXT_INPUT,
			text: messages[0]?.content ?? content,
			sessionId,
			branchId,
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
				branchId: input.branchId ?? this.#activeBranchId ?? undefined,
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
			// Note: AgentState has no onClarificationResolved yet.
			// The stream unblocks, but the UI item stays visually pending.
			// This matches the existing createAgent() behavior (same TODO).
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
 * Create a workspace that owns session list, branch management, and streaming.
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
