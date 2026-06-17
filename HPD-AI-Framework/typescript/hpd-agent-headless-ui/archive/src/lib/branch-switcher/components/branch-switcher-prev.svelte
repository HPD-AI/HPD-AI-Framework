<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { ThreadSwitcherPrevProps, ThreadSwitcherPrevHTMLProps } from '../types.js';
	import { ThreadSwitcherPrevState } from '../thread-switcher.svelte.js';

	let {
		'aria-label': ariaLabel = 'Previous thread',
		class: className,
		child,
		children,
		...restProps
	}: ThreadSwitcherPrevProps = $props();

	const prevState = ThreadSwitcherPrevState.create(boxWith(() => ariaLabel));

	const mergedProps = $derived(mergeProps(restProps, prevState.props, className ? { class: className } : {}) as ThreadSwitcherPrevHTMLProps);
</script>

{#if child}
	{@render child({ props: mergedProps })}
{:else}
	<button {...mergedProps}>
		{@render children?.()}
	</button>
{/if}
