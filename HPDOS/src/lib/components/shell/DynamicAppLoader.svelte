<script lang="ts">
	/**
	 * DynamicAppLoader
	 *
	 * Routes app tabs to the right renderer:
	 *  - isolation.enabled → <web-fragment> via web-fragments library
	 *  - otherwise          → direct Svelte component mount (lazy import)
	 *
	 * web-fragments is initialized here once before the first fragment is created.
	 */

	import { onMount, onDestroy, mount, unmount } from 'svelte';
	import { appRegistry } from '../../apps/registry';
	import { initFragments } from '../../fragments/client';
	import type { AppTab } from '../../apps/types';

	interface Props {
		tab: AppTab;
	}

	let { tab }: Props = $props();

	const manifest = $derived(appRegistry.get(tab.appId));
	const useFragment = $derived(manifest?.isolation?.enabled ?? false);

	// ── Shared state ──────────────────────────────────────────────────────────

	let isLoading = $state(true);
	let loadingError = $state<string | null>(null);

	// ── Fragment path ─────────────────────────────────────────────────────────

	let containerRef = $state<HTMLElement | undefined>();
	let fragmentEl = $state<HTMLElement | null>(null);

	function createFragment() {
		if (!containerRef || !manifest) {
			loadingError = `App not found in registry: ${tab.appId}`;
			isLoading = false;
			return;
		}
		try {
			isLoading = true;
			loadingError = null;

			const endpoint = manifest.isolation?.endpoint || `/apps/${tab.appId}`;

			// bound=false: plain external URL server — use a native iframe.
			// bound=true: web-fragments-aware server with gateway — use <web-fragment>.
			if (manifest.isolation?.bound === false) {
				fragmentEl = document.createElement('iframe');
				(fragmentEl as HTMLIFrameElement).src = endpoint;
				(fragmentEl as HTMLIFrameElement).style.cssText = 'width:100%;height:100%;border:none;';
			} else {
				fragmentEl = document.createElement('web-fragment');
				fragmentEl.setAttribute('fragment-id', tab.appId);
				fragmentEl.setAttribute('src', endpoint);
			}

			containerRef.appendChild(fragmentEl);
			fragmentEl.addEventListener('load', onFragmentLoad);
			fragmentEl.addEventListener('error', onFragmentError);

			isLoading = false;
			console.log(`[DynamicAppLoader] Fragment created: ${tab.appId}`);
		} catch (err) {
			loadingError = err instanceof Error ? err.message : String(err);
			isLoading = false;
		}
	}

	function destroyFragment() {
		if (fragmentEl) {
			fragmentEl.removeEventListener('load', onFragmentLoad);
			fragmentEl.removeEventListener('error', onFragmentError);
			if (containerRef?.contains(fragmentEl)) containerRef.removeChild(fragmentEl);
			fragmentEl = null;
		}
	}

	function onFragmentLoad() { isLoading = false; }
	function onFragmentError() {
		loadingError = `Failed to load fragment: ${tab.appId}`;
		isLoading = false;
	}

	// ── Direct Svelte mount path ──────────────────────────────────────────────

	let directContainer = $state<HTMLElement | undefined>();
	let svelteInstance: ReturnType<typeof mount> | null = null;

	async function mountSvelte() {
		if (!directContainer || !manifest) return;
		isLoading = true;
		loadingError = null;
		try {
			const mod = await manifest.component();
			svelteInstance = mount(mod.default, {
				target: directContainer,
				props: { state: manifest.defaultState ?? {}, tabId: tab.id, manifest },
			});
			console.log(`[DynamicAppLoader] Svelte mounted: ${tab.appId}`);
		} catch (err) {
			loadingError = err instanceof Error ? err.message : String(err);
		} finally {
			isLoading = false;
		}
	}

	function unmountSvelte() {
		if (svelteInstance) {
			unmount(svelteInstance);
			svelteInstance = null;
		}
	}

	// ── Lifecycle ─────────────────────────────────────────────────────────────

	onMount(async () => {
		if (useFragment) {
			await initFragments();
			// Await the manifest's onMount hook so external apps (e.g. code-server)
			// can launch their process and set isolation.endpoint before the fragment
			// element is created and its src is read.
			if (manifest?.onMount) {
				try { await manifest.onMount(tab as any); } catch (e) {
					console.warn('[DynamicAppLoader] onMount hook failed:', e);
				}
			}
			createFragment();
		}
		// Direct mount: directContainer only exists after isLoading → false renders the
		// {:else} branch. Watch for it via $effect below instead of mounting here.
	});

	// Mount the Svelte component once directContainer becomes available.
	// This handles the case where isLoading=true hides the container on first render.
	$effect(() => {
		if (!useFragment && directContainer && !svelteInstance && !loadingError) {
			mountSvelte();
		}
	});

	onDestroy(() => {
		destroyFragment();
		unmountSvelte();
	});

	// Recreate fragment when tab.appId changes (but not on initial mount)
	let prevAppId = tab.appId;
	$effect(() => {
		const currentAppId = tab.appId;
		if (useFragment && currentAppId !== prevAppId && containerRef) {
			prevAppId = currentAppId;
			destroyFragment();
			createFragment();
		}
	});
</script>

<div class="dynamic-app-loader" data-app-id={tab.appId} data-tab-id={tab.id}>
	{#if isLoading}
		<div class="loading-state">
			<div class="spinner"></div>
			<p class="loading-text">Loading {manifest?.name || tab.appId}...</p>
		</div>
	{:else if loadingError}
		<div class="error-state">
			<div class="error-icon">⚠️</div>
			<h3 class="error-title">Failed to load {tab.label}</h3>
			<p class="error-message">{loadingError}</p>
			<button class="retry-button" onclick={() => useFragment ? createFragment() : mountSvelte()}>
				Retry
			</button>
		</div>
	{/if}

	{#if useFragment}
		<div bind:this={containerRef} class="app-container" style:display={isLoading || loadingError ? 'none' : undefined}></div>
	{/if}

	<!--
		Always rendered for the direct-mount path so bind:this resolves
		before mountSvelte() is called. Hidden while loading/errored.
	-->
	{#if !useFragment}
		<div
			bind:this={directContainer}
			class="app-container"
			style:display={isLoading || loadingError ? 'none' : undefined}
		></div>
	{/if}
</div>

<style>
	.dynamic-app-loader {
		width: 100%;
		height: 100%;
		display: flex;
		flex-direction: column;
		overflow: hidden;
	}

	.app-container {
		width: 100%;
		height: 100%;
		overflow: hidden;
	}

	.loading-state {
		display: flex;
		flex-direction: column;
		align-items: center;
		justify-content: center;
		height: 100%;
		gap: 1rem;
		color: var(--color-text-secondary, #a0aec0);
	}

	.spinner {
		width: 40px;
		height: 40px;
		border: 3px solid var(--color-border-default, #4b5563);
		border-top-color: var(--color-accent-primary, #14b8a6);
		border-radius: 50%;
		animation: spin 0.8s linear infinite;
	}

	@keyframes spin {
		to { transform: rotate(360deg); }
	}

	.loading-text {
		font-size: 0.875rem;
		margin: 0;
	}

	.error-state {
		display: flex;
		flex-direction: column;
		align-items: center;
		justify-content: center;
		height: 100%;
		gap: 1rem;
		padding: 2rem;
		text-align: center;
		color: var(--color-text-primary, #e2e8f0);
	}

	.error-icon { font-size: 3rem; }

	.error-title {
		font-size: 1.25rem;
		font-weight: 600;
		margin: 0;
		color: var(--color-error, #ef4444);
	}

	.error-message {
		font-size: 0.875rem;
		margin: 0;
		color: var(--color-text-secondary, #a0aec0);
		max-width: 400px;
	}

	.retry-button {
		margin-top: 0.5rem;
		padding: 0.5rem 1rem;
		background: var(--color-accent-primary, #14b8a6);
		color: white;
		border: none;
		border-radius: 0.375rem;
		font-size: 0.875rem;
		font-weight: 500;
		cursor: pointer;
		transition: background 0.2s;
	}

	.retry-button:hover {
		background: var(--color-accent-hover, #0d9488);
	}
</style>
