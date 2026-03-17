<script lang="ts">
	import { boxWith, mergeProps } from 'svelte-toolbelt';
	import { ArtifactPanelState } from '../artifact.svelte.js';
	import type { ArtifactPanelComponentProps } from '../types.js';

	let { class: className, child, children, ref = $bindable(null), ...restProps }: ArtifactPanelComponentProps = $props();

	// Create panel state with boxed ref
	const panelState = ArtifactPanelState.create({
		ref: boxWith(
			() => ref,
			(v) => (ref = v)
		)
	});

	// Build style string - merge user styles with default full-height layout
	const defaultStyle = 'height: 100%; min-height: 0; display: flex; flex-direction: column;';
	const mergedStyle = $derived(
		restProps.style ? `${defaultStyle} ${restProps.style}` : defaultStyle
	);

	// Props for the panel container - includes default styles for full height
	const mergedProps = $derived(
		mergeProps(restProps as Record<string, unknown>, {
			[panelState.getHPDAttr('panel')]: '',
			...panelState.sharedProps,
			style: mergedStyle
		}, className ? { class: className } : {}) as Record<string, unknown>
	);

	// Snippet props exposed to children
	const snippetProps = $derived({
		open: panelState.open,
		openId: panelState.openId,
		title: panelState.title,
		content: panelState.content,
		close: () => panelState.close()
	});
</script>

{#if panelState.shouldRender}
	{#if child}
		{@render child({ props: mergedProps, ...snippetProps })}
	{:else}
		<div bind:this={ref} {...mergedProps}>
			{@render children?.(snippetProps)}
		</div>
	{/if}
{/if}
