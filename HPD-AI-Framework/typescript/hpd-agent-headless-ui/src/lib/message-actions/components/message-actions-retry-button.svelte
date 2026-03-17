<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { MessageActionsRetryButtonProps, MessageActionsRetryButtonHTMLProps } from '../types.js';
	import { MessageActionsRetryButtonState } from '../message-actions.svelte.js';

	let {
		'aria-label': ariaLabel = 'Retry message',
		onSuccess,
		onError,
		class: className,
		child,
		children,
		...restProps
	}: MessageActionsRetryButtonProps = $props();

	const retryState = MessageActionsRetryButtonState.create({
		ariaLabel: boxWith(() => ariaLabel),
		onSuccess: boxWith(() => onSuccess),
		onError: boxWith(() => onError),
	});

	const mergedProps = $derived(mergeProps(restProps, retryState.props, className ? { class: className } : {}) as MessageActionsRetryButtonHTMLProps);
</script>

{#if child}
	{@render child({ props: mergedProps, ...retryState.snippetProps })}
{:else}
	<button {...mergedProps}>
		{@render children?.(retryState.snippetProps)}
	</button>
{/if}
