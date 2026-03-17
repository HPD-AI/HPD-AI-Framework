/**
 * MessageState - Reactive State for AI Messages
 *
 * Manages state for individual AI messages with streaming, thinking,
 * tool execution, and reasoning support.
 */

import type { Message, ToolCall, MessageRole } from '../agent/types.ts';
import type { MessageStatus, MessageHTMLProps, MessageSnippetProps } from './types.ts';

export class MessageState {
	// ============================================
	// Props (Immutable) - initialized in constructor
	// ============================================

	readonly id: string;
	readonly role: MessageRole;

	// ============================================
	// Reactive State ($state runes)
	// ============================================

	content = $state('');
	streaming = $state(false);
	thinking = $state(false);
	reasoning = $state('');
	toolCalls = $state<ToolCall[]>([]);
	timestamp = $state(new Date());

	// ============================================
	// Derived State ($derived)
	// ============================================

	/**
	 * Whether the message has reasoning text
	 */
	readonly hasReasoning = $derived(this.reasoning.length > 0);

	/**
	 * Whether the message has any tool calls
	 */
	readonly hasTools = $derived(this.toolCalls.length > 0);

	/**
	 * Whether the message has active (executing) tool calls
	 */
	readonly hasActiveTools = $derived(
		this.toolCalls.some((tool) => tool.status === 'pending' || tool.status === 'executing')
	);

	/**
	 * Current message status - prioritized by AI activity
	 */
	readonly status = $derived.by((): MessageStatus => {
		if (this.streaming) return 'streaming';
		if (this.thinking) return 'thinking';
		if (this.hasActiveTools) return 'executing';
		return 'complete';
	});

	/**
	 * HTML props for the root element (data attributes + ARIA)
	 */
	readonly props = $derived.by((): MessageHTMLProps => ({
		'data-message-id': this.id,
		'data-role': this.role,
		'data-streaming': this.streaming ? '' : undefined,
		'data-thinking': this.thinking ? '' : undefined,
		'data-has-tools': this.hasTools ? '' : undefined,
		'data-has-reasoning': this.hasReasoning ? '' : undefined,
		'data-status': this.status,
		'aria-live': this.streaming ? 'polite' : 'off',
		'aria-busy': this.streaming || this.thinking,
		'aria-label': `${this.role} message`,
		class: undefined
	}));

	/**
	 * Snippet props for content customization
	 */
	readonly snippetProps = $derived.by((): MessageSnippetProps => ({
		content: this.content,
		role: this.role,
		streaming: this.streaming,
		thinking: this.thinking,
		hasReasoning: this.hasReasoning,
		reasoning: this.reasoning,
		toolCalls: this.toolCalls,
		hasActiveTools: this.hasActiveTools,
		timestamp: this.timestamp,
		status: this.status
	}));

	// ============================================
	// Constructor
	// ============================================

	constructor(message: Message) {
		this.id = message.id;
		this.role = message.role;
		this.content = message.content;
		this.streaming = message.streaming ?? false;
		this.thinking = message.thinking ?? false;
		this.reasoning = message.reasoning ?? '';
		this.toolCalls = message.toolCalls ?? [];
		this.timestamp = message.timestamp ?? new Date();
	}

	// ============================================
	// Methods
	// ============================================

	/**
	 * Update the message state from a Message object
	 * Used when parent AgentState updates the message
	 */
	update(message: Message) {
		this.content = message.content;
		this.streaming = message.streaming ?? false;
		this.thinking = message.thinking ?? false;
		this.reasoning = message.reasoning ?? '';
		this.toolCalls = message.toolCalls ?? [];
		this.timestamp = message.timestamp ?? new Date();
	}
}

/**
 * Create a MessageState instance from a Message
 */
export function createMessageState(message: Message): MessageState {
	return new MessageState(message);
}
