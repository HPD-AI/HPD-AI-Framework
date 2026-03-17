<script lang="ts">
	import { boxWith, mergeProps } from 'svelte-toolbelt';
	import { PermissionDialogDenyState } from '../permission-dialog.svelte.js';
	import type { PermissionDialogDenyProps } from '../types.js';
	import { createId } from '$lib/internal/create-id.js';

	const uid = $props.id();

	let {
		id = createId(uid),
		reason,
		disabled = false,
		class: className,
		children,
		child,
		ref = $bindable(null),
		...restProps
	}: PermissionDialogDenyProps = $props();

	const denyState = PermissionDialogDenyState.create({
		id: boxWith(() => id),
		reason: boxWith(() => reason),
		disabled: boxWith(() => disabled),
		ref: boxWith(
			() => ref,
			(v) => (ref = v)
		)
	});

	const mergedProps = $derived(mergeProps(restProps, denyState.props, className ? { class: className } : {}) as Record<string, unknown>);

	let defaultEl = $state<HTMLButtonElement | null>(null);
	$effect(() => { denyState.setRef(defaultEl); });
</script>

{#if child}
	{@render child({ props: mergedProps })}
{:else}
	<button bind:this={defaultEl} {...mergedProps}>
		{@render children?.()}
	</button>
{/if}
