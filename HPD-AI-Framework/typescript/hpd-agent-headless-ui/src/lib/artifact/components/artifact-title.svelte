<script lang="ts">
	import { mergeProps } from 'svelte-toolbelt';
	import { ArtifactProviderState, artifactAttrs } from '../artifact.svelte.js';
	import type { ArtifactTitleComponentProps } from '../types.js';

	let { class: className, child, children, ref = $bindable(null), ...restProps }: ArtifactTitleComponentProps = $props();

	// Get provider state to access title snippet
	const providerState = ArtifactProviderState.get();

	// Props for the title container
	const mergedProps = $derived(
		mergeProps(restProps as Record<string, unknown>, {
			[artifactAttrs.getAttr('title')]: ''
		}, className ? { class: className } : {}) as Record<string, unknown>
	);
</script>

{#if child}
	{@render child({ props: mergedProps })}
{:else}
	<div bind:this={ref} {...mergedProps}>
		{#if providerState.title}
			{@render providerState.title()}
		{:else}
			{@render children?.()}
		{/if}
	</div>
{/if}
