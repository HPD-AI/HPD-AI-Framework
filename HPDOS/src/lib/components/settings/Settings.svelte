<script lang="ts">
	import Providers from '../providers/Providers.svelte';

	type SettingsSection = 'providers';
	let activeSection = $state<SettingsSection>('providers');

	const sections: { id: SettingsSection; label: string }[] = [
		{ id: 'providers', label: 'Providers' },
	];
</script>

<div class="settings-root">
	<div class="settings-nav">
		<h2 class="settings-nav-heading">Settings</h2>
		{#each sections as s (s.id)}
			<button
				class="settings-nav-item"
				class:active={activeSection === s.id}
				onclick={() => activeSection = s.id}
			>
				{s.label}
			</button>
		{/each}
	</div>

	<div class="settings-content">
		{#if activeSection === 'providers'}
			<Providers />
		{/if}
	</div>
</div>

<style>
	.settings-root {
		display: flex;
		height: 100%;
		overflow: hidden;
	}

	.settings-nav {
		width: 180px;
		flex: none;
		border-right: 1px solid rgb(255 255 255 / 0.07);
		padding: 1.25rem 0;
		overflow-y: auto;
	}

	.settings-nav-heading {
		font-size: 0.7rem;
		font-weight: 600;
		letter-spacing: 0.08em;
		text-transform: uppercase;
		color: rgb(var(--color-text-tertiary));
		padding: 0 1rem 0.75rem;
		margin: 0;
	}

	.settings-nav-item {
		display: block;
		width: 100%;
		padding: 0.45rem 1rem;
		background: none;
		border: none;
		text-align: left;
		font-size: 0.875rem;
		color: rgb(var(--color-text-secondary));
		cursor: pointer;
		transition: background 0.1s, color 0.1s;
		border-radius: 0;
	}
	.settings-nav-item:hover { background: rgb(255 255 255 / 0.04); color: rgb(var(--color-text-primary)); }
	.settings-nav-item.active { background: rgb(255 255 255 / 0.07); color: rgb(var(--color-text-primary)); font-weight: 500; }

	.settings-content {
		flex: 1;
		min-width: 0;
		overflow: hidden;
		display: flex;
		flex-direction: column;
	}
</style>
