<script lang="ts">
	import { Message } from '$lib/index.js';
	import type { Message as MessageType, MessageRole } from '$lib/agent/types.js';

	let {
		role = 'assistant' as MessageRole,
		content = 'Hello!',
		streaming = false,
		thinking = false,
		reasoning = '',
		...restProps
	} = $props();

	// Create reactive message object from props
	const message = $derived<MessageType>({
		id: 'story-msg-' + Date.now(),
		role,
		content,
		streaming,
		thinking,
		reasoning,
		toolCalls: [],
		timestamp: new Date()
	});
</script>

<div class="demo-container">
	<Message {message}>
		{#snippet children({ content, role, streaming, thinking, status, reasoning, hasReasoning })}
			<div class="message" data-role={role} data-status={status}>
				<div class="message-header">
					<strong class="role">{role === 'user' ? 'You' : 'Assistant'}</strong>
					<span class="status-badge" data-status={status}>{status}</span>
				</div>

				{#if thinking}
					<div class="thinking-indicator">Thinking...</div>
				{/if}

				{#if hasReasoning}
					<div class="reasoning">
						<strong>Reasoning:</strong>
						{reasoning}
					</div>
				{/if}

				<div class="message-content">
					{content}
					{#if streaming}
						<span class="cursor">▊</span>
					{/if}
				</div>
			</div>
		{/snippet}
	</Message>
</div>

<style>
	.demo-container {
		padding: 2rem;
		max-width: 800px;
		font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
	}

	.message {
		border: 1px solid #ddd;
		border-radius: 12px;
		padding: 1rem;
		margin: 1rem 0;
		transition: box-shadow 0.2s;
	}

	.message:hover {
		box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
	}

	.message[data-role='user'] {
		background: linear-gradient(135deg, #f0f0ff 0%, #e6e6ff 100%);
		border-color: #c0c0ff;
	}

	.message[data-role='assistant'] {
		background: #ffffff;
	}

	.message-header {
		display: flex;
		gap: 0.5rem;
		align-items: center;
		margin-bottom: 0.75rem;
		font-size: 0.9rem;
	}

	.role {
		font-weight: 600;
		color: #333;
	}

	.message[data-role='user'] .role {
		color: #4444ff;
	}

	.message[data-role='assistant'] .role {
		color: #764ba2;
	}

	.status-badge {
		font-size: 0.75rem;
		padding: 0.125rem 0.5rem;
		border-radius: 12px;
		font-weight: 500;
		text-transform: uppercase;
		letter-spacing: 0.5px;
	}

	.status-badge[data-status='streaming'] {
		background: #4f46e5;
		color: white;
	}

	.status-badge[data-status='thinking'] {
		background: #764ba2;
		color: white;
	}

	.status-badge[data-status='executing'] {
		background: #f59e0b;
		color: white;
	}

	.status-badge[data-status='complete'] {
		background: #047857;
		color: white;
	}

	.thinking-indicator {
		color: #764ba2;
		font-style: italic;
		margin-bottom: 0.5rem;
		animation: pulse 1.5s infinite;
	}

	@keyframes pulse {
		0%,
		100% {
			opacity: 1;
		}
		50% {
			opacity: 0.6;
		}
	}

	.reasoning {
		background: #f9f9f9;
		border-left: 3px solid #764ba2;
		padding: 0.75rem;
		margin-bottom: 0.75rem;
		font-size: 0.9rem;
		color: #666;
		border-radius: 4px;
	}

	.reasoning strong {
		color: #764ba2;
		display: block;
		margin-bottom: 0.25rem;
	}

	.message-content {
		line-height: 1.6;
		color: #333;
	}

	.cursor {
		display: inline-block;
		margin-left: 2px;
		animation: blink 1s infinite;
	}

	@keyframes blink {
		0%,
		50% {
			opacity: 1;
		}
		51%,
		100% {
			opacity: 0;
		}
	}
</style>
