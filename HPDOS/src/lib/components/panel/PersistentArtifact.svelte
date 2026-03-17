<script lang="ts">
	/**
	 * PersistentArtifact - Container for right panel persistent content
	 *
	 * Similar to MainContent artifact but:
	 * - Close button on the right instead of left
	 * - Takes full width (no split view)
	 * - Used for persistent app content in right panel
	 */

	import { appRegistry } from '../../apps/index';
	import type { AppTab } from '../../apps/types';
	import DynamicAppLoader from '../shell/DynamicAppLoader.svelte';

	interface Props {
		appId?: string | null;
		onClose?: () => void;
	}

	let { appId = null, onClose }: Props = $props();

	function handleClose() {
		if (onClose) {
			onClose();
		}
	}

	// Get app manifest if appId is set
	let appManifest = $derived.by(() => {
		if (!appId) return null;
		return appRegistry.get(appId);
	});

	// Create AppTab object for DynamicAppLoader
	let appTab = $derived.by(() => {
		if (!appManifest) return null;
		return {
			id: `persistent-${appManifest.id}`,
			appId: appManifest.id,
			label: appManifest.name,
			icon: appManifest.icon,
			state: appManifest.defaultState || {},
			createdAt: new Date(),
			isActive: true
		} as AppTab;
	});
</script>

<div class="persistent-artifact-container">
	<!-- Content Area -->
	<div class="persistent-artifact-content shell-scrollbar">
		{#if appTab}
			<DynamicAppLoader 
				tab={appTab}
			/>
		{:else}
			<div class="shell-content-box" style="height: 100%; margin: 0;">
				<h3 class="shell-content-box-title">Persistent Content</h3>
				<div class="shell-content-box-text">
					<p>Persistent app content will render here</p>
					<p style="margin-top: var(--spacing-md); font-style: italic;">
						This panel stays open while you work in the main area.
					</p>
					
					<!-- Mock Content Box -->
					<div class="app-mock-container">
						<div class="app-mock-header"></div>
						<div class="app-mock-lines">
							<div class="app-mock-line" style="width: 100%;"></div>
							<div class="app-mock-line" style="width: 85%;"></div>
							<div class="app-mock-line" style="width: 65%;"></div>
						</div>
					</div>
				</div>
			</div>
		{/if}
	</div>
</div>

<style>
	.persistent-artifact-container {
		position: relative;
		height: 100%;
		width: 100%;
		display: flex;
		flex-direction: column;
	}

	.persistent-artifact-content {
		flex: 1;
		overflow-y: auto;
		padding: var(--spacing-md);
	}
</style>
