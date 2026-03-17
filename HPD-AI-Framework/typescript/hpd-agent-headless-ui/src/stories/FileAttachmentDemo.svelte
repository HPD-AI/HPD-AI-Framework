<script lang="ts">
	import { boxWith } from 'svelte-toolbelt';
	import { FileAttachment, FileAttachmentState } from '$lib/index.js';
	import type { AssetReference } from '$lib/index.js';

	type UploadMode = 'success' | 'error' | 'slow';

	function formatBytes(bytes: number): string {
		if (bytes < 1024) return `${bytes} B`;
		if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
		return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
	}

	let {
		disabled = false,
		uploadMode = 'success' as UploadMode,
		sessionId = 'demo-session',
		...restProps
	} = $props();

	// Simulated upload functions
	function makeUploadFn(mode: UploadMode) {
		return async (sid: string, file: File): Promise<AssetReference> => {
			if (mode === 'slow') {
				await new Promise((r) => setTimeout(r, 2000));
			}
			if (mode === 'error') {
				await new Promise((r) => setTimeout(r, 600));
				throw new Error('Upload failed: server returned 500');
			}
			await new Promise((r) => setTimeout(r, 400));
			return {
				assetId: `asset-${Date.now()}`,
				contentType: file.type || 'application/octet-stream',
				name: file.name,
				sizeBytes: file.size,
			};
		};
	}

	// Pre-constructed state — makes resolvedAssets readable outside the snippet
	const attachments = new FileAttachmentState({
		uploadFn: boxWith(() => makeUploadFn(uploadMode)),
		sessionId: boxWith(() => sessionId),
		disabled: boxWith(() => disabled),
	});

	let fileInput: HTMLInputElement | undefined = $state();
</script>

<div class="demo-container">
	<!-- Hidden real file input -->
	<input
		bind:this={fileInput}
		type="file"
		multiple
		style="display:none"
		onchange={(e) => {
			const files = e.currentTarget.files;
			if (files?.length) attachments.add(files);
			e.currentTarget.value = '';
		}}
	/>

	<FileAttachment.Root state={attachments}>
		{#snippet children(s: any)}
			<div class="attachment-zone" class:uploading={s.isUploading}>
				<!-- Drop zone / trigger -->
				<button
					class="trigger-btn"
					disabled={s.disabled}
					onclick={() => fileInput?.click()}
				>
					{#if s.isUploading}
						<span class="spinner"></span>
						Uploading…
					{:else}
						<span class="icon">📎</span>
						Attach files
					{/if}
				</button>

				<!-- Attachment list -->
				{#if s.hasAttachments}
					<ul class="attachment-list">
						{#each s.attachments as att}
							<li class="attachment-item" data-status={att.status}>
								<span class="att-icon">
									{att.status === 'done' ? '✓' : att.status === 'error' ? '✕' : '⏳'}
								</span>
								<span class="att-name">{att.file.name}</span>
								<span class="att-size">{formatBytes(att.file.size)}</span>
								{#if att.status === 'error'}
									<span class="att-error">{att.error}</span>
									<button class="att-action retry" onclick={() => s.retry(att.localId)}>
										Retry
									</button>
								{/if}
								<button class="att-action remove" onclick={() => s.remove(att.localId)}>
									Remove
								</button>
							</li>
						{/each}
					</ul>

					<div class="list-footer">
						<button class="clear-btn" onclick={() => s.clear()}>Clear all</button>
						<span class="submit-hint" class:blocked={!s.canSubmit}>
							{s.canSubmit ? 'Ready to send' : 'Fix errors before sending'}
						</span>
					</div>
				{/if}
			</div>
		{/snippet}
	</FileAttachment.Root>

	<!-- Live state output -->
	<div class="output-panel">
		<h4 class="output-title">resolvedAssets</h4>
		<pre class="output-code">{JSON.stringify(attachments.resolvedAssets, null, 2)}</pre>
		<div class="output-pills">
			<span class="pill" class:active={attachments.hasAttachments}>hasAttachments</span>
			<span class="pill" class:active={attachments.isUploading}>isUploading</span>
			<span class="pill" class:active={!attachments.canSubmit} class:danger={!attachments.canSubmit}>
				{attachments.canSubmit ? 'canSubmit ✓' : 'canSubmit ✗'}
			</span>
		</div>
	</div>
</div>

<style>
	.demo-container {
		display: flex;
		gap: 1.5rem;
		padding: 2rem;
		font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
		font-size: 0.9rem;
		max-width: 860px;
	}

	.attachment-zone {
		flex: 1;
		display: flex;
		flex-direction: column;
		gap: 1rem;
		background: #fff;
		border: 1px solid #e5e7eb;
		border-radius: 12px;
		padding: 1.25rem;
		min-width: 320px;
	}
	.attachment-zone.uploading {
		border-color: #4f46e5;
	}

	/* Trigger button */
	.trigger-btn {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		padding: 0.6rem 1.1rem;
		border: 2px dashed #d1d5db;
		border-radius: 8px;
		background: #f9fafb;
		cursor: pointer;
		font-size: 0.9rem;
		color: #374151;
		width: 100%;
		justify-content: center;
		transition: border-color 0.15s, background 0.15s;
	}
	.trigger-btn:hover:not(:disabled) {
		border-color: #4f46e5;
		background: #eef2ff;
		color: #4f46e5;
	}
	.trigger-btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}
	.icon {
		font-size: 1.1em;
	}

	/* Spinner */
	.spinner {
		display: inline-block;
		width: 1rem;
		height: 1rem;
		border: 2px solid #c7d2fe;
		border-top-color: #4f46e5;
		border-radius: 50%;
		animation: spin 0.8s linear infinite;
	}
	@keyframes spin {
		to { transform: rotate(360deg); }
	}

	/* Attachment list */
	.attachment-list {
		list-style: none;
		margin: 0;
		padding: 0;
		display: flex;
		flex-direction: column;
		gap: 0.4rem;
	}
	.attachment-item {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		padding: 0.45rem 0.65rem;
		border-radius: 6px;
		background: #f9fafb;
		border: 1px solid #e5e7eb;
		font-size: 0.85rem;
	}
	.attachment-item[data-status='done']     { border-color: #10b981; background: #f0fdf4; }
	.attachment-item[data-status='error']    { border-color: #ef4444; background: #fef2f2; flex-wrap: wrap; }
	.attachment-item[data-status='uploading']{ border-color: #6366f1; background: #eef2ff; }

	.att-icon { font-size: 0.85em; flex-shrink: 0; }
	.att-name { font-weight: 500; color: #111827; flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
	.att-size { font-size: 0.75rem; color: #9ca3af; flex-shrink: 0; }
	.att-error { font-size: 0.75rem; color: #dc2626; width: 100%; padding-left: 1.5rem; }

	.att-action {
		border: none;
		background: transparent;
		cursor: pointer;
		font-size: 0.78rem;
		padding: 0.15rem 0.4rem;
		border-radius: 4px;
		flex-shrink: 0;
	}
	.att-action.remove { color: #9ca3af; }
	.att-action.remove:hover { color: #ef4444; background: #fee2e2; }
	.att-action.retry  { color: #4f46e5; }
	.att-action.retry:hover { background: #eef2ff; }

	/* List footer */
	.list-footer {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding-top: 0.25rem;
	}
	.clear-btn {
		border: 1px solid #d1d5db;
		background: #f9fafb;
		border-radius: 5px;
		padding: 0.25rem 0.65rem;
		cursor: pointer;
		font-size: 0.8rem;
		color: #374151;
	}
	.clear-btn:hover { background: #fee2e2; border-color: #ef4444; color: #ef4444; }

	.submit-hint {
		font-size: 0.78rem;
		color: #10b981;
	}
	.submit-hint.blocked { color: #ef4444; }

	/* Output panel */
	.output-panel {
		width: 220px;
		flex-shrink: 0;
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}
	.output-title {
		margin: 0;
		font-size: 0.8rem;
		font-weight: 600;
		color: #6b7280;
		text-transform: uppercase;
		letter-spacing: 0.05em;
	}
	.output-code {
		flex: 1;
		background: #1e1e2e;
		color: #cdd6f4;
		border-radius: 8px;
		padding: 0.75rem;
		font-size: 0.72rem;
		line-height: 1.5;
		margin: 0;
		overflow: auto;
		white-space: pre-wrap;
		word-break: break-all;
		min-height: 6rem;
	}
	.output-pills {
		display: flex;
		flex-wrap: wrap;
		gap: 0.3rem;
	}
	.pill {
		font-size: 0.72rem;
		padding: 0.15rem 0.5rem;
		border-radius: 99px;
		background: #e5e7eb;
		color: #6b7280;
		transition: background 0.15s, color 0.15s;
	}
	.pill.active { background: #d1fae5; color: #065f46; }
	.pill.danger { background: #fee2e2; color: #991b1b; }
</style>
