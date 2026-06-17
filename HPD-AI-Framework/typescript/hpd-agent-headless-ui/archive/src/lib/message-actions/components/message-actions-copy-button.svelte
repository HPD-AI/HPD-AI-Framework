<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { MessageActionsCopyButtonProps, MessageActionsCopyButtonHTMLProps } from '../types.js';
	import { MessageActionsCopyButtonState } from '../message-actions.svelte.js';

	let {
		content,
		resetDelay = 2000,
		'aria-label': ariaLabel = 'Copy message',
		onSuccess,
		onError,
		class: className,
		child,
		children,
		...restProps
	}: MessageActionsCopyButtonProps = $props();

	const copyState = MessageActionsCopyButtonState.create({
		content: boxWith(() => content),
		resetDelay: boxWith(() => resetDelay),
		ariaLabel: boxWith(() => ariaLabel),
		onSuccess: boxWith(() => onSuccess),
		onError: boxWith(() => onError),
	});

	const mergedProps = $derived(mergeProps(restProps, copyState.props, className ? { class: className } : {}) as MessageActionsCopyButtonHTMLProps);
</script>

{#if child}
	{@render child({ props: mergedProps, ...copyState.snippetProps })}
{:else}
	<button {...mergedProps}>
		{@render children?.(copyState.snippetProps)}
	</button>
{/if}
