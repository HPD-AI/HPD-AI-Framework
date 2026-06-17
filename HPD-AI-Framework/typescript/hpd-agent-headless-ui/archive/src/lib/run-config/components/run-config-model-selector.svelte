<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { RunConfigModelSelectorProps, RunConfigModelSelectorHTMLProps } from '../types.js';
	import { RunConfigModelSelectorState } from '../run-config.svelte.js';

	let {
		runConfig,
		providers = [],
		disabled = false,
		class: className,
		child,
		children,
		...restProps
	}: RunConfigModelSelectorProps = $props();

	const state = RunConfigModelSelectorState.create({
		runConfig: boxWith(() => runConfig),
		providers: boxWith(() => providers),
		disabled: boxWith(() => disabled),
	});

	const mergedProps = $derived(mergeProps(restProps, state.props, className ? { class: className } : {}) as RunConfigModelSelectorHTMLProps);
</script>

{#if child}
	{@render child({ props: mergedProps, ...state.snippetProps })}
{:else}
	<div {...mergedProps}>
		{@render children?.(state.snippetProps)}
	</div>
{/if}
