<script lang="ts">
	import type { AppRecorderState, ActivePage } from './AppRecorderState.svelte';

	let { editor }: { editor: AppRecorderState } = $props();

	interface Tab {
		id: ActivePage;
		label: string;
		icon: string;
		comingSoon?: boolean;
	}

	const tabs: Tab[] = [
		{ id: 'media',    label: 'Media',    icon: '⬛' },
		{ id: 'edit',     label: 'Edit',     icon: '✂' },
		{ id: 'annotate', label: 'Annotate', icon: '✏' },
		{ id: 'audio',    label: 'Audio',    icon: '♪',  comingSoon: true },
		{ id: 'color',    label: 'Color',    icon: '◑',  comingSoon: true },
		{ id: 'export',   label: 'Export',   icon: '⬆' },
	];
</script>

<nav class="page-nav" role="tablist" aria-label="Editor pages">
	{#each tabs as tab}
		<button
			role="tab"
			aria-selected={editor.activePage === tab.id}
			aria-disabled={tab.comingSoon}
			class="nav-tab"
			class:active={editor.activePage === tab.id}
			class:coming-soon={tab.comingSoon}
			title={tab.comingSoon ? `${tab.label} — Coming soon` : tab.label}
			onclick={() => { if (!tab.comingSoon) editor.setActivePage(tab.id); }}
		>
			<span class="nav-icon" aria-hidden="true">{tab.icon}</span>
			<span class="nav-label">{tab.label}</span>
		</button>
	{/each}
</nav>

<style>
	.page-nav {
		display: flex;
		align-items: center;
		justify-content: center;
		height: 44px;
		flex-shrink: 0;
		background: rgb(var(--color-bg-secondary));
		border-top: 1px solid rgb(var(--color-border-default));
		gap: 0;
	}

	.nav-tab {
		display: flex;
		align-items: center;
		gap: 0.375rem;
		padding: 0 1.25rem;
		height: 100%;
		background: none;
		border: none;
		border-bottom: 2px solid transparent;
		color: rgb(var(--color-text-secondary));
		font-size: var(--font-size-sm);
		font-weight: var(--font-weight-medium);
		cursor: pointer;
		transition: color var(--duration-fast), border-color var(--duration-fast);
		white-space: nowrap;
		user-select: none;
	}

	.nav-tab:hover:not(.coming-soon) {
		color: rgb(var(--color-text-primary));
	}

	.nav-tab.active {
		color: rgb(var(--color-accent-primary));
		border-bottom-color: rgb(var(--color-accent-primary));
	}

	.nav-tab.coming-soon {
		opacity: 0.35;
		cursor: not-allowed;
	}

	.nav-icon {
		font-size: 0.8rem;
		line-height: 1;
	}

	.nav-label {
		font-size: var(--font-size-sm);
	}
</style>
