<script lang="ts">
	import { boxWith, mergeProps } from 'svelte-toolbelt';
	import { PermissionDialogHeaderState } from '../permission-dialog.svelte.js';
	import type { PermissionDialogHeaderProps } from '../types.js';
	import { createId } from '$lib/internal/create-id.js';

	const uid = $props.id();

	let {
		id = createId(uid),
		level = 2,
		class: className,
		children,
		child,
		ref = $bindable(null),
		...restProps
	}: PermissionDialogHeaderProps = $props();

	const headerState = PermissionDialogHeaderState.create({
		id: boxWith(() => id),
		level: boxWith(() => level),
		ref: boxWith(
			() => ref,
			(v) => (ref = v)
		)
	});

	const mergedProps = $derived(mergeProps(restProps, headerState.props, className ? { class: className } : {}) as Record<string, unknown>);

	let defaultEl = $state<HTMLElement | null>(null);
	$effect(() => { headerState.setRef(defaultEl); });

	// Snippet props for customization
	const snippetProps = $derived.by(() => ({
		functionName: headerState.root.currentRequest?.functionName,
		sourceName: headerState.root.currentRequest?.sourceName
	}));
</script>

{#if child}
	{@render child({ props: mergedProps, ...snippetProps })}
{:else if level === 1}
	<h1 bind:this={defaultEl} {...mergedProps}>{@render children?.(snippetProps)}</h1>
{:else if level === 2}
	<h2 bind:this={defaultEl} {...mergedProps}>{@render children?.(snippetProps)}</h2>
{:else if level === 3}
	<h3 bind:this={defaultEl} {...mergedProps}>{@render children?.(snippetProps)}</h3>
{:else if level === 4}
	<h4 bind:this={defaultEl} {...mergedProps}>{@render children?.(snippetProps)}</h4>
{:else if level === 5}
	<h5 bind:this={defaultEl} {...mergedProps}>{@render children?.(snippetProps)}</h5>
{:else}
	<h6 bind:this={defaultEl} {...mergedProps}>{@render children?.(snippetProps)}</h6>
{/if}
