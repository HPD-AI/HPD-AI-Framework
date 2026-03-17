<script lang="ts">
	import { Message, type MessageSnippetProps } from '$lib/index.js';
	import type { Message as MessageType } from '$lib/agent/types.js';

	interface Props {
		scenario: string;
		content?: string;
		streaming?: boolean;
	}

	let { scenario, content = 'Hello from the message', streaming = false }: Props = $props();

	const baseMsg: MessageType = {
		id: 'test-id-123',
		role: 'assistant',
		content: 'Hello from the message',
		streaming: false,
		thinking: false,
		reasoning: '',
		toolCalls: [],
		timestamp: new Date(),
	};

	// Reactive message derived from props for update tests
	const updatableMsg: MessageType = $derived({
		...baseMsg,
		content,
		streaming,
	});
</script>

{#if scenario === 'initial-content'}
	<Message message={baseMsg}>
		{#snippet children({ content: c }: MessageSnippetProps)}
			<span data-testid="content-output">{c}</span>
		{/snippet}
	</Message>

{:else if scenario === 'initial-role'}
	<Message message={baseMsg}>
		{#snippet children({ role }: MessageSnippetProps)}
			<span data-testid="role-output">{role}</span>
		{/snippet}
	</Message>

{:else if scenario === 'streaming'}
	<Message message={{ ...baseMsg, streaming: true }}>
		{#snippet children({ streaming: s }: MessageSnippetProps)}
			<span data-testid="streaming-output">{String(s)}</span>
		{/snippet}
	</Message>

{:else if scenario === 'thinking'}
	<Message message={{ ...baseMsg, thinking: true }}>
		{#snippet children({ thinking: t }: MessageSnippetProps)}
			<span data-testid="thinking-output">{String(t)}</span>
		{/snippet}
	</Message>

{:else if scenario === 'update-content'}
	<Message message={updatableMsg}>
		{#snippet children({ content: c }: MessageSnippetProps)}
			<span data-testid="content-output">{c}</span>
		{/snippet}
	</Message>

{:else if scenario === 'update-streaming'}
	<Message message={updatableMsg}>
		{#snippet children({ streaming: s }: MessageSnippetProps)}
			<span data-testid="streaming-output">{String(s)}</span>
		{/snippet}
	</Message>

{:else if scenario === 'data-attrs'}
	<Message message={{ ...baseMsg, role: 'user' }}>
		{#snippet child({ props })}
			<div data-testid="message-root" {...props}></div>
		{/snippet}
	</Message>
{/if}
