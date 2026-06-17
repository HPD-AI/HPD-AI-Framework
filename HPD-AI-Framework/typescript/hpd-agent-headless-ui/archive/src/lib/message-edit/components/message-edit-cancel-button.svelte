<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { MessageEditCancelButtonProps } from '../types.js';
	import { MessageEditCancelButtonState } from '../message-edit.svelte.js';

	let {
		'aria-label': ariaLabel = 'Cancel edit',
		class: className,
		child,
		children,
		...restProps
	}: MessageEditCancelButtonProps = $props();

	const cancelState = MessageEditCancelButtonState.create({
		ariaLabel: boxWith(() => ariaLabel),
	});

	const mergedProps = $derived(mergeProps(restProps, cancelState.props, className ? { class: className } : {}) as Record<string, unknown>);
</script>

{#if child}
	{@render child({ props: mergedProps, ...cancelState.snippetProps })}
{:else}
	<button onclick={cancelState.cancel} {...mergedProps}>
		{@render children?.(cancelState.snippetProps)}
	</button>
{/if}
