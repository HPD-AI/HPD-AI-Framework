<script lang="ts">
	import type { ToolCall } from '@hpd/hpd-agent-headless-ui';
	import { ToolExecution } from '@hpd/hpd-agent-headless-ui';

	interface Props {
		toolCall: ToolCall;
	}

	let { toolCall }: Props = $props();

	let expanded = $state(false);

	// Collapse when done
	$effect(() => {
		if (toolCall.status === 'complete' || toolCall.status === 'error') {
			expanded = false;
		}
	});
</script>

<!--
	All sub-components use the `child` named snippet (not `children`) to avoid
	Bun's bug where parameterized `children` snippets render nothing.
-->
<ToolExecution.Root {toolCall} bind:expanded>
	{#snippet child(s)}
		<div {...s.props} class="dtc-root" data-status={toolCall.status}>

			<ToolExecution.Trigger>
				{#snippet child(t)}
					<button {...t.props} class="dtc-trigger">
						<ToolExecution.Status>
							{#snippet child(st)}
								<span {...st.props} class="dtc-icon" data-status={toolCall.status}>
									{#if s.isComplete}✓{:else if s.hasError}✕{:else}○{/if}
								</span>
							{/snippet}
						</ToolExecution.Status>

						<span class="dtc-name">{s.name}</span>

						{#if s.isActive}
							<span class="dtc-spinner" aria-hidden="true"></span>
						{/if}

						<svg
							class="dtc-chevron"
							class:dtc-chevron-open={s.expanded}
							width="10" height="10" viewBox="0 0 24 24"
							fill="none" stroke="currentColor" stroke-width="2.5"
						>
							<path d="M6 9l6 6 6-6"/>
						</svg>
					</button>
				{/snippet}
			</ToolExecution.Trigger>

			{#if s.expanded}
				<ToolExecution.Content>
					{#snippet child(c)}
						<div {...c.props} class="dtc-body">
							{#if s.hasArgs}
								<ToolExecution.Args>
									{#snippet child(a)}
										<div {...a.props}>
											<pre class="dtc-code">{JSON.stringify(s.args, null, 2)}</pre>
										</div>
									{/snippet}
								</ToolExecution.Args>
							{/if}

							<ToolExecution.Result>
								{#snippet child(r)}
									<div {...r.props}>
										{#if s.hasError && s.error}
											<pre class="dtc-code dtc-code-error">{s.error}</pre>
										{:else if s.hasResult && s.result}
											<pre class="dtc-code dtc-code-result">{s.result}</pre>
										{/if}
									</div>
								{/snippet}
							</ToolExecution.Result>
						</div>
					{/snippet}
				</ToolExecution.Content>
			{/if}

		</div>
	{/snippet}
</ToolExecution.Root>

<style>
	.dtc-root {
		border-radius: 8px;
		border: 1px solid rgb(255 255 255 / 0.08);
		background: rgb(255 255 255 / 0.03);
		overflow: hidden;
		margin: 0.25rem 0;
		font-size: 0.8125rem;
	}

	.dtc-root[data-active] {
		border-color: rgb(var(--color-accent-primary) / 0.3);
	}

	.dtc-root[data-error] {
		border-color: rgb(var(--color-error) / 0.4);
	}

	.dtc-trigger {
		display: flex;
		align-items: center;
		gap: 0.4rem;
		width: 100%;
		padding: 0.375rem 0.625rem;
		background: transparent;
		border: none;
		cursor: pointer;
		color: rgb(var(--color-text-secondary));
		text-align: left;
		transition: background 0.1s;
	}

	.dtc-trigger:hover {
		background: rgb(255 255 255 / 0.04);
	}

	.dtc-icon {
		font-size: 0.6875rem;
		flex: none;
		width: 12px;
		text-align: center;
	}

	.dtc-icon[data-status="complete"] { color: rgb(var(--color-success)); }
	.dtc-icon[data-status="error"] { color: rgb(var(--color-error)); }
	.dtc-icon[data-status="executing"],
	.dtc-icon[data-status="pending"] { color: rgb(var(--color-accent-primary)); }

	.dtc-name {
		flex: 1;
		font-family: ui-monospace, monospace;
		font-size: 0.75rem;
		color: rgb(var(--color-text-secondary));
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	.dtc-spinner {
		width: 10px;
		height: 10px;
		border: 1.5px solid rgb(var(--color-accent-primary) / 0.3);
		border-top-color: rgb(var(--color-accent-primary));
		border-radius: 50%;
		animation: dtc-spin 0.7s linear infinite;
		flex: none;
	}

	@keyframes dtc-spin {
		to { transform: rotate(360deg); }
	}

	.dtc-chevron {
		flex: none;
		color: rgb(var(--color-text-tertiary));
		transition: transform 0.15s;
	}

	.dtc-chevron-open {
		transform: rotate(180deg);
	}

	.dtc-body {
		border-top: 1px solid rgb(255 255 255 / 0.06);
	}

	.dtc-code {
		margin: 0;
		padding: 0.5rem 0.625rem;
		font-family: ui-monospace, monospace;
		font-size: 0.75rem;
		color: rgb(var(--color-text-secondary));
		white-space: pre-wrap;
		word-break: break-all;
		max-height: 200px;
		overflow-y: auto;
	}

	.dtc-code + .dtc-code {
		border-top: 1px solid rgb(255 255 255 / 0.06);
	}

	.dtc-code-result { color: rgb(var(--color-text-tertiary)); }
	.dtc-code-error { color: rgb(var(--color-error) / 0.8); }
</style>
