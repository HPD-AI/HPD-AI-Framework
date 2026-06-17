/**
 * MessageEdit Component Types
 */

import type { Snippet } from 'svelte';
import type { HTMLAttributes, HTMLButtonAttributes, HTMLTextareaAttributes } from 'svelte/elements';
import type { Workspace } from '../workspace/types.ts';

// ============================================
// Root
// ============================================

export interface MessageEditRootProps extends Omit<HTMLAttributes<HTMLDivElement>, 'children'> {
	/** Workspace instance */
	workspace: Workspace;
	/** Index of the message being edited in the message list */
	messageIndex: number;
	/** Current content of the message — pre-fills the draft on startEdit */
	initialValue: string;
	/**
	 * Controlled editing state. Omit for uncontrolled (Root manages internally).
	 * When provided, pass onStartEdit/onSave/onCancel to drive the state.
	 */
	editing?: boolean;
	/** Called when startEdit() is invoked in controlled mode */
	onStartEdit?: () => void;
	/** Called after a successful save (branch fork + model re-run) */
	onSave?: () => void;
	/** Called when the user cancels without saving */
	onCancel?: () => void;
	/** Called if editMessage throws */
	onError?: (err: unknown) => void;
	/** Full HTML control */
	child?: Snippet<[{ props: Record<string, unknown> } & MessageEditRootSnippetProps]>;
	/** Content snippet */
	children?: Snippet<[MessageEditRootSnippetProps]>;
}

export interface MessageEditRootHTMLProps {
	'data-message-edit-root': '';
	'data-editing'?: '';
	'data-pending'?: '';
	[key: string]: unknown;
}

export interface MessageEditRootSnippetProps {
	/** Whether edit mode is active */
	editing: boolean;
	/** Current draft value */
	draft: string;
	/** Whether a save is in flight */
	pending: boolean;
	/** Whether the draft is non-empty and not pending */
	canSave: boolean;
	/** Enter edit mode (resets draft to initialValue) */
	startEdit: () => void;
	/** Trigger save programmatically */
	save: () => Promise<void>;
	/** Trigger cancel programmatically */
	cancel: () => void;
}

// ============================================
// Textarea
// ============================================

export interface MessageEditTextareaProps
	extends Omit<HTMLTextareaAttributes, 'value' | 'children'> {
	/** Placeholder text */
	placeholder?: string;
	/** Accessible label */
	'aria-label'?: string;
	/** Full HTML control */
	child?: Snippet<[{ props: Record<string, unknown> } & MessageEditTextareaSnippetProps]>;
}

export interface MessageEditTextareaHTMLProps {
	'data-message-edit-textarea': '';
	'aria-label': string;
	'aria-multiline': 'true';
	disabled: boolean;
	[key: string]: unknown;
}

export interface MessageEditTextareaSnippetProps {
	value: string;
	pending: boolean;
	placeholder: string;
	handleKeyDown: (event: KeyboardEvent) => void;
	handleChange: (value: string) => void;
}

// ============================================
// SaveButton
// ============================================

export interface MessageEditSaveButtonProps extends Omit<HTMLButtonAttributes, 'children'> {
	'aria-label'?: string;
	child?: Snippet<[{ props: Record<string, unknown> } & MessageEditSaveButtonSnippetProps]>;
	children?: Snippet<[MessageEditSaveButtonSnippetProps]>;
}

export interface MessageEditSaveButtonHTMLProps {
	'data-message-edit-save': '';
	'data-disabled'?: '';
	type: 'button';
	disabled: boolean;
	'aria-label': string;
	[key: string]: unknown;
}

export interface MessageEditSaveButtonSnippetProps {
	pending: boolean;
	disabled: boolean;
	save: () => Promise<void>;
}

// ============================================
// CancelButton
// ============================================

export interface MessageEditCancelButtonProps extends Omit<HTMLButtonAttributes, 'children'> {
	'aria-label'?: string;
	child?: Snippet<[{ props: Record<string, unknown> } & MessageEditCancelButtonSnippetProps]>;
	children?: Snippet<[MessageEditCancelButtonSnippetProps]>;
}

export interface MessageEditCancelButtonHTMLProps {
	'data-message-edit-cancel': '';
	'data-disabled'?: '';
	type: 'button';
	disabled: boolean;
	'aria-label': string;
	[key: string]: unknown;
}

export interface MessageEditCancelButtonSnippetProps {
	pending: boolean;
	cancel: () => void;
}
