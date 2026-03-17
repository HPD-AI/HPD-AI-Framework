<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { AudioVisualizerRootProps } from '../types.js';
	import { AudioVisualizerState } from '../audio-visualizer.svelte.js';

	let {
		child,
		children,
		class: className,
		bands,
		mode,
		onVolumesChange,
		...restProps
	}: AudioVisualizerRootProps = $props();

	const rootState = AudioVisualizerState.create({
		bands: boxWith(() => bands),
		mode: boxWith(() => mode),
		onVolumesChange: boxWith(() => onVolumesChange)
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
