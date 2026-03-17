<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { AudioPlaybackGateRootProps } from '../types.js';
	import { AudioPlaybackGateState } from '../audio-playback-gate.svelte.js';

	let {
		child,
		children,
		class: className,
		onStatusChange,
		audioContext,
		...restProps
	}: AudioPlaybackGateRootProps = $props();

	// Create state with boxWith for reactive props
	const rootState = AudioPlaybackGateState.create({
		onStatusChange: boxWith(() => onStatusChange),
		audioContext: boxWith(() => audioContext)
	});

	// Cleanup on destroy
	$effect(() => {
		return () => rootState.destroy();
	});

	// Merge props
	const mergedProps = $derived(mergeProps(restProps, rootState.props, className ? { class: className } : {}) as Record<string, unknown>);
</script>

<!--
	Root component for audio playback gate
	Provides context and manages AudioContext lifecycle
-->
{#if child}
	{@render child({ props: mergedProps, ...rootState.snippetProps })}
{:else}
	<div {...mergedProps}>
		{@render children?.(rootState.snippetProps)}
	</div>
{/if}
