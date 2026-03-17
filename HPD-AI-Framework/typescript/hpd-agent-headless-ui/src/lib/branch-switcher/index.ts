/**
 * BranchSwitcher - Compound headless component for sibling branch navigation
 *
 * @example
 * ```svelte
 * <script>
 *   import * as BranchSwitcher from '@hpd/hpd-agent-headless-ui/branch-switcher';
 * </script>
 *
 * <BranchSwitcher.Root branch={branchManager.activeBranch}>
 *   {#snippet children({ hasSiblings })}
 *     {#if hasSiblings}
 *       <BranchSwitcher.Prev onclick={() => branchManager.goToPreviousSibling()} />
 *       <BranchSwitcher.Position />
 *       <BranchSwitcher.Next onclick={() => branchManager.goToNextSibling()} />
 *     {/if}
 *   {/snippet}
 * </BranchSwitcher.Root>
 * ```
 */

export * from './exports.ts';

export {
	BranchSwitcherRootState,
	BranchSwitcherPrevState,
	BranchSwitcherNextState,
	BranchSwitcherPositionState,
	branchSwitcherAttrs,
} from './branch-switcher.svelte.js';

export type * from './types.js';
