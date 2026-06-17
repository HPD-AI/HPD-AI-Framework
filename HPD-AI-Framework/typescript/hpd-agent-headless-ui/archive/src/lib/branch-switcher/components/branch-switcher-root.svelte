<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { ThreadSwitcherRootProps, ThreadSwitcherRootHTMLProps } from '../types.js';
	import { ThreadSwitcherRootState } from '../thread-switcher.svelte.js';

	let {
		thread,
		class: className,
		child,
		children,
		...restProps
	}: ThreadSwitcherRootProps = $props();

	const rootState = ThreadSwitcherRootState.create({
		thread: boxWith(() => thread),
	});

	const mergedProps = $derived(mergeProps(restProps, rootState.props, className ? { class: className } : {}) as ThreadSwitcherRootHTMLProps);
</script>

{#if child}
	{@render child({ props: mergedProps, ...rootState.snippetProps })}
{:else}
	<div {...mergedProps}>
		{@render children?.(rootState.snippetProps)}
	</div>
{/if}
