/**
 * MessageList Component
 *
 * Container for displaying chat messages with auto-scrolling and accessibility.
 *
 * @example
 * ```svelte
 * <script>
 *   import { MessageList } from '@hpd-research/hpd-agent-svelte-headless-ui';
 *   let messages = [...];
 * </script>
 *
 * <MessageList {messages}>
 *   {#each messages as message}
 *     <Message {message} />
 *   {/each}
 * </MessageList>
 * ```
 */

export * from './exports.ts';
export { MessageListState } from './message-list.svelte.ts';
export type { MessageListProps, MessageListSnippetProps } from './types.ts';
