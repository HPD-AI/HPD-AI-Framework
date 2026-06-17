/**
 * AgentState - Reactive State Manager for HPD Agent
 *
 * This is the core state manager that holds all chat data and implements
 * the event handler methods that EventMapper calls when HPD protocol events arrive.
 */

import type { AgentEvent, KnownAgentEvent, ToolResultPayload } from '@hpd-research/hpd-agent-client';
import type {
	Message,
	MessageRole,
	ToolCall,
	ToolCallStatus,
	PermissionRequest,
	ClarificationRequest,
	ClientToolInvokeRequest
} from './types.ts';

function formatToolResultPayload(result: ToolResultPayload): string {
	if (result.text) return result.text;
	if (result.json !== undefined) return JSON.stringify(result.json);
	if (result.content && result.content.length > 0) return JSON.stringify(result.content);
	return '';
}

export class AgentState {
	// ============================================
	// Reactive State ($state runes)
	// ============================================

	#messages = $state<Message[]>([]);
	#streaming = $state(false);
	#reasoning = $state(false);
	#error = $state<string | null>(null);

	// Tool execution tracking
	#activeTools = $state<ToolCall[]>([]);

	// Bidirectional request tracking
	#pendingPermissions = $state<PermissionRequest[]>([]);
	#pendingClarifications = $state<ClarificationRequest[]>([]);
	#pendingClientToolRequests = $state<ClientToolInvokeRequest[]>([]);

	// Turn tracking
	#currentTurnId = $state<string | null>(null);
	#currentConversationId = $state<string | null>(null);

	// ============================================
	// Derived State ($derived)
	// ============================================

	readonly isWaitingForUser = $derived(
		this.#pendingPermissions.length > 0 || this.#pendingClarifications.length > 0
	);

	readonly lastMessage = $derived(this.#messages[this.#messages.length - 1]);

	readonly canSend = $derived(!this.#streaming && !this.isWaitingForUser && !this.#error);

	readonly hasMessages = $derived(this.#messages.length > 0);

	// ============================================
	// Public State (reactive — $derived so Svelte tracks through class boundaries)
	// ============================================

	readonly messages = $derived(this.#messages);
	readonly streaming = $derived(this.#streaming);
	readonly reasoning = $derived(this.#reasoning);
	readonly error = $derived(this.#error);
	readonly activeTools = $derived(this.#activeTools);
	readonly pendingPermissions = $derived(this.#pendingPermissions);
	readonly pendingClarifications = $derived(this.#pendingClarifications);

	// ============================================
	// Event Handlers (called by EventMapper)
	// ============================================

	// --- Text Content Events ---

	onTextMessageStart(messageId: string, role: string) {
		const existing = this.#messages.findIndex((m) => m.id === messageId);
		if (existing !== -1) {
			// Same message already created (reasoning came first) — transition to text streaming
			this.#messages[existing] = { ...this.#messages[existing], streaming: true, thinking: false };
		} else {
			this.#messages.push({
				id: messageId,
				role: role as MessageRole,
				content: '',
				streaming: true,
				thinking: false,
				timestamp: new Date(),
				toolCalls: []
			});
		}
		this.#streaming = true;
	}

	onTextDelta(text: string, messageId: string) {
		const index = this.#messages.findIndex((m) => m.id === messageId);
		if (index !== -1) {
			// Create new message object to trigger reactivity
			this.#messages[index] = {
				...this.#messages[index],
				content: this.#messages[index].content + text
			};
		}
	}

	onTextMessageEnd(messageId: string) {
		const index = this.#messages.findIndex((m) => m.id === messageId);
		if (index !== -1) {
			// Create new message object to trigger reactivity
			this.#messages[index] = {
				...this.#messages[index],
				streaming: false
			};
		}
		this.#streaming = false;
	}

	// --- Reasoning Events ---

	onReasoningMessageStart(messageId: string, role: string) {
		const existing = this.#messages.findIndex((m) => m.id === messageId);
		if (existing !== -1) {
			this.#messages[existing] = { ...this.#messages[existing], streaming: true, thinking: true, reasoning: this.#messages[existing].reasoning ?? '' };
		} else {
			this.#messages.push({
				id: messageId,
				role: role as MessageRole,
				content: '',
				streaming: true,
				thinking: true,
				timestamp: new Date(),
				toolCalls: [],
				reasoning: ''
			});
		}
		this.#reasoning = true;
	}

	onReasoningDelta(text: string, messageId: string) {
		const index = this.#messages.findIndex((m) => m.id === messageId);
		if (index !== -1) {
			const current = this.#messages[index];
			// Create new message object to trigger reactivity
			this.#messages[index] = {
				...current,
				reasoning: (current.reasoning || '') + text
			};
		}
	}

	onReasoningMessageEnd(messageId: string) {
		const index = this.#messages.findIndex((m) => m.id === messageId);
		if (index !== -1) {
			// Create new message object to trigger reactivity
			this.#messages[index] = {
				...this.#messages[index],
				streaming: false,
				thinking: false
			};
		}
		this.#reasoning = false;
	}

	// --- Tool Call Events ---

	onToolCallStart(callId: string, name: string, messageId: string) {
		const toolCall: ToolCall = {
			callId,
			name,
			messageId,
			status: 'pending',
			startTime: new Date()
		};

		this.#activeTools = [...this.#activeTools, toolCall];

		// Replace message object to trigger reactivity
		const msgIdx = this.#messages.findIndex((m) => m.id === messageId);
		if (msgIdx !== -1) {
			const msg = this.#messages[msgIdx];
			this.#messages[msgIdx] = { ...msg, toolCalls: [...msg.toolCalls, toolCall] };
		}
	}

	#replaceToolCall(callId: string, updater: (tc: ToolCall) => ToolCall) {
		this.#activeTools = this.#activeTools.map((t) => t.callId === callId ? updater(t) : t);

		const msgIdx = this.#messages.findIndex((m) => m.toolCalls.some((t) => t.callId === callId));
		if (msgIdx !== -1) {
			const msg = this.#messages[msgIdx];
			this.#messages[msgIdx] = {
				...msg,
				toolCalls: msg.toolCalls.map((t) => t.callId === callId ? updater(t) : t)
			};
		}
	}

	onToolCallArgs(callId: string, argsJson: string) {
		try {
			const args = JSON.parse(argsJson);
			this.#replaceToolCall(callId, (t) => ({ ...t, args, status: 'executing' }));
		} catch (e) {
			console.error('Failed to parse tool args:', e);
			this.#replaceToolCall(callId, (t) => ({ ...t, error: 'Invalid arguments', status: 'error' }));
		}
	}

	onToolCallEnd(callId: string) {
		const toolCall = this.#activeTools.find((t) => t.callId === callId);
		if (!toolCall) return;

		// Mark complete if TOOL_CALL_RESULT hasn't already done it
		if (toolCall.status === 'executing' || toolCall.status === 'pending') {
			this.#replaceToolCall(callId, (t) => ({ ...t, status: 'complete', endTime: new Date() }));
			this.#activeTools = this.#activeTools.filter((t) => t.callId !== callId);
		}
	}

	onToolCallResult(callId: string, result: ToolResultPayload, name?: string) {
		this.#replaceToolCall(callId, (t) => ({
			...t,
			name: name ?? t.name,
			result,
			resultText: formatToolResultPayload(result),
			status: 'complete',
			endTime: new Date()
		}));
		this.#activeTools = this.#activeTools.filter((t) => t.callId !== callId);
	}

	// --- Permission Events ---

	onPermissionRequest(request: {
		permissionId: string;
		sourceName: string;
		functionName: string;
		description?: string;
		callId: string;
		arguments?: Record<string, unknown>;
	}) {
		this.#pendingPermissions.push(request);
	}

	onPermissionApproved(permissionId: string, sourceName: string) {
		this.#pendingPermissions = this.#pendingPermissions.filter(
			(p) => p.permissionId !== permissionId
		);
	}

	onPermissionDenied(permissionId: string, sourceName: string, reason: string) {
		this.#pendingPermissions = this.#pendingPermissions.filter(
			(p) => p.permissionId !== permissionId
		);
	}

	// --- Clarification Events ---

	onClarificationRequest(request: {
		requestId: string;
		sourceName: string;
		question: string;
		agentName?: string;
		options?: string[];
	}) {
		this.#pendingClarifications.push(request);
	}

	onClarificationResolved(requestId: string, sourceName: string) {
		this.#pendingClarifications = this.#pendingClarifications.filter(
			(request) => request.requestId !== requestId
		);
	}

	// --- Client Tool Events ---

	onClientToolInvokeRequest(request: {
		requestId: string;
		toolName: string;
		callId: string;
		arguments: Record<string, unknown>;
		description?: string;
	}) {
		this.#pendingClientToolRequests.push(request);
		// TODO: Automatically invoke registered client tool handlers
	}

	onclientToolHarnessesRegistered(
		registeredToolHarnesses: string[],
		totalTools: number,
		timestamp: string
	) {
		console.log(
			`[AgentState] Registered ${totalTools} tools in ${registeredToolHarnesses.length} groups at ${timestamp}`
		);
	}

	// --- Message Turn Events ---

	onMessageTurnStarted(
		messageTurnId: string,
		conversationId: string,
		agentName: string,
		timestamp: string
	) {
		this.#currentTurnId = messageTurnId;
		this.#currentConversationId = conversationId;
		console.log(`[AgentState] Turn started: ${messageTurnId} in ${conversationId}`);
	}

	onMessageTurnFinished(
		messageTurnId: string,
		conversationId: string,
		duration: string,
		timestamp: string
	) {
		this.#currentTurnId = null;
		console.log(`[AgentState] Turn finished: ${messageTurnId} (${duration})`);
	}

	onMessageTurnError(message: string) {
		this.#error = message;
		this.#streaming = false;
		this.#reasoning = false;
		console.error('[AgentState] Turn error:', message);
	}

	// ============================================
	// Public Methods (for user interaction)
	// ============================================

	/**
	 * Dispatch a transport event to the correct handler.
	 * Single entry point — protocol knowledge lives here, not in callers.
	 */
	dispatch(event: AgentEvent): void {
		const known = event as KnownAgentEvent;
		switch (known.type) {
			case 'TEXT_MESSAGE_START':
				this.onTextMessageStart(known.messageId, known.role);
				break;
			case 'TEXT_DELTA':
				this.onTextDelta(known.text, known.messageId);
				break;
			case 'TEXT_MESSAGE_END':
				this.onTextMessageEnd(known.messageId);
				break;
			case 'REASONING_MESSAGE_START':
				this.onReasoningMessageStart(known.messageId, known.role);
				break;
			case 'REASONING_DELTA':
				this.onReasoningDelta(known.text, known.messageId);
				break;
			case 'REASONING_MESSAGE_END':
				this.onReasoningMessageEnd(known.messageId);
				break;
			case 'TOOL_CALL_START':
				this.onToolCallStart(known.callId, known.name, known.messageId);
				break;
			case 'TOOL_CALL_ARGS':
				this.onToolCallArgs(known.callId, known.argsJson);
				break;
			case 'TOOL_CALL_END':
				this.onToolCallEnd(known.callId);
				break;
			case 'TOOL_CALL_RESULT':
				this.onToolCallResult(known.callId, known.result, known.name);
				break;
			case 'MESSAGE_TURN_STARTED':
				this.onMessageTurnStarted(known.messageTurnId, known.conversationId, known.agentName, known.timestamp);
				break;
			case 'MESSAGE_TURN_FINISHED':
				this.onMessageTurnFinished(known.messageTurnId, known.conversationId, known.duration, known.timestamp);
				break;
			case 'MESSAGE_TURN_ERROR':
				this.onMessageTurnError(known.message);
				break;
			// All other event types (permissions, continuations, etc.) are handled
			// by callers that need them (e.g. createAgent). ThreadManager ignores them.
	}
	}

	/**
	 * Clear error state
	 */
	clearError() {
		this.#error = null;
	}

	/**
	 * Add a user message (for local display before sending to backend)
	 */

	/**
	 * Directly load a history of fully-formed messages.
	 * Used when restoring a thread from the backend — bypasses streaming state.
	 * Does NOT affect #streaming, #reasoning, or #activeTools.
	 */
	loadHistory(messages: Message[]): void {
		this.#messages = messages;
	}

	addUserMessage(content: string): Message {
		const message: Message = {
			id: `user-${Date.now()}`,
			role: 'user',
			content,
			streaming: false,
			thinking: false,
			timestamp: new Date(),
			toolCalls: []
		};

		this.#messages.push(message);
		return message;
	}

	/**
	 * Clear all messages
	 */
	clearMessages() {
		this.#messages = [];
		this.#activeTools = [];
		this.#pendingPermissions = [];
		this.#pendingClarifications = [];
		this.#error = null;
	}
}
