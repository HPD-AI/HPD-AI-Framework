<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { RunConfigRunTimeoutInputProps, RunConfigRunTimeoutInputHTMLProps } from '../types.js';
	import { RunConfigRunTimeoutInputState } from '../run-config.svelte.js';

	let {
		runConfig,
		disabled = false,
		class: className,
		child,
		children,
		...restProps
	}: RunConfigRunTimeoutInputProps = $props();

	const state = RunConfigRunTimeoutInputState.create({
		runConfig: boxWith(() => runConfig),
		disabled: boxWith(() => disabled),
	});

	const mergedProps = $derived(mergeProps(restProps, state.props, className ? { class: className } : {}) as RunConfigRunTimeoutInputHTMLProps);
</script>

{#if child}
	{@render child({ props: mergedProps, ...state.snippetProps })}
{:else}
	<div {...mergedProps}>
		{@render children?.(state.snippetProps)}
	</div>
{/if}
