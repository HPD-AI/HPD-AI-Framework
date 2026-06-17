<script lang="ts">
	import { boxWith, mergeProps } from 'svelte-toolbelt';
	import type { MessageListProps } from '../types.js';
	import { MessageListState } from '../message-list.svelte.js';
	import { createId } from '$lib/internal/create-id.js';

	const uid = $props.id();

	let {
		messages,
		scrollBehavior = 'bottom',
		atBottomThreshold = 50,
		keyboardNav = true,
		'aria-label': ariaLabel = 'Message history',
		ref = $bindable(null),
		id = createId(uid),
		class: className,
		child,
		children,
		...restProps
	}: MessageListProps = $props();

	const listState = MessageListState.create({
		messages: boxWith(() => messages),
		scrollBehavior: boxWith(() => scrollBehavior),
		atBottomThreshold: boxWith(() => atBottomThreshold),
		keyboardNav: boxWith(() => keyboardNav),
		ariaLabel: boxWith(() => ariaLabel),
		id: boxWith(() => id),
		ref: boxWith(
			() => ref,
			(v) => (ref = v)
		),
	});

	// mergedProps contains only plain string-keyed props — no Symbol attachments.
	const mergedProps = $derived(mergeProps(restProps, listState.props, className ? { class: className } : {}) as Record<string, unknown>);

	// Wire the default-path div's ref into listState (scroll listener setup).
	let defaultEl = $state<HTMLDivElement | null>(null);
	$effect(() => { listState.setRef(defaultEl); });

	const snippetProps = $derived({
		messages,
		messageCount: listState.messageCount,
		isAtBottom: listState.isAtBottom,
		scrollToBottom: listState.scrollToBottom,
		registerMessageElement: listState.registerMessageElement,
		unregisterMessageElement: listState.unregisterMessageElement,
		setRef: listState.setRef,
	});
</script>

{#if child}
	{@render child({ ...snippetProps, props: mergedProps })}
{:else}
	<div bind:this={defaultEl} {...mergedProps}>
		{@render children?.(snippetProps)}
	</div>
{/if}
