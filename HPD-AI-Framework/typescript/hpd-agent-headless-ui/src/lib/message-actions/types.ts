/**
 * MessageActions Types
 *
 * Type definitions for the MessageActions compound component.
 * Provides headless Edit, Retry, and branch navigation (Prev/Next/Position)
 * for AI chat messages.
 */

import type { Snippet } from 'svelte';
import type { Branch } from '@hpd/hpd-agent-client';
import type { MessageRole } from '../agent/types.ts';

// ============================================
// Status
// ============================================

/** Current state of an action button */
export type MessageActionStatus = 'idle' | 'pending' | 'error';

// ============================================
// Root Component Types
// ============================================

export interface MessageActionsRootHTMLProps {
	'data-message-actions-root': '';
	'data-role': MessageRole;
	'data-message-index': number;
	'data-has-siblings'?: '';
	class?: string | undefined;
	[key: string]: unknown;
}

export interface MessageActionsRootSnippetProps {
	/** Role of the message this toolbar is attached to */
	role: MessageRole;
	/** Index of the message in the branch */
	messageIndex: number;
	/** Whether any action is currently pending */
	pending: boolean;
	/** Whether this message is a fork point with sibling branches */
	hasSiblings: boolean;
	/** Current sibling position string e.g. "2 / 3" */
	position: string;
}

export interface MessageActionsRootProps {
	/** Workspace instance */
	workspace: import('../workspace/types.ts').Workspace;
	/** Index of the message in the branch message list */
	messageIndex: number;
	/** Role of the message (user / assistant / system) */
	role: MessageRole;
	/**
	 * The currently active branch. Used to derive sibling navigation state.
	 * Pass workspace.activeBranch here.
	 */
	branch?: Branch | null;
	child?: Snippet<[MessageActionsRootSnippetProps & { props: MessageActionsRootHTMLProps }]>;
	children?: Snippet<[MessageActionsRootSnippetProps]>;
	[key: string]: unknown;
}

// ============================================
// EditButton Component Types
// ============================================

export interface MessageActionsEditButtonHTMLProps {
	'data-message-actions-edit': '';
	'data-status': MessageActionStatus;
	'data-disabled'?: '';
	type: 'button';
	disabled: boolean;
	'aria-label': string;
	class?: string | undefined;
	[key: string]: unknown;
}

export interface MessageActionsEditButtonSnippetProps {
	/** Current status of the edit action */
	status: MessageActionStatus;
	/** Whether the button is disabled */
	disabled: boolean;
	/** Call this with the new message content to trigger an edit */
	edit: (newContent: string) => Promise<void>;
}

export interface MessageActionsEditButtonProps {
	'aria-label'?: string;
	/** Called when edit succeeds */
	onSuccess?: () => void;
	/** Called when edit fails */
	onError?: (err: unknown) => void;
	child?: Snippet<[MessageActionsEditButtonSnippetProps & { props: MessageActionsEditButtonHTMLProps }]>;
	children?: Snippet<[MessageActionsEditButtonSnippetProps]>;
	[key: string]: unknown;
}

// ============================================
// RetryButton Component Types
// ============================================

export interface MessageActionsRetryButtonHTMLProps {
	'data-message-actions-retry': '';
	'data-status': MessageActionStatus;
	'data-disabled'?: '';
	type: 'button';
	disabled: boolean;
	'aria-label': string;
	class?: string | undefined;
	[key: string]: unknown;
}

export interface MessageActionsRetryButtonSnippetProps {
	/** Current status of the retry action */
	status: MessageActionStatus;
	/** Whether the button is disabled */
	disabled: boolean;
	/** Call this to trigger a retry */
	retry: () => Promise<void>;
}

export interface MessageActionsRetryButtonProps {
	'aria-label'?: string;
	/** Called when retry succeeds */
	onSuccess?: () => void;
	/** Called when retry fails */
	onError?: (err: unknown) => void;
	child?: Snippet<[MessageActionsRetryButtonSnippetProps & { props: MessageActionsRetryButtonHTMLProps }]>;
	children?: Snippet<[MessageActionsRetryButtonSnippetProps]>;
	[key: string]: unknown;
}

// ============================================
// CopyButton Component Types
// ============================================

export interface MessageActionsCopyButtonHTMLProps {
	'data-message-actions-copy': '';
	'data-copied'?: '';
	type: 'button';
	'aria-label': string;
	class?: string | undefined;
	[key: string]: unknown;
}

export interface MessageActionsCopyButtonSnippetProps {
	/** True briefly after a successful copy, then resets to false */
	copied: boolean;
	/** Call this to copy content to clipboard */
	copy: () => Promise<void>;
}

export interface MessageActionsCopyButtonProps {
	/** The text to copy. Usually the message content. */
	content: string;
	/** How long (ms) to hold the copied=true state before resetting. Default: 2000 */
	resetDelay?: number;
	'aria-label'?: string;
	/** Called when copy succeeds */
	onSuccess?: () => void;
	/** Called when copy fails (e.g. clipboard not available) */
	onError?: (err: unknown) => void;
	child?: Snippet<[MessageActionsCopyButtonSnippetProps & { props: MessageActionsCopyButtonHTMLProps }]>;
	children?: Snippet<[MessageActionsCopyButtonSnippetProps]>;
	[key: string]: unknown;
}

// ============================================
// Branch Navigation Types (Prev / Next / Position)
// ============================================

export interface MessageActionsPrevHTMLProps {
	'data-message-actions-prev': '';
	'data-disabled'?: '';
	type: 'button';
	disabled: boolean;
	'aria-label': string;
	onclick: () => Promise<void>;
	class?: string | undefined;
	[key: string]: unknown;
}

export interface MessageActionsPrevSnippetProps {
	/** Call this to navigate to the previous sibling branch */
	goPrevious: () => Promise<void>;
}

export interface MessageActionsPrevProps {
	'aria-label'?: string;
	child?: Snippet<[MessageActionsPrevSnippetProps & { props: MessageActionsPrevHTMLProps }]>;
	children?: Snippet<[MessageActionsPrevSnippetProps]>;
	[key: string]: unknown;
}

export interface MessageActionsNextHTMLProps {
	'data-message-actions-next': '';
	'data-disabled'?: '';
	type: 'button';
	disabled: boolean;
	'aria-label': string;
	onclick: () => Promise<void>;
	class?: string | undefined;
	[key: string]: unknown;
}

export interface MessageActionsNextSnippetProps {
	/** Call this to navigate to the next sibling branch */
	goNext: () => Promise<void>;
}

export interface MessageActionsNextProps {
	'aria-label'?: string;
	child?: Snippet<[MessageActionsNextSnippetProps & { props: MessageActionsNextHTMLProps }]>;
	children?: Snippet<[MessageActionsNextSnippetProps]>;
	[key: string]: unknown;
}

export interface MessageActionsPositionHTMLProps {
	'data-message-actions-position': '';
	'aria-live': 'polite';
	'aria-atomic': 'true';
	class?: string | undefined;
	[key: string]: unknown;
}

export interface MessageActionsPositionSnippetProps {
	/** e.g. "2 / 3" */
	position: string;
	/** e.g. "Fork 2 / 3" or "Original (1 / 3)" */
	label: string;
}

export interface MessageActionsPositionProps {
	child?: Snippet<[MessageActionsPositionSnippetProps & { props: MessageActionsPositionHTMLProps }]>;
	children?: Snippet<[MessageActionsPositionSnippetProps]>;
	[key: string]: unknown;
}
