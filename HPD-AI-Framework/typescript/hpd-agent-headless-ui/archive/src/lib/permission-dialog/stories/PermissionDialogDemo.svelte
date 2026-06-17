<script lang="ts">
	import { createMockAgent } from '$lib/testing/mock-agent.svelte.js';
	import * as PermissionDialog from '../index.js';
	import type { PermissionRequest } from '$lib/agent/types.js';

	// Props for demo customization
	let {
		variant = 'default',
		showTrigger = true,
		showStatus = true
	}: {
		variant?: 'default' | 'minimal' | 'detailed' | 'child';
		showTrigger?: boolean;
		showStatus?: boolean;
	} = $props();

	// Create mock agent
	const agent = createMockAgent();

	// Track latest action
	let lastAction = $state<string>('');

	// Trigger a test permission request
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
		lastAction = `Permission request triggered: ${request.functionName}`;
	}

	// Trigger multiple permissions (queue demo)
	function triggerQueue() {
		const requests: PermissionRequest[] = [
			{
				permissionId: `perm-${Date.now()}-1`,
				sourceName: 'file-system',
				functionName: 'deleteFile',
				description: 'Delete user data directory',
				callId: `call-${Date.now()}-1`,
				arguments: { path: '/Users/data', recursive: true }
			},
			{
				permissionId: `perm-${Date.now()}-2`,
				sourceName: 'network',
				functionName: 'makeHttpRequest',
				description: 'Send data to external API',
				callId: `call-${Date.now()}-2`,
				arguments: { url: 'https://api.example.com', method: 'POST' }
			},
			{
				permissionId: `perm-${Date.now()}-3`,
				sourceName: 'database',
				functionName: 'dropTable',
				description: 'Drop users table from database',
				callId: `call-${Date.now()}-3`,
				arguments: { table: 'users', cascade: true }
			}
		];

		requests.forEach(request => agent.state!.onPermissionRequest(request));
		lastAction = `${requests.length} permissions queued`;
	}
</script>

<div class="demo-container">
	{#if showTrigger}
		<div class="demo-controls">
			<div class="button-group">
				<button onclick={triggerPermission} class="trigger-btn">
					🔓 Single Permission
				</button>
				<button onclick={triggerQueue} class="trigger-btn trigger-btn-queue">
					📋 Queue (3 Permissions)
				</button>
			</div>
			{#if showStatus && lastAction}
				<p class="status-text">{lastAction}</p>
			{/if}
			<p class="hint-text">
				Status: {agent.state!.pendingPermissions.length} pending permission(s)
			</p>
		</div>
	{/if}

	<!-- Compound Components Pattern (default, minimal, detailed) -->
	{#if variant === 'child'}
		<!-- Pattern 3: Child Snippet - Custom element with ARIA attributes -->
		<PermissionDialog.Root {agent}>
			<PermissionDialog.Overlay class="permission-overlay" />
			<PermissionDialog.Content>
				{#snippet child({ props, request, status, approve, deny })}
					<section {...props} class="custom-card-content">
						<header class="card-header">
							<h2 class="card-title">Action Required</h2>
							{#if status === 'complete'}
								<span class="badge badge-processing">Processing...</span>
							{:else if status === 'executing'}
								<span class="badge badge-pending">Pending</span>
							{/if}
						</header>

						{#if request}
							<div class="card-body">
								<div class="action-info">
									<strong class="action-label">Action:</strong>
									<code class="action-value">{request.functionName}</code>
								</div>

								{#if request.description}
									<p class="action-description">{request.description}</p>
								{/if}

								{#if request.arguments && Object.keys(request.arguments).length > 0}
									<div class="action-args">
										<strong class="args-label">Parameters:</strong>
										<pre class="args-value">{JSON.stringify(request.arguments, null, 2)}</pre>
									</div>
								{/if}
							</div>

							<footer class="card-footer">
								<button class="btn-secondary" onclick={() => deny()}>
									Decline
								</button>
								<button class="btn-primary" onclick={() => approve('ask')}>
									Proceed
								</button>
							</footer>
						{/if}
					</section>
				{/snippet}
			</PermissionDialog.Content>
		</PermissionDialog.Root>
	{:else}
		<!-- Pattern 1: Compound Components -->
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
								{#if variant === 'detailed'}
									<details class="args-details">
										<summary>View Arguments</summary>
										<pre class="args-code">{JSON.stringify(args, null, 2)}</pre>
									</details>
								{:else if variant === 'default'}
									<div class="args-preview">
										<strong>Arguments:</strong>
										<code>{JSON.stringify(args)}</code>
									</div>
								{/if}
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
	{/if}
</div>

<style>
	.demo-container {
		max-width: 700px;
		display: flex;
		flex-direction: column;
		gap: 1.5rem;
		font-family: system-ui, -apple-system, sans-serif;
	}

	.demo-controls {
		margin-bottom: 1rem;
		padding: 1.5rem;
		background: #f9fafb;
		border: 1px solid #e5e7eb;
		border-radius: 8px;
		text-align: center;
	}

	.button-group {
		display: flex;
		gap: 0.75rem;
		justify-content: center;
	}

	.trigger-btn {
		flex: 1;
		padding: 0.75rem 1.5rem;
		font-size: 0.9375rem;
		background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
		color: white;
		border: none;
		border-radius: 8px;
		font-weight: 600;
		cursor: pointer;
		transition: all 0.2s;
	}

	.trigger-btn:hover {
		opacity: 0.9;
		transform: translateY(-1px);
	}

	.trigger-btn-queue {
		background: linear-gradient(135deg, #f59e0b 0%, #d97706 100%);
	}

	.status-text {
		margin-top: 1rem;
		font-size: 0.875rem;
		color: #059669;
		font-weight: 500;
	}

	.hint-text {
		margin-top: 0.5rem;
		font-size: 0.875rem;
		color: #6b7280;
	}

	/* Dialog Styles */
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
		animation: slideIn 0.3s ease-out;
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

	.status-badge {
		padding: 0.25rem 0.75rem;
		border-radius: 9999px;
		font-size: 0.75rem;
		font-weight: 600;
		text-transform: uppercase;
	}

	.status-badge.active {
		background: #fef3c7;
		color: #92400e;
	}

	.status-badge.processing {
		background: #dbeafe;
		color: #1e40af;
	}

	.description-text {
		color: #6b7280;
		line-height: 1.5;
		margin: 0;
	}

	.args-preview {
		padding: 0.75rem;
		background: #f9fafb;
		border: 1px solid #e5e7eb;
		border-radius: 6px;
		font-size: 0.875rem;
	}

	.args-preview strong {
		color: #4b5563;
		display: block;
		margin-bottom: 0.5rem;
	}

	.args-preview code {
		color: #6366f1;
		font-family: 'Monaco', 'Courier New', monospace;
		font-size: 0.8rem;
	}

	.args-details {
		padding: 0.75rem;
		background: #f9fafb;
		border: 1px solid #e5e7eb;
		border-radius: 6px;
	}

	.args-details summary {
		cursor: pointer;
		font-weight: 600;
		color: #4b5563;
		user-select: none;
		font-size: 0.875rem;
	}

	.args-code {
		margin-top: 0.5rem;
		padding: 0.75rem;
		background: #1e1e1e;
		color: #10b981;
		border-radius: 4px;
		overflow-x: auto;
		font-size: 0.75rem;
		font-family: 'Monaco', 'Courier New', monospace;
	}

	:global(.permission-actions) {
		display: flex;
		gap: 0.75rem;
		justify-content: flex-end;
	}

	:global(.btn) {
		padding: 0.625rem 1.25rem;
		font-size: 0.875rem;
		font-weight: 600;
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
		transform: translateY(-1px);
	}

	:global(.btn-approve-once) {
		background: #f59e0b;
		color: white;
	}

	:global(.btn-approve-once:hover) {
		background: #d97706;
		transform: translateY(-1px);
	}

	:global(.btn-approve-always) {
		background: #10b981;
		color: white;
	}

	:global(.btn-approve-always:hover) {
		background: #059669;
		transform: translateY(-1px);
	}

	@keyframes fadeIn {
		from {
			opacity: 0;
		}
		to {
			opacity: 1;
		}
	}

	@keyframes slideIn {
		from {
			opacity: 0;
			transform: translate(-50%, -48%);
		}
		to {
			opacity: 1;
			transform: translate(-50%, -50%);
		}
	}

	/* Child Snippet Variant Styles */
	:global(.custom-card-content) {
		position: fixed;
		top: 50%;
		left: 50%;
		transform: translate(-50%, -50%);
		background: white;
		border-radius: 16px;
		max-width: 500px;
		width: 90%;
		box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.1), 0 10px 10px -5px rgba(0, 0, 0, 0.04);
		animation: slideIn 0.3s ease-out;
		z-index: 9999;
		border: 2px solid #e5e7eb;
		overflow: hidden;
	}

	.card-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		padding: 1.5rem;
		background: linear-gradient(135deg, #f9fafb 0%, #f3f4f6 100%);
		border-bottom: 1px solid #e5e7eb;
	}

	.card-title {
		margin: 0;
		font-size: 1.25rem;
		font-weight: 700;
		color: #1f2937;
	}

	.badge {
		padding: 0.375rem 0.75rem;
		border-radius: 9999px;
		font-size: 0.75rem;
		font-weight: 600;
		text-transform: uppercase;
	}

	.badge-pending {
		background: #fef3c7;
		color: #92400e;
	}

	.badge-processing {
		background: #dbeafe;
		color: #1e40af;
	}

	.card-body {
		padding: 1.5rem;
		display: flex;
		flex-direction: column;
		gap: 1rem;
	}

	.action-info {
		display: flex;
		align-items: center;
		gap: 0.75rem;
	}

	.action-label {
		font-size: 0.875rem;
		font-weight: 600;
		color: #6b7280;
	}

	.action-value {
		padding: 0.25rem 0.5rem;
		background: #f3f4f6;
		border: 1px solid #e5e7eb;
		border-radius: 4px;
		font-family: 'Monaco', 'Courier New', monospace;
		font-size: 0.875rem;
		color: #1f2937;
	}

	.action-description {
		margin: 0;
		color: #4b5563;
		line-height: 1.5;
	}

	.action-args {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}

	.args-label {
		font-size: 0.875rem;
		font-weight: 600;
		color: #6b7280;
	}

	.args-value {
		margin: 0;
		padding: 0.75rem;
		background: #1e1e1e;
		color: #10b981;
		border-radius: 6px;
		font-family: 'Monaco', 'Courier New', monospace;
		font-size: 0.75rem;
		overflow-x: auto;
	}

	.card-footer {
		display: flex;
		justify-content: flex-end;
		gap: 0.75rem;
		padding: 1.5rem;
		background: #f9fafb;
		border-top: 1px solid #e5e7eb;
	}

	.btn-secondary,
	.btn-primary {
		padding: 0.625rem 1.25rem;
		font-size: 0.875rem;
		font-weight: 600;
		border-radius: 6px;
		border: none;
		cursor: pointer;
		transition: all 0.2s;
	}

	.btn-secondary {
		background: white;
		color: #374151;
		border: 1px solid #d1d5db;
	}

	.btn-secondary:hover {
		background: #f3f4f6;
		border-color: #9ca3af;
	}

	.btn-primary {
		background: linear-gradient(135deg, #10b981 0%, #059669 100%);
		color: white;
	}

	.btn-primary:hover {
		opacity: 0.9;
		transform: translateY(-1px);
	}
</style>
