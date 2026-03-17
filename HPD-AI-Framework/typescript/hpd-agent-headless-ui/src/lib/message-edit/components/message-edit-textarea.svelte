<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { MessageEditTextareaProps } from '../types.js';
	import { MessageEditTextareaState } from '../message-edit.svelte.js';
	import InputRoot from '$lib/input/components/input.svelte';

	let {
		placeholder = 'Edit message…',
		'aria-label': ariaLabel = 'Edit message',
		class: className,
		child,
		...restProps
	}: MessageEditTextareaProps = $props();

	const textareaState = MessageEditTextareaState.create({
		placeholder: boxWith(() => placeholder),
		ariaLabel: boxWith(() => ariaLabel),
	});

	const mergedProps = $derived(mergeProps(restProps, textareaState.props, className ? { class: className } : {}));

	let ref: HTMLTextAreaElement | null = $state(null);

	$effect(() => {
		if (ref) {
			ref.focus();
		}
	});
</script>

{#if child}
	{@render child({ props: mergedProps, ...textareaState.snippetProps })}
{:else}
	<InputRoot
		bind:ref
		{...(mergedProps as Record<string, unknown>)}
		value={textareaState.snippetProps.value}
		placeholder={textareaState.snippetProps.placeholder}
		disabled={textareaState.snippetProps.pending}
		onChange={(d) => textareaState.handleChange(d.value)}
		onkeydown={textareaState.handleKeyDown}
	/>
{/if}
