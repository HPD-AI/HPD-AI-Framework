<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { ThreadSwitcherNextProps, ThreadSwitcherNextHTMLProps } from '../types.js';
	import { ThreadSwitcherNextState } from '../thread-switcher.svelte.js';

	let {
		'aria-label': ariaLabel = 'Next thread',
		class: className,
		child,
		children,
		...restProps
	}: ThreadSwitcherNextProps = $props();

	const nextState = ThreadSwitcherNextState.create(boxWith(() => ariaLabel));

	const mergedProps = $derived(mergeProps(restProps, nextState.props, className ? { class: className } : {}) as ThreadSwitcherNextHTMLProps);
</script>

{#if child}
	{@render child({ props: mergedProps })}
{:else}
	<button {...mergedProps}>
		{@render children?.()}
	</button>
{/if}
