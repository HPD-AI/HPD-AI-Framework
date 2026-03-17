/**
 * ToolExecution Types
 *
 * Type definitions for the ToolExecution component.
 */

import type { Snippet } from 'svelte';
import type { ToolCall, ToolCallStatus } from '../agent/types.ts';

// ============================================
// Event Details
// ============================================

/**
 * Reasons why the expanded state changed
 */
export type ToolExecutionExpandReason = 'trigger-press' | 'keyboard' | 'imperative-action';

/**
 * Event details for expand/collapse changes
 */
export interface ToolExecutionExpandEventDetails {
	reason: ToolExecutionExpandReason;
	event?: Event;
	trigger?: Element;
	cancel: () => void;
	readonly isCanceled: boolean;
}

// ============================================
// Root Component Types
// ============================================

export interface ToolExecutionRootHTMLProps {
	'data-tool-execution-root': '';
	'data-tool-id': string;
	'data-tool-name': string;
	'data-tool-status': ToolCallStatus;
	'data-expanded'?: '';
	'data-active'?: '';
	'data-complete'?: '';
	'data-error'?: '';
	role: 'status';
	'aria-label': string;
	'aria-busy': boolean;
	'aria-live': 'polite';
	class?: string | undefined;
	[key: string]: unknown;
}

export interface ToolExecutionRootSnippetProps {
	name: string;
	status: ToolCallStatus;
	expanded: boolean;
	isActive: boolean;
	isComplete: boolean;
	hasError: boolean;
	hasResult: boolean;
	hasArgs: boolean;
	duration?: number;
	args: Record<string, unknown>;
	result?: string;
	error?: string;
}

export interface ToolExecutionRootProps {
	toolCall: ToolCall;
	expanded?: boolean;
	onExpandChange?: (expanded: boolean, details: ToolExecutionExpandEventDetails) => void;
	// child snippet receives both props and state
	child?: Snippet<[ToolExecutionRootSnippetProps & { props: ToolExecutionRootHTMLProps }]>;
	children?: Snippet<[ToolExecutionRootSnippetProps]>;
	[key: string]: unknown;
}

// ============================================
// Trigger Component Types
// ============================================

export interface ToolExecutionTriggerHTMLProps {
	'data-tool-execution-trigger': '';
	'data-expanded'?: '';
	type: 'button';
	'aria-expanded': boolean;
	'aria-controls': string;
	class?: string | undefined;
	[key: string]: unknown;
}

export interface ToolExecutionTriggerSnippetProps {
	expanded: boolean;
	name: string;
	status: ToolCallStatus;
}

export interface ToolExecutionTriggerProps {
	child?: Snippet<[{ props: ToolExecutionTriggerHTMLProps }]>;
	children?: Snippet<[ToolExecutionTriggerSnippetProps]>;
	[key: string]: unknown;
}

// ============================================
// Content Component Types
// ============================================

export interface ToolExecutionContentHTMLProps {
	'data-tool-execution-content': '';
	'data-expanded'?: '';
	id: string;
	'aria-labelledby': string;
	class?: string | undefined;
	[key: string]: unknown;
}

export interface ToolExecutionContentSnippetProps {
	expanded: boolean;
	hasArgs: boolean;
	hasResult: boolean;
	hasError: boolean;
}

export interface ToolExecutionContentProps {
	child?: Snippet<[{ props: ToolExecutionContentHTMLProps }]>;
	children?: Snippet<[ToolExecutionContentSnippetProps]>;
	[key: string]: unknown;
}

// ============================================
// Status Component Types
// ============================================

export interface ToolExecutionStatusHTMLProps {
	'data-tool-execution-status': '';
	'data-tool-status': ToolCallStatus;
	'data-active'?: '';
	'data-complete'?: '';
	'data-error'?: '';
	class?: string | undefined;
	[key: string]: unknown;
}

export interface ToolExecutionStatusSnippetProps {
	status: ToolCallStatus;
	isActive: boolean;
	isComplete: boolean;
	hasError: boolean;
}

export interface ToolExecutionStatusProps {
	child?: Snippet<[{ props: ToolExecutionStatusHTMLProps }]>;
	children?: Snippet<[ToolExecutionStatusSnippetProps]>;
	[key: string]: unknown;
}

// ============================================
// Args Component Types
// ============================================

export interface ToolExecutionArgsHTMLProps {
	'data-tool-execution-args': '';
	'data-has-args'?: '';
	class?: string | undefined;
	[key: string]: unknown;
}

export interface ToolExecutionArgsSnippetProps {
	args: Record<string, unknown>;
	hasArgs: boolean;
	argsJson: string;
}

export interface ToolExecutionArgsProps {
	child?: Snippet<[{ props: ToolExecutionArgsHTMLProps }]>;
	children?: Snippet<[ToolExecutionArgsSnippetProps]>;
	[key: string]: unknown;
}

// ============================================
// Result Component Types
// ============================================

export interface ToolExecutionResultHTMLProps {
	'data-tool-execution-result': '';
	'data-has-result'?: '';
	'data-has-error'?: '';
	class?: string | undefined;
	[key: string]: unknown;
}

export interface ToolExecutionResultSnippetProps {
	result?: string;
	error?: string;
	hasResult: boolean;
	hasError: boolean;
}

export interface ToolExecutionResultProps {
	child?: Snippet<[{ props: ToolExecutionResultHTMLProps }]>;
	children?: Snippet<[ToolExecutionResultSnippetProps]>;
	[key: string]: unknown;
}
