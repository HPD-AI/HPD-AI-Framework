/**
 * ThreadSwitcher - Compound headless component for sibling thread navigation
 *
 * @example
 * ```svelte
 * <script>
 *   import * as ThreadSwitcher from '@hpd-research/hpd-agent-svelte-headless-ui/thread-switcher';
 * </script>
 *
 * <ThreadSwitcher.Root thread={threadManager.activeThread}>
 *   {#snippet children({ hasSiblings })}
 *     {#if hasSiblings}
 *       <ThreadSwitcher.Prev onclick={() => threadManager.goToPreviousSibling()} />
 *       <ThreadSwitcher.Position />
 *       <ThreadSwitcher.Next onclick={() => threadManager.goToNextSibling()} />
 *     {/if}
 *   {/snippet}
 * </ThreadSwitcher.Root>
 * ```
 */

export * from './exports.ts';

export {
	ThreadSwitcherRootState,
	ThreadSwitcherPrevState,
	ThreadSwitcherNextState,
	ThreadSwitcherPositionState,
	threadSwitcherAttrs,
} from './thread-switcher.svelte.js';

export type * from './types.js';
