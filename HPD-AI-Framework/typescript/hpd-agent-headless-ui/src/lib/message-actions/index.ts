/**
 * MessageActions - Compound headless component for message-level actions
 *
 * Provides Edit, Retry, and branch navigation (Prev/Next/Position) all
 * scoped to a single message bubble. The navigator parts (Prev/Next/Position)
 * only become active when the active branch was forked at this message index,
 * so no conditional logic is needed in the consumer template.
 *
 * @example
 * ```svelte
 * <script>
 *   import * as MessageActions from '@hpd/hpd-agent-headless-ui/message-actions';
 *   let draft = $state('');
 * </script>
 *
 * <MessageActions.Root {workspace} messageIndex={i} role="user" branch={workspace.activeBranch}>
 *   {#snippet children({ hasSiblings })}
 *     <MessageActions.EditButton>
 *       {#snippet children({ edit, status })}
 *         <button onclick={() => edit(draft)} disabled={status === 'pending'}>Edit</button>
 *       {/snippet}
 *     </MessageActions.EditButton>
 *     <MessageActions.RetryButton>
 *       {#snippet children({ retry, status })}
 *         <button onclick={retry} disabled={status === 'pending'}>Retry</button>
 *       {/snippet}
 *     </MessageActions.RetryButton>
 *     {#if hasSiblings}
 *       <MessageActions.Prev />
 *       <MessageActions.Position />
 *       <MessageActions.Next />
 *     {/if}
 *   {/snippet}
 * </MessageActions.Root>
 * ```
 */

export * from './exports.ts';

export {
	MessageActionsRootState,
	MessageActionsEditButtonState,
	MessageActionsRetryButtonState,
	MessageActionsCopyButtonState,
	MessageActionsPrevState,
	MessageActionsNextState,
	MessageActionsPositionState,
	messageActionsAttrs,
} from './message-actions.svelte.js';

export type * from './types.js';
