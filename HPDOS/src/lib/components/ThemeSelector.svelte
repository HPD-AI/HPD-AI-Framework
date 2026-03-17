<script lang="ts">
	import { setColorScheme, getColorScheme, type ColorScheme } from '../utils/theme';

	const themes: Array<{ value: ColorScheme; label: string }> = [
		{ value: 'auto',         label: 'Auto'         },
		{ value: 'dark-teal',    label: 'Dark Teal'    },
		{ value: 'light-blue',   label: 'Light Blue'   },
		{ value: 'dark-purple',  label: 'Dark Purple'  },
		{ value: 'light-purple', label: 'Light Purple' },
	];

	let currentTheme = $state<ColorScheme>(getColorScheme());
	let open = $state(false);

	function select(theme: ColorScheme) {
		setColorScheme(theme);
		currentTheme = theme;
		open = false;
	}

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape') open = false;
	}
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div class="ts-root" onkeydown={handleKeydown}>
	<button class="ts-trigger" onclick={() => open = !open} title="Settings" aria-label="Settings" aria-expanded={open}>
		<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
			<line x1="3" y1="6" x2="21" y2="6"/>
			<line x1="3" y1="12" x2="21" y2="12"/>
			<line x1="3" y1="18" x2="21" y2="18"/>
		</svg>
	</button>

	{#if open}
		<!-- svelte-ignore a11y_no_static_element_interactions -->
		<div class="ts-backdrop" onclick={() => open = false}></div>
		<div class="ts-menu" role="menu">
			<p class="ts-heading">Theme</p>
			{#each themes as theme}
				<button
					class="ts-item"
					class:ts-item-active={currentTheme === theme.value}
					role="menuitem"
					onclick={() => select(theme.value)}
				>
					<span>{theme.label}</span>
					{#if currentTheme === theme.value}
						<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3">
							<polyline points="20 6 9 17 4 12"/>
						</svg>
					{/if}
				</button>
			{/each}
		</div>
	{/if}
</div>

<style>
	.ts-root {
		position: relative;
	}

	.ts-trigger {
		width: 48px;
		height: 48px;
		display: flex;
		align-items: center;
		justify-content: center;
		background: rgb(255 255 255 / 0.05);
		border: 1px solid rgb(255 255 255 / 0.08);
		border-radius: 12px;
		color: rgb(var(--color-text-primary));
		cursor: pointer;
		transition: all 0.15s;
	}

	.ts-trigger:hover {
		background: rgb(255 255 255 / 0.1);
		border-color: rgb(var(--color-accent-primary) / 0.5);
		transform: translateY(-2px);
	}

	.ts-trigger svg {
		width: 24px;
		height: 24px;
	}

	.ts-backdrop {
		position: fixed;
		inset: 0;
		z-index: 200;
	}

	.ts-menu {
		position: absolute;
		bottom: calc(100% + 8px);
		right: 0;
		min-width: 180px;
		background: rgb(var(--color-surface-1) / 0.98);
		border: 1px solid rgb(255 255 255 / 0.1);
		border-radius: 12px;
		padding: 0.5rem;
		box-shadow: 0 10px 25px -5px rgb(0 0 0 / 0.5);
		z-index: 201;
	}

	.ts-heading {
		margin: 0 0 0.25rem;
		padding: 0.25rem 0.75rem;
		font-size: 0.75rem;
		font-weight: 600;
		text-transform: uppercase;
		letter-spacing: 0.05em;
		color: rgb(var(--color-text-tertiary));
	}

	.ts-item {
		display: flex;
		align-items: center;
		justify-content: space-between;
		width: 100%;
		padding: 0.6rem 0.75rem;
		background: transparent;
		border: none;
		border-radius: 8px;
		color: rgb(var(--color-text-primary));
		font-size: 0.875rem;
		cursor: pointer;
		transition: background 0.15s;
		text-align: left;
	}

	.ts-item:hover {
		background: rgb(255 255 255 / 0.08);
	}

	.ts-item-active {
		background: rgb(var(--color-accent-primary) / 0.15);
		color: rgb(var(--color-accent-light));
	}

	.ts-item svg {
		width: 16px;
		height: 16px;
		color: rgb(var(--color-accent-primary));
		flex-shrink: 0;
	}
</style>
