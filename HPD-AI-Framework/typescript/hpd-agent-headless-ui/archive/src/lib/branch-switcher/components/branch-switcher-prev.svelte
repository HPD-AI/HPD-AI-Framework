<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { BranchSwitcherPrevProps, BranchSwitcherPrevHTMLProps } from '../types.js';
	import { BranchSwitcherPrevState } from '../branch-switcher.svelte.js';

	let {
		'aria-label': ariaLabel = 'Previous branch',
		class: className,
		child,
		children,
		...restProps
	}: BranchSwitcherPrevProps = $props();

	const prevState = BranchSwitcherPrevState.create(boxWith(() => ariaLabel));

	const mergedProps = $derived(mergeProps(restProps, prevState.props, className ? { class: className } : {}) as BranchSwitcherPrevHTMLProps);
</script>

{#if child}
	{@render child({ props: mergedProps })}
{:else}
	<button {...mergedProps}>
		{@render children?.()}
	</button>
{/if}
