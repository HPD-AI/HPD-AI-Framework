/**
 * Input Component
 *
 * AI-aware text input with auto-resize, form integration, and accessibility.
 *
 * @example
 * ```svelte
 * <script>
 *   import { Input } from '$lib/input';
 *
 *   let message = $state('');
 *
 *   function handleSubmit(details) {
 *     console.log('Submitted:', details.value);
 *   }
 * </script>
 *
 * <Input.Root
 *   bind:value={message}
 *   onSubmit={handleSubmit}
 *   placeholder="Type a message..."
 * />
 * ```
 */

export { default as Root } from './components/input.svelte';
export { InputState } from './input.svelte.ts';

export type {
	InputProps,
	InputState as InputStateType,
	InputChangeEventReason,
	InputChangeEventDetails,
	InputSubmitEventDetails,
	InputSnippetProps,
} from './types.ts';
