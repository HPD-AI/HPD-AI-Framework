<script lang="ts">
	import { mergeProps } from 'svelte-toolbelt';
	import type { AudioPlaybackGateTriggerProps } from '../types.js';
	import { AudioPlaybackGateState } from '../audio-playback-gate.svelte.js';

	let {
		child,
		children,
		class: className,
		...restProps
	}: AudioPlaybackGateTriggerProps = $props();

	// Get state from context (assuming parent provides it)
	// For now, trigger props are self-contained
	// TODO: If we want context sharing, we'd use Context.get() here

	// For standalone usage without context, we need the enableAudio function
	// This would typically come from parent context

	// Placeholder - in real usage, this would come from parent via props
	const disabled = $derived(false);
	const status = $derived<'blocked' | 'ready' | 'error'>('blocked');

	const snippetProps = $derived.by(() => ({
		canPlayAudio: disabled,
		status,
		disabled
	}));
</script>

<!--
	Trigger button for enabling audio
	Typically used inside AudioPlaybackGate.Root
-->
{#if child}
	{@render child({ props: restProps, ...snippetProps })}
{:else}
	<button {...restProps} class={className} type="button">
		{@render children?.(snippetProps)}
	</button>
{/if}
