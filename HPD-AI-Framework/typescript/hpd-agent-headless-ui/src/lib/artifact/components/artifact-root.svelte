<script lang="ts">
	import { boxWith, mergeProps } from 'svelte-toolbelt';
	import { ArtifactRootState } from '../artifact.svelte.js';
	import type { ArtifactRootComponentProps } from '../types.js';

	let {
		id,
		defaultOpen = false,
		onOpenChange,
		class: className,
		child,
		children,
		ref = $bindable(null),
		...restProps
	}: ArtifactRootComponentProps = $props();

	// Create root state with boxed values
	const rootState = ArtifactRootState.create({
		id: boxWith(() => id),
		defaultOpen: boxWith(() => defaultOpen),
		onOpenChange: boxWith(() => onOpenChange)
	});

	// Props for the root container
	const mergedProps = $derived(
		mergeProps(restProps as Record<string, unknown>, {
			[rootState.getHPDAttr('root')]: '',
			...rootState.sharedProps
		}, className ? { class: className } : {}) as Record<string, unknown>
	);

	// Snippet props exposed to children
	const snippetProps = $derived({
		open: rootState.open,
		setOpen: (open: boolean) => rootState.setOpen(open),
		toggle: () => rootState.toggle()
	});
</script>

{#if child}
	{@render child({ props: mergedProps, ...snippetProps })}
{:else}
	<div bind:this={ref} {...mergedProps}>
		{@render children?.(snippetProps)}
	</div>
{/if}
