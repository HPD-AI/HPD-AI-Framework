<script lang="ts">
	import { MessageActions } from '@hpd/hpd-agent-headless-ui';
	import type { Workspace, MessageRole } from '@hpd/hpd-agent-headless-ui';

	interface Props {
		workspace: Workspace;
		messageIndex: number;
		role: MessageRole;
		content: string;
	}

	let { workspace, messageIndex, role, content }: Props = $props();

	// Inline edit state (user messages only)
	let editing = $state(false);
	let draft = $state('');

	function startEdit() {
		draft = content;
		editing = true;
	}

	function cancelEdit() {
		editing = false;
		draft = '';
	}
</script>

<MessageActions.Root
	{workspace}
	{messageIndex}
	{role}
	branch={workspace.activeBranch}
>
	{#snippet child({ props, pending, hasSiblings })}
		<div {...props} class="ma-root">

			{#if editing}
				<!-- Inline edit UI (user only) -->
				<div class="ma-edit-area">
					<textarea
						class="ma-edit-textarea"
						bind:value={draft}
						rows={3}
						onkeydown={e => { if (e.key === 'Escape') cancelEdit(); }}
					></textarea>
					<div class="ma-edit-actions">
						<MessageActions.EditButton onSuccess={() => { editing = false; draft = ''; }}>
							{#snippet child({ props: btnProps, edit, status })}
								<button
									{...btnProps}
									class="ma-btn ma-btn-primary"
									disabled={status === 'pending' || !draft.trim()}
									onclick={() => edit(draft)}
								>
									{status === 'pending' ? 'Saving…' : 'Save'}
								</button>
							{/snippet}
						</MessageActions.EditButton>
						<button class="ma-btn" onclick={cancelEdit} disabled={pending}>Cancel</button>
					</div>
				</div>
			{:else}
				<!-- Action toolbar -->
				<div class="ma-bar">

					<!-- Copy (all roles) -->
					<MessageActions.CopyButton {content}>
						{#snippet child({ props: btnProps, copied, copy })}
							<button
								{...btnProps}
								class="ma-icon-btn"
								onclick={copy}
								title={copied ? 'Copied!' : 'Copy'}
							>
								{#if copied}
									<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
										<path d="M20 6L9 17l-5-5"/>
									</svg>
								{:else}
									<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
										<rect x="9" y="9" width="13" height="13" rx="2" ry="2"/>
										<path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/>
									</svg>
								{/if}
							</button>
						{/snippet}
					</MessageActions.CopyButton>

					{#if role === 'user'}
						<!-- Edit button — opens inline editor -->
						<button
							class="ma-icon-btn"
							onclick={startEdit}
							disabled={pending}
							title="Edit message"
							aria-label="Edit message"
						>
							<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
								<path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
								<path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
							</svg>
						</button>

						{#if hasSiblings}
							<!-- Branch switcher -->
							<div class="ma-switcher">
								<MessageActions.Prev>
									{#snippet child({ props: btnProps })}
										<button {...btnProps} class="ma-icon-btn" title="Previous version">
											<svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
												<path d="M15 18l-6-6 6-6"/>
											</svg>
										</button>
									{/snippet}
								</MessageActions.Prev>
								<MessageActions.Position>
									{#snippet child({ props: posProps, position })}
										<span {...posProps} class="ma-position">{position}</span>
									{/snippet}
								</MessageActions.Position>
								<MessageActions.Next>
									{#snippet child({ props: btnProps })}
										<button {...btnProps} class="ma-icon-btn" title="Next version">
											<svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
												<path d="M9 18l6-6-6-6"/>
											</svg>
										</button>
									{/snippet}
								</MessageActions.Next>
							</div>
						{/if}
					{/if}

					<!-- Retry (all roles) -->
					<MessageActions.RetryButton>
						{#snippet child({ props: btnProps, status, retry })}
							<button
								{...btnProps}
								class="ma-icon-btn"
								onclick={retry}
								title="Retry"
							>
								<svg
									width="13" height="13" viewBox="0 0 24 24" fill="none"
									stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"
									class:ma-spin={status === 'pending'}
								>
									<path d="M1 4v6h6"/>
									<path d="M3.51 15a9 9 0 1 0 .49-3.77"/>
								</svg>
							</button>
						{/snippet}
					</MessageActions.RetryButton>

				</div>
			{/if}

		</div>
	{/snippet}
</MessageActions.Root>

<style>
	.ma-root {
		position: relative;
	}

	.ma-switcher {
		display: flex;
		align-items: center;
		gap: 0;
		border: 1px solid rgb(255 255 255 / 0.1);
		border-radius: 6px;
		overflow: hidden;
	}

	.ma-position {
		font-size: 0.6875rem;
		color: rgb(var(--color-text-tertiary));
		padding: 0 0.25rem;
		min-width: 2.5rem;
		text-align: center;
		white-space: nowrap;
	}

	.ma-bar {
		display: flex;
		align-items: center;
		gap: 0.125rem;
		opacity: 0;
		transition: opacity 0.15s;
		padding: 0.25rem 0;
	}

	:global(.chat-message-row:hover) .ma-bar,
	:global(.chat-message-row:focus-within) .ma-bar,
	.ma-bar:has(.ma-switcher) {
		opacity: 1;
	}

	.ma-icon-btn {
		display: flex;
		align-items: center;
		justify-content: center;
		width: 26px;
		height: 26px;
		border-radius: 6px;
		border: none;
		background: transparent;
		color: rgb(var(--color-text-tertiary));
		cursor: pointer;
		transition: background 0.1s, color 0.1s;
		padding: 0;
	}

	.ma-icon-btn:hover:not(:disabled) {
		background: rgb(255 255 255 / 0.08);
		color: rgb(var(--color-text-secondary));
	}

	.ma-icon-btn:disabled {
		opacity: 0.35;
		cursor: default;
	}

	.ma-spin {
		animation: ma-spin 0.8s linear infinite;
	}

	@keyframes ma-spin {
		to { transform: rotate(360deg); }
	}

	/* Inline edit */
	.ma-edit-area {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
		width: 100%;
		margin-top: 0.375rem;
	}

	.ma-edit-textarea {
		width: 100%;
		background: rgb(255 255 255 / 0.06);
		border: 1px solid rgb(var(--color-accent-primary) / 0.4);
		border-radius: 8px;
		padding: 0.5rem 0.625rem;
		font-size: 0.875rem;
		color: rgb(var(--color-text-primary));
		font-family: inherit;
		line-height: 1.5;
		resize: vertical;
		outline: none;
		box-sizing: border-box;
	}

	.ma-edit-textarea:focus {
		border-color: rgb(var(--color-accent-primary) / 0.7);
	}

	.ma-edit-actions {
		display: flex;
		gap: 0.375rem;
	}

	.ma-btn {
		padding: 0.3rem 0.75rem;
		border-radius: 6px;
		border: 1px solid rgb(255 255 255 / 0.12);
		background: transparent;
		color: rgb(var(--color-text-secondary));
		font-size: 0.8125rem;
		cursor: pointer;
		transition: background 0.1s;
	}

	.ma-btn:hover:not(:disabled) {
		background: rgb(255 255 255 / 0.06);
	}

	.ma-btn:disabled {
		opacity: 0.4;
		cursor: default;
	}

	.ma-btn-primary {
		background: rgb(var(--color-accent-primary) / 0.15);
		border-color: rgb(var(--color-accent-primary) / 0.4);
		color: rgb(var(--color-accent-primary));
	}

	.ma-btn-primary:hover:not(:disabled) {
		background: rgb(var(--color-accent-primary) / 0.25);
	}
</style>
