<!--
	OS.svelte - ShellOS Root Layout

	Layout Structure:
	┌─────────────────────────────────────────────────────────┐
	│  ┌─────────┐┌───────────────────────────────────────────┐│
	│  │         ││            [Vertical Split]               ││
	│  │         ││  ┌─────────────────────────────────────┐  ││
	│  │ Sidebar ││  │        [Horizontal Split]           │  ││
	│  │ (drawer)││  │  ┌────────┬────────┬─────────────┐  │  ││
	│  │         ││  │  │Artifact│  Chat  │   Panel     │  │  ││
	│  │         ││  │  └────────┴────────┴─────────────┘  │  ││
	│  │         ││  ├─────────────────────────────────────┤  ││
	│  │         ││  │           Bottom Panel              │  ││
	│  │         ││  └─────────────────────────────────────┘  ││
	│  └─────────┘└───────────────────────────────────────────┘│
	│  [Footer - fixed height outside split]                   │
	└─────────────────────────────────────────────────────────┘
-->
<script lang="ts">
	import PersistentArtifact from './panel/PersistentArtifact.svelte';
	import Chat from './chat/Chat.svelte';
	import Settings from './settings/Settings.svelte';
	import ThemeSelector from './ThemeSelector.svelte';
	import { appRegistry } from '../apps/index';
	import { SplitPanel } from '@hpd/hpd-agent-headless-ui';
	import { workspace } from '../workspace.svelte';
	import { appShellState } from '../appShellState.svelte';
	import Sidebar from './sidebar/Sidebar.svelte';

	// ===== State =====

	let sidebarOpen = $state(false);
	let appDialogOpen = $state(false);
	let mainView = $state<'chat' | 'settings'>('chat');
	let layoutState = $state<SplitPanel.SplitPanelRootState | null>(null);

	// Wire panel controls into appShellState once layoutState is available,
	// so the agent tool call path can drive the panel imperatively.
	$effect(() => {
		if (!layoutState) return;
		appShellState.registerPanelControls(
			() => {
				layoutState!.expandPane('right-panel');
				layoutState!.setPaneSize('right-panel', 400, 'pixels');
			},
			() => layoutState!.collapsePane('right-panel'),
		);
	});

	// ===== Handlers =====

	function openApp(appId: string) {
		appShellState.openApp(appId);
		appDialogOpen = false;
		sidebarOpen = false;
	}

	function toggleSidebar() {
		sidebarOpen = !sidebarOpen;
	}

	function closeDialog() {
		appDialogOpen = false;
	}

	function handleBackdropKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape' || e.key === 'Enter' || e.key === ' ') {
			appDialogOpen = false;
		}
	}
</script>

<div class="os-root">

	<!-- ===== SIDEBAR DRAWER ===== -->
	<aside class="os-sidebar" data-open={sidebarOpen}>
		<div class="os-sidebar-header">
			<button class="os-icon-btn" onclick={toggleSidebar} title="Close sidebar" aria-label="Close sidebar">
				<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
					<polyline points="15 18 9 12 15 6"/>
				</svg>
			</button>
		</div>
		<div class="os-sidebar-content">
			<Sidebar {workspace} onNavigate={() => { sidebarOpen = false; }} />
		</div>
	</aside>

	<!-- ===== MAIN AREA ===== -->
	<div class="os-body" data-view={mainView}>
		{#if mainView === 'settings'}
			<div class="os-settings-wrapper">
				<Settings />
			</div>
		{:else}
		<!-- TODO: storageKey has no effect until storageBackend is also set —
		     persistence only activates when both are provided (see split-panel-root-state.svelte.ts) -->
		<SplitPanel.Root
			id="hpdos-layout"
			storageKey="hpdos-layout"
			bind:layout={layoutState}
			class="os-split-root"
		>
			<SplitPanel.Split axis="vertical">

				<!-- Top row: artifact | chat | right-panel -->
				<SplitPanel.Split axis="horizontal">

					<!-- Artifact pane (collapsed by default) -->
					<!-- TODO: add collapseStrategy="force-mount" to keep the pill in DOM during collapse
					     (avoids unmount/remount flash on the pill button) -->
					<SplitPanel.Pane
						id="artifact"
						minSize={200}
						initialSize={300}
						initialSizeUnit="pixels"
						collapsed={true}
						priority="low"
						autoCollapseThreshold={80}
					>
						{#snippet children({ toggle, isCollapsed })}
							{#if isCollapsed}
								<div class="os-artifact-pill">
									<button class="os-icon-btn" onclick={toggle} title="Expand artifact" aria-label="Expand artifact">
										<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
											<polyline points="15 18 21 12 15 6"/>
										</svg>
									</button>
								</div>
							{:else}
								<div class="artifact-header">
									<span class="artifact-title">Artifact Preview</span>
									<button class="os-icon-btn" onclick={toggle} title="Close" aria-label="Close artifact">
										<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
											<line x1="18" y1="6" x2="6" y2="18"/>
											<line x1="6" y1="6" x2="18" y2="18"/>
										</svg>
									</button>
								</div>
								<div class="artifact-content-wrapper">
									<!-- Artifact content rendered here -->
								</div>
							{/if}
						{/snippet}
					</SplitPanel.Pane>

					<SplitPanel.Handle toggleCollapseOnClick={true}>
						{#snippet children({ axis })}
							<div class="handle-grip" data-axis={axis}></div>
						{/snippet}
					</SplitPanel.Handle>

					<!-- Chat pane (fills remaining space) -->
					<SplitPanel.Pane id="chat" minSize={300} priority="high">
						<div class="os-chat-wrapper">
							<Chat {workspace} onOpenSettings={() => mainView = 'settings'} />
						</div>
					</SplitPanel.Pane>

					<SplitPanel.Handle toggleCollapseOnClick={true}>
						{#snippet children({ axis })}
							<div class="handle-grip" data-axis={axis}></div>
						{/snippet}
					</SplitPanel.Handle>

					<!-- Right panel (collapsed by default) -->
					<!-- TODO: add collapseStrategy="force-mount" to keep the pill in DOM during collapse
					     (avoids unmount/remount flash on the pill button) -->
					<SplitPanel.Pane
						id="right-panel"
						minSize={60}
						initialSize={400}
						initialSizeUnit="pixels"
						collapsed={true}
						priority="low"
						autoCollapseThreshold={80}
					>
						{#snippet children({ toggle, isCollapsed })}
							{#if isCollapsed}
								<div class="os-panel-pill">
									<button class="os-icon-btn" onclick={toggle} title="Expand panel" aria-label="Expand panel">
										<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
											<polyline points="9 18 15 12 9 6"/>
										</svg>
									</button>
								</div>
							{:else}
								<PersistentArtifact appId={appShellState.selectedAppId} onClose={toggle} />
							{/if}
						{/snippet}
					</SplitPanel.Pane>

				</SplitPanel.Split>

				<SplitPanel.Handle toggleCollapseOnClick={true}>
					{#snippet children({ axis })}
						<div class="handle-grip" data-axis={axis}></div>
					{/snippet}
				</SplitPanel.Handle>

				<!-- Bottom panel (collapsed by default) -->
				<SplitPanel.Pane
					id="bottom"
					minSize={100}
					initialSize={240}
					initialSizeUnit="pixels"
					collapsed={true}
					priority="low"
					autoCollapseThreshold={60}
				>
					{#snippet children({ toggle })}
						<div class="os-bottom-panel">
							<div class="os-bottom-panel-header">
								<span>Terminal</span>
								<button class="os-icon-btn" onclick={toggle} title="Close terminal" aria-label="Close terminal">
									<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
										<line x1="18" y1="6" x2="6" y2="18"/>
										<line x1="6" y1="6" x2="18" y2="18"/>
									</svg>
								</button>
							</div>
							<div class="os-bottom-panel-content">
								<p class="os-terminal-text">$ ready</p>
							</div>
						</div>
					{/snippet}
				</SplitPanel.Pane>

			</SplitPanel.Split>
		</SplitPanel.Root>
		{/if}
	</div>

	<!-- ===== FOOTER ===== -->
	<footer class="os-footer">
		<div class="os-footer-left">
			<button class="os-dock-btn" title="Toggle sidebar" aria-label="Toggle sidebar" onclick={toggleSidebar}>
				<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
					<rect x="3" y="3" width="18" height="18" rx="2"/>
					<line x1="9" y1="3" x2="9" y2="21"/>
				</svg>
			</button>
			<!-- TODO: terminal button — re-enable when terminal is implemented
			<button class="os-dock-btn" title="Toggle terminal" aria-label="Toggle terminal" onclick={() => layoutState?.togglePane('bottom')}>
				<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
					<polyline points="4 17 10 11 4 5"/>
					<line x1="12" y1="19" x2="20" y2="19"/>
				</svg>
			</button>
			-->
		</div>

		<div class="os-footer-center">
			<button class="os-dock-btn" title="Apps" aria-label="Open apps" onclick={() => { appDialogOpen = true; }}>
				<svg viewBox="0 0 24 24" fill="currentColor">
					<rect x="3" y="3" width="4" height="4"/>
					<rect x="10" y="3" width="4" height="4"/>
					<rect x="17" y="3" width="4" height="4"/>
					<rect x="3" y="10" width="4" height="4"/>
					<rect x="10" y="10" width="4" height="4"/>
					<rect x="17" y="10" width="4" height="4"/>
					<rect x="3" y="17" width="4" height="4"/>
					<rect x="10" y="17" width="4" height="4"/>
					<rect x="17" y="17" width="4" height="4"/>
				</svg>
			</button>
		</div>

		<div class="os-footer-right">
			<button class="os-dock-btn" class:os-dock-btn-active={mainView === 'settings'} title="Settings" aria-label="Settings" onclick={() => mainView = mainView === 'settings' ? 'chat' : 'settings'}>
				<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
					<circle cx="12" cy="12" r="3"/>
					<path d="M19.4 15a1.65 1.65 0 00.33 1.82l.06.06a2 2 0 010 2.83 2 2 0 01-2.83 0l-.06-.06a1.65 1.65 0 00-1.82-.33 1.65 1.65 0 00-1 1.51V21a2 2 0 01-4 0v-.09A1.65 1.65 0 009 19.4a1.65 1.65 0 00-1.82.33l-.06.06a2 2 0 01-2.83-2.83l.06-.06A1.65 1.65 0 004.68 15a1.65 1.65 0 00-1.51-1H3a2 2 0 010-4h.09A1.65 1.65 0 004.6 9a1.65 1.65 0 00-.33-1.82l-.06-.06a2 2 0 012.83-2.83l.06.06A1.65 1.65 0 009 4.68a1.65 1.65 0 001-1.51V3a2 2 0 014 0v.09a1.65 1.65 0 001 1.51 1.65 1.65 0 001.82-.33l.06-.06a2 2 0 012.83 2.83l-.06.06A1.65 1.65 0 0019.4 9a1.65 1.65 0 001.51 1H21a2 2 0 010 4h-.09a1.65 1.65 0 00-1.51 1z"/>
				</svg>
			</button>
			<ThemeSelector />
		</div>
	</footer>

	<!-- ===== APP DIALOG ===== -->
	{#if appDialogOpen}
		<div
			class="os-dialog-backdrop"
			role="button"
			tabindex="0"
			aria-label="Close dialog"
			onclick={closeDialog}
			onkeydown={handleBackdropKeydown}
		></div>
		<div class="os-dialog" role="dialog" aria-modal="true" aria-labelledby="dialog-title">
			<div class="os-dialog-header">
				<h2 id="dialog-title">Apps</h2>
				<button class="os-icon-btn" onclick={closeDialog} title="Close" aria-label="Close dialog">
					<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
						<line x1="18" y1="6" x2="6" y2="18"/>
						<line x1="6" y1="6" x2="18" y2="18"/>
					</svg>
				</button>
			</div>
			<div class="os-dialog-content">
				{#each appRegistry.list() as app (app.id)}
					<button class="os-app-item" onclick={() => openApp(app.id)}>
						<div class="os-app-icon">
							{#if app.icon.includes('<svg')}
								<!-- eslint-disable-next-line svelte/no-at-html-tags -->
								{@html app.icon}
							{:else}
								<span>{app.icon}</span>
							{/if}
						</div>
						<div class="os-app-info">
							<span class="os-app-name">{app.name}</span>
							<span class="os-app-desc">{app.description}</span>
						</div>
					</button>
				{/each}
			</div>
		</div>
	{/if}
</div>

<style>
	/* ===== Root ===== */
	.os-root {
		position: fixed;
		inset: 0;
		display: flex;
		flex-direction: column;
		background: linear-gradient(135deg, rgb(var(--color-bg-primary)) 0%, rgb(var(--color-bg-secondary)) 50%, rgb(var(--color-bg-tertiary)) 100%);
		color: rgb(var(--color-text-primary));
		font-family: system-ui, -apple-system, sans-serif;
	}

	/* ===== Sidebar ===== */
	.os-sidebar {
		position: fixed;
		top: 0;
		left: 0;
		bottom: 60px;
		width: 400px;
		background: rgb(var(--color-surface-1) / 0.95);
		backdrop-filter: blur(12px);
		border-right: 1px solid rgb(255 255 255 / 0.08);
		display: flex;
		flex-direction: column;
		padding: 0.5rem;
		z-index: 50;
		transform: translateX(-100%);
		transition: transform 0.25s ease;
	}

	.os-sidebar[data-open="true"] {
		transform: translateX(0);
	}

	.os-sidebar-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 0.5rem;
		border-bottom: 1px solid rgb(255 255 255 / 0.06);
		margin-bottom: 0.5rem;
	}

	.os-sidebar-title {
		font-size: 0.875rem;
		font-weight: 600;
		color: rgb(var(--color-text-secondary));
	}

	.os-sidebar-content {
		flex: 1;
		overflow-y: auto;
		padding: 0.5rem;
	}

	/* ===== Body ===== */
	.os-body {
		flex: 1;
		min-height: 0;
		overflow: hidden;
		display: flex;
		flex-direction: column;
	}

	/* ===== Split panel root fills the body ===== */
	.os-body :global([data-split-panel-root]) {
		flex: 1;
		min-height: 0;
		width: 100%;
	}

	/* ===== Drag state ===== */
	.os-body :global([data-split-panel-root][data-dragging]),
	.os-body :global([data-split-panel-root][data-dragging] *) {
		user-select: none;
		-webkit-user-select: none;
	}

	/* ===== Resize handles ===== */
	.os-body :global([data-split-panel-handle]) {
		background: rgb(255 255 255 / 0.04);
		transition: background 0.15s;
		flex: none;
		z-index: 1;
		position: relative;
	}

	.os-body :global([data-split-panel-handle]:hover),
	.os-body :global([data-split-panel-handle][data-state="dragging"]) {
		background: rgb(var(--color-accent-primary) / 0.3);
	}

	.os-body :global([data-split-panel-handle][data-orientation="horizontal"]) {
		width: 4px;
	}

	.os-body :global([data-split-panel-handle][data-orientation="vertical"]) {
		height: 4px;
	}

	/* ===== Handle grip dot ===== */
	.os-body :global(.handle-grip) {
		position: absolute;
		background: rgb(255 255 255 / 0.15);
		border-radius: 2px;
		pointer-events: none;
		transition: background 0.15s;
	}

	.os-body :global([data-split-panel-handle]:hover .handle-grip),
	.os-body :global([data-split-panel-handle][data-state="dragging"] .handle-grip) {
		background: rgb(255 255 255 / 0.4);
	}

	.os-body :global(.handle-grip[data-axis="row"]) {
		width: 3px;
		height: 32px;
		top: 50%;
		left: 50%;
		transform: translate(-50%, -50%);
	}

	.os-body :global(.handle-grip[data-axis="column"]) {
		width: 32px;
		height: 3px;
		top: 50%;
		left: 50%;
		transform: translate(-50%, -50%);
	}

	/* ===== Pane content fills available space ===== */
	.os-body :global([data-split-panel-pane]) {
		overflow: hidden;
		display: flex;
		flex-direction: column;
	}

	/* ===== Artifact pane ===== */
	.artifact-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 0.75rem 1rem;
		border-bottom: 1px solid rgba(255, 255, 255, 0.06);
		flex: none;
	}

	.artifact-title {
		font-size: 0.875rem;
		font-weight: 500;
		color: #a1a1aa;
	}

	.artifact-content-wrapper {
		flex: 1;
		padding: 1rem;
		overflow-y: auto;
	}

	/* ===== Settings fills the body ===== */
	.os-body[data-view="settings"] :global(.os-settings-wrapper),
	.os-body :global(.os-settings-wrapper) {
		display: flex;
		flex-direction: column;
		width: 100%;
		height: 100%;
		overflow: hidden;
	}

	/* ===== Chat wrapper ===== */
	.os-chat-wrapper {
		display: flex;
		flex-direction: column;
		width: 100%;
		height: 100%;
		min-height: 0;
		min-width: 0;
		overflow: hidden;
	}

	/* ===== Artifact pill (collapsed left artifact pane) ===== */
	.os-artifact-pill {
		display: flex;
		align-items: center;
		padding: 0.5rem;
		background: rgb(var(--color-surface-1) / 0.8);
		border-right: 1px solid rgb(255 255 255 / 0.06);
		height: 100%;
	}

	/* ===== Panel pill (collapsed right panel) ===== */
	.os-panel-pill {
		display: flex;
		align-items: center;
		padding: 0.5rem;
		background: rgb(var(--color-surface-1) / 0.8);
		border-left: 1px solid rgb(255 255 255 / 0.06);
		height: 100%;
	}

	/* ===== Bottom panel ===== */
	.os-bottom-panel {
		display: flex;
		flex-direction: column;
		height: 100%;
		background: rgb(var(--color-bg-active) / 0.9);
		border-top: 1px solid rgb(255 255 255 / 0.06);
	}

	.os-bottom-panel-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 0.5rem 1rem;
		border-bottom: 1px solid rgb(255 255 255 / 0.06);
		font-size: 0.75rem;
		font-weight: 500;
		text-transform: uppercase;
		letter-spacing: 0.05em;
		color: rgb(var(--color-text-tertiary));
		flex: none;
	}

	.os-bottom-panel-content {
		flex: 1;
		padding: 0.75rem 1rem;
		overflow-y: auto;
		font-family: 'SF Mono', 'Fira Code', monospace;
		font-size: 0.8125rem;
	}

	.os-terminal-text {
		color: rgb(var(--color-success));
		margin: 0;
	}

	/* ===== Footer ===== */
	.os-footer {
		height: 60px;
		flex: none;
		background: rgb(var(--color-bg-primary) / 0.95);
		border-top: 1px solid rgb(255 255 255 / 0.06);
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 0.5rem;
		padding: 0 1rem;
	}

	.os-footer-left,
	.os-footer-right {
		flex: 1;
		display: flex;
		gap: 0.5rem;
	}

	.os-footer-left {
		justify-content: flex-start;
	}

	.os-footer-right {
		justify-content: flex-end;
	}

	.os-footer-center {
		display: flex;
		justify-content: center;
	}

	/* ===== Buttons ===== */
	.os-icon-btn {
		width: 36px;
		height: 36px;
		display: flex;
		align-items: center;
		justify-content: center;
		background: transparent;
		border: none;
		border-radius: 8px;
		color: rgb(var(--color-text-secondary));
		cursor: pointer;
		transition: all 0.15s;
	}

	.os-icon-btn:hover {
		background: rgb(255 255 255 / 0.08);
		color: rgb(var(--color-text-primary));
	}

	.os-icon-btn svg {
		width: 20px;
		height: 20px;
	}

	.os-dock-btn {
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

	.os-dock-btn-active {
		background: rgb(var(--color-accent-primary) / 0.15) !important;
		border-color: rgb(var(--color-accent-primary) / 0.5) !important;
		color: rgb(var(--color-accent-primary)) !important;
	}

	.os-dock-btn:hover {
		background: rgb(255 255 255 / 0.1);
		border-color: rgb(var(--color-accent-primary) / 0.5);
		transform: translateY(-2px);
	}

	.os-dock-btn svg {
		width: 24px;
		height: 24px;
	}

	/* ===== Dialog ===== */
	.os-dialog-backdrop {
		position: fixed;
		inset: 0;
		background: rgb(0 0 0 / 0.6);
		backdrop-filter: blur(4px);
		z-index: 100;
	}

	.os-dialog {
		position: fixed;
		top: 50%;
		left: 50%;
		transform: translate(-50%, -50%);
		width: 90%;
		max-width: 500px;
		max-height: 80vh;
		background: rgb(var(--color-surface-1) / 0.95);
		border: 1px solid rgb(255 255 255 / 0.1);
		border-radius: 16px;
		box-shadow: 0 25px 50px -12px rgb(0 0 0 / 0.5);
		z-index: 101;
		display: flex;
		flex-direction: column;
	}

	.os-dialog-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 1.25rem 1.5rem;
		border-bottom: 1px solid rgb(255 255 255 / 0.06);
	}

	.os-dialog-header h2 {
		margin: 0;
		font-size: 1.25rem;
		font-weight: 600;
	}

	.os-dialog-content {
		flex: 1;
		overflow-y: auto;
		padding: 1rem;
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}

	.os-app-item {
		display: flex;
		align-items: center;
		gap: 1rem;
		padding: 1rem;
		background: transparent;
		border: 1px solid rgb(255 255 255 / 0.06);
		border-radius: 12px;
		cursor: pointer;
		transition: all 0.15s;
		text-align: left;
		width: 100%;
		color: inherit;
	}

	.os-app-item:hover {
		background: rgb(255 255 255 / 0.05);
		border-color: rgb(var(--color-accent-primary) / 0.4);
	}

	.os-app-icon {
		width: 48px;
		height: 48px;
		display: flex;
		align-items: center;
		justify-content: center;
		background: rgb(var(--color-accent-primary) / 0.1);
		border-radius: 12px;
		color: rgb(var(--color-accent-light));
		font-size: 1.5rem;
	}

	.os-app-icon :global(svg) {
		width: 24px;
		height: 24px;
	}

	.os-app-info {
		flex: 1;
		display: flex;
		flex-direction: column;
		gap: 0.25rem;
	}

	.os-app-name {
		font-weight: 500;
		color: rgb(var(--color-text-primary));
	}

	.os-app-desc {
		font-size: 0.875rem;
		color: rgb(var(--color-text-tertiary));
	}

	/* ===== Utility ===== */
	.os-placeholder {
		font-size: 0.8125rem;
		color: rgb(var(--color-text-quaternary));
		margin: 0;
	}
</style>
