/**
 * ChatInput Component
 *
 * Compositional chat input container with support for accessories.
 *
 * @example
 * ```svelte
 * <script>
 *   import { ChatInput } from '@hpd/hpd-agent-headless-ui';
 *
 *   let message = $state('');
 *
 *   function handleSubmit(details) {
 *     console.log('Submitted:', details.value);
 *   }
 * </script>
 *
 * <ChatInput.Root bind:value={message} onSubmit={handleSubmit}>
 *   <ChatInput.Leading>
 *     <button>📎</button>
 *     <button>🎤</button>
 *   </ChatInput.Leading>
 *
 *   <ChatInput.Input placeholder="Type a message..." />
 *
 *   <ChatInput.Trailing>
 *     <button>😊</button>
 *     <button>Send</button>
 *   </ChatInput.Trailing>
 * </ChatInput.Root>
 * ```
 */

export * from './exports.ts';
export { ChatInputRootState } from './chat-input.svelte.ts';

export type {
	ChatInputRootProps,
	ChatInputInputProps,
	ChatInputLeadingProps,
	ChatInputTrailingProps,
	ChatInputTopProps,
	ChatInputBottomProps,
	ChatInputAccessorySnippetProps,
	ChatInputInputSnippetProps
} from './types.ts';
