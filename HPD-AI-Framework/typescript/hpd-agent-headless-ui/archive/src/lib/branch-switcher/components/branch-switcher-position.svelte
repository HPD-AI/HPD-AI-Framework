<script lang="ts">
	import { mergeProps } from 'svelte-toolbelt';
	import type { ThreadSwitcherPositionProps, ThreadSwitcherPositionHTMLProps } from '../types.js';
	import { ThreadSwitcherPositionState } from '../thread-switcher.svelte.js';

	let { class: className, child, children, ...restProps }: ThreadSwitcherPositionProps = $props();

	const positionState = ThreadSwitcherPositionState.create();

	const mergedProps = $derived(mergeProps(restProps, positionState.props, className ? { class: className } : {}) as ThreadSwitcherPositionHTMLProps);
</script>

{#if child}
	{@render child({ props: mergedProps, ...positionState.snippetProps })}
{:else}
	<span {...mergedProps}>
		{#if children}
			{@render children(positionState.snippetProps)}
		{:else}
			{positionState.snippetProps.label || positionState.snippetProps.position}
		{/if}
	</span>
{/if}
