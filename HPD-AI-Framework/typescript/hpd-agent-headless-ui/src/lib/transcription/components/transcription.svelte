<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { TranscriptionRootProps } from '../types.js';
	import { TranscriptionState } from '../transcription.svelte.js';

	let {
		child,
		children,
		class: className,
		onTextChange,
		onClear,
		...restProps
	}: TranscriptionRootProps = $props();

	// Create state with boxWith for reactive props
	const rootState = TranscriptionState.create({
		onTextChange: boxWith(() => onTextChange),
		onClear: boxWith(() => onClear)
	});

	// Cleanup on destroy
	$effect(() => {
		return () => rootState.destroy();
	});

	// Merge props
	const mergedProps = $derived(mergeProps(restProps, rootState.props, className ? { class: className } : {}) as Record<string, unknown>);
</script>

<!--
	Root component for transcription display
	Shows live STT transcription with interim/final states
-->
{#if child}
	{@render child({ props: mergedProps, ...rootState.snippetProps })}
{:else}
	<div {...mergedProps}>
		{@render children?.(rootState.snippetProps)}
	</div>
{/if}
