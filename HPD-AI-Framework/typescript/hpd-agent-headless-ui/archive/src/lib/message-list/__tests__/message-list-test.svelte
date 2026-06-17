<script lang="ts">
	import MessageList from '../components/message-list.svelte';
	import type { Message } from '../../agent/types.js';

	import type { ScrollBehavior } from '../types.js';

	let {
		messages = [] as Message[],
		scrollBehavior = 'bottom' as ScrollBehavior,
		atBottomThreshold = 50,
		keyboardNav = true,
		'aria-label': ariaLabel = 'Message history',
		onIsAtBottom = undefined as ((v: boolean) => void) | undefined,
		onScrollToBottom = undefined as ((fn: () => void) => void) | undefined,
		onRegister = undefined as
			| ((fn: (id: string, el: HTMLElement) => void) => void)
			| undefined,
		...restProps
	} = $props();
</script>

<MessageList
	data-testid="message-list"
	{messages}
	{scrollBehavior}
	{atBottomThreshold}
	{keyboardNav}
	aria-label={ariaLabel}
	{...restProps}
>
	{#snippet children({ isAtBottom, scrollToBottom, registerMessageElement, unregisterMessageElement })}
		{#if onIsAtBottom}
			<!-- expose isAtBottom to test via a data attr on a sentinel -->
			<span data-testid="at-bottom-sentinel" data-at-bottom={isAtBottom}></span>
		{/if}
		{#if onScrollToBottom}
			<!-- expose scrollToBottom fn to test via a button -->
			<button data-testid="scroll-to-bottom-btn" onclick={scrollToBottom}>↓</button>
		{/if}
		{#each messages as message (message.id)}
			<div
				data-message-id={message.id}
				data-testid={`msg-${message.id}`}
			>{message.content}</div>
		{/each}
	{/snippet}
</MessageList>
