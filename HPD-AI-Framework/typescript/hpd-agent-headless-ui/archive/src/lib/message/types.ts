/**
 * Message Component Types
 *
 * Types for the Message component - AI-specific message display with
 * streaming, thinking, tool calls, and reasoning support.
 */

import type { Snippet } from 'svelte';
import type { Message, ToolCall } from '../agent/types.ts';

/**
 * Props for the Message component
 */
export interface MessageProps {
	/** The message to render */
	message: Message;

	/** Optional snippet for full HTML control */
	child?: Snippet<[{ props: MessageHTMLProps }]>;

	/** Optional snippet for content customization */
	children?: Snippet<[MessageSnippetProps]>;

	/** Additional CSS classes */
	class?: string;

	/** Additional HTML attributes */
	[key: string]: unknown;
}

/**
 * Props passed to the child snippet (full HTML control)
 */
export interface MessageHTMLProps {
	'data-message-id': string;
	'data-role': 'user' | 'assistant' | 'system';
	'data-streaming'?: '';
	'data-thinking'?: '';
	'data-has-tools'?: '';
	'data-has-reasoning'?: '';
	'data-status': MessageStatus;
	'aria-live'?: 'polite' | 'off';
	'aria-busy'?: boolean;
	'aria-label': string;
	class?: string;
}

/**
 * Props passed to the children snippet (content customization)
 */
export interface MessageSnippetProps {
	/** Message text content */
	content: string;

	/** Message role */
	role: 'user' | 'assistant' | 'system';

	/** Whether the message is currently streaming */
	streaming: boolean;

	/** Whether the AI is thinking/reasoning */
	thinking: boolean;

	/** Whether the message has reasoning text */
	hasReasoning: boolean;

	/** Reasoning text (if any) */
	reasoning: string;

	/** Tool calls embedded in this message */
	toolCalls: ToolCall[];

	/** Whether the message has active tool calls */
	hasActiveTools: boolean;

	/** Message timestamp */
	timestamp: Date;

	/** Current message status */
	status: MessageStatus;
}

/**
 * Message status - derived from state
 */
export type MessageStatus = 'streaming' | 'thinking' | 'executing' | 'complete';
