import type {
	AgentEvent,
	KnownAgentEvent,
	AgentRunInputEvent,
	EventSubscription,
	PermissionChoice,
	CreateSessionRequest,
	UpdateSessionRequest,
	ListSessionsOptions,
	CreateThreadRequest,
	ForkThreadRequest,
	AgentSummaryDto,
	StoredAgentDto,
	CreateAgentRequest,
	UpdateAgentRequest,
	Session,
	Thread,
	SiblingThread,
	ThreadMessage,
	ContentReference,
	RunConfig,
	ChatRunConfig,
	ClientToolHandler,
	ClientToolRegistry,
} from '@hpd-research/hpd-agent-client';
export type { RunConfig, ChatRunConfig };
import type { AgentState } from '../agent/agent.svelte.ts';

/**
 * All methods of AgentClient used by WorkspaceImpl.
 * Allows test injection of a fake client without importing the real class.
 */
export interface AgentClientLike {
	// Event-native runtime
	run(input: AgentRunInputEvent): Promise<void>;
	on<TType extends KnownAgentEvent['type']>(
		type: TType,
		handler: (event: Extract<KnownAgentEvent, { type: TType }>) => void | Promise<void>
	): EventSubscription;
	onAny(handler: (event: AgentEvent) => void | Promise<void>): EventSubscription;
	onError(handler: (error: Error) => void | Promise<void>): EventSubscription;
	abort(): void;
	tools?: Pick<ClientToolRegistry, 'registerFallback'>;

	// Session CRUD
	listSessions(options?: ListSessionsOptions): Promise<Session[]>;
	getSession(sessionId: string): Promise<Session | null>;
	createSession(options?: CreateSessionRequest): Promise<Session>;
	updateSession(sessionId: string, request: UpdateSessionRequest): Promise<Session>;
	deleteSession(sessionId: string): Promise<void>;

	// Thread CRUD
	listThreads(sessionId: string): Promise<Thread[]>;
	getThread(sessionId: string, threadId: string): Promise<Thread | null>;
	createThread(sessionId: string, options?: CreateThreadRequest): Promise<Thread>;
	forkThread(sessionId: string, threadId: string, options: ForkThreadRequest): Promise<Thread>;
	deleteThread(sessionId: string, threadId: string, options?: { recursive?: boolean }): Promise<void>;
	getThreadMessages(sessionId: string, threadId: string): Promise<ThreadMessage[]>;

	// Sibling navigation
	getThreadSiblings(sessionId: string, threadId: string): Promise<SiblingThread[]>;
	getNextSibling(sessionId: string, threadId: string): Promise<Thread | null>;
	getPreviousSibling(sessionId: string, threadId: string): Promise<Thread | null>;

	// Agent definition CRUD
	listAgents(): Promise<AgentSummaryDto[]>;
	getAgent(agentId: string): Promise<StoredAgentDto | null>;
	createAgent(request: CreateAgentRequest): Promise<StoredAgentDto>;
	updateAgent(agentId: string, request: UpdateAgentRequest): Promise<StoredAgentDto>;
	deleteAgent(agentId: string): Promise<void>;

	// Content upload
	uploadContent(sessionId: string, threadId: string, file: File | Blob, name?: string): Promise<ContentReference>;
}

export interface CreateWorkspaceOptions {
	/** Base URL of the HPD Agent API */
	baseUrl: string;

	/** Transport type (default: 'sse') */
	transport?: 'sse' | 'websocket';

	/** Additional request headers (SSE only) */
	headers?: Record<string, string>;

	/** Session to activate on init (defaults to most recent) */
	sessionId?: string;

	/** Thread to activate on init within the initial session (defaults to 'main') */
	initialThreadId?: string;

	/** Maximum number of thread states to keep in memory (default: 10) */
	maxCachedThreads?: number;

	/** Handler for client tool invocations */
	onClientToolInvoke?: ClientToolHandler;

	/** Default agent definition ID to use for all streams (defaults to "default" on the server) */
	agentId?: string;

	/** Called when a stream completes */
	onComplete?: () => void;

	/** Called when a stream errors */
	onError?: (message: string) => void;

	/**
	 * @internal — test-only. Inject a fake AgentClient instead of constructing
	 * one from baseUrl. Allows unit tests to control both streaming events and
	 * CRUD operations without a real server.
	 */
	_client?: AgentClientLike;
}

/**
 * Options passed to Workspace.send().
 * All fields are optional — omit to use the server defaults.
 */
export interface SendOptions {
	/** Per-send run configuration (model, temperature, etc.) */
	runConfig?: RunConfig;
	/** Resolved content references to attach to the message as UriContent */
	attachments?: ContentReference[];
}

export interface Workspace {
	// ==========================================
	// Level 1: Session list
	// ==========================================

	/** All sessions */
	readonly sessions: Session[];

	/** ID of the currently active session */
	readonly activeSessionId: string | null;

	/** True while loading (session switch, thread switch, init) */
	readonly loading: boolean;

	/** Error message, or null */
	readonly error: string | null;

	/** Switch to an existing session */
	selectSession(sessionId: string): Promise<void>;

	/** Create a new session and switch to it */
	createSession(options?: CreateSessionRequest): Promise<void>;

	/** Delete a session. If active, switches to another first. */
	deleteSession(sessionId: string): Promise<void>;

	// ==========================================
	// Level 2: Thread view (of active session)
	// ==========================================

	/** All threads of the active session */
	readonly threads: Map<string, Thread>;

	/** ID of the currently active thread */
	readonly activeThreadId: string | null;

	/** Active thread metadata (derived) */
	readonly activeThread: Thread | null;

	/** Sibling threads of the active thread, sorted by siblingIndex */
	readonly activeSiblings: Thread[];

	readonly canGoNext: boolean;
	readonly canGoPrevious: boolean;
	readonly currentSiblingPosition: { current: number; total: number };

	/** Switch to a different thread in the active session */
	switchThread(threadId: string): Promise<void>;

	goToNextSibling(): Promise<void>;
	goToPreviousSibling(): Promise<void>;
	goToSiblingByIndex(index: number): Promise<void>;

	/**
	 * Fork at messageIndex, switch to the fork, send editedContent.
	 * The edit creates a new sibling thread from the parent.
	 */
	editMessage(messageIndex: number, newContent: string): Promise<void>;

	/** Delete a thread. If active, navigates to a sibling first.
	 *  Pass recursive: true to delete the entire subtree (must be enabled server-side via AllowRecursiveThreadDelete). */
	deleteThread(threadId: string, options?: { recursive?: boolean }): Promise<void>;

	/** Create a new empty thread in the active session */
	createThread(options?: CreateThreadRequest): Promise<Thread>;

	/** Refresh thread metadata from backend */
	refreshThread(threadId: string): Promise<void>;

	/** Force reload on next switchThread() (drop cached state) */
	invalidateThread(threadId: string): void;

	// ==========================================
	// Level 3: Thread streaming state
	// ==========================================

	/** Reactive state of the active thread (messages, streaming, tools, etc.) */
	readonly state: AgentState | null;

	// ==========================================
	// Agent selection
	// ==========================================

	/** All available agent definitions loaded at init */
	readonly agents: AgentSummaryDto[];

	/** ID of the currently selected agent definition (null = server default) */
	readonly activeAgentId: string | null;

	/** Switch the active agent definition. Pass null to revert to server default. */
	selectAgent(agentId: string | null): void;

	/** Refresh the agent list from the server */
	listAgents(): Promise<AgentSummaryDto[]>;

	/** The underlying AgentClient — use to call uploadContent() or other low-level ops */
	readonly client: AgentClientLike;

	/** Send a message. Accepts optional SendOptions for per-send runConfig and file attachments. */
	send(content: string, options?: SendOptions): Promise<void>;

	/** Send an event-native input envelope. Missing workspace scope is stamped when possible. */
	run(input: AgentRunInputEvent): Promise<void>;

	/** Abort the current stream */
	abort(): void;

	/** Approve a pending permission request */
	approve(permissionId: string, choice?: PermissionChoice): Promise<void>;

	/** Deny a pending permission request */
	deny(permissionId: string, reason?: string): Promise<void>;

	/** Respond to a clarification request */
	clarify(clarificationId: string, answer: string): Promise<void>;

	/** Clear all messages on the active thread */
	clear(): void;
}
