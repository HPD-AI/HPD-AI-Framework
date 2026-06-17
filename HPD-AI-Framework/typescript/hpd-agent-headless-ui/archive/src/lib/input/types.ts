/**
 * Input Component Types
 *
 * AI-aware text input component with form integration and auto-resize support.
 */

import type { Snippet } from 'svelte';
import type { HTMLTextareaAttributes } from 'svelte/elements';

// ============================================
// Change Event Types
// ============================================

export type InputChangeEventReason = 'input-change' | 'clear' | 'submit';

export interface InputChangeEventDetails {
	/** The reason for the change */
	reason: InputChangeEventReason;
	/** The original DOM event */
	event: Event;
	/** The new value */
	value: string;
}

// ============================================
// Submit Event Types
// ============================================

export interface InputSubmitEventDetails {
	/** The submitted value */
	value: string;
	/** The original keyboard event */
	event: KeyboardEvent;
}

// ============================================
// Component Props
// ============================================

export interface InputProps extends Omit<HTMLTextareaAttributes, 'value' | 'children'> {
	/**
	 * The controlled value of the input.
	 * If provided, the input operates in controlled mode.
	 */
	value?: string;

	/**
	 * The default value for uncontrolled mode.
	 * @default ''
	 */
	defaultValue?: string;

	/**
	 * Called when the input value changes.
	 */
	onChange?: (details: InputChangeEventDetails) => void;

	/**
	 * Called when the user submits (Enter key).
	 * Note: Shift+Enter adds a newline instead of submitting.
	 */
	onSubmit?: (details: InputSubmitEventDetails) => void;

	/**
	 * Whether the input is disabled (e.g., during AI streaming).
	 * @default false
	 */
	disabled?: boolean;

	/**
	 * Whether to auto-resize the textarea based on content.
	 * @default false
	 */
	autoResize?: boolean;

	/**
	 * Maximum number of rows before scrolling.
	 * @default 5
	 */
	maxRows?: number;

	/**
	 * Placeholder text.
	 * @default 'Type a message...'
	 */
	placeholder?: string;

	/**
	 * Whether to auto-focus on mount.
	 * @default false
	 */
	autoFocus?: boolean;

	/**
	 * Form field name for form submission.
	 */
	name?: string;

	/**
	 * Whether the field is required.
	 * @default false
	 */
	required?: boolean;

	/**
	 * ARIA label for accessibility.
	 * @default 'Message input'
	 */
	'aria-label'?: string;

	/**
	 * Custom CSS class names.
	 */
	class?: string;

	/**
	 * Unique identifier.
	 */
	id?: string;

	/**
	 * Bindable ref to the textarea element.
	 */
	ref?: HTMLTextAreaElement | null;

	/**
	 * Custom child snippet for complete control over rendering.
	 */
	child?: Snippet<[{ props: Record<string, unknown> }]>;
}

// ============================================
// Component State
// ============================================

export interface InputState {
	/** Current value */
	value: string;
	/** Whether input is disabled */
	disabled: boolean;
	/** Whether input has content */
	filled: boolean;
	/** Whether input is focused */
	focused: boolean;
	/** Number of rows (1-maxRows) */
	rows: number;
	/** Maximum allowed rows */
	maxRows: number;
}

// ============================================
// Snippet Props
// ============================================

export interface InputSnippetProps {
	/** Current input value */
	value: string;
	/** Whether input is disabled */
	disabled: boolean;
	/** Whether input has content */
	filled: boolean;
	/** Whether input is focused */
	focused: boolean;
	/** Current number of rows */
	rows: number;
}

// ============================================
// TypeScript Namespace
// ============================================

export namespace Input {
	export type Props = InputProps;
	export type State = InputState;
	export type ChangeEventReason = InputChangeEventReason;
	export type ChangeEventDetails = InputChangeEventDetails;
	export type SubmitEventDetails = InputSubmitEventDetails;
	export type SnippetProps = InputSnippetProps;
}
