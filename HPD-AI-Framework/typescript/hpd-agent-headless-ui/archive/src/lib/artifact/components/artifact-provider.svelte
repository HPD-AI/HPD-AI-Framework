<script lang="ts">
	import { boxWith, mergeProps } from 'svelte-toolbelt';
	import { ArtifactProviderState } from '../artifact.svelte.js';
	import type { ArtifactProviderComponentProps } from '../types.js';

	let {
		onOpenChange,
		class: className,
		child,
		children,
		ref = $bindable(null),
		...restProps
	}: ArtifactProviderComponentProps = $props();

	// Create provider state with boxed values
	const providerState = ArtifactProviderState.create({
		onOpenChange: boxWith(() => onOpenChange)
	});

	// Props for the provider container
	const mergedProps = $derived(
		mergeProps(restProps as Record<string, unknown>, {
			[providerState.getHPDAttr('provider')]: '',
			...providerState.sharedProps
		}, className ? { class: className } : {}) as Record<string, unknown>
	);
</script>

{#if child}
	{@render child({ props: mergedProps })}
{:else}
	<div bind:this={ref} {...mergedProps}>
		{@render children?.()}
	</div>
{/if}
