<script lang="ts">
	import { RunConfig } from '@hpd/hpd-agent-headless-ui';
	import type { RunConfigState } from '@hpd/hpd-agent-headless-ui';
	import { providers, type ModelInfo } from '../../providers.svelte.js';

	interface ProviderOption {
		key: string;
		label: string;
		models: { id: string; label: string }[];
	}

	interface Props {
		runConfig: RunConfigState;
		onOpenSettings: () => void;
	}

	let { runConfig, onOpenSettings }: Props = $props();

	let open = $state(false);
	let triggerEl = $state<HTMLButtonElement | null>(null);
	let menuPos = $state({ bottom: 0, left: 0 });

	function openPicker() {
		if (triggerEl) {
			const r = triggerEl.getBoundingClientRect();
			menuPos = { bottom: window.innerHeight - r.top + 8, left: r.left };
		}
		open = true;
	}

	// ── Model cache (per provider, 5 min TTL) ──────────────────────────────
	type CacheEntry = { models: ModelInfo[]; fetchedAt: number };
	const modelCache = new Map<string, CacheEntry>();
	const CACHE_TTL = 5 * 60 * 1000;

	let loadingModels = $state(false);
	let providerOptions = $state<ProviderOption[]>([]);
	const freeSet = new Set<string>();

	// ── Search + custom input ───────────────────────────────────────────────
	let search = $state('');
	let customProviderId = $state<string | null>(null);
	let customInput = $state('');

	// ── Load models when opened ────────────────────────────────────────────
	$effect(() => {
		if (!open) return;
		loadModels();
	});

	async function loadModels() {
		const connected = providers.providers.filter(p => p.isAuthenticated && !p.isExpired);
		if (connected.length === 0) { providerOptions = []; return; }

		loadingModels = true;
		try {
			const results = await Promise.all(
				connected.map(async p => {
					const cached = modelCache.get(p.providerId);
					if (cached && Date.now() - cached.fetchedAt < CACHE_TTL) return { p, models: cached.models };
					const models = await providers.getModels(p.providerId, true);
					modelCache.set(p.providerId, { models, fetchedAt: Date.now() });
					return { p, models };
				})
			);

			freeSet.clear();
			providerOptions = results.map(({ p, models }) => {
				for (const m of models) {
					if (m.isFree) freeSet.add(`${p.providerId}/${m.id}`);
				}
				const recommended = models.filter(m => m.isRecommended);
				const rest = models.filter(m => !m.isRecommended);
				return {
					key: p.providerId,
					label: p.displayName,
					models: [...recommended, ...rest].map(m => ({ id: m.id, label: m.description ?? m.id })),
				} satisfies ProviderOption;
			});
		} finally {
			loadingModels = false;
		}
	}

	// ── Filtered list ──────────────────────────────────────────────────────
	const filtered = $derived.by(() => {
		const q = search.trim().toLowerCase();
		if (!q) return providerOptions;
		return providerOptions.flatMap(p => {
			const models = p.models.filter((m: { id: string; label: string }) =>
				m.id.toLowerCase().includes(q) || m.label.toLowerCase().includes(q)
			);
			return models.length ? [{ ...p, models }] : [];
		});
	});

	// ── Pill label ─────────────────────────────────────────────────────────
	const pillLabel = $derived.by(() => {
		const mid = runConfig.modelId;
		if (mid) return mid.includes('/') ? mid.split('/').slice(1).join('/') : mid;
		return providers.defaults?.modelId ?? 'Model';
	});

	function close() {
		open = false;
		search = '';
		customProviderId = null;
		customInput = '';
	}

	function selectModel(providerKey: string, modelId: string, setFn: (pk: string | undefined, mid: string | undefined) => void) {
		setFn(providerKey, modelId);
		close();
	}

	function commitCustom(providerKey: string, setFn: (pk: string | undefined, mid: string | undefined) => void) {
		const val = customInput.trim();
		if (!val) return;
		selectModel(providerKey, val, setFn);
	}

	function isSelected(providerKey: string, modelId: string): boolean {
		return runConfig.providerKey === providerKey && runConfig.modelId === modelId;
	}

	function isFree(providerKey: string, modelId: string): boolean {
		return freeSet.has(`${providerKey}/${modelId}`);
	}

	function handleKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape') close();
	}
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div class="mp-root" onkeydown={handleKeydown}>

	<!-- Pill trigger -->
	<button
		bind:this={triggerEl}
		class="mp-pill"
		class:mp-pill-open={open}
		onclick={() => open ? close() : openPicker()}
		title="Select model"
		aria-label="Select model"
		aria-expanded={open}
	>
		<span class="mp-pill-text">{pillLabel}</span>
		<svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
			<path d="M6 9l6-6 6 6"/>
		</svg>
	</button>

	{#if open}
		<!-- Backdrop -->
		<!-- svelte-ignore a11y_no_static_element_interactions -->
		<div class="mp-backdrop" onclick={close}></div>

		<!-- Menu -->
		<div class="mp-menu" role="dialog" aria-label="Select model"
			style="bottom: {menuPos.bottom}px; left: {menuPos.left}px;"
		>

			<!-- Search -->
			<div class="mp-search-row">
				<svg class="mp-search-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
					<circle cx="11" cy="11" r="8"/><path d="M21 21l-4.35-4.35"/>
				</svg>
				<!-- svelte-ignore a11y_autofocus -->
				<input
					class="mp-search"
					type="text"
					placeholder="Search models…"
					bind:value={search}
					autofocus
				/>
			</div>

			<!-- Body -->
			<div class="mp-body">
				{#if !providers.loading && providers.providers.filter(p => p.isAuthenticated && !p.isExpired).length === 0}
					<div class="mp-empty">
						<p>No providers connected.</p>
						<button class="mp-link-btn" onclick={() => { close(); onOpenSettings(); }}>
							Go to Settings → Providers
						</button>
					</div>

				{:else if loadingModels}
					<div class="mp-empty">Loading…</div>

				{:else if filtered.length === 0}
					<div class="mp-empty">No models match "{search}"</div>

				{:else}
					<RunConfig.ModelSelector {runConfig} providers={providerOptions}>
						{#snippet child({ setModel })}
							{#each filtered as provider (provider.key)}
								{#if providerOptions.length > 1}
									<div class="mp-group-header">{provider.label}</div>
								{/if}

								{#each provider.models as model (model.id)}
									<button
										class="mp-item"
										class:mp-item-selected={isSelected(provider.key, model.id)}
										onclick={() => selectModel(provider.key, model.id, setModel)}
									>
										<span class="mp-item-label">{model.label}</span>
										{#if isFree(provider.key, model.id)}
											<span class="mp-badge-free">free</span>
										{/if}
										{#if isSelected(provider.key, model.id)}
											<svg class="mp-check" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
												<path d="M20 6L9 17l-5-5"/>
											</svg>
										{/if}
									</button>
								{/each}

								<!-- Custom model ID -->
								{#if customProviderId === provider.key}
									<div class="mp-custom-row">
										<input
											class="mp-custom-input"
											type="text"
											placeholder="Model ID…"
											bind:value={customInput}
											onkeydown={e => {
												if (e.key === 'Enter') commitCustom(provider.key, setModel);
												if (e.key === 'Escape') { customProviderId = null; customInput = ''; }
											}}
										/>
										<button class="mp-custom-confirm" onclick={() => commitCustom(provider.key, setModel)}>Use</button>
									</div>
								{:else}
									<button class="mp-item mp-item-custom" onclick={() => { customProviderId = provider.key; customInput = ''; }}>
										<span class="mp-item-label">Enter custom model ID…</span>
									</button>
								{/if}
							{/each}
						{/snippet}
					</RunConfig.ModelSelector>
				{/if}
			</div>

		</div>
	{/if}

</div>

<style>
	.mp-root {
		position: relative;
	}

	/* Pill */
	.mp-pill {
		display: flex;
		align-items: center;
		gap: 0.3rem;
		padding: 0.25rem 0.6rem;
		border-radius: 20px;
		border: 1px solid rgb(255 255 255 / 0.12);
		background: transparent;
		color: rgb(var(--color-text-secondary));
		font-size: 0.75rem;
		cursor: pointer;
		transition: background 0.1s, color 0.1s, border-color 0.1s;
		max-width: 160px;
	}
	.mp-pill:hover {
		background: rgb(255 255 255 / 0.06);
		color: rgb(var(--color-text-primary));
		border-color: rgb(255 255 255 / 0.2);
	}
	.mp-pill-open {
		background: rgb(var(--color-accent-primary) / 0.1);
		border-color: rgb(var(--color-accent-primary) / 0.4);
		color: rgb(var(--color-accent-primary));
	}
	.mp-pill-text {
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	/* Backdrop */
	.mp-backdrop {
		position: fixed;
		inset: 0;
		z-index: 200;
	}

	/* Menu */
	.mp-menu {
		position: fixed;
		width: 300px;
		max-height: 380px;
		display: flex;
		flex-direction: column;
		background: rgb(var(--color-surface-1) / 0.98);
		border: 1px solid rgb(255 255 255 / 0.1);
		border-radius: 12px;
		box-shadow: 0 10px 25px -5px rgb(0 0 0 / 0.5);
		overflow: hidden;
		z-index: 201;
	}

	/* Search */
	.mp-search-row {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		padding: 0.625rem 0.75rem;
		border-bottom: 1px solid rgb(255 255 255 / 0.07);
		flex: none;
	}
	.mp-search-icon {
		width: 14px;
		height: 14px;
		flex: none;
		color: rgb(var(--color-text-tertiary));
	}
	.mp-search {
		flex: 1;
		background: transparent;
		border: none;
		outline: none;
		font-size: 0.8125rem;
		color: rgb(var(--color-text-primary));
		font-family: inherit;
	}
	.mp-search::placeholder { color: rgb(var(--color-text-tertiary)); }

	/* Body */
	.mp-body {
		flex: 1;
		overflow-y: auto;
		padding: 0.375rem;
	}

	/* Group header */
	.mp-group-header {
		padding: 0.375rem 0.625rem 0.25rem;
		font-size: 0.6875rem;
		font-weight: 600;
		letter-spacing: 0.06em;
		text-transform: uppercase;
		color: rgb(var(--color-text-tertiary));
	}

	/* Model item */
	.mp-item {
		display: flex;
		align-items: center;
		width: 100%;
		padding: 0.45rem 0.625rem;
		background: transparent;
		border: none;
		border-radius: 6px;
		cursor: pointer;
		text-align: left;
		gap: 0.375rem;
		transition: background 0.1s;
	}
	.mp-item:hover { background: rgb(255 255 255 / 0.06); }
	.mp-item-selected { background: rgb(var(--color-accent-primary) / 0.12); }
	.mp-item-custom { opacity: 0.5; }
	.mp-item-custom:hover { opacity: 1; background: rgb(255 255 255 / 0.04); }

	.mp-item-label {
		flex: 1;
		font-size: 0.8125rem;
		color: rgb(var(--color-text-primary));
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}
	.mp-item-selected .mp-item-label { color: rgb(var(--color-accent-primary)); }
	.mp-item-custom .mp-item-label { font-style: italic; }

	.mp-badge-free {
		font-size: 0.65rem;
		font-weight: 600;
		padding: 0.1rem 0.35rem;
		border-radius: 4px;
		background: rgb(var(--color-success) / 0.15);
		color: rgb(var(--color-success));
		border: 1px solid rgb(var(--color-success) / 0.25);
		flex: none;
	}

	.mp-check {
		width: 13px;
		height: 13px;
		flex: none;
		color: rgb(var(--color-accent-primary));
	}

	/* Custom ID row */
	.mp-custom-row {
		display: flex;
		align-items: center;
		gap: 0.375rem;
		padding: 0.375rem 0.625rem;
	}
	.mp-custom-input {
		flex: 1;
		background: rgb(255 255 255 / 0.06);
		border: 1px solid rgb(255 255 255 / 0.12);
		border-radius: 6px;
		padding: 0.3rem 0.5rem;
		font-size: 0.8125rem;
		color: rgb(var(--color-text-primary));
		font-family: inherit;
		outline: none;
	}
	.mp-custom-input:focus { border-color: rgb(var(--color-accent-primary) / 0.5); }
	.mp-custom-confirm {
		padding: 0.3rem 0.6rem;
		border-radius: 6px;
		border: 1px solid rgb(var(--color-accent-primary) / 0.4);
		background: rgb(var(--color-accent-primary) / 0.12);
		color: rgb(var(--color-accent-primary));
		font-size: 0.8125rem;
		cursor: pointer;
	}

	/* Empty state */
	.mp-empty {
		padding: 1.5rem 1rem;
		text-align: center;
		font-size: 0.8125rem;
		color: rgb(var(--color-text-tertiary));
		display: flex;
		flex-direction: column;
		gap: 0.75rem;
		align-items: center;
	}
	.mp-empty p { margin: 0; }
	.mp-link-btn {
		background: none;
		border: none;
		color: rgb(var(--color-accent-primary));
		font-size: 0.8125rem;
		cursor: pointer;
		text-decoration: underline;
		padding: 0;
	}
</style>
