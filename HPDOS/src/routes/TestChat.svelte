<script lang="ts">
	import Chat from '../lib/components/chat/Chat.svelte';
	import Sidebar from '../lib/components/sidebar/Sidebar.svelte';
	import Providers from '../lib/components/providers/Providers.svelte';
	import { workspace } from '../lib/workspace.svelte';

	let view = $state<'chat' | 'providers'>('chat');
</script>

<div class="layout">
	<div class="sidebar">
		<Sidebar {workspace} />
	</div>
	<div class="main">
		{#if view === 'providers'}
			<div class="view-header">
				<button class="back-btn" onclick={() => view = 'chat'}>← Back</button>
				<span class="view-title">Providers</span>
			</div>
			<Providers />
		{:else}
			<Chat {workspace} />
		{/if}
	</div>
	<!-- Nav rail -->
	<div class="nav-rail">
		<button class="nav-btn" class:nav-active={view === 'chat'} onclick={() => view = 'chat'} title="Chat">
			<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round">
				<path d="M21 15a2 2 0 01-2 2H7l-4 4V5a2 2 0 012-2h14a2 2 0 012 2z"/>
			</svg>
		</button>
		<button class="nav-btn" class:nav-active={view === 'providers'} onclick={() => view = 'providers'} title="Providers">
			<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round">
				<circle cx="12" cy="12" r="3"/>
				<path d="M19.4 15a1.65 1.65 0 00.33 1.82l.06.06a2 2 0 010 2.83 2 2 0 01-2.83 0l-.06-.06a1.65 1.65 0 00-1.82-.33 1.65 1.65 0 00-1 1.51V21a2 2 0 01-4 0v-.09A1.65 1.65 0 009 19.4a1.65 1.65 0 00-1.82.33l-.06.06a2 2 0 01-2.83-2.83l.06-.06A1.65 1.65 0 004.68 15a1.65 1.65 0 00-1.51-1H3a2 2 0 010-4h.09A1.65 1.65 0 004.6 9a1.65 1.65 0 00-.33-1.82l-.06-.06a2 2 0 012.83-2.83l.06.06A1.65 1.65 0 009 4.68a1.65 1.65 0 001-1.51V3a2 2 0 014 0v.09a1.65 1.65 0 001 1.51 1.65 1.65 0 001.82-.33l.06-.06a2 2 0 012.83 2.83l-.06.06A1.65 1.65 0 0019.4 9a1.65 1.65 0 001.51 1H21a2 2 0 010 4h-.09a1.65 1.65 0 00-1.51 1z"/>
			</svg>
		</button>
	</div>
</div>

<style>
	.layout {
		position: fixed;
		inset: 0;
		display: flex;
		background: #0f0f17;
		color: #e2e8f0;
		font-family: system-ui, sans-serif;
	}

	.sidebar {
		width: 240px;
		flex: none;
		overflow-y: auto;
		border-right: 1px solid rgba(255,255,255,0.08);
		background: #1c1c2a;
	}

	.main {
		flex: 1;
		min-width: 0;
		display: flex;
		flex-direction: column;
		overflow: hidden;
	}

	.view-header {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		padding: 0.75rem 1.25rem;
		border-bottom: 1px solid rgb(255 255 255 / 0.07);
		flex: none;
	}
	.back-btn {
		background: none;
		border: none;
		color: rgb(var(--color-text-tertiary, 148 163 184));
		font-size: 0.85rem;
		cursor: pointer;
		padding: 0.25rem 0;
	}
	.back-btn:hover { color: #e2e8f0; }
	.view-title {
		font-size: 0.9rem;
		font-weight: 600;
		color: #e2e8f0;
	}

	/* Nav rail */
	.nav-rail {
		width: 52px;
		flex: none;
		display: flex;
		flex-direction: column;
		align-items: center;
		padding: 0.75rem 0;
		gap: 0.25rem;
		border-left: 1px solid rgba(255,255,255,0.06);
		background: #14141f;
	}
	.nav-btn {
		width: 38px;
		height: 38px;
		display: flex;
		align-items: center;
		justify-content: center;
		border: none;
		border-radius: 10px;
		background: transparent;
		color: rgba(255,255,255,0.35);
		cursor: pointer;
		transition: background 0.15s, color 0.15s;
	}
	.nav-btn:hover { background: rgba(255,255,255,0.07); color: rgba(255,255,255,0.7); }
	.nav-btn.nav-active { background: rgba(255,255,255,0.1); color: #fff; }
</style>
