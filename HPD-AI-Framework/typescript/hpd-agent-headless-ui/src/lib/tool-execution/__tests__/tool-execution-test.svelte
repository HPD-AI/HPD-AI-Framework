<script lang="ts">
	/**
	 * ToolExecution Test Component
	 *
	 * Test harness for the ToolExecution component
	 */
	import * as ToolExecution from '../exports.js';
	import type { ToolCall } from '../../agent/types.js';
	import type { ToolExecutionExpandEventDetails } from '../types.js';

	interface Props {
		toolCall?: ToolCall;
		expanded?: boolean;
		onExpandChange?: (expanded: boolean, details: ToolExecutionExpandEventDetails) => void;
	}

	let {
		toolCall = createDefaultToolCall(),
		expanded,
		onExpandChange
	}: Props = $props();

	// Use local state for expanded if not provided
	let localExpanded = $state(false);
	$effect(() => { localExpanded = expanded ?? false; });

	function createDefaultToolCall(): ToolCall {
		return {
			callId: 'test-call-1',
			name: 'testTool',
			messageId: 'msg-1',
			status: 'pending',
			args: { param1: 'value1', param2: 42 },
			startTime: new Date('2024-01-01T00:00:00'),
		};
	}

	// For testing binding
	let bindingDisplay = $derived(String(localExpanded));

	// Alternative trigger for testing binds
	function toggleViaAltTrigger() {
		localExpanded = !localExpanded;
	}
</script>

<ToolExecution.Root
	{toolCall}
	bind:expanded={localExpanded}
	{onExpandChange}
	data-testid="root"
>
	<ToolExecution.Trigger data-testid="trigger">
		<span>Toggle {toolCall.name}</span>
		<ToolExecution.Status data-testid="status">
			{#snippet children(props)}
				<span>{props.status}</span>
			{/snippet}
		</ToolExecution.Status>
	</ToolExecution.Trigger>

	<ToolExecution.Content data-testid="content">
		<ToolExecution.Args data-testid="args">
			{#snippet children(props)}
				<pre>{props.argsJson}</pre>
			{/snippet}
		</ToolExecution.Args>

		<ToolExecution.Result data-testid="result">
			{#snippet children(props)}
				{#if props.hasError}
					<span class="error">{props.error}</span>
				{:else if props.hasResult}
					<span class="result">{props.result}</span>
				{:else}
					<span class="no-result">No result yet</span>
				{/if}
			{/snippet}
		</ToolExecution.Result>
	</ToolExecution.Content>
</ToolExecution.Root>

<!-- For testing bindings -->
<div data-testid="binding">{bindingDisplay}</div>
<button data-testid="alt-trigger" onclick={toggleViaAltTrigger}>Alt Toggle</button>
