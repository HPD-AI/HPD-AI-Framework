<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { MessageEditRootProps } from '../types.js';
	import { MessageEditRootState } from '../message-edit.svelte.js';

	let {
		workspace,
		messageIndex,
		initialValue,
		editing,
		onStartEdit,
		onSave,
		onCancel,
		onError,
		class: className,
		child,
		children,
		...restProps
	}: MessageEditRootProps = $props();

	const rootState = MessageEditRootState.create({
		workspace: boxWith(() => workspace),
		messageIndex: boxWith(() => messageIndex),
		initialValue: boxWith(() => initialValue),
		editing: boxWith(() => editing),
		onStartEdit: boxWith(() => onStartEdit),
		onSave: boxWith(() => onSave),
		onCancel: boxWith(() => onCancel),
		onError: boxWith(() => onError),
	});

	const mergedProps = $derived(mergeProps(restProps, rootState.props, className ? { class: className } : {}) as Record<string, unknown>);
	const snippetProps = $derived(rootState.snippetProps);
</script>

{#if child}
	{@render child({ props: mergedProps, ...snippetProps })}
{:else}
	<div {...mergedProps}>
		{@render children?.(snippetProps)}
	</div>
{/if}
