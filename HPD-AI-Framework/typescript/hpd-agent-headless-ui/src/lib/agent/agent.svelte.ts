/**
 * AgentState - Reactive State Manager for HPD Agent
 *
 * This is the core state manager that holds all chat data and implements
 * the event handler methods that EventMapper calls when HPD protocol events arrive.
 */

import type { AgentEvent } from '@hpd/hpd-agent-client';
import type {
	Message,
	MessageRole,
	ToolCall,
	ToolCallStatus,
	PermissionRequest,
	ClarificationRequest,
	ClientToolInvokeRequest
} from './types.ts';
import { AudioPlayerState } from '../audio-player/audio-player.svelte.ts';
import { TranscriptionState } from '../transcription/transcription.svelte.ts';
import { VoiceActivityIndicatorState } from '../voice-activity-indicator/voice-activity-indicator.svelte.ts';
import { InterruptionIndicatorState } from '../interruption-indicator/interruption-indicator.svelte.ts';
import { TurnIndicatorState } from '../turn-indicator/turn-indicator.svelte.ts';
import { AudioVisualizerState } from '../audio-visualizer/audio-visualizer.svelte.ts';

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

	// Audio component states (Phase 3 - Voice UI)
	// TODO: These are placeholder states - they will be properly initialized when needed
	#audioPlayer = $state<AudioPlayerState | null>(null);
	#transcription = $state<TranscriptionState | null>(null);
	#voiceActivity = $state<VoiceActivityIndicatorState | null>(null);
	#interruption = $state<InterruptionIndicatorState | null>(null);
	#turnIndicator = $state<TurnIndicatorState | null>(null);
	#audioVisualizer = $state<AudioVisualizerState | null>(null);

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

	// Audio component accessors
	readonly audioPlayer = $derived(this.#audioPlayer);
	readonly transcription = $derived(this.#transcription);
	readonly voiceActivity = $derived(this.#voiceActivity);
	readonly interruption = $derived(this.#interruption);
	readonly turnIndicator = $derived(this.#turnIndicator);
	readonly audioVisualizer = $derived(this.#audioVisualizer);

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

	onToolCallResult(callId: string, result: string) {
		this.#replaceToolCall(callId, (t) => ({ ...t, result, status: 'complete', endTime: new Date() }));
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

	onclientToolKitsRegistered(
		registeredToolKits: string[],
		totalTools: number,
		timestamp: string
	) {
		console.log(
			`[AgentState] Registered ${totalTools} tools in ${registeredToolKits.length} groups at ${timestamp}`
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

	// --- Audio Events (TTS) ---

	onSynthesisStarted(synthesisId: string, modelId?: string, voice?: string, streamId?: string) {
		this.#audioPlayer?.onSynthesisStarted(synthesisId, modelId, voice, streamId);
		this.#turnIndicator?.onSynthesisStarted(synthesisId, modelId, voice, streamId);
	}

	onAudioChunk(
		synthesisId: string,
		base64Audio: string,
		mimeType: string,
		chunkIndex: number,
		duration: string,
		isLast: boolean,
		streamId?: string
	) {
		this.#audioPlayer?.onAudioChunk(synthesisId, base64Audio, mimeType, chunkIndex, duration, isLast, streamId);
		// AudioVisualizer uses AnalyserNode from AudioPlayer, not direct audio chunks
	}

	onSynthesisCompleted(
		synthesisId: string,
		wasInterrupted: boolean,
		totalChunks: number,
		deliveredChunks: number,
		streamId?: string
	) {
		this.#audioPlayer?.onSynthesisCompleted(synthesisId, wasInterrupted, totalChunks, deliveredChunks, streamId);
	}

	// --- Audio Events (STT) ---

	onTranscriptionDelta(
		transcriptionId: string,
		text: string,
		isFinal: boolean,
		confidence?: number
	) {
		this.#transcription?.onTranscriptionDelta(transcriptionId, text, isFinal, confidence);
	}

	onTranscriptionCompleted(
		transcriptionId: string,
		finalText: string,
		_processingDuration: string
	) {
		// Note: _processingDuration is ignored as TranscriptionState expects confidence instead
		this.#transcription?.onTranscriptionCompleted(transcriptionId, finalText);
	}

	// --- Audio Events (Interruption) ---

	onUserInterrupted(transcribedText?: string) {
		this.#interruption?.onUserInterrupted(transcribedText);
	}

	onSpeechPaused(synthesisId: string, reason: 'user_speaking' | 'potential_interruption') {
		this.#interruption?.onSpeechPaused(synthesisId, reason);
		this.#audioPlayer?.onSpeechPaused(synthesisId, reason);
	}

	onSpeechResumed(synthesisId: string, pauseDuration: string) {
		this.#interruption?.onSpeechResumed(synthesisId, pauseDuration);
		this.#audioPlayer?.onSpeechResumed(synthesisId, pauseDuration);
	}

	// --- Audio Events (Preemptive Generation) ---

	onPreemptiveGenerationStarted(generationId: string, turnCompletionProbability: number) {
		console.log(`[AgentState] Preemptive generation started: ${generationId}`, { turnCompletionProbability });
		// TODO: Handle preemptive generation state when needed
	}

	onPreemptiveGenerationDiscarded(generationId: string, reason: 'user_continued' | 'low_confidence') {
		console.log(`[AgentState] Preemptive generation discarded: ${generationId}`, { reason });
		// TODO: Handle preemptive generation state when needed
	}

	// --- Audio Events (VAD) ---

	onVadStartOfSpeech(timestamp: string, speechProbability: number) {
		this.#voiceActivity?.onVadStartOfSpeech(timestamp, speechProbability);
		this.#turnIndicator?.onVadStartOfSpeech(timestamp, speechProbability);
	}

	onVadEndOfSpeech(timestamp: string, speechDuration: string, speechProbability: number) {
		this.#voiceActivity?.onVadEndOfSpeech(timestamp, speechDuration, speechProbability);
	}

	// --- Audio Events (Metrics) ---

	onAudioPipelineMetrics(
		metricType: 'latency' | 'quality' | 'throughput' | 'error',
		metricName: string,
		value: number,
		unit?: string
	) {
		console.log(`[AgentState] Audio pipeline metrics:`, { metricType, metricName, value, unit });
		// TODO: Expose metrics for debugging/monitoring UI
	}

	// --- Audio Events (Turn Detection) ---

	onTurnDetected(
		transcribedText: string,
		completionProbability: number,
		silenceDuration: string,
		detectionMethod: 'heuristic' | 'ml' | 'manual' | 'timeout'
	) {
		this.#turnIndicator?.onTurnDetected(transcribedText, completionProbability, silenceDuration, detectionMethod);
	}

	// --- Audio Events (Filler) ---

	onFillerAudioPlayed(phrase: string, duration: string) {
		console.log(`[AgentState] Filler audio played: ${phrase}`, { duration });
		// TODO: Handle filler audio indication when needed
	}

	// ============================================
	// Public Methods (for user interaction)
	// ============================================

	/**
	 * Dispatch a transport event to the correct handler.
	 * Single entry point — protocol knowledge lives here, not in callers.
	 */
	dispatch(event: AgentEvent): void {
		switch (event.type) {
			case 'TEXT_MESSAGE_START':
				this.onTextMessageStart(event.messageId, event.role);
				break;
			case 'TEXT_DELTA':
				this.onTextDelta(event.text, event.messageId);
				break;
			case 'TEXT_MESSAGE_END':
				this.onTextMessageEnd(event.messageId);
				break;
			case 'REASONING_MESSAGE_START':
				this.onReasoningMessageStart(event.messageId, event.role);
				break;
			case 'REASONING_DELTA':
				this.onReasoningDelta(event.text, event.messageId);
				break;
			case 'REASONING_MESSAGE_END':
				this.onReasoningMessageEnd(event.messageId);
				break;
			case 'TOOL_CALL_START':
				this.onToolCallStart(event.callId, event.name, event.messageId);
				break;
			case 'TOOL_CALL_ARGS':
				this.onToolCallArgs(event.callId, event.argsJson);
				break;
			case 'TOOL_CALL_END':
				this.onToolCallEnd(event.callId);
				break;
			case 'TOOL_CALL_RESULT':
				this.onToolCallResult(event.callId, event.result);
				break;
			case 'MESSAGE_TURN_STARTED':
				this.onMessageTurnStarted(event.messageTurnId, event.conversationId, event.agentName, event.timestamp);
				break;
			case 'MESSAGE_TURN_FINISHED':
				this.onMessageTurnFinished(event.messageTurnId, event.conversationId, event.duration, event.timestamp);
				break;
			case 'MESSAGE_TURN_ERROR':
				this.onMessageTurnError(event.message);
				break;
			// All other event types (audio, VAD, permissions, etc.) are handled
			// by callers that need them (e.g. createAgent). BranchManager ignores them.
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
	 * Used when restoring a branch from the backend — bypasses streaming state.
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
