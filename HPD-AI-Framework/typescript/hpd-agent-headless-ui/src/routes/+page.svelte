<script lang="ts">
	import { createMockWorkspace, Message, MessageList, Input, ToolExecution, PermissionDialog } from '$lib/index.js';
	import type { PermissionRequest } from '$lib/agent/types.js';

	// Create mock workspace instance
	const agent = createMockWorkspace({
		typingDelay: 30,
		enableReasoning: false
	});

	// Input state
	let input = $state('');

	// Send message handler using Input component's onSubmit
	async function handleSubmit(details: { value: string }) {
		await agent.send(details.value);
	}

	// Test permission trigger
	function triggerPermission() {
		const request: PermissionRequest = {
			permissionId: `perm-${Date.now()}`,
			sourceName: 'file-system',
			functionName: 'deleteFile',
			description: 'Delete important configuration file',
			callId: `call-${Date.now()}`,
			arguments: {
				path: '/etc/important-config.json',
				force: true
			}
		};
		agent.state!.onPermissionRequest(request);
	}
</script>

<div class="container">
	<header class="header">
		<h1>HPD Agent Headless UI</h1>
		<p class="subtitle">The world's first headless AI chat library</p>
		<div class="badge">Phase 1 Demo</div>
	</header>

	<main class="chat-container">
		<MessageList.Root messages={agent.state!.messages} class="messages">
			{#if agent.state!.messages.length === 0}
				<div class="empty-state">
					<p>👋 Send a message to start chatting with the mock agent</p>
					<p class="hint">
						Now using MessageList + Message components - truly headless AI primitives!
					</p>
				</div>
			{/if}

			{#each agent.state!.messages as message (message.id)}
				<Message {message}>
					{#snippet children({ content, role, streaming, thinking, status, toolCalls })}
						<div class="message" data-role={role} data-streaming={streaming || undefined}>
							<div class="message-header">
								<span class="role">{role === 'user' ? 'You' : 'Assistant'}</span>
								{#if streaming}
									<span class="status">typing...</span>
								{:else if thinking}
									<span class="status">thinking...</span>
								{/if}
								<span class="status-badge" data-status={status}>{status}</span>
							</div>
							<div class="message-content">
								{content}
								{#if streaming}
									<span class="cursor">▊</span>
								{/if}
							</div>

							<!-- Tool Executions -->
							{#if toolCalls && toolCalls.length > 0}
								<div class="tool-calls">
									{#each toolCalls as toolCall (toolCall.callId)}
										<ToolExecution.Root {toolCall} class="tool-execution">
											<ToolExecution.Trigger class="tool-trigger">
												<div class="tool-header">
													<span class="tool-icon"></span>
													<span class="tool-name">{toolCall.name}</span>
													<ToolExecution.Status class="tool-status">
														{#snippet children({ status })}
															<span class="status-badge" data-status={status}>
																{status}
															</span>
														{/snippet}
													</ToolExecution.Status>
												</div>
											</ToolExecution.Trigger>

											<ToolExecution.Content class="tool-content">
												<ToolExecution.Args class="tool-args">
													{#snippet children({ argsJson, hasArgs })}
														{#if hasArgs}
															<div class="args-section">
																<h4>Arguments:</h4>
																<pre class="code-block">{argsJson}</pre>
															</div>
														{/if}
													{/snippet}
												</ToolExecution.Args>

												<ToolExecution.Result class="tool-result">
													{#snippet children({ result, error, hasResult, hasError })}
														{#if hasError}
															<div class="error-section">
																<h4>Error:</h4>
																<pre class="error-block">{error}</pre>
															</div>
														{:else if hasResult}
															<div class="result-section">
																<h4>Result:</h4>
																<pre class="result-block">{result}</pre>
															</div>
														{/if}
													{/snippet}
												</ToolExecution.Result>
											</ToolExecution.Content>
										</ToolExecution.Root>
									{/each}
								</div>
							{/if}
						</div>
					{/snippet}
				</Message>
			{/each}
		</MessageList.Root>

		<div class="input-container">
			<Input.Root
				bind:value={input}
				onSubmit={handleSubmit}
				placeholder="Type a message... (Enter to send, Shift+Enter for new line)"
				maxRows={5}
				disabled={agent.state!.streaming}
				class="chat-input"
			/>
			<button
				onclick={() => handleSubmit({ value: input })}
				disabled={!input.trim() || agent.state!.streaming}
				class="send-button"
			>
				{#if agent.state!.streaming}
					<span class="loading">●</span>
				{:else}
					Send
				{/if}
			</button>
		</div>

		<div class="status-bar">
			{#if agent.state!.streaming}
				<span class="indicator streaming">● Streaming</span>
			{:else if agent.state!.canSend}
				<span class="indicator ready">● Ready</span>
			{:else}
				<span class="indicator">● Idle</span>
			{/if}
			<span class="message-count">{agent.state!.messages.length} messages</span>
			<button onclick={triggerPermission} class="test-permission-btn">
				🔓 Test Permission
			</button>
		</div>
	</main>
</div>

<!-- PermissionDialog Component -->
<PermissionDialog.Root {agent}>
	<PermissionDialog.Overlay class="permission-overlay" />
	<PermissionDialog.Content class="permission-content">
		<PermissionDialog.Header class="permission-header">
			🔐 Permission Required
		</PermissionDialog.Header>

		<PermissionDialog.Description class="permission-description">
			{#snippet children({ functionName, description, arguments: args, status })}
				<div class="permission-details">
					<div class="function-info">
						<strong class="function-name">{functionName}</strong>
						{#if status === 'complete'}
							<span class="status-badge processing">Processing...</span>
						{:else if status === 'executing'}
							<span class="status-badge active">Waiting for approval</span>
						{/if}
					</div>

					{#if description}
						<p class="description-text">{description}</p>
					{/if}

					{#if args && Object.keys(args).length > 0}
						<div class="args-preview">
							<strong>Arguments:</strong>
							<code>{JSON.stringify(args)}</code>
						</div>
					{/if}
				</div>
			{/snippet}
		</PermissionDialog.Description>

		<PermissionDialog.Actions class="permission-actions">
			<PermissionDialog.Deny class="btn btn-deny">
				  Deny
			</PermissionDialog.Deny>
			<PermissionDialog.Approve choice="ask" class="btn btn-approve-once">
				⏸️ Ask Each Time
			</PermissionDialog.Approve>
			<PermissionDialog.Approve choice="allow_always" class="btn btn-approve-always">
				  Always Allow
			</PermissionDialog.Approve>
		</PermissionDialog.Actions>
	</PermissionDialog.Content>
</PermissionDialog.Root>

<style>
	:global(body) {
		margin: 0;
		padding: 0;
		font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial,
			sans-serif;
		background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
		min-height: 100vh;
	}

	.container {
		max-width: 900px;
		margin: 0 auto;
		padding: 2rem;
		min-height: 100vh;
		display: flex;
		flex-direction: column;
	}

	.header {
		text-align: center;
		color: white;
		margin-bottom: 2rem;
	}

	.header h1 {
		font-size: 2.5rem;
		margin: 0 0 0.5rem 0;
		font-weight: 700;
	}

	.subtitle {
		font-size: 1.1rem;
		opacity: 0.9;
		margin: 0 0 1rem 0;
	}

	.badge {
		display: inline-block;
		background: rgba(255, 255, 255, 0.2);
		padding: 0.5rem 1rem;
		border-radius: 20px;
		font-size: 0.9rem;
		font-weight: 500;
	}

	.chat-container {
		background: white;
		border-radius: 16px;
		box-shadow: 0 20px 60px rgba(0, 0, 0, 0.3);
		overflow: hidden;
		display: flex;
		flex-direction: column;
		height: 600px;
	}

	:global([data-message-list]) {
		flex: 1;
		overflow-y: auto;
		padding: 1.5rem;
		display: flex;
		flex-direction: column;
		gap: 1rem;
	}

	.empty-state {
		text-align: center;
		color: #666;
		padding: 3rem 1rem;
	}

	.empty-state p {
		margin: 0.5rem 0;
	}

	.hint {
		font-size: 0.9rem;
		color: #999;
	}

	.message {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
		animation: slideIn 0.3s ease-out;
	}

	@keyframes slideIn {
		from {
			opacity: 0;
			transform: translateY(10px);
		}
		to {
			opacity: 1;
			transform: translateY(0);
		}
	}

	.message[data-role='user'] {
		align-items: flex-end;
	}

	.message[data-role='assistant'] {
		align-items: flex-start;
	}

	.message-header {
		display: flex;
		gap: 0.5rem;
		align-items: center;
		font-size: 0.85rem;
	}

	.role {
		font-weight: 600;
		color: #666;
	}

	.message[data-role='user'] .role {
		color: #667eea;
	}

	.message[data-role='assistant'] .role {
		color: #764ba2;
	}

	.status {
		color: #999;
		font-style: italic;
	}

	.status-badge {
		font-size: 0.75rem;
		padding: 0.125rem 0.375rem;
		border-radius: 4px;
		background: #f0f0f0;
		color: #666;
		font-weight: 500;
		text-transform: uppercase;
	}

	.status-badge[data-status='streaming'] {
		background: #667eea;
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

	.message-content {
		max-width: 70%;
		padding: 1rem 1.25rem;
		border-radius: 16px;
		line-height: 1.5;
		word-wrap: break-word;
	}

	.message[data-role='user'] .message-content {
		background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
		color: white;
		border-bottom-right-radius: 4px;
	}

	.message[data-role='assistant'] .message-content {
		background: #f5f5f5;
		color: #333;
		border-bottom-left-radius: 4px;
	}

	.cursor {
		display: inline-block;
		width: 8px;
		animation: blink 1s infinite;
		margin-left: 2px;
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

	.input-container {
		padding: 1.5rem;
		border-top: 1px solid #eee;
		display: flex;
		gap: 1rem;
		background: #fafafa;
	}

	/* Style the Input component's textarea */
	:global(.chat-input) {
		flex: 1;
		padding: 0.75rem;
		border: 2px solid #ddd;
		border-radius: 8px;
		font-family: inherit;
		font-size: 1rem;
		resize: none;
		transition: border-color 0.2s;
	}

	:global(.chat-input:focus) {
		outline: none;
		border-color: #667eea;
	}

	:global(.chat-input:disabled) {
		background: #f5f5f5;
		cursor: not-allowed;
	}

	.send-button {
		padding: 0.75rem 2rem;
		background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
		color: white;
		border: none;
		border-radius: 8px;
		font-size: 1rem;
		font-weight: 600;
		cursor: pointer;
		transition: transform 0.2s, opacity 0.2s;
	}

	.send-button:hover:not(:disabled) {
		transform: translateY(-2px);
	}

	.send-button:active:not(:disabled) {
		transform: translateY(0);
	}

	.send-button:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.loading {
		display: inline-block;
		animation: pulse 1.5s infinite;
	}

	@keyframes pulse {
		0%,
		100% {
			opacity: 1;
		}
		50% {
			opacity: 0.5;
		}
	}

	.status-bar {
		padding: 0.75rem 1.5rem;
		background: #f5f5f5;
		border-top: 1px solid #eee;
		display: flex;
		justify-content: space-between;
		align-items: center;
		font-size: 0.85rem;
		color: #666;
	}

	.indicator {
		display: flex;
		align-items: center;
		gap: 0.5rem;
	}

	.indicator.streaming {
		color: #667eea;
		font-weight: 500;
	}

	.indicator.ready {
		color: #10b981;
		font-weight: 500;
	}

	.message-count {
		color: #999;
	}

	.test-permission-btn {
		padding: 0.5rem 1rem;
		background: #667eea;
		color: white;
		border: none;
		border-radius: 6px;
		font-size: 0.85rem;
		cursor: pointer;
		transition: background 0.2s;
	}

	.test-permission-btn:hover {
		background: #5568d3;
	}

	/* Permission Dialog Styles */
	:global(.permission-overlay) {
		position: fixed;
		inset: 0;
		background: rgba(0, 0, 0, 0.5);
		animation: fadeIn 0.2s ease-out;
		z-index: 9998;
	}

	:global(.permission-content) {
		position: fixed;
		top: 50%;
		left: 50%;
		transform: translate(-50%, -50%);
		background: white;
		border-radius: 12px;
		padding: 2rem;
		max-width: 500px;
		width: 90%;
		box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.1), 0 10px 10px -5px rgba(0, 0, 0, 0.04);
		animation: slideInDialog 0.3s ease-out;
		z-index: 9999;
	}

	:global(.permission-header) {
		font-size: 1.5rem;
		font-weight: 700;
		margin-bottom: 1rem;
		color: #1f2937;
	}

	:global(.permission-description) {
		margin-bottom: 1.5rem;
	}

	.permission-details {
		display: flex;
		flex-direction: column;
		gap: 1rem;
	}

	.function-info {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		flex-wrap: wrap;
	}

	.function-name {
		font-size: 1.125rem;
		color: #1f2937;
		font-family: 'Monaco', 'Courier New', monospace;
	}

	.permission-details .status-badge {
		padding: 0.25rem 0.75rem;
		border-radius: 9999px;
		font-size: 0.75rem;
		font-weight: 600;
	}

	.permission-details .status-badge.active {
		background: #fef3c7;
		color: #92400e;
	}

	.permission-details .status-badge.processing {
		background: #dbeafe;
		color: #1e40af;
	}

	.description-text {
		color: #6b7280;
		line-height: 1.5;
	}

	.args-preview {
		padding: 0.75rem;
		background: #f9fafb;
		border-radius: 6px;
		font-size: 0.875rem;
	}

	.args-preview code {
		color: #6366f1;
		font-family: 'Monaco', 'Courier New', monospace;
	}

	:global(.permission-actions) {
		display: flex;
		gap: 0.75rem;
		justify-content: flex-end;
	}

	:global(.btn) {
		padding: 0.625rem 1.25rem;
		font-size: 0.9375rem;
		font-weight: 500;
		border-radius: 6px;
		border: none;
		cursor: pointer;
		transition: all 0.2s;
	}

	:global(.btn-deny) {
		background: #ef4444;
		color: white;
	}

	:global(.btn-deny:hover) {
		background: #dc2626;
	}

	:global(.btn-approve-once) {
		background: #f59e0b;
		color: white;
	}

	:global(.btn-approve-once:hover) {
		background: #d97706;
	}

	:global(.btn-approve-always) {
		background: #10b981;
		color: white;
	}

	:global(.btn-approve-always:hover) {
		background: #059669;
	}

	@keyframes fadeIn {
		from {
			opacity: 0;
		}
		to {
			opacity: 1;
		}
	}

	@keyframes slideInDialog {
		from {
			opacity: 0;
			transform: translate(-50%, -48%);
		}
		to {
			opacity: 1;
			transform: translate(-50%, -50%);
		}
	}
</style>
