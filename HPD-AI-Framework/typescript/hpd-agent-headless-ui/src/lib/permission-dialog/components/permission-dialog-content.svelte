<script lang="ts">
	import { boxWith, mergeProps } from 'svelte-toolbelt';
	import { PermissionDialogContentState } from '../permission-dialog.svelte.js';
	import type { PermissionDialogContentProps } from '../types.js';
	import { createId } from '$lib/internal/create-id.js';
	import { noop } from '$lib/internal/noop.js';

	const uid = $props.id();

	let {
		id = createId(uid),
		class: className,
		children,
		child,
		ref = $bindable(null),
		forceMount = false,
		onCloseAutoFocus = noop,
		onOpenAutoFocus = noop,
		onEscapeKeydown = noop,
		onInteractOutside = noop,
		...restProps
	}: PermissionDialogContentProps = $props();

	const contentState = PermissionDialogContentState.create({
		id: boxWith(() => id),
		ref: boxWith(
			() => ref,
			(v) => (ref = v)
		)
	});

	const mergedProps = $derived(mergeProps(restProps, contentState.props, className ? { class: className } : {}) as Record<string, unknown>);

	function mountRef(el: HTMLDivElement) {
		contentState.setRef(el);
		return { destroy() { contentState.setRef(null); } };
	}

	// Snippet props for customization
	const snippetProps = $derived.by(() => ({
		request: contentState.root.currentRequest,
		status: contentState.root.status,
		isOpen: contentState.root.isOpen,
		approve: contentState.root.approve.bind(contentState.root),
		deny: contentState.root.deny.bind(contentState.root)
	}));
</script>

{#if contentState.shouldRender || forceMount}
	{#if child}
		{@render child({
			props: mergedProps,
			...snippetProps
		})}
	{:else}
		<div use:mountRef {...mergedProps}>
			{@render children?.(snippetProps)}
		</div>
	{/if}
{/if}
