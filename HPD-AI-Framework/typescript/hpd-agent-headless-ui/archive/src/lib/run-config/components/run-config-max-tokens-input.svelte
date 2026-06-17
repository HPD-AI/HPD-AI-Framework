<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { RunConfigMaxTokensInputProps, RunConfigMaxTokensInputHTMLProps } from '../types.js';
	import { RunConfigMaxTokensInputState } from '../run-config.svelte.js';

	let {
		runConfig,
		min = 1,
		max = undefined,
		disabled = false,
		class: className,
		child,
		children,
		...restProps
	}: RunConfigMaxTokensInputProps = $props();

	const state = RunConfigMaxTokensInputState.create({
		runConfig: boxWith(() => runConfig),
		min: boxWith(() => min),
		max: boxWith(() => max),
		disabled: boxWith(() => disabled),
	});

	const mergedProps = $derived(mergeProps(restProps, state.props, className ? { class: className } : {}) as RunConfigMaxTokensInputHTMLProps);
</script>

{#if child}
	{@render child({ props: mergedProps, ...state.snippetProps })}
{:else}
	<div {...mergedProps}>
		{@render children?.(state.snippetProps)}
	</div>
{/if}
