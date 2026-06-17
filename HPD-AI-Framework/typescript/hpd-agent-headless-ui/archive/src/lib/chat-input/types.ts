/**
 * ChatInput Type Definitions
 *
 * Type definitions for the ChatInput compositional component.
 */

import type { HPDPrimitiveDivAttributes } from '$lib/shared/types.js';
import type { OnChangeFn, WithChild } from '$lib/internal/types.js';
import type { Without } from 'svelte-toolbelt';

// ============================================
// Root Component Props
// ============================================

export type ChatInputRootPropsWithoutHTML = WithChild<{
	/**
	 * Controlled value
	 */
	value?: string;

	/**
	 * Default value (uncontrolled mode)
	 */
	defaultValue?: string;

	/**
	 * Whether the input is disabled
	 * @default false
	 */
	disabled?: boolean;

	/**
	 * Callback when input is submitted (Enter key or programmatic submit)
	 */
	onSubmit?: (details: { value: string }) => void;

	/**
	 * Callback when value changes
	 */
	onChange?: OnChangeFn<string>;

	/**
	 * Rest props support
	 */
	[key: string]: unknown;
}>;

export type ChatInputRootProps = ChatInputRootPropsWithoutHTML &
	Without<HPDPrimitiveDivAttributes, ChatInputRootPropsWithoutHTML>;

// ============================================
// Input Component Props
// ============================================

export type ChatInputInputSnippetProps = {
	/**
	 * Current input value
	 */
	value: string;

	/**
	 * Whether input is focused
	 */
	focused: boolean;

	/**
	 * Whether input is disabled
	 */
	disabled: boolean;

	/**
	 * Whether input is empty
	 */
	isEmpty: boolean;

	/**
	 * Character count
	 */
	characterCount: number;

	/**
	 * Whether submit is allowed
	 */
	canSubmit: boolean;
};

export type ChatInputInputPropsWithoutHTML = {
	/**
	 * Placeholder text
	 */
	placeholder?: string;

	/**
	 * Maximum number of rows for auto-resize
	 * @default 5
	 */
	maxRows?: number;

	/**
	 * Minimum number of rows
	 * @default 1
	 */
	minRows?: number;

	/**
	 * Override disabled state from root
	 */
	disabled?: boolean;

	/**
	 * Component ref
	 */
	ref?: HTMLTextAreaElement | null;

	/**
	 * Child snippet (full override)
	 */
	child?: import('svelte').Snippet<[ChatInputInputSnippetProps & { props: Record<string, unknown> }]>;

	/**
	 * Children snippet
	 */
	children?: import('svelte').Snippet<[ChatInputInputSnippetProps]>;

	/**
	 * Rest props support
	 */
	[key: string]: unknown;
};

export type ChatInputInputProps = ChatInputInputPropsWithoutHTML &
	Without<HPDPrimitiveDivAttributes, ChatInputInputPropsWithoutHTML>;

// ============================================
// Accessory Component Props (Leading, Trailing, Top, Bottom)
// ============================================

export type ChatInputAccessorySnippetProps = {
	/**
	 * Current input value
	 */
	value: string;

	/**
	 * Whether input is focused
	 */
	focused: boolean;

	/**
	 * Whether input is disabled
	 */
	disabled: boolean;

	/**
	 * Whether input is empty
	 */
	isEmpty: boolean;

	/**
	 * Character count
	 */
	characterCount: number;

	/**
	 * Whether submit is allowed
	 */
	canSubmit: boolean;

	/**
	 * Submit the input programmatically
	 */
	submit: () => void;

	/**
	 * Clear the input
	 */
	clear: () => void;
};

export type ChatInputAccessoryPropsWithoutHTML = {
	/**
	 * Component ref
	 */
	ref?: HTMLElement | null;

	/**
	 * Child snippet - ADVANCED: Full control over wrapper element
	 *
	 * Use this when you need to completely replace the wrapper div with a custom element
	 * (e.g., animated wrapper, custom component, etc.). You receive both the state props
	 * and the element props that should be spread onto your wrapper.
	 *
	 * @example
	 * ```svelte
	 * <ChatInput.Bottom>
	 *   {#snippet child({ props, characterCount })}
	 *     <MyAnimatedDiv {...props}>
	 *       <span>{characterCount} chars</span>
	 *     </MyAnimatedDiv>
	 *   {/snippet}
	 * </ChatInput.Bottom>
	 * ```
	 */
	child?: import('svelte').Snippet<[ChatInputAccessorySnippetProps & { props: Record<string, unknown> }]>;

	/**
	 * Children snippet - COMMON: Render content with access to state
	 *
	 * Use this for the common case where you just want to render content inside the
	 * accessory container. The wrapper div is provided for you, you just provide the
	 * content and have access to the input state.
	 *
	 * @example
	 * ```svelte
	 * <ChatInput.Bottom>
	 *   {#snippet children({ characterCount, isEmpty })}
	 *     <span>{characterCount} characters</span>
	 *     {#if isEmpty}
	 *       <span class="hint">Start typing...</span>
	 *     {/if}
	 *   {/snippet}
	 * </ChatInput.Bottom>
	 * ```
	 */
	children?: import('svelte').Snippet<[ChatInputAccessorySnippetProps]>;

	/**
	 * Rest props support
	 */
	[key: string]: unknown;
};

export type ChatInputAccessoryProps = ChatInputAccessoryPropsWithoutHTML &
	Without<HPDPrimitiveDivAttributes, ChatInputAccessoryPropsWithoutHTML>;

// Specific accessory types (all use the same base)
export type ChatInputLeadingProps = ChatInputAccessoryProps;
export type ChatInputTrailingProps = ChatInputAccessoryProps;
export type ChatInputTopProps = ChatInputAccessoryProps;
export type ChatInputBottomProps = ChatInputAccessoryProps;

// ============================================
// Component-level flat interfaces (used directly in .svelte $props())
// ============================================

export interface ChatInputRootComponentProps {
	value?: string;
	defaultValue?: string;
	disabled?: boolean;
	onSubmit?: (details: { value: string }) => void;
	onChange?: OnChangeFn<string>;
	child?: import('svelte').Snippet<[{ props: Record<string, unknown> }]>;
	children?: import('svelte').Snippet;
	ref?: HTMLElement | null;
	[key: string]: unknown;
}

export interface ChatInputInputComponentProps {
	placeholder?: string;
	maxRows?: number;
	minRows?: number;
	disabled?: boolean;
	ref?: HTMLTextAreaElement | null;
	child?: import('svelte').Snippet<[ChatInputInputSnippetProps & { props: Record<string, unknown> }]>;
	children?: import('svelte').Snippet<[ChatInputInputSnippetProps]>;
	[key: string]: unknown;
}

export interface ChatInputAccessoryComponentProps {
	ref?: HTMLElement | null;
	child?: import('svelte').Snippet<[ChatInputAccessorySnippetProps & { props: Record<string, unknown> }]>;
	children?: import('svelte').Snippet<[ChatInputAccessorySnippetProps]>;
	[key: string]: unknown;
}
