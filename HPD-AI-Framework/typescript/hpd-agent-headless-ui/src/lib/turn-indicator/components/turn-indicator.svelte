<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { TurnIndicatorRootProps } from '../types.js';
	import { TurnIndicatorState } from '../turn-indicator.svelte.js';

	let {
		child,
		children,
		class: className,
		onTurnChange,
		...restProps
	}: TurnIndicatorRootProps = $props();

	const rootState = TurnIndicatorState.create({
		onTurnChange: boxWith(() => onTurnChange)
	});

	$effect(() => {
		return () => rootState.destroy();
	});

	const mergedProps = $derived(mergeProps(restProps, rootState.props, className ? { class: className } : {}) as Record<string, unknown>);
</script>

{#if child}
	{@render child({ props: mergedProps, ...rootState.snippetProps })}
{:else}
	<div {...mergedProps}>
		{@render children?.(rootState)}
	</div>
{/if}
