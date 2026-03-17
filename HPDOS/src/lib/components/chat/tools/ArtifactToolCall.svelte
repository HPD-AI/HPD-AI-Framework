<script lang="ts">
	import type { ToolCall } from '@hpd/hpd-agent-headless-ui';
	interface Props {
		toolCall: ToolCall;
	}

	let { toolCall }: Props = $props();

	// Parse well-known args — args is optional on ToolCall
	const artifactId   = $derived((toolCall.args?.['id']       as string)  ?? '');
	const title        = $derived((toolCall.args?.['title']    as string)  ?? 'Artifact');
	const language     = $derived((toolCall.args?.['language'] as string)  ?? '');
	const artifactType = $derived((toolCall.args?.['type']     as string)  ?? 'code');
	const content      = $derived((toolCall.args?.['content']  as string)  ?? '');

	const isReady  = $derived(toolCall.status === 'complete' && !!artifactId);
	const isActive = $derived(toolCall.status === 'executing' || toolCall.status === 'pending');
	const hasError = $derived(toolCall.status === 'error');

	// Open by default when the card first appears; user can toggle after
	let open = $state(true);
	let iframeKey = $state(0);

	function refreshIframe() {
		iframeKey++;
	}
</script>

{#if isReady}
	<div class="atc-root" class:atc-open={open}>

		<div class="atc-header">
			<button class="atc-trigger" onclick={() => open = !open} aria-expanded={open}>
				<span class="atc-trigger-icon" aria-hidden="true">
					{#if artifactType === 'html'}🌐{:else}📄{/if}
				</span>
				<span class="atc-trigger-title">{title}</span>
				{#if language}
					<span class="atc-badge">{language}</span>
				{/if}
				<svg
					class="atc-chevron"
					class:atc-chevron-open={open}
					width="10" height="10" viewBox="0 0 24 24"
					fill="none" stroke="currentColor" stroke-width="2.5"
				>
					<path d="M6 9l6 6 6-6"/>
				</svg>
			</button>
			{#if artifactType === 'html'}
				<button
					class="atc-refresh"
					onclick={refreshIframe}
					title="Refresh preview"
					aria-label="Refresh preview"
				>
					<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
						<path d="M1 4v6h6"/><path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10"/>
					</svg>
				</button>
			{/if}
		</div>

		{#if open}
			<div class="atc-content">
				{#if artifactType === 'html'}
					{#key iframeKey}
						<iframe
							class="atc-iframe"
							srcdoc={content}
							sandbox="allow-scripts"
							title={title}
						></iframe>
					{/key}
				{:else}
					<pre class="atc-pre"><code class="language-{language}">{content}</code></pre>
				{/if}
			</div>
		{/if}

	</div>

{:else}
	<div class="atc-status" class:atc-status-error={hasError}>
		{#if isActive}
			<span class="atc-spinner" aria-hidden="true"></span>
			<span class="atc-status-label">Creating artifact…</span>
		{:else if hasError}
			<span class="atc-status-icon">✕</span>
			<span class="atc-status-label">Artifact failed</span>
		{/if}
	</div>
{/if}

<style>
	.atc-root {
		border-radius: 12px;
		border: 1px solid rgb(255 255 255 / 0.08);
		background: rgb(255 255 255 / 0.03);
		overflow: hidden;
		margin: 0.5rem 0;
		width: 100%;
		align-self: stretch;
	}

	.atc-open {
		border-color: rgb(var(--color-accent-primary) / 0.25);
	}

	.atc-header {
		display: flex;
		align-items: center;
	}

	.atc-trigger {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		flex: 1;
		min-width: 0;
		padding: 0.625rem 0.875rem;
		background: transparent;
		border: none;
		cursor: pointer;
		color: rgb(var(--color-text-secondary));
		text-align: left;
		transition: background 0.12s;
	}
	.atc-trigger:hover {
		background: rgb(255 255 255 / 0.04);
	}

	.atc-trigger-icon { font-size: 0.875rem; flex: none; }

	.atc-trigger-title {
		flex: 1;
		font-size: 0.8125rem;
		font-weight: 600;
		color: rgb(var(--color-text-primary));
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	.atc-badge {
		font-size: 0.7rem;
		padding: 0.1rem 0.4rem;
		border-radius: 4px;
		background: rgb(255 255 255 / 0.07);
		color: rgb(var(--color-text-tertiary));
		flex: none;
	}

	.atc-refresh {
		display: flex;
		align-items: center;
		justify-content: center;
		width: 28px;
		height: 28px;
		border-radius: 6px;
		border: none;
		background: rgb(255 255 255 / 0.05);
		color: rgb(var(--color-text-tertiary));
		cursor: pointer;
		flex: none;
		margin-right: 0.5rem;
		transition: background 0.12s, color 0.12s;
	}
	.atc-refresh:hover {
		background: rgb(255 255 255 / 0.12);
		color: rgb(var(--color-text-primary));
	}

	.atc-chevron {
		flex: none;
		color: rgb(var(--color-text-tertiary));
		transition: transform 0.15s;
	}
	.atc-chevron-open { transform: rotate(180deg); }

	.atc-content {
		border-top: 1px solid rgb(255 255 255 / 0.06);
		overflow: hidden;
	}

	.atc-pre {
		margin: 0;
		padding: 1rem;
		font-family: 'JetBrains Mono', 'Fira Code', ui-monospace, monospace;
		font-size: 0.8125rem;
		line-height: 1.6;
		color: rgb(var(--color-text-primary));
		white-space: pre;
		tab-size: 2;
		overflow: auto;
		max-height: 24rem;
	}

	.atc-iframe {
		width: 100%;
		height: 400px;
		border: none;
		background: #fff;
		display: block;
	}

	.atc-status {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		padding: 0.375rem 0.625rem;
		font-size: 0.8rem;
		color: rgb(var(--color-text-tertiary));
		border-radius: 8px;
		border: 1px solid rgb(255 255 255 / 0.06);
		margin: 0.25rem 0;
	}
	.atc-status-error {
		border-color: rgb(var(--color-error) / 0.3);
		color: rgb(var(--color-error) / 0.8);
	}
	.atc-status-label { font-size: 0.75rem; }
	.atc-status-icon { font-size: 0.7rem; }

	.atc-spinner {
		width: 10px;
		height: 10px;
		border: 1.5px solid rgb(var(--color-accent-primary) / 0.3);
		border-top-color: rgb(var(--color-accent-primary));
		border-radius: 50%;
		animation: atc-spin 0.7s linear infinite;
		flex: none;
	}
	@keyframes atc-spin { to { transform: rotate(360deg); } }
</style>
