<script lang="ts">
	import { createMockAgent } from '$lib/testing/mock-agent.svelte.js';
	import * as PermissionDialog from '../index.js';
	import type { PermissionRequest } from '$lib/agent/types.js';

	// Create mock agent
	const agent = createMockAgent();

	// Trigger a test permission request
	function triggerPermission() {
		const request: PermissionRequest = {
			permissionId: `perm-${Date.now()}`,
			sourceName: 'api',
			functionName: 'makePayment',
			description: 'Process payment of $1,234.56',
			callId: `call-${Date.now()}`,
			arguments: {
				amount: 1234.56,
				currency: 'USD',
				recipient: 'vendor@example.com'
			}
		};

		agent.state!.onPermissionRequest(request);
	}
</script>

<div class="demo-container">
	<div class="demo-controls">
		<button onclick={triggerPermission} class="trigger-btn">
			💳 Trigger Payment Permission
		</button>
		<p class="hint-text">
			This demo uses a custom render function  
		</p>
	</div>

	<!-- Custom Render Function Pattern   -->
	<PermissionDialog.Root {agent}>
		{#snippet render({ request, status, approve, deny })}
			{#if request}
				<div class="custom-overlay"></div>
				<div class="custom-dialog">
					<!-- Custom UI with complete control -->
					<div class="dialog-header">
						<div class="icon">💳</div>
						<h2>Payment Authorization</h2>
						<div class="status-indicator" class:active={status === 'executing'} class:processing={status === 'complete'}>
							{status === 'inProgress' ? 'Idle' : status === 'executing' ? 'Active' : 'Processing'}
						</div>
					</div>

					<div class="dialog-body">
						<div class="amount-display">
							<span class="currency">{request.arguments?.currency || 'USD'}</span>
							<span class="amount">${request.arguments?.amount || '0.00'}</span>
						</div>

						<div class="recipient-info">
							<span>To:</span>
							<span>{request.arguments?.recipient || 'Unknown'}</span>
						</div>

						{#if request.description}
							<div class="description-box">
								{request.description}
							</div>
						{/if}

						<div class="security-notice">
							<svg width="16" height="16" fill="none" stroke="currentColor" viewBox="0 0 24 24">
								<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z"></path>
							</svg>
							This action requires your approval
						</div>
					</div>

					<div class="dialog-actions">
						<button
							class="btn btn-cancel"
							onclick={() => deny('User cancelled payment')}
							disabled={status === 'complete'}
						>
							Cancel
						</button>
						<button
							class="btn btn-authorize"
							onclick={() => approve('ask')}
							disabled={status === 'complete'}
						>
							{#if status === 'complete'}
								<span class="spinner"></span> Processing...
							{:else}
								Authorize Payment
							{/if}
						</button>
					</div>
				</div>
			{/if}
		{/snippet}
	</PermissionDialog.Root>
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

	.trigger-btn {
		padding: 0.75rem 1.5rem;
		font-size: 1rem;
		background: linear-gradient(135deg, #8b5cf6 0%, #7c3aed 100%);
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

	.hint-text {
		margin-top: 1rem;
		font-size: 0.875rem;
		color: #6b7280;
	}

	/* Custom Dialog Styles */
	.custom-overlay {
		position: fixed;
		inset: 0;
		background: rgba(0, 0, 0, 0.6);
		backdrop-filter: blur(4px);
		animation: fadeIn 0.2s ease-out;
	}

	.custom-dialog {
		position: fixed;
		top: 50%;
		left: 50%;
		transform: translate(-50%, -50%);
		background: white;
		border-radius: 16px;
		max-width: 450px;
		width: 90%;
		box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.25);
		animation: slideUp 0.3s cubic-bezier(0.16, 1, 0.3, 1);
		overflow: hidden;
	}

	.dialog-header {
		background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
		color: white;
		padding: 1.5rem;
		text-align: center;
		position: relative;
	}

	.dialog-header .icon {
		font-size: 2.5rem;
		margin-bottom: 0.5rem;
	}

	.dialog-header h2 {
		font-size: 1.25rem;
		font-weight: 700;
		margin: 0;
	}

	.status-indicator {
		position: absolute;
		top: 1rem;
		right: 1rem;
		padding: 0.375rem 0.75rem;
		border-radius: 9999px;
		font-size: 0.75rem;
		font-weight: 600;
		background: rgba(255, 255, 255, 0.2);
		backdrop-filter: blur(8px);
	}

	.status-indicator.active {
		background: #10b981;
	}

	.status-indicator.processing {
		background: #3b82f6;
	}

	.dialog-body {
		padding: 2rem;
		display: flex;
		flex-direction: column;
		gap: 1.5rem;
	}

	.amount-display {
		text-align: center;
		padding: 1.5rem;
		background: linear-gradient(135deg, #f5f7fa 0%, #c3cfe2 100%);
		border-radius: 12px;
	}

	.currency {
		display: block;
		font-size: 0.875rem;
		color: #6b7280;
		font-weight: 600;
	}

	.amount {
		display: block;
		font-size: 2.5rem;
		font-weight: 700;
		color: #1f2937;
		margin-top: 0.25rem;
	}

	.recipient-info {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		padding: 0.75rem;
		background: #f9fafb;
		border-radius: 8px;
	}

	.recipient-info span:first-child {
		font-weight: 600;
		color: #6b7280;
		font-size: 0.875rem;
	}

	.recipient-info span {
		font-family: 'Monaco', 'Courier New', monospace;
		color: #1f2937;
		font-size: 0.875rem;
	}

	.description-box {
		padding: 1rem;
		background: #fffbeb;
		border-left: 4px solid #f59e0b;
		border-radius: 4px;
		color: #92400e;
		font-size: 0.875rem;
	}

	.security-notice {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		padding: 0.75rem;
		background: #dbeafe;
		border-radius: 6px;
		color: #1e40af;
		font-size: 0.875rem;
	}

	.dialog-actions {
		display: flex;
		gap: 1rem;
		padding: 0 2rem 2rem;
	}

	.btn {
		flex: 1;
		padding: 0.875rem;
		font-size: 1rem;
		font-weight: 600;
		border-radius: 8px;
		border: none;
		cursor: pointer;
		transition: all 0.2s;
		display: flex;
		align-items: center;
		justify-content: center;
		gap: 0.5rem;
	}

	.btn:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}

	.btn-cancel {
		background: #f3f4f6;
		color: #374151;
	}

	.btn-cancel:hover:not(:disabled) {
		background: #e5e7eb;
	}

	.btn-authorize {
		background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
		color: white;
	}

	.btn-authorize:hover:not(:disabled) {
		transform: translateY(-1px);
		box-shadow: 0 10px 20px -10px rgba(102, 126, 234, 0.6);
	}

	.spinner {
		display: inline-block;
		width: 14px;
		height: 14px;
		border: 2px solid rgba(255, 255, 255, 0.3);
		border-top-color: white;
		border-radius: 50%;
		animation: spin 0.6s linear infinite;
	}

	@keyframes fadeIn {
		from { opacity: 0; }
		to { opacity: 1; }
	}

	@keyframes slideUp {
		from {
			opacity: 0;
			transform: translate(-50%, -45%);
		}
		to {
			opacity: 1;
			transform: translate(-50%, -50%);
		}
	}

	@keyframes spin {
		to { transform: rotate(360deg); }
	}
</style>
