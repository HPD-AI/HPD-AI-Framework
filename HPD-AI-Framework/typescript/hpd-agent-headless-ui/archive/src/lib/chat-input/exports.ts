/**
 * ChatInput Component Exports
 */

export { default as Root } from './components/chat-input-root.svelte';
export { default as Input } from './components/chat-input-input.svelte';
export { default as Leading } from './components/chat-input-leading.svelte';
export { default as Trailing } from './components/chat-input-trailing.svelte';
export { default as Top } from './components/chat-input-top.svelte';
export { default as Bottom } from './components/chat-input-bottom.svelte';

export type {
	ChatInputRootProps as RootProps,
	ChatInputInputProps as InputProps,
	ChatInputLeadingProps as LeadingProps,
	ChatInputTrailingProps as TrailingProps,
	ChatInputTopProps as TopProps,
	ChatInputBottomProps as BottomProps,
	ChatInputAccessorySnippetProps as AccessorySnippetProps,
	ChatInputInputSnippetProps as InputSnippetProps
} from './types.ts';
