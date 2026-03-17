<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { InterruptionIndicatorRootProps } from '../types.js';
	import { InterruptionIndicatorState } from '../interruption-indicator.svelte.js';

	let {
		child,
		children,
		class: className,
		onInterruptionChange,
		onPauseChange,
		...restProps
	}: InterruptionIndicatorRootProps = $props();

	const rootState = InterruptionIndicatorState.create({
		onInterruptionChange: boxWith(() => onInterruptionChange),
		onPauseChange: boxWith(() => onPauseChange)
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
