<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { RunConfigTemperatureSliderProps, RunConfigTemperatureSliderHTMLProps } from '../types.js';
	import { RunConfigTemperatureSliderState } from '../run-config.svelte.js';

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
	}: RunConfigTemperatureSliderProps = $props();

	const state = RunConfigTemperatureSliderState.create({
		runConfig: boxWith(() => runConfig),
		min: boxWith(() => min),
		max: boxWith(() => max),
		step: boxWith(() => step),
		disabled: boxWith(() => disabled),
	});

	const mergedProps = $derived(mergeProps(restProps, state.props, className ? { class: className } : {}) as RunConfigTemperatureSliderHTMLProps);
</script>

{#if child}
	{@render child({ props: mergedProps, ...state.snippetProps })}
{:else}
	<div {...mergedProps}>
		{@render children?.(state.snippetProps)}
	</div>
{/if}
