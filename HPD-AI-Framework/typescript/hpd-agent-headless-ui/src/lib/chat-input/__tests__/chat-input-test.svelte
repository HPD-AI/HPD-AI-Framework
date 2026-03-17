<script lang="ts">
	/**
	 * ChatInput Test Harness Component
	 *
	 * Provides a wrapper for testing ChatInput components in the browser.
	 */
	import * as ChatInput from '../index.js';

	let {
		value = $bindable(''),
		defaultValue,
		disabled = false,
		placeholder = 'Type a message...',
		maxRows = 5,
		showLeading = false,
		showTrailing = false,
		showTop = false,
		showBottom = false,
		onSubmit,
		onChange,
		'data-testid': testId = 'chat-input'
	}: {
		value?: string;
		defaultValue?: string;
		disabled?: boolean;
		placeholder?: string;
		maxRows?: number;
		showLeading?: boolean;
		showTrailing?: boolean;
		showTop?: boolean;
		showBottom?: boolean;
		onSubmit?: (details: { value: string }) => void;
		onChange?: (value: string) => void;
		'data-testid'?: string;
	} = $props();

	// Track submissions for testing
	let submitCount = $state(0);
	let lastSubmitted = $state('');
	let currentValue = $state('');
	$effect(() => { currentValue = defaultValue || ''; });

	function handleSubmit(details: { value: string }) {
		submitCount++;
		lastSubmitted = details.value;
		if (onSubmit) onSubmit(details);
	}

	function handleChange(newValue: string) {
		currentValue = newValue;
		value = newValue;
		if (onChange) onChange(newValue);
	}
</script>

<div data-testid="test-wrapper">
	<ChatInput.Root
		{defaultValue}
		{disabled}
		onSubmit={handleSubmit}
		onChange={handleChange}
		data-testid={testId}
	>
		{#if showTop}
			<ChatInput.Top data-testid="chat-input-top">
				{#snippet children({ characterCount, isEmpty })}
					<div data-testid="top-content">
						<span data-testid="chip-1">Context 1</span>
						<span data-testid="chip-2">Context 2</span>
					</div>
				{/snippet}
			</ChatInput.Top>
		{/if}

		<div class="input-row" data-testid="input-row">
			{#if showLeading}
				<ChatInput.Leading data-testid="chat-input-leading">
					{#snippet children({ submit, disabled })}
						<button data-testid="attach-button" {disabled} type="button">📎</button>
						<button data-testid="voice-button" {disabled} type="button">🎤</button>
					{/snippet}
				</ChatInput.Leading>
			{/if}

			<ChatInput.Input {placeholder} {maxRows} data-testid="chat-input-input" />

			{#if showTrailing}
				<ChatInput.Trailing data-testid="chat-input-trailing">
					{#snippet children({ submit, canSubmit, disabled })}
						<button data-testid="emoji-button" {disabled} type="button">😊</button>
						<button
							data-testid="send-button"
							disabled={!canSubmit || disabled}
							onclick={() => submit()}
							type="button"
						>
							Send
						</button>
					{/snippet}
				</ChatInput.Trailing>
			{/if}
		</div>

		{#if showBottom}
			<ChatInput.Bottom data-testid="chat-input-bottom">
				{#snippet children({ characterCount, isEmpty })}
					<div data-testid="bottom-content">
						<span data-testid="char-count">{characterCount} characters</span>
						{#if isEmpty}
							<span data-testid="hint">Press Enter to send</span>
						{/if}
					</div>
				{/snippet}
			</ChatInput.Bottom>
		{/if}
	</ChatInput.Root>

	<!-- Output for testing -->
	<div data-testid="test-output">
		<span data-testid="submit-count">{submitCount}</span>
		<span data-testid="last-submitted">{lastSubmitted}</span>
		<span data-testid="current-value">{currentValue}</span>
	</div>
</div>

<style>
	/* Minimal styling for test harness */
	.input-row {
		display: flex;
		align-items: center;
		gap: 0.5rem;
	}

	:global([data-chat-input-leading]),
	:global([data-chat-input-trailing]) {
		display: flex;
		align-items: center;
		gap: 0.5rem;
	}

	:global([data-chat-input-input]) {
		flex: 1;
	}

	:global([data-input]) {
		width: 100%;
		padding: 0.5rem;
		border: 1px solid #ccc;
		border-radius: 4px;
		font-family: inherit;
		font-size: 1rem;
		resize: none;
	}
</style>
