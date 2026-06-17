<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { MessageEditSaveButtonProps } from '../types.js';
	import { MessageEditSaveButtonState } from '../message-edit.svelte.js';

	let {
		'aria-label': ariaLabel = 'Save edit',
		class: className,
		child,
		children,
		...restProps
	}: MessageEditSaveButtonProps = $props();

	const saveState = MessageEditSaveButtonState.create({
		ariaLabel: boxWith(() => ariaLabel),
	});

	const mergedProps = $derived(mergeProps(restProps, saveState.props, className ? { class: className } : {}) as Record<string, unknown>);
</script>

{#if child}
	{@render child({ props: mergedProps, ...saveState.snippetProps })}
{:else}
	<button onclick={saveState.save} {...mergedProps}>
		{@render children?.(saveState.snippetProps)}
	</button>
{/if}
