<script lang="ts">
	import { SessionList } from '@hpd/hpd-agent-headless-ui';
	import type { Workspace } from '@hpd/hpd-agent-headless-ui';

	interface Props {
		workspace: Workspace;
		onNavigate?: () => void;
	}

	let { workspace, onNavigate }: Props = $props();

	// Agent selector state
	let agentSelectorOpen = $state(false);

	const sessions = $derived(workspace.sessions);
	let activeSessionId = $state(workspace.activeSessionId);
	$effect(() => { activeSessionId = workspace.activeSessionId; });
	// Only show loading spinner while sessions haven't loaded yet.
	// workspace.loading also covers branch loading — don't hide the list for that.
	const loading = $derived(workspace.loading && sessions.length === 0);
	const agents = $derived(workspace.agents);
	const activeAgentId = $derived(workspace.activeAgentId);
	const activeAgentName = $derived(
		agents.find((a) => a.id === activeAgentId)?.name ?? 'Default'
	);
</script>

<div class="sidebar-root">

	<!-- ===== AGENT SELECTOR ===== -->
	<div class="sidebar-section sidebar-agent">
		<button
			class="sidebar-agent-btn"
			onclick={() => (agentSelectorOpen = !agentSelectorOpen)}
			aria-expanded={agentSelectorOpen}
			aria-haspopup="listbox"
		>
			<span class="sidebar-agent-label">Agent</span>
			<span class="sidebar-agent-name">{activeAgentName}</span>
			<span class="sidebar-agent-chevron" data-open={agentSelectorOpen}>›</span>
		</button>

		{#if agentSelectorOpen && agents.length > 0}
			<ul class="sidebar-agent-list" role="listbox" aria-label="Select agent">
				<li>
					<button
						class="sidebar-agent-option"
						class:sidebar-agent-option--active={activeAgentId === null}
						role="option"
						aria-selected={activeAgentId === null}
						onclick={() => { workspace.selectAgent(null); agentSelectorOpen = false; }}
					>
						Default
					</button>
				</li>
				{#each agents as agent (agent.id)}
					<li>
						<button
							class="sidebar-agent-option"
							class:sidebar-agent-option--active={activeAgentId === agent.id}
							role="option"
							aria-selected={activeAgentId === agent.id}
							onclick={() => { workspace.selectAgent(agent.id); agentSelectorOpen = false; }}
						>
							{agent.name}
						</button>
					</li>
				{/each}
			</ul>
		{/if}
	</div>

<!-- ===== SESSION LIST ===== -->
	<div class="sidebar-section sidebar-sessions">
		<div class="sidebar-sessions-header">
			<span class="sidebar-section-label">Conversations</span>
			<button
				class="sidebar-new-btn"
				onclick={() => { workspace.createSession(); onNavigate?.(); }}
				aria-label="New conversation"
				title="New conversation"
			>+</button>
		</div>

<div class="sidebar-session-list">
			{#if loading}
				<div class="sidebar-loading"><div class="sidebar-spinner"></div></div>
			{:else if sessions.length === 0}
				<p class="sidebar-empty">No conversations yet.</p>
			{:else}
				{#each sessions as session (session.id)}
					<div
						class="sidebar-session-item-btn"
						class:active={session.id === activeSessionId}
						role="option"
						aria-selected={session.id === activeSessionId}
						tabindex="0"
						onclick={() => { workspace.selectSession(session.id); onNavigate?.(); }}
						onkeydown={(e) => { if (e.key === 'Enter' || e.key === ' ') { workspace.selectSession(session.id); onNavigate?.(); } }}
					>
						<div class="sidebar-session-item">
							<span class="sidebar-session-title">
								{session.metadata?.title ?? session.id.slice(0, 8)}
							</span>
							<button
								class="sidebar-session-delete"
								onclick={(e) => { e.stopPropagation(); workspace.deleteSession(session.id); }}
								aria-label="Delete"
								title="Delete"
							>✕</button>
						</div>
					</div>
				{/each}
			{/if}
		</div>
	</div>

</div>

<style>
	.sidebar-root {
		display: flex;
		flex-direction: column;
		height: 100%;
		overflow: hidden;
		gap: 0;
	}

	/* ===== Section base ===== */
	.sidebar-section {
		flex: none;
		padding: 0.5rem;
	}

	.sidebar-section-label {
		font-size: 0.6875rem;
		font-weight: 600;
		text-transform: uppercase;
		letter-spacing: 0.06em;
		color: rgb(var(--color-text-quaternary, var(--color-text-tertiary)));
	}

	/* ===== Agent selector ===== */
	.sidebar-agent {
		border-bottom: 1px solid rgb(255 255 255 / 0.06);
	}

	.sidebar-agent-btn {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		width: 100%;
		background: transparent;
		border: none;
		padding: 0.375rem 0.5rem;
		border-radius: 8px;
		cursor: pointer;
		color: rgb(var(--color-text-primary));
		transition: background 0.15s;
	}

	.sidebar-agent-btn:hover {
		background: rgb(255 255 255 / 0.05);
	}

	.sidebar-agent-label {
		font-size: 0.6875rem;
		font-weight: 600;
		text-transform: uppercase;
		letter-spacing: 0.06em;
		color: rgb(var(--color-text-tertiary));
		flex: none;
	}

	.sidebar-agent-name {
		flex: 1;
		font-size: 0.8125rem;
		color: rgb(var(--color-text-primary));
		text-align: left;
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	.sidebar-agent-chevron {
		flex: none;
		font-size: 0.875rem;
		color: rgb(var(--color-text-tertiary));
		transition: transform 0.15s;
	}

	.sidebar-agent-chevron[data-open="true"] {
		transform: rotate(90deg);
	}

	.sidebar-agent-list {
		list-style: none;
		margin: 0.25rem 0 0;
		padding: 0;
		background: rgb(var(--color-bg-active) / 0.8);
		border: 1px solid rgb(255 255 255 / 0.07);
		border-radius: 8px;
		overflow: hidden;
	}

	.sidebar-agent-option {
		display: block;
		width: 100%;
		padding: 0.5rem 0.75rem;
		background: transparent;
		border: none;
		cursor: pointer;
		font-size: 0.8125rem;
		color: rgb(var(--color-text-secondary));
		text-align: left;
		transition: background 0.1s;
	}

	.sidebar-agent-option:hover {
		background: rgb(255 255 255 / 0.06);
	}

	.sidebar-agent-option--active {
		color: rgb(var(--color-accent-light, var(--color-accent-primary)));
		font-weight: 500;
	}

/* ===== Session list ===== */
	.sidebar-sessions {
		flex: 1;
		display: flex;
		flex-direction: column;
		min-height: 0;
		overflow: hidden;
		padding: 0;
	}

	.sidebar-sessions-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 0.5rem 0.75rem 0.25rem;
		flex: none;
	}

	.sidebar-new-btn {
		width: 28px;
		height: 28px;
		display: flex;
		align-items: center;
		justify-content: center;
		background: transparent;
		border: 1px solid rgb(255 255 255 / 0.08);
		border-radius: 6px;
		cursor: pointer;
		color: rgb(var(--color-text-secondary));
		font-size: 1.125rem;
		line-height: 1;
		transition: all 0.15s;
		padding: 0;
	}

	.sidebar-new-btn:hover {
		background: rgb(var(--color-accent-primary) / 0.1);
		border-color: rgb(var(--color-accent-primary) / 0.4);
		color: rgb(var(--color-accent-light, var(--color-accent-primary)));
	}

	:global(.sidebar-session-list) {
		flex: 1;
		overflow-y: auto;
		padding: 0.25rem 0.5rem 0.5rem;
		display: flex;
		flex-direction: column;
		gap: 2px;
	}

	/* ===== Session item ===== */
	.sidebar-session-item-btn {
		display: block;
		width: 100%;
		background: transparent;
		border: none;
		padding: 0;
		cursor: pointer;
		border-radius: 8px;
		text-align: left;
	}
	.sidebar-session-item-btn:hover .sidebar-session-item {
		background: rgb(255 255 255 / 0.05);
	}
	.sidebar-session-item-btn.active .sidebar-session-item {
		background: rgb(var(--color-accent-primary) / 0.12);
	}
	.sidebar-session-item-btn.active .sidebar-session-title {
		color: rgb(var(--color-accent-light, var(--color-accent-primary)));
	}

	:global([data-session-list-item]) {
		display: block;
		width: 100%;
		background: transparent;
		border: none;
		padding: 0;
		cursor: pointer;
		border-radius: 8px;
		text-align: left;
		transition: background 0.1s;
	}

	:global([data-session-list-item]:hover) .sidebar-session-item {
		background: rgb(255 255 255 / 0.05);
	}

	:global([data-session-list-item][data-active]) .sidebar-session-item {
		background: rgb(var(--color-accent-primary) / 0.12);
	}

	.sidebar-session-item {
		display: flex;
		align-items: center;
		gap: 0.375rem;
		padding: 0.5rem 0.625rem;
		border-radius: 8px;
		transition: background 0.1s;
	}

	:global([data-session-list-item][data-active]) .sidebar-session-title {
		color: rgb(var(--color-accent-light, var(--color-accent-primary)));
	}

.sidebar-session-title {
		flex: 1;
		font-size: 0.8125rem;
		color: rgb(var(--color-text-primary));
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	.sidebar-session-time {
		flex: none;
		font-size: 0.6875rem;
		color: rgb(var(--color-text-tertiary));
	}

	.sidebar-session-delete {
		flex: none;
		width: 20px;
		height: 20px;
		display: none;
		align-items: center;
		justify-content: center;
		background: transparent;
		border: none;
		border-radius: 4px;
		cursor: pointer;
		color: rgb(var(--color-text-tertiary));
		font-size: 0.625rem;
		transition: all 0.1s;
		padding: 0;
	}

	.sidebar-session-item:hover .sidebar-session-delete {
		display: flex;
	}

	.sidebar-session-delete:hover {
		background: rgb(var(--color-error, 239 68 68) / 0.15);
		color: rgb(var(--color-error, 239 68 68));
	}

	/* ===== Loading / empty ===== */
	.sidebar-loading {
		display: flex;
		justify-content: center;
		padding: 1.5rem;
	}

	.sidebar-spinner {
		width: 20px;
		height: 20px;
		border: 2px solid rgb(255 255 255 / 0.1);
		border-top-color: rgb(var(--color-accent-primary));
		border-radius: 50%;
		animation: sidebar-spin 0.75s linear infinite;
	}

	@keyframes sidebar-spin {
		to { transform: rotate(360deg); }
	}

	:global([data-session-list-empty]) {
		padding: 1rem 0.75rem;
	}

	.sidebar-empty {
		font-size: 0.8125rem;
		color: rgb(var(--color-text-tertiary));
		margin: 0;
		text-align: center;
	}
</style>
