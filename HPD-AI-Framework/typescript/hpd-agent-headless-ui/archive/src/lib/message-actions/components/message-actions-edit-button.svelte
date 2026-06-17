<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { MessageActionsEditButtonProps, MessageActionsEditButtonHTMLProps } from '../types.js';
	import { MessageActionsEditButtonState } from '../message-actions.svelte.js';

	let {
		'aria-label': ariaLabel = 'Edit message',
		onSuccess,
		onError,
		class: className,
		child,
		children,
		...restProps
	}: MessageActionsEditButtonProps = $props();

	const editState = MessageActionsEditButtonState.create({
		ariaLabel: boxWith(() => ariaLabel),
		onSuccess: boxWith(() => onSuccess),
		onError: boxWith(() => onError),
	});

	const mergedProps = $derived(mergeProps(restProps, editState.props, className ? { class: className } : {}) as MessageActionsEditButtonHTMLProps);
</script>

{#if child}
	{@render child({ props: mergedProps, ...editState.snippetProps })}
{:else}
	<button {...mergedProps}>
		{@render children?.(editState.snippetProps)}
	</button>
{/if}
