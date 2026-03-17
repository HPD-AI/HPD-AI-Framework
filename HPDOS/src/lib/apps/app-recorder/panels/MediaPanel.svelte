<script lang="ts">
	import type { AppRecorderState } from '../AppRecorderState.svelte';

	let { editor }: { editor: AppRecorderState } = $props();

	let activeTab = $state<'media' | 'transitions'>('media');

	function formatRelativeTime(ms: number): string {
		const diff = Date.now() - ms;
		const mins = Math.floor(diff / 60_000);
		const hours = Math.floor(diff / 3_600_000);
		const days = Math.floor(diff / 86_400_000);
		if (days > 0)  return `${days}d ago`;
		if (hours > 0) return `${hours}h ago`;
		if (mins > 0)  return `${mins}m ago`;
		return 'Just now';
	}

	function formatDuration(ms: number): string {
		const s = Math.floor(ms / 1000);
		const m = Math.floor(s / 60);
		return `${m}:${String(s % 60).padStart(2, '0')}`;
	}

	// Transition presets (placeholder until real bundle)
	const TRANSITION_PRESETS = [
		{ id: 'fade',        label: 'Fade' },
		{ id: 'wipe-right',  label: 'Wipe Right' },
		{ id: 'wipe-left',   label: 'Wipe Left' },
		{ id: 'circle-in',   label: 'Circle In' },
		{ id: 'circle-out',  label: 'Circle Out' },
		{ id: 'dissolve',    label: 'Dissolve' },
		{ id: 'blinds',      label: 'Blinds' },
		{ id: 'zoom-in',     label: 'Zoom In' },
	];
</script>

<div class="media-panel">

	<!-- ── Clip list or empty CTA ── -->
	{#if editor.clips.length === 0}
		<div class="clip-cta">
			<button class="cta-btn" onclick={() => editor.openSourcePicker([])}>
				<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
					<circle cx="12" cy="12" r="3"/>
					<path d="M3 9a2 2 0 0 1 2-2h.93a2 2 0 0 0 1.664-.89l.812-1.22A2 2 0 0 1 10.07 4h3.86a2 2 0 0 1 1.664.89l.812 1.22A2 2 0 0 0 18.07 7H19a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>
				</svg>
				Record Screen
			</button>
			<button class="cta-btn" onclick={() => editor.openImportPicker()}>
				<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
					<path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"/>
				</svg>
				Import Video
			</button>
		</div>
	{:else}
		<!-- Clip list (sorted by timeline position) -->
		<ul class="clip-list">
			{#each [...editor.clips].sort((a, b) => a.position - b.position) as clip (clip.id)}
				<!-- svelte-ignore a11y_click_events_have_key_events -->
				<li
					class="clip-row"
					class:clip-row-selected={editor.selectedClipId === clip.id}
					role="button"
					tabindex="0"
					onclick={() => editor.selectClip(clip.id)}
					onkeydown={(e) => e.key === 'Enter' && editor.selectClip(clip.id)}
				>
					<div class="clip-thumb-small">
						<video src={clip.path} class="clip-video-thumb" preload="metadata" muted></video>
					</div>
					<div class="clip-meta">
						<span class="clip-name" title={clip.path.split('/').pop()}>{clip.path.split('/').pop()}</span>
						<div class="clip-stats">
							{#if editor.clipMetadata.get(clip.id)}
								<span>{editor.clipMetadata.get(clip.id)!.width}×{editor.clipMetadata.get(clip.id)!.height}</span>
								<span>{editor.clipMetadata.get(clip.id)!.fps}fps</span>
							{/if}
							<span>{formatDuration(clip.end - clip.start)}</span>
						</div>
					</div>
					<button
						class="clip-remove-btn"
						onclick={(e) => { e.stopPropagation(); editor.removeClip(clip.id); }}
						title="Remove clip"
						aria-label="Remove clip"
					>✕</button>
				</li>
			{/each}
		</ul>
		<!-- Always-visible add buttons -->
		<div class="clip-actions">
			<button class="action-btn" onclick={() => editor.openSourcePicker([])}>Record</button>
			<button class="action-btn" onclick={() => editor.openImportPicker()}>Import</button>
		</div>
	{/if}

	<!-- ── Tabs ── -->
	<div class="tab-bar">
		<button
			class="tab"
			class:active={activeTab === 'media'}
			onclick={() => activeTab = 'media'}
		>Media</button>
		<button
			class="tab"
			class:active={activeTab === 'transitions'}
			onclick={() => activeTab = 'transitions'}
		>Transitions</button>
	</div>

	<!-- ── Tab content ── -->
	<div class="tab-content">

		{#if activeTab === 'media'}
			<!-- Recent projects -->
			{#if editor.recentProjects.length === 0}
				<div class="empty-tab">No recent projects</div>
			{:else}
				<ul class="project-list">
					{#each editor.recentProjects as project (project.id)}
						<li class="project-row" role="button" tabindex="0"
							onclick={() => {/* agent handles load */}}
							onkeydown={(e) => e.key === 'Enter' && void 0}
						>
							<div class="project-thumb">
								{#if project.thumbnailUrl}
									<img src={project.thumbnailUrl} alt={project.name} />
								{:else}
									<div class="project-thumb-placeholder">🎬</div>
								{/if}
							</div>
							<div class="project-info">
								<span class="project-name">{project.name}</span>
								<span class="project-time">{formatRelativeTime(project.lastEditedAt)}</span>
							</div>
						</li>
					{/each}
				</ul>
			{/if}

		{:else}
			<!-- Transitions grid -->
			<div class="transitions-grid">
				{#each TRANSITION_PRESETS as t (t.id)}
					<button class="transition-card" title={t.label}>
						<div class="transition-thumb">
							<div class="transition-preview"></div>
						</div>
						<span class="transition-label">{t.label}</span>
					</button>
				{/each}
			</div>
		{/if}

	</div>
</div>

<style>
	.media-panel {
		display: flex;
		flex-direction: column;
		height: 100%;
		width: 100%;
		background: rgb(var(--color-bg-secondary));
		overflow: hidden;
		border-right: 1px solid rgb(var(--color-border-default));
	}

	/* ── Clip CTA (no video loaded) ── */
	.clip-cta {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
		padding: 0.75rem;
		border-bottom: 1px solid rgb(var(--color-border-subtle));
	}
	.cta-btn {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		height: 52px;
		padding: 0 1rem;
		background: rgb(var(--color-surface-1));
		border: 1px solid rgb(var(--color-border-default));
		border-radius: var(--radius-md);
		color: rgb(var(--color-text-primary));
		font-size: 0.8125rem;
		font-weight: 500;
		cursor: pointer;
		width: 100%;
		transition: background var(--duration-fast), border-color var(--duration-fast);
	}
	.cta-btn:hover {
		background: rgb(var(--color-surface-2));
		border-color: rgb(var(--color-accent-primary) / 0.5);
	}
	.cta-btn svg { width: 16px; height: 16px; flex-shrink: 0; opacity: 0.7; }

	/* ── Clip list ── */
	.clip-list {
		list-style: none;
		margin: 0;
		padding: 0.35rem 0;
		overflow-y: auto;
		flex-shrink: 0;
		max-height: 240px;
	}
	.clip-row {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		padding: 0.35rem 0.65rem;
		cursor: pointer;
		transition: background var(--duration-fast);
		border-radius: 0;
	}
	.clip-row:hover { background: rgb(var(--color-surface-1)); }
	.clip-row-selected { background: rgb(var(--color-accent-primary) / 0.08); }
	.clip-thumb-small {
		width: 36px;
		height: 22px;
		border-radius: 2px;
		overflow: hidden;
		background: #000;
		flex-shrink: 0;
	}
	.clip-video-thumb { width: 100%; height: 100%; object-fit: cover; }
	.clip-meta { display: flex; flex-direction: column; gap: 0.15rem; flex: 1; min-width: 0; }
	.clip-name {
		font-size: 0.72rem;
		font-weight: 500;
		color: rgb(var(--color-text-primary));
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}
	.clip-stats {
		display: flex;
		gap: 0.35rem;
		font-size: 0.65rem;
		color: rgb(var(--color-text-secondary));
	}
	.clip-remove-btn {
		background: none;
		border: none;
		color: rgb(var(--color-text-tertiary));
		font-size: 0.65rem;
		cursor: pointer;
		padding: 0.15rem;
		opacity: 0;
		transition: opacity var(--duration-fast), color var(--duration-fast);
		flex-shrink: 0;
	}
	.clip-row:hover .clip-remove-btn { opacity: 1; }
	.clip-remove-btn:hover { color: rgb(239 68 68); }

	.clip-actions {
		display: flex;
		gap: 0.5rem;
		padding: 0 0.75rem 0.75rem;
		border-bottom: 1px solid rgb(var(--color-border-subtle));
	}
	.action-btn {
		flex: 1;
		padding: 0.35rem 0;
		background: rgb(var(--color-surface-1));
		border: 1px solid rgb(var(--color-border-default));
		border-radius: var(--radius-sm);
		color: rgb(var(--color-text-primary));
		font-size: 0.75rem;
		cursor: pointer;
	}
	.action-btn:hover { background: rgb(var(--color-surface-2)); }

	/* ── Tabs ── */
	.tab-bar {
		display: flex;
		border-bottom: 1px solid rgb(var(--color-border-default));
		flex-shrink: 0;
	}
	.tab {
		flex: 1;
		padding: 0.5rem;
		background: transparent;
		border: none;
		border-bottom: 2px solid transparent;
		color: rgb(var(--color-text-secondary));
		font-size: 0.75rem;
		font-weight: 500;
		cursor: pointer;
		transition: color var(--duration-fast), border-color var(--duration-fast);
	}
	.tab.active {
		color: rgb(var(--color-accent-primary));
		border-bottom-color: rgb(var(--color-accent-primary));
	}
	.tab:hover:not(.active) { color: rgb(var(--color-text-primary)); }

	/* ── Tab content ── */
	.tab-content {
		flex: 1;
		overflow-y: auto;
		min-height: 0;
	}

	.empty-tab {
		padding: 2rem 1rem;
		text-align: center;
		font-size: 0.75rem;
		color: rgb(var(--color-text-tertiary));
		font-style: italic;
	}

	/* ── Project list ── */
	.project-list { list-style: none; margin: 0; padding: 0.5rem 0; }
	.project-row {
		display: flex;
		align-items: center;
		gap: 0.625rem;
		padding: 0.4rem 0.75rem;
		cursor: pointer;
		border-radius: 0;
		transition: background var(--duration-fast);
	}
	.project-row:hover { background: rgb(var(--color-surface-1)); }
	.project-thumb {
		width: 40px;
		height: 40px;
		border-radius: var(--radius-sm);
		overflow: hidden;
		background: rgb(var(--color-surface-1));
		flex-shrink: 0;
		display: flex;
		align-items: center;
		justify-content: center;
	}
	.project-thumb img { width: 100%; height: 100%; object-fit: cover; }
	.project-thumb-placeholder { font-size: 1.25rem; }
	.project-info { display: flex; flex-direction: column; gap: 0.15rem; min-width: 0; }
	.project-name {
		font-size: 0.75rem;
		font-weight: 500;
		color: rgb(var(--color-text-primary));
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}
	.project-time { font-size: 0.7rem; color: rgb(var(--color-text-tertiary)); }

	/* ── Transitions grid ── */
	.transitions-grid {
		display: grid;
		grid-template-columns: 1fr 1fr;
		gap: 0.5rem;
		padding: 0.75rem;
	}
	.transition-card {
		display: flex;
		flex-direction: column;
		gap: 0.35rem;
		background: rgb(var(--color-surface-1));
		border: 1px solid rgb(var(--color-border-default));
		border-radius: var(--radius-sm);
		padding: 0.4rem;
		cursor: pointer;
		transition: border-color var(--duration-fast), background var(--duration-fast);
	}
	.transition-card:hover {
		border-color: rgb(var(--color-accent-primary) / 0.5);
		background: rgb(var(--color-surface-2));
	}
	.transition-thumb {
		width: 100%;
		aspect-ratio: 16 / 9;
		border-radius: 3px;
		overflow: hidden;
		background: rgb(var(--color-bg-primary));
	}
	.transition-preview {
		width: 100%;
		height: 100%;
		background: linear-gradient(135deg, rgb(var(--color-accent-primary) / 0.3), rgb(var(--color-surface-2)));
	}
	.transition-label {
		font-size: 0.65rem;
		color: rgb(var(--color-text-secondary));
		text-align: center;
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}
</style>
