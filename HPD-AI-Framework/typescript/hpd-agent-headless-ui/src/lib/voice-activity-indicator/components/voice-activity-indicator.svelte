<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { VoiceActivityIndicatorRootProps } from '../types.js';
	import { VoiceActivityIndicatorState } from '../voice-activity-indicator.svelte.js';

	let {
		child,
		children,
		class: className,
		onActivityChange,
		...restProps
	}: VoiceActivityIndicatorRootProps = $props();

	// Create state with boxWith for reactive props
	const rootState = VoiceActivityIndicatorState.create({
		onActivityChange: boxWith(() => onActivityChange)
	});

	// Cleanup on destroy
	$effect(() => {
		return () => rootState.destroy();
	});

	// Merge props
	const mergedProps = $derived(mergeProps(restProps, rootState.props, className ? { class: className } : {}) as Record<string, unknown>);
</script>

<!--
	Root component for voice activity indicator
	Shows visual feedback when user is speaking
-->
{#if child}
	{@render child({ props: mergedProps, ...rootState.snippetProps })}
{:else}
	<div {...mergedProps}>
		{@render children?.(rootState)}
	</div>
{/if}
