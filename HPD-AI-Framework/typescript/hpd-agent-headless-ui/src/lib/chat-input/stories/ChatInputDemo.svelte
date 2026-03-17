<script lang="ts">
	import { ChatInput } from '$lib/index.js';

	let {
		value = $bindable(''),
		placeholder = 'Type a message...',
		disabled = false,
		maxRows = 5,
		showLeading = true,
		showTrailing = true,
		showTop = false,
		showBottom = false,
		showOutput = true,
	} = $props();

	let lastSubmitted = $state('');
	let submitLog = $state<string[]>([]);

	function handleSubmit(details: { value: string }) {
		lastSubmitted = details.value;
		submitLog = [...submitLog, `Submitted: "${details.value}"`];
		// Clear input after submit
		value = '';
	}

	function handleAttach() {
		submitLog = [...submitLog, 'Attach button clicked'];
	}

	function handleVoice() {
		submitLog = [...submitLog, 'Voice button clicked'];
	}

	function handleEmoji() {
		submitLog = [...submitLog, 'Emoji button clicked'];
	}
</script>

<div class="demo-container">
	<ChatInput.Root bind:value {disabled} onSubmit={handleSubmit}>
		{#if showTop}
			<ChatInput.Top>
				<div class="context-chips">
					<span class="chip">Context chip 1</span>
					<span class="chip">Context chip 2</span>
				</div>
			</ChatInput.Top>
		{/if}

		<div class="input-row">
			{#if showLeading}
				<ChatInput.Leading>
					<button class="icon-btn" onclick={handleAttach} title="Attach file" {disabled}>📎</button>
					<button class="icon-btn" onclick={handleVoice} title="Voice input" {disabled}>🎤</button>
				</ChatInput.Leading>
			{/if}

			<ChatInput.Input {placeholder} {maxRows} />

			{#if showTrailing}
				<ChatInput.Trailing>
					<button class="icon-btn" onclick={handleEmoji} title="Emoji" {disabled}>😊</button>
					<button
						class="send-btn"
						disabled={disabled || value.trim() === ''}
						onclick={() => handleSubmit({ value })}
					>
						Send
					</button>
				</ChatInput.Trailing>
			{/if}
		</div>

		{#if showBottom}
			<ChatInput.Bottom>
				{#snippet children({ characterCount, isEmpty })}
					<div class="status-bar">
						<span class="char-count">{characterCount} characters</span>
						{#if isEmpty}
							<span class="hint">Tip: Press Enter to send</span>
						{/if}
					</div>
				{/snippet}
			</ChatInput.Bottom>
		{/if}
	</ChatInput.Root>

	{#if showOutput}
		<div class="output-section">
			<h4>Component State:</h4>
			<pre>Value: {JSON.stringify(value)}
Disabled: {disabled}
Length: {value.length}
Can Submit: {value.trim().length > 0}</pre>

			{#if lastSubmitted}
				<h4>Last Submitted:</h4>
				<pre>{lastSubmitted}</pre>
			{/if}

			{#if submitLog.length > 0}
				<h4>Event Log:</h4>
				<div class="log">
					{#each submitLog.slice(-5) as log}
						<div class="log-entry">{log}</div>
					{/each}
				</div>
			{/if}
		</div>
	{/if}
</div>

<style>
	.demo-container {
		max-width: 800px;
		padding: 2rem;
		font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
	}

	/* Input row layout */
	.input-row {
		display: flex;
		align-items: center; /* Center align instead of flex-end for better button alignment */
		gap: 0.5rem;
		border: 2px solid #ddd;
		border-radius: 12px;
		padding: 0.5rem;
		background: white;
		transition: border-color 0.2s;
	}

	.input-row:focus-within {
		border-color: #667eea;
	}

	/* ChatInput component layout - IMPORTANT: Users must add these styles themselves */

	/* Leading/Trailing accessories - display inline with gap */
	:global([data-chat-input-leading]),
	:global([data-chat-input-trailing]) {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		flex-shrink: 0; /* Don't shrink these, only the input should flex */
	}

	/* Input wrapper - takes remaining space */
	:global([data-chat-input-input]) {
		flex: 1;
		min-width: 0; /* Allow flexbox to shrink below content size */
	}

	/* Style the actual textarea inside the Input component */
	:global([data-input]) {
		width: 100%;
		padding: 0.5rem;
		border: none;
		outline: none;
		font-family: inherit;
		font-size: 1rem;
		line-height: 1.5;
		resize: none; /* Disable manual resize since we have auto-resize */
		overflow-y: auto;
	}

	:global([data-input]:focus) {
		outline: none;
	}

	/* Top/Bottom accessories */
	:global([data-chat-input-top]),
	:global([data-chat-input-bottom]) {
		display: block;
		width: 100%;
	}

	/* Buttons */
	.icon-btn {
		padding: 0.625rem; /* Slightly more padding for better button size */
		background: transparent;
		border: 1px solid #ddd;
		border-radius: 8px;
		cursor: pointer;
		font-size: 1.2rem;
		line-height: 1;
		display: inline-flex;
		align-items: center;
		justify-content: center;
		transition:
			background 0.2s,
			transform 0.1s;
	}

	.icon-btn:hover:not(:disabled) {
		background: #f5f5f5;
	}

	.icon-btn:active:not(:disabled) {
		transform: scale(0.95);
	}

	.icon-btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.send-btn {
		padding: 0.5rem 1.5rem;
		background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
		color: white;
		border: none;
		border-radius: 8px;
		font-weight: 600;
		cursor: pointer;
		transition:
			transform 0.2s,
			opacity 0.2s;
	}

	.send-btn:hover:not(:disabled) {
		transform: translateY(-2px);
	}

	.send-btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	/* Context chips */
	.context-chips {
		display: flex;
		gap: 0.5rem;
		margin-bottom: 0.75rem;
		flex-wrap: wrap;
	}

	.chip {
		display: inline-block;
		padding: 0.25rem 0.75rem;
		background: #f0f0f0;
		border-radius: 16px;
		font-size: 0.85rem;
		color: #666;
	}

	/* Status bar */
	.status-bar {
		display: flex;
		justify-content: space-between;
		align-items: center;
		margin-top: 0.5rem;
		font-size: 0.85rem;
		color: #999;
	}

	.char-count {
		font-weight: 500;
	}

	.hint {
		font-style: italic;
	}

	/* Output section */
	.output-section {
		margin-top: 2rem;
		padding: 1rem;
		background: #f9f9f9;
		border-radius: 8px;
	}

	.output-section h4 {
		margin: 0 0 0.5rem 0;
		color: #666;
		font-size: 0.9rem;
		font-weight: 600;
	}

	.output-section pre {
		margin: 0 0 1rem 0;
		padding: 0.75rem;
		background: white;
		border: 1px solid #ddd;
		border-radius: 4px;
		font-family: 'Monaco', 'Courier New', monospace;
		font-size: 0.85rem;
		color: #333;
		overflow-x: auto;
	}

	.log {
		background: white;
		border: 1px solid #ddd;
		border-radius: 4px;
		padding: 0.5rem;
		max-height: 150px;
		overflow-y: auto;
	}

	.log-entry {
		padding: 0.25rem 0.5rem;
		font-family: 'Monaco', 'Courier New', monospace;
		font-size: 0.85rem;
		color: #333;
		border-bottom: 1px solid #f0f0f0;
	}

	.log-entry:last-child {
		border-bottom: none;
	}
</style>
