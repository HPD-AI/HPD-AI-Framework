<script lang="ts">
	import { boxWith, mergeProps } from 'svelte-toolbelt';
	import { PermissionDialogApproveState } from '../permission-dialog.svelte.js';
	import type { PermissionDialogApproveProps } from '../types.js';
	import { createId } from '$lib/internal/create-id.js';

	const uid = $props.id();

	let {
		id = createId(uid),
		choice = 'ask',
		disabled = false,
		class: className,
		children,
		child,
		ref = $bindable(null),
		...restProps
	}: PermissionDialogApproveProps = $props();

	const approveState = PermissionDialogApproveState.create({
		id: boxWith(() => id),
		choice: boxWith(() => choice),
		disabled: boxWith(() => disabled),
		ref: boxWith(
			() => ref,
			(v) => (ref = v)
		)
	});

	const mergedProps = $derived(mergeProps(restProps, approveState.props, className ? { class: className } : {}) as Record<string, unknown>);

	let defaultEl = $state<HTMLButtonElement | null>(null);
	$effect(() => { approveState.setRef(defaultEl); });
</script>

{#if child}
	{@render child({ props: mergedProps })}
{:else}
	<button bind:this={defaultEl} {...mergedProps}>
		{@render children?.()}
	</button>
{/if}
