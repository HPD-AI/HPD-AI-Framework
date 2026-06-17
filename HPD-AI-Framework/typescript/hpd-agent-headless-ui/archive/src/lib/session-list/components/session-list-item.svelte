<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { SessionListItemProps, SessionListItemHTMLProps } from '../types.js';
	import { SessionListItemState } from '../session-list.svelte.js';

	let {
		session,
		class: className,
		child,
		children,
		...restProps
	}: SessionListItemProps & { class?: string } = $props();

	let itemNode: HTMLElement | null = $state(null);

	// Rename to avoid conflict with $state rune
	const itemState = SessionListItemState.create({
		session: boxWith(() => session),
		itemNode: boxWith(() => itemNode),
	});

	const mergedProps = $derived(mergeProps(restProps, itemState.props, className ? { class: className } : {}) as SessionListItemHTMLProps);
</script>

{#if child}
	{@render child({ props: mergedProps, ...itemState.snippetProps })}
{:else}
	<div bind:this={itemNode} {...mergedProps}>
		{@render children?.(itemState.snippetProps)}
	</div>
{/if}
