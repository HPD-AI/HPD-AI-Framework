<script lang="ts">
	import Input from '../components/input.svelte';

	let {
		value = $bindable(''),
		defaultValue = '',
		placeholder = 'Type a message...',
		disabled = false,
		maxRows = 5,
		autoFocus = false,
		showSubmitButton = true,
		showOutput = true,
	} = $props();

	let lastSubmitted = $state('');
	let changeLog = $state<string[]>([]);

	function handleSubmit(details: { value: string }) {
		lastSubmitted = details.value;
		changeLog = [...changeLog, `Submitted: "${details.value}"`];
		// Clear input after submit (consumer responsibility)
		value = '';
	}

	function handleChange(details: { value: string; reason: string }) {
		if (changeLog.length > 10) {
			changeLog = changeLog.slice(-10);
		}
	}
</script>

<div class="demo-container">
	<div class="input-section">
		<Input
			bind:value
			{defaultValue}
			{placeholder}
			{disabled}
			{maxRows}
			{autoFocus}
			onSubmit={handleSubmit}
			onChange={handleChange}
			class="demo-input"
		/>
		{#if showSubmitButton}
			<button onclick={() => handleSubmit({ value })} disabled={disabled || !value.trim()} class="submit-btn">
				Send
			</button>
		{/if}
	</div>

	{#if showOutput}
		<div class="output-section">
			<h4>Component State:</h4>
			<pre>Value: {JSON.stringify(value)}
Disabled: {disabled}
Filled: {value.length > 0}
Length: {value.length}</pre>

			{#if lastSubmitted}
				<h4>Last Submitted:</h4>
				<pre>{lastSubmitted}</pre>
			{/if}

			{#if changeLog.length > 0}
				<h4>Event Log:</h4>
				<div class="log">
					{#each changeLog.slice(-5) as log}
						<div class="log-entry">{log}</div>
					{/each}
				</div>
			{/if}
		</div>
	{/if}
</div>

<style>
	.demo-container {
		max-width: 600px;
		display: flex;
		flex-direction: column;
		gap: 1.5rem;
		font-family: system-ui, -apple-system, sans-serif;
	}

	.input-section {
		display: flex;
		gap: 0.75rem;
		align-items: flex-start;
	}

	:global(.demo-input) {
		flex: 1;
		padding: 0.75rem;
		border: 2px solid #ddd;
		border-radius: 8px;
		font-family: inherit;
		font-size: 1rem;
		resize: none;
		transition: border-color 0.2s;
	}

	:global(.demo-input:focus) {
		outline: none;
		border-color: #667eea;
	}

	:global(.demo-input:disabled) {
		background: #f5f5f5;
		cursor: not-allowed;
		opacity: 0.6;
	}

	.submit-btn {
		padding: 0.75rem 1.5rem;
		background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
		color: white;
		border: none;
		border-radius: 8px;
		font-weight: 600;
		cursor: pointer;
		white-space: nowrap;
	}

	.submit-btn:hover:not(:disabled) {
		opacity: 0.9;
	}

	.submit-btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.output-section {
		padding: 1rem;
		background: #f5f5f5;
		border-radius: 8px;
		font-size: 0.875rem;
	}

	.output-section h4 {
		margin: 0 0 0.5rem 0;
		font-size: 0.875rem;
		color: #666;
		text-transform: uppercase;
		letter-spacing: 0.5px;
	}

	.output-section pre {
		margin: 0 0 1rem 0;
		padding: 0.5rem;
		background: white;
		border: 1px solid #ddd;
		border-radius: 4px;
		font-size: 0.8rem;
		overflow-x: auto;
	}

	.log {
		display: flex;
		flex-direction: column;
		gap: 0.25rem;
	}

	.log-entry {
		padding: 0.25rem 0.5rem;
		background: white;
		border-left: 3px solid #667eea;
		font-family: 'Monaco', 'Courier New', monospace;
		font-size: 0.75rem;
	}
</style>
