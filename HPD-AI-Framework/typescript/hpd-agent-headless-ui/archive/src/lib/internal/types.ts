/**
 * Internal type utilities for HPD Headless UI
 */

import type { Snippet } from 'svelte';
import type { attachRef, ReadableBoxedValues, WritableBoxedValues } from 'svelte-toolbelt';

/**
 * Props wrapper that adds `child` and `children` snippet support
 */
export type WithChild<
	Props extends Record<PropertyKey, unknown> = {},
	SnippetProps extends Record<PropertyKey, unknown> = { _default: never },
	Ref = HTMLElement,
> = Omit<Props, 'child' | 'children'> & {
	child?: SnippetProps extends { _default: never }
		? Snippet<[{ props: Record<string, unknown> }]>
		: Snippet<[SnippetProps & { props: Record<string, unknown> }]>;
	children?: SnippetProps extends { _default: never } ? Snippet : Snippet<[SnippetProps]>;
	ref?: Ref | null | undefined;
};

/**
 * Options for components with ref attachments
 */
export type WithRefOpts<T = {}, Ref extends HTMLElement = HTMLElement> = T &
	ReadableBoxedValues<{ id: string | undefined | null }> &
	WritableBoxedValues<{ ref: Ref | null }>;

/**
 * Return type of attachRef from svelte-toolbelt
 */
export type RefAttachment<T extends HTMLElement = HTMLElement> = ReturnType<typeof attachRef<T>>;

/**
 * Omit properties from T that exist in U
 */
export type Without<T extends object, U extends object> = Omit<T, keyof U>;

/**
 * Callback function type for state changes
 */
export type OnChangeFn<T> = (value: T) => void;

/**
 * Keyboard event type
 */
export type HPDKeyboardEvent = KeyboardEvent;

/**
 * Mouse event type
 */
export type HPDMouseEvent = MouseEvent;
