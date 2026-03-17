<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { RunConfigTopPSliderProps, RunConfigTopPSliderHTMLProps } from '../types.js';
	import { RunConfigTopPSliderState } from '../run-config.svelte.js';

	let {
		runConfig,
		min = 0,
		max = 1,
		step = 0.01,
		disabled = false,
		class: className,
		child,
		children,
		...restProps
	}: RunConfigTopPSliderProps = $props();

	const state = RunConfigTopPSliderState.create({
		runConfig: boxWith(() => runConfig),
		min: boxWith(() => min),
		max: boxWith(() => max),
		step: boxWith(() => step),
		disabled: boxWith(() => disabled),
	});

	const mergedProps = $derived(mergeProps(restProps, state.props, className ? { class: className } : {}) as RunConfigTopPSliderHTMLProps);
</script>

{#if child}
	{@render child({ props: mergedProps, ...state.snippetProps })}
{:else}
	<div {...mergedProps}>
		{@render children?.(state.snippetProps)}
	</div>
{/if}
