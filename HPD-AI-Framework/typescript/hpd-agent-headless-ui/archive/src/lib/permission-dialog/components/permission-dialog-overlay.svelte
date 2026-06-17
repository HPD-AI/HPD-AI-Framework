<script lang="ts">
	import { boxWith, mergeProps } from 'svelte-toolbelt';
	import { PermissionDialogOverlayState } from '../permission-dialog.svelte.js';
	import type { PermissionDialogOverlayProps } from '../types.js';
	import { createId } from '$lib/internal/create-id.js';

	const uid = $props.id();

	let {
		id = createId(uid),
		class: className,
		children,
		child,
		ref = $bindable(null),
		forceMount = false,
		...restProps
	}: PermissionDialogOverlayProps = $props();

	const overlayState = PermissionDialogOverlayState.create({
		id: boxWith(() => id),
		ref: boxWith(
			() => ref,
			(v) => (ref = v)
		)
	});

	const mergedProps = $derived(mergeProps(restProps, overlayState.props, className ? { class: className } : {}) as Record<string, unknown>);

	function mountRef(el: HTMLDivElement) {
		overlayState.setRef(el);
		return { destroy() { overlayState.setRef(null); } };
	}
</script>

{#if overlayState.shouldRender || forceMount}
	{#if child}
		{@render child({ props: mergedProps })}
	{:else}
		<div use:mountRef {...mergedProps}>
			{@render children?.()}
		</div>
	{/if}
{/if}
