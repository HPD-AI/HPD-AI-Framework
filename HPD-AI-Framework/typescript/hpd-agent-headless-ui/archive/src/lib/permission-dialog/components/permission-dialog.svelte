<script lang="ts">
	import { boxWith } from 'svelte-toolbelt';
	import type { PermissionDialogRootProps } from '../types.js';
	import { PermissionDialogRootState } from '../permission-dialog.svelte.js';

	let {
		agent,
		onOpenChangeComplete,
		render,
		children
	}: PermissionDialogRootProps = $props();

	// Create root state
	const rootState = PermissionDialogRootState.create({
		agent: boxWith(() => agent),
		onOpenChangeComplete: boxWith(() => onOpenChangeComplete)
	});

	// Render props for custom render function  
	const renderProps = $derived({
		request: rootState.currentRequest,
		status: rootState.status,
		approve: rootState.approve.bind(rootState),
		deny: rootState.deny.bind(rootState)
	});
</script>

<!--
  Render priority:
  1. If render function provided, use it
  2. Otherwise, use compound components (children)
-->
{#if render}
	{@render render(renderProps)}
{:else}
	{@render children?.()}
{/if}
