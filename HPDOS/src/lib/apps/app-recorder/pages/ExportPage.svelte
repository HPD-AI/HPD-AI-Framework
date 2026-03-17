<script lang="ts">
	import type { AppRecorderState } from '../AppRecorderState.svelte';
	import type {
		ExportFormat, ExportQuality, GifFrameRate, GifSizePreset, AspectRatio,
	} from '../AppRecorderState.svelte';
	import { ASPECT_RATIO_DIMENSIONS } from '../AppRecorderState.svelte';

	let { editor }: { editor: AppRecorderState } = $props();

	// ── Chip helpers ──────────────────────────────────────────────────────────

	const formatOptions: { value: ExportFormat; label: string }[] = [
		{ value: 'mp4', label: 'MP4' },
		{ value: 'gif', label: 'GIF' },
	];

	const qualityOptions: { value: ExportQuality; label: string }[] = [
		{ value: 'medium', label: 'Medium' },
		{ value: 'good',   label: 'High' },
		{ value: 'source', label: 'Source' },
	];

	const gifFpsOptions: { value: GifFrameRate; label: string }[] = [
		{ value: 10, label: '10 fps' },
		{ value: 15, label: '15 fps' },
		{ value: 20, label: '20 fps' },
		{ value: 24, label: '24 fps' },
	];

	const gifSizeOptions: { value: GifSizePreset; label: string }[] = [
		{ value: 'small',  label: 'Small' },
		{ value: 'medium', label: 'Medium' },
		{ value: 'large',  label: 'Large' },
	];

	const aspectOptions: { value: AspectRatio; label: string }[] = [
		{ value: '16:9',  label: '16:9' },
		{ value: '4:3',   label: '4:3' },
		{ value: '1:1',   label: '1:1' },
		{ value: '9:16',  label: '9:16' },
		{ value: '21:9',  label: '21:9' },
	];

	// ── Output resolution display ──────────────────────────────────────────────
	const resLabel = $derived.by(() => {
		const { width, height } = editor.outputResolution;
		return `${width} × ${height}`;
	});

	// ── Recent exports formatted ───────────────────────────────────────────────
	function formatBytes(bytes: number): string {
		if (bytes === 0) return '—';
		if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
		return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
	}

	function formatRelative(ms: number): string {
		const diff = Date.now() - ms;
		const mins = Math.floor(diff / 60_000);
		if (mins < 1) return 'Just now';
		if (mins < 60) return `${mins}m ago`;
		const hrs = Math.floor(mins / 60);
		if (hrs < 24) return `${hrs}h ago`;
		return `${Math.floor(hrs / 24)}d ago`;
	}
</script>

<div class="export-page">

	<!-- ── Left: Export settings ── -->
	<div class="settings-col">

		<!-- Format -->
		<div class="card">
			<div class="card-label">Format</div>
			<div class="chip-group">
				{#each formatOptions as opt}
					<button
						class="chip"
						class:active={editor.exportSettings.format === opt.value}
						onclick={() => editor.setExportSettings({ format: opt.value })}
					>{opt.label}</button>
				{/each}
			</div>
		</div>

		<!-- Quality (MP4 only) -->
		{#if editor.exportSettings.format === 'mp4'}
			<div class="card">
				<div class="card-label">Quality</div>
				<div class="chip-group">
					{#each qualityOptions as opt}
						<button
							class="chip"
							class:active={editor.exportSettings.quality === opt.value}
							onclick={() => editor.setExportSettings({ quality: opt.value })}
						>{opt.label}</button>
					{/each}
				</div>
			</div>
		{/if}

		<!-- GIF options -->
		{#if editor.exportSettings.format === 'gif'}
			<div class="card">
				<div class="card-label">Frame Rate</div>
				<div class="chip-group">
					{#each gifFpsOptions as opt}
						<button
							class="chip"
							class:active={editor.exportSettings.gifFrameRate === opt.value}
							onclick={() => editor.setExportSettings({ gifFrameRate: opt.value })}
						>{opt.label}</button>
					{/each}
				</div>
				<div class="toggle-row" style="margin-top: 0.75rem;">
					<span class="toggle-label">Loop</span>
					<button
						class="toggle"
						class:on={editor.exportSettings.gifLoop}
						role="switch"
						aria-checked={editor.exportSettings.gifLoop}
						onclick={() => editor.setExportSettings({ gifLoop: !editor.exportSettings.gifLoop })}
					>
						<span class="toggle-thumb"></span>
					</button>
				</div>
				<div class="card-label" style="margin-top: 0.875rem;">Size</div>
				<div class="chip-group">
					{#each gifSizeOptions as opt}
						<button
							class="chip"
							class:active={editor.exportSettings.gifSize === opt.value}
							onclick={() => editor.setExportSettings({ gifSize: opt.value })}
						>{opt.label}</button>
					{/each}
				</div>
			</div>
		{/if}

		<!-- Aspect ratio -->
		<div class="card">
			<div class="card-label">Aspect Ratio</div>
			<div class="chip-group">
				{#each aspectOptions as opt}
					<button
						class="chip"
						class:active={editor.aspectRatio === opt.value}
						onclick={() => editor.setAspectRatio(opt.value)}
					>{opt.label}</button>
				{/each}
			</div>
			<div class="resolution-hint">{resLabel}</div>
		</div>

		<!-- Export button -->
		<button
			class="export-btn"
			disabled={editor.exportStatus.active || editor.clips.length === 0}
			onclick={() => editor.startExport()}
		>
			{#if editor.exportStatus.active}
				<span class="spinner" aria-hidden="true"></span>
				Exporting…
			{:else}
				<span aria-hidden="true">⬆</span>
				Export Video
			{/if}
		</button>

		{#if editor.clips.length === 0}
			<p class="no-video-hint">Import or record a video first.</p>
		{/if}

	</div>

	<!-- ── Right: Preview + queue ── -->
	<div class="queue-col">

		<!-- Clip preview -->
		<div class="card preview-card">
			<div class="preview-thumb">
				{#if editor.clips.length > 0}
					<video src={editor.clips[0]?.path} class="preview-video" muted preload="metadata"></video>
				{:else}
					<div class="preview-placeholder">No video loaded</div>
				{/if}
				<div class="preview-badge">
					{editor.exportSettings.format.toUpperCase()} · {resLabel}
				</div>
			</div>
		</div>

		<!-- Active export progress -->
		{#if editor.exportStatus.active}
			<div class="card progress-card">
				<div class="progress-header">
					<span class="format-badge">{editor.exportStatus.format?.toUpperCase() ?? ''}</span>
					<span class="progress-pct">{editor.exportStatus.percent}%</span>
				</div>
				<div class="progress-track">
					<div class="progress-fill" style:width="{editor.exportStatus.percent}%"></div>
				</div>
				<button class="btn-ghost btn-sm cancel-btn" onclick={() => editor.failExport('Cancelled')}>
					Cancel
				</button>
			</div>
		{/if}

		<!-- Recent exports -->
		<div class="card recent-card">
			<div class="card-label">Recent Exports</div>
			{#if editor.recentExports.length === 0}
				<p class="empty-hint">No exports yet.</p>
			{:else}
				<ul class="export-list">
					{#each editor.recentExports as exp (exp.id)}
						<li class="export-row">
							<span class="format-badge">{exp.format.toUpperCase()}</span>
							<span class="export-filename" title={exp.outputPath}>{exp.filename}</span>
							<span class="export-meta">{formatBytes(exp.fileSizeBytes)} · {formatRelative(exp.exportedAt)}</span>
							<button
								class="finder-link"
								onclick={() => {/* TODO: shell open */}}
								title="Show in Finder"
							>↗</button>
						</li>
					{/each}
				</ul>
			{/if}
		</div>

	</div>
</div>

<style>
	.export-page {
		flex: 1;
		display: grid;
		grid-template-columns: 360px 1fr;
		gap: 0;
		min-height: 0;
		overflow: hidden;
		background: rgb(var(--color-bg-primary));
	}

	/* ── Columns ── */
	.settings-col,
	.queue-col {
		display: flex;
		flex-direction: column;
		gap: 0.75rem;
		padding: 1.25rem;
		overflow-y: auto;
	}

	.settings-col {
		border-right: 1px solid rgb(var(--color-border-default));
	}

	/* ── Cards ── */
	.card {
		background: rgb(var(--color-surface-1));
		border: 1px solid rgb(var(--color-border-subtle));
		border-radius: var(--radius-md);
		padding: 0.875rem 1rem;
	}

	.card-label {
		font-size: var(--font-size-xs);
		font-weight: var(--font-weight-semibold);
		text-transform: uppercase;
		letter-spacing: 0.06em;
		color: rgb(var(--color-text-secondary));
		margin-bottom: 0.6rem;
	}

	/* ── Chip groups ── */
	.chip-group {
		display: flex;
		flex-wrap: wrap;
		gap: 0.375rem;
	}

	.chip {
		padding: 0.3rem 0.75rem;
		border-radius: var(--radius-sm);
		border: 1px solid rgb(var(--color-border-default));
		background: rgb(var(--color-bg-tertiary));
		color: rgb(var(--color-text-secondary));
		font-size: var(--font-size-sm);
		font-weight: var(--font-weight-medium);
		cursor: pointer;
		transition: all var(--duration-fast);
	}
	.chip:hover { color: rgb(var(--color-text-primary)); border-color: rgb(var(--color-border-default)); }
	.chip.active {
		background: rgb(var(--color-accent-primary) / 0.15);
		border-color: rgb(var(--color-accent-primary) / 0.6);
		color: rgb(var(--color-accent-light));
	}

	/* ── Toggle ── */
	.toggle-row {
		display: flex;
		align-items: center;
		justify-content: space-between;
	}
	.toggle-label {
		font-size: var(--font-size-sm);
		color: rgb(var(--color-text-primary));
	}
	.toggle {
		width: 36px;
		height: 20px;
		border-radius: 10px;
		background: rgb(var(--color-bg-active));
		border: 1px solid rgb(var(--color-border-default));
		cursor: pointer;
		position: relative;
		padding: 0;
		transition: background var(--duration-fast);
	}
	.toggle.on {
		background: rgb(var(--color-accent-primary));
		border-color: rgb(var(--color-accent-primary));
	}
	.toggle-thumb {
		position: absolute;
		top: 2px;
		left: 2px;
		width: 14px;
		height: 14px;
		border-radius: 50%;
		background: white;
		transition: transform var(--duration-fast);
	}
	.toggle.on .toggle-thumb { transform: translateX(16px); }

	/* ── Resolution hint ── */
	.resolution-hint {
		margin-top: 0.5rem;
		font-size: var(--font-size-xs);
		color: rgb(var(--color-text-tertiary));
		font-variant-numeric: tabular-nums;
	}

	/* ── Export button ── */
	.export-btn {
		display: flex;
		align-items: center;
		justify-content: center;
		gap: 0.5rem;
		width: 100%;
		height: 48px;
		background: rgb(var(--color-accent-primary));
		color: rgb(var(--color-bg-primary));
		border: none;
		border-radius: var(--radius-md);
		font-size: var(--font-size-base);
		font-weight: var(--font-weight-semibold);
		cursor: pointer;
		transition: background var(--duration-fast);
	}
	.export-btn:hover:not(:disabled) { background: rgb(var(--color-accent-hover)); }
	.export-btn:disabled { opacity: 0.45; cursor: not-allowed; }

	.spinner {
		width: 14px;
		height: 14px;
		border: 2px solid rgb(255 255 255 / 0.3);
		border-top-color: white;
		border-radius: 50%;
		animation: spin 0.7s linear infinite;
	}
	@keyframes spin { to { transform: rotate(360deg); } }

	.no-video-hint {
		font-size: var(--font-size-xs);
		color: rgb(var(--color-text-tertiary));
		text-align: center;
		margin: 0;
	}

	/* ── Preview card ── */
	.preview-card { padding: 0; overflow: hidden; }
	.preview-thumb {
		position: relative;
		aspect-ratio: 16 / 9;
		background: rgb(var(--color-bg-tertiary));
		display: flex;
		align-items: center;
		justify-content: center;
	}
	.preview-video {
		width: 100%;
		height: 100%;
		object-fit: contain;
	}
	.preview-placeholder {
		font-size: var(--font-size-sm);
		color: rgb(var(--color-text-tertiary));
	}
	.preview-badge {
		position: absolute;
		bottom: 0.5rem;
		right: 0.5rem;
		background: rgb(0 0 0 / 0.65);
		border-radius: var(--radius-sm);
		padding: 0.2rem 0.5rem;
		font-size: var(--font-size-xs);
		color: rgb(var(--color-text-primary));
		font-variant-numeric: tabular-nums;
	}

	/* ── Progress card ── */
	.progress-card { display: flex; flex-direction: column; gap: 0.5rem; }
	.progress-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
	}
	.progress-pct {
		font-size: var(--font-size-sm);
		font-variant-numeric: tabular-nums;
		color: rgb(var(--color-text-secondary));
	}
	.progress-track {
		height: 4px;
		border-radius: 2px;
		background: rgb(var(--color-bg-active));
		overflow: hidden;
	}
	.progress-fill {
		height: 100%;
		background: rgb(var(--color-accent-primary));
		border-radius: 2px;
		transition: width 0.2s ease;
	}
	.cancel-btn { align-self: flex-end; }

	/* ── Format badge ── */
	.format-badge {
		display: inline-block;
		padding: 0.1rem 0.4rem;
		border-radius: var(--radius-sm);
		background: rgb(var(--color-accent-primary) / 0.15);
		border: 1px solid rgb(var(--color-accent-primary) / 0.3);
		color: rgb(var(--color-accent-light));
		font-size: 0.65rem;
		font-weight: var(--font-weight-semibold);
		letter-spacing: 0.05em;
	}

	/* ── Recent exports ── */
	.recent-card { flex: 1; }
	.empty-hint {
		font-size: var(--font-size-sm);
		color: rgb(var(--color-text-tertiary));
		margin: 0;
	}
	.export-list {
		list-style: none;
		margin: 0;
		padding: 0;
		display: flex;
		flex-direction: column;
		gap: 0.1rem;
	}
	.export-row {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		padding: 0.45rem 0;
		border-bottom: 1px solid rgb(var(--color-border-subtle));
		font-size: var(--font-size-sm);
	}
	.export-row:last-child { border-bottom: none; }
	.export-filename {
		flex: 1;
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
		color: rgb(var(--color-text-primary));
	}
	.export-meta {
		color: rgb(var(--color-text-tertiary));
		font-size: var(--font-size-xs);
		white-space: nowrap;
		font-variant-numeric: tabular-nums;
	}
	.finder-link {
		background: none;
		border: none;
		color: rgb(var(--color-accent-primary));
		cursor: pointer;
		font-size: 0.8rem;
		padding: 0.1rem 0.25rem;
		border-radius: var(--radius-sm);
		transition: background var(--duration-fast);
	}
	.finder-link:hover { background: rgb(var(--color-accent-primary) / 0.1); }

	/* ── Shared ── */
	.btn-ghost {
		padding: 0.4rem 0.75rem;
		background: transparent;
		color: inherit;
		border: 1px solid rgb(var(--color-border-default));
		border-radius: var(--radius-sm);
		cursor: pointer;
		font-size: var(--font-size-sm);
	}
	.btn-ghost:hover { background: rgb(255 255 255 / 0.05); }
	.btn-sm { padding: 0.25rem 0.5rem; font-size: var(--font-size-xs); }
</style>
