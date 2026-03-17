<script lang="ts">
	import type { AppComponentProps } from '../types';
	import { isHybridWebView } from '../../ipc/bridge';
	import { AppRecorderState } from './AppRecorderState.svelte';
	import PageNav from './PageNav.svelte';
	import SourcePickerOverlay from './overlays/SourcePickerOverlay.svelte';
	import RecordingHud from './overlays/RecordingHud.svelte';
	import ExportProgressOverlay from './overlays/ExportProgressOverlay.svelte';

	// Pages — static imports (all small until canvas/PixiJS is added)
	import MediaPage from './pages/MediaPage.svelte';
	import EditPage from './pages/EditPage.svelte';
	import AnnotatePage from './pages/AnnotatePage.svelte';
	import AudioPage from './pages/AudioPage.svelte';
	import ColorPage from './pages/ColorPage.svelte';
	import ExportPage from './pages/ExportPage.svelte';

	let { tabId = 'default' }: AppComponentProps = $props();

	const editor = new AppRecorderState();
	const native = isHybridWebView();

	// Dev mode: seed fake clips so the UI is explorable without a real video.
	// Remove this line (or gate it) before shipping.
	if (import.meta.env.DEV) editor.seedDev();
</script>

<div class="app-recorder">

	<!-- ── Global overlays (float above all pages) ── -->
	{#if editor.sourcePickerOpen}
		<SourcePickerOverlay
			sources={editor.sourcePickerSources}
			onpick={(id) => editor.resolveSourcePick(id)}
			oncancel={() => editor.resolveSourcePick(null)}
		/>
	{/if}

	{#if editor.hudVisible}
		<RecordingHud startedAt={editor.recordingStartedAt} />
	{/if}

	{#if editor.exportStatus.active}
		<ExportProgressOverlay
			format={editor.exportStatus.format}
			percent={editor.exportStatus.percent}
		/>
	{/if}

	{#if editor.importPickerOpen}
		<div class="overlay-backdrop">
			<div class="overlay-card">
				<p class="overlay-title">Import Video</p>
				<p class="overlay-body">Select a video file to import for editing.</p>
				<input
					type="file"
					accept="video/*"
					onchange={(e) => {
						const f = (e.currentTarget as HTMLInputElement).files?.[0];
						if (!f) { editor.resolveImportPick(null); return; }
						// Create an object URL so the video element can load it
						const url = URL.createObjectURL(f);
						// Probe duration via a temporary video element
						const probe = document.createElement('video');
						probe.preload = 'metadata';
						probe.onloadedmetadata = () => {
							const durationMs = probe.duration * 1000;
							URL.revokeObjectURL(probe.src);
							// Append at end of current timeline
							editor.addClip(url, editor.durationMs, durationMs);
							editor.resolveImportPick(url);
						};
						probe.onerror = () => {
							URL.revokeObjectURL(probe.src);
							editor.resolveImportPick(null);
						};
						probe.src = url;
					}}
				/>
				<button class="btn-ghost" onclick={() => editor.resolveImportPick(null)}>Cancel</button>
			</div>
		</div>
	{/if}

	{#if editor.unsavedChangesOpen}
		<div class="overlay-backdrop">
			<div class="overlay-card">
				<p class="overlay-title">Unsaved Changes</p>
				<p class="overlay-body">You have unsaved changes. Discard them?</p>
				<div class="overlay-actions">
					<button class="btn-danger" onclick={() => editor.resolveUnsavedChanges('discard')}>Discard</button>
					<button class="btn-ghost"  onclick={() => editor.resolveUnsavedChanges('cancel')}>Cancel</button>
				</div>
			</div>
		</div>
	{/if}

	{#if editor.exportStatus.outputPath && !editor.exportStatus.active}
		<div class="export-toast">
			<span class="export-toast-icon">✓</span>
			<span>Export complete — <code>{editor.exportStatus.outputPath}</code></span>
			<button class="btn-ghost btn-sm" onclick={() => { editor.exportStatus = { ...editor.exportStatus, outputPath: null }; }}>✕</button>
		</div>
	{/if}

	<!-- ── Page area ── -->
	<div class="page-area">
		{#if editor.activePage === 'media'}
			<MediaPage {editor} {native} />
		{:else if editor.activePage === 'edit'}
			<EditPage {editor} {tabId} />
		{:else if editor.activePage === 'annotate'}
			<AnnotatePage {editor} />
		{:else if editor.activePage === 'audio'}
			<AudioPage {editor} />
		{:else if editor.activePage === 'color'}
			<ColorPage {editor} />
		{:else if editor.activePage === 'export'}
			<ExportPage {editor} />
		{/if}
	</div>

	<!-- ── Page navigation (always at bottom) ── -->
	<PageNav {editor} />

</div>

<style>
	.app-recorder {
		display: flex;
		flex-direction: column;
		height: 100%;
		width: 100%;
		background: rgb(var(--color-bg-primary));
		color: rgb(var(--color-text-primary));
		position: relative;
		overflow: hidden;
	}

	/* Page fills all space above nav */
	.page-area {
		flex: 1;
		min-height: 0;
		overflow: hidden;
		display: flex;
		flex-direction: column;
	}

	/* ── Global overlays ── */
	.overlay-backdrop {
		position: absolute;
		inset: 0;
		background: rgb(0 0 0 / 0.6);
		display: flex;
		align-items: center;
		justify-content: center;
		z-index: 200;
		backdrop-filter: blur(var(--glass-blur));
	}
	.overlay-card {
		background: rgb(var(--color-surface-1));
		border: 1px solid rgb(255 255 255 / 0.1);
		border-radius: var(--radius-lg);
		padding: 1.5rem;
		min-width: 320px;
		max-width: 480px;
		display: flex;
		flex-direction: column;
		gap: 0.75rem;
	}
	.overlay-title { font-size: 1rem; font-weight: 600; margin: 0; }
	.overlay-body  { font-size: 0.875rem; opacity: 0.7; margin: 0; }
	.overlay-actions { display: flex; gap: 0.5rem; justify-content: flex-end; }

	/* ── Export toast ── */
	.export-toast {
		position: absolute;
		bottom: 3.5rem; /* above PageNav */
		left: 50%;
		transform: translateX(-50%);
		background: rgb(var(--color-success) / 0.15);
		border: 1px solid rgb(var(--color-success) / 0.4);
		border-radius: var(--radius-md);
		padding: 0.6rem 1rem;
		display: flex;
		align-items: center;
		gap: 0.5rem;
		font-size: 0.8rem;
		z-index: 100;
		max-width: 480px;
	}
	.export-toast-icon { color: rgb(var(--color-success)); font-weight: bold; }

	/* ── Buttons ── */
	.btn-primary {
		padding: 0.6rem 1.4rem;
		background: rgb(var(--color-accent-primary));
		color: rgb(var(--color-text-primary));
		border: none;
		border-radius: var(--radius-md);
		font-weight: 600;
		cursor: pointer;
		font-size: 0.875rem;
	}
	.btn-primary:hover { background: rgb(var(--color-accent-hover)); }
	.btn-ghost {
		padding: 0.5rem 1rem;
		background: transparent;
		color: inherit;
		border: 1px solid rgb(255 255 255 / 0.15);
		border-radius: var(--radius-sm);
		cursor: pointer;
		font-size: 0.875rem;
	}
	.btn-ghost:hover { background: rgb(255 255 255 / 0.05); }
	.btn-danger {
		padding: 0.5rem 1rem;
		background: rgb(var(--color-error));
		color: rgb(var(--color-text-primary));
		border: none;
		border-radius: var(--radius-sm);
		cursor: pointer;
		font-size: 0.875rem;
	}
	.btn-danger:hover { background: rgb(var(--color-error) / 0.85); }
	.btn-sm { padding: 0.25rem 0.5rem; font-size: 0.75rem; }
</style>
