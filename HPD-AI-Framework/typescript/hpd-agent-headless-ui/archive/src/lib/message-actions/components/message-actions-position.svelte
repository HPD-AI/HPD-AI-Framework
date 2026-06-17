<script lang="ts">
	import { mergeProps } from 'svelte-toolbelt';
	import type { MessageActionsPositionProps, MessageActionsPositionHTMLProps } from '../types.js';
	import { MessageActionsPositionState } from '../message-actions.svelte.js';

	let { class: className, child, children, ...restProps }: MessageActionsPositionProps = $props();

	const positionState = MessageActionsPositionState.create();

	const mergedProps = $derived(mergeProps(restProps, positionState.props, className ? { class: className } : {}) as MessageActionsPositionHTMLProps);
</script>

{#if child}
	{@render child({ props: mergedProps, ...positionState.snippetProps })}
{:else}
	<span {...mergedProps}>
		{#if children}
			{@render children(positionState.snippetProps)}
		{:else}
			{positionState.snippetProps.label || positionState.snippetProps.position}
		{/if}
	</span>
{/if}
