<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { AudioPlayerRootProps } from '../types.js';
	import { AudioPlayerState } from '../audio-player.svelte.js';

	let {
		child,
		children,
		class: className,
		useWebAudio = false,
		bufferThreshold = 2,
		onStatusChange,
		onError,
		...restProps
	}: AudioPlayerRootProps = $props();

	// Create state with boxWith for reactive props
	const rootState = AudioPlayerState.create({
		useWebAudio: boxWith(() => useWebAudio),
		bufferThreshold: boxWith(() => bufferThreshold),
		onStatusChange: boxWith(() => onStatusChange),
		onError: boxWith(() => onError)
	});

	// Expose state for testing
	$effect(() => {
		if (typeof window !== 'undefined' && restProps['data-testid']) {
			(window as any).__audioPlayerState = rootState;
		}
	});

	// Cleanup on destroy
	$effect(() => {
		return () => rootState.destroy();
	});

	// Merge props
	const mergedProps = $derived(mergeProps(restProps, rootState.props, className ? { class: className } : {}) as Record<string, unknown>);
</script>

<!--
	Root component for audio player
	Handles TTS audio playback with streaming chunks
-->
{#if child}
	{@render child({ props: mergedProps, ...rootState.snippetProps })}
{:else}
	<div {...mergedProps}>
		{@render children?.(rootState.snippetProps)}
	</div>
{/if}
