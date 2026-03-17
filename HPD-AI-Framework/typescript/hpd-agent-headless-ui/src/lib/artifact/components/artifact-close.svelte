<script lang="ts">
	import { mergeProps } from 'svelte-toolbelt';
	import { ArtifactCloseState } from '../artifact.svelte.js';
	import type { ArtifactCloseComponentProps } from '../types.js';

	let {
		disabled = false,
		class: className,
		child,
		children,
		ref = $bindable(null),
		...restProps
	}: ArtifactCloseComponentProps = $props();

	// Create close state
	const closeState = ArtifactCloseState.create();

	// Handle click
	function handleClick(e: MouseEvent) {
		if (disabled) {
			e.preventDefault();
			return;
		}
		closeState.close();
	}

	// Props for the close button
	const mergedProps = $derived(
		mergeProps(restProps as Record<string, unknown>, {
			[closeState.getHPDAttr('close')]: '',
			type: 'button' as const,
			disabled: disabled || undefined,
			onclick: handleClick,
			...closeState.sharedProps
		}, className ? { class: className } : {}) as Record<string, unknown>
	);

	// Snippet props exposed to children
	const snippetProps = $derived({
		open: closeState.open
	});
</script>

{#if child}
	{@render child({ props: mergedProps, ...snippetProps })}
{:else}
	<button bind:this={ref} {...mergedProps}>
		{@render children?.(snippetProps)}
	</button>
{/if}
