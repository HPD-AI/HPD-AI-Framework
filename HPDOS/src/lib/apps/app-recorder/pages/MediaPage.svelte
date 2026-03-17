<script lang="ts">
	import type { AppRecorderState } from '../AppRecorderState.svelte';

	let { editor, native = false }: { editor: AppRecorderState; native?: boolean } = $props();

	function formatRelativeTime(ms: number): string {
		const diff = Date.now() - ms;
		const mins  = Math.floor(diff / 60_000);
		const hours = Math.floor(diff / 3_600_000);
		const days  = Math.floor(diff / 86_400_000);
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

	// Current clip stats derived from state
	const primaryClip = $derived(editor.clips[0] ?? null);
	const hasClip  = $derived(editor.clips.length > 0);
	const clipName = $derived(primaryClip?.path.split('/').pop() ?? '');
	const clipMeta = $derived(primaryClip ? editor.clipMetadata.get(primaryClip.id) ?? null : null);
</script>

<div class="media-page">

	<!-- ── Left: Actions + Project Library ── -->
	<div class="library-col">

		<div class="library-header">
			<span class="col-title">Media</span>
		</div>

		<!-- Record / Import CTAs -->
		<div class="cta-section">
			{#if native}
				<button
					class="cta-record"
					onclick={() => editor.openSourcePicker([])}
				>
					<svg class="cta-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
						<circle cx="12" cy="12" r="3" fill="currentColor" stroke="none"/>
						<circle cx="12" cy="12" r="8"/>
					</svg>
					<div class="cta-text">
						<span class="cta-title">Record Screen</span>
						<span class="cta-sub">Capture screen, window, or app</span>
					</div>
				</button>
			{/if}
			<button
				class="cta-import"
				onclick={() => editor.openImportPicker()}
			>
				<svg class="cta-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
					<path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"/>
					<line x1="12" y1="11" x2="12" y2="17"/>
					<polyline points="9 14 12 17 15 14"/>
				</svg>
				<div class="cta-text">
					<span class="cta-title">Import Video</span>
					<span class="cta-sub">Open an existing video file</span>
				</div>
			</button>
		</div>

		<!-- Project library -->
		<div class="library-section">
			<div class="section-header">
				<span class="section-title">Recent Projects</span>
				{#if editor.recentProjects.length > 0}
					<span class="section-count">{editor.recentProjects.length}</span>
				{/if}
			</div>

			{#if editor.recentProjects.length === 0}
				<div class="library-empty">
					<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1" class="empty-icon">
						<rect x="2" y="3" width="20" height="14" rx="2"/>
						<path d="M8 21h8M12 17v4"/>
					</svg>
					<p>No recent projects</p>
					<span>Record or import a video to get started.</span>
				</div>
			{:else}
				<ul class="project-list">
					{#each editor.recentProjects as project (project.id)}
						<li
							class="project-row"
							class:active={editor.projectId === project.id}
							role="button"
							tabindex="0"
							onclick={() => { /* agent handles load */ }}
							onkeydown={(e) => { if (e.key === 'Enter') { /* agent handles load */ } }}
						>
							<div class="project-thumb">
								{#if project.thumbnailUrl}
									<img src={project.thumbnailUrl} alt={project.name} />
								{:else}
									<div class="thumb-placeholder">🎬</div>
								{/if}
							</div>
							<div class="project-info">
								<span class="project-name">{project.name}</span>
								<span class="project-meta">{formatRelativeTime(project.lastEditedAt)}</span>
							</div>
							{#if editor.projectId === project.id}
								<span class="active-badge">Open</span>
							{/if}
						</li>
					{/each}
				</ul>
			{/if}
		</div>

	</div>

	<!-- ── Right: Current clip preview + open actions ── -->
	<div class="preview-col">

		{#if !hasClip}
			<!-- No clip loaded — large welcome state -->
			<div class="welcome-state">
				<div class="welcome-graphic">
					<svg viewBox="0 0 80 60" fill="none" class="welcome-svg">
						<rect x="4" y="8" width="72" height="44" rx="4" fill="rgb(var(--color-surface-1))" stroke="rgb(var(--color-border-default))" stroke-width="1.5"/>
						<circle cx="40" cy="30" r="12" fill="rgb(var(--color-accent-primary) / 0.15)" stroke="rgb(var(--color-accent-primary))" stroke-width="1.5"/>
						<polygon points="36,24 36,36 50,30" fill="rgb(var(--color-accent-primary))"/>
						<rect x="12" y="52" width="16" height="3" rx="1.5" fill="rgb(var(--color-border-default))"/>
						<rect x="32" y="52" width="20" height="3" rx="1.5" fill="rgb(var(--color-border-default))"/>
						<rect x="56" y="52" width="12" height="3" rx="1.5" fill="rgb(var(--color-border-default))"/>
					</svg>
				</div>
				<h2 class="welcome-title">HPD Video</h2>
				<p class="welcome-sub">AI-powered screen recording and video editing</p>

				<div class="welcome-actions">
					{#if native}
						<button class="welcome-btn primary" onclick={() => editor.openSourcePicker([])}>
							Record Screen
						</button>
					{/if}
					<button class="welcome-btn secondary" onclick={() => editor.openImportPicker()}>
						Import Video
					</button>
				</div>

				{#if native}
					<div class="native-badge">
						<span class="badge-dot"></span>
						Native recording ready
					</div>
				{/if}
			</div>

		{:else}
			<!-- Clip loaded — show preview + edit action -->
			<div class="clip-preview">

				<div class="preview-header">
					<span class="preview-title">Current Clip</span>
					<button
						class="edit-btn"
						onclick={() => editor.setActivePage('edit')}
					>
						Open in Edit ✂
					</button>
				</div>

				<div class="video-preview-wrap">
					<video
						src={primaryClip?.path}
						class="video-preview"
						preload="metadata"
						controls
					></video>
				</div>

				<div class="clip-meta-grid">
					<div class="meta-item">
						<span class="meta-key">File</span>
						<span class="meta-val" title={primaryClip?.path}>{clipName}</span>
					</div>
					{#if clipMeta}
						<div class="meta-item">
							<span class="meta-key">Resolution</span>
							<span class="meta-val">{clipMeta.width}×{clipMeta.height}</span>
						</div>
						<div class="meta-item">
							<span class="meta-key">Frame rate</span>
							<span class="meta-val">{clipMeta.fps} fps</span>
						</div>
					{/if}
					{#if editor.durationMs > 0}
						<div class="meta-item">
							<span class="meta-key">Duration</span>
							<span class="meta-val">{formatDuration(editor.durationMs)}</span>
						</div>
					{/if}
					<div class="meta-item">
						<span class="meta-key">Aspect ratio</span>
						<span class="meta-val">{editor.aspectRatio}</span>
					</div>
				</div>

				<!-- Quick action row -->
				<div class="quick-actions">
					<button class="quick-btn" onclick={() => editor.setActivePage('edit')}>✂ Edit</button>
					<button class="quick-btn" onclick={() => editor.setActivePage('annotate')}>✏ Annotate</button>
					<button class="quick-btn" onclick={() => editor.setActivePage('export')}>⬆ Export</button>
				</div>

			</div>
		{/if}

	</div>

</div>

<style>
	.media-page {
		flex: 1;
		min-height: 0;
		display: flex;
		flex-direction: row;
		height: 100%;
		overflow: hidden;
		background: rgb(var(--color-bg-primary));
	}

	/* ── Left column: library ── */
	.library-col {
		width: 280px;
		flex-shrink: 0;
		display: flex;
		flex-direction: column;
		background: rgb(var(--color-bg-secondary));
		border-right: 1px solid rgb(var(--color-border-default));
		overflow: hidden;
	}

	.library-header {
		padding: 0.75rem 1rem 0.5rem;
		border-bottom: 1px solid rgb(var(--color-border-subtle));
		flex-shrink: 0;
	}
	.col-title {
		font-size: 0.7rem;
		font-weight: 700;
		text-transform: uppercase;
		letter-spacing: 0.08em;
		color: rgb(var(--color-text-secondary));
	}

	/* ── CTAs ── */
	.cta-section {
		display: flex;
		flex-direction: column;
		gap: 0.375rem;
		padding: 0.75rem;
		border-bottom: 1px solid rgb(var(--color-border-subtle));
		flex-shrink: 0;
	}

	.cta-record,
	.cta-import {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		padding: 0.625rem 0.75rem;
		border-radius: var(--radius-md);
		border: 1px solid transparent;
		cursor: pointer;
		text-align: left;
		transition: background var(--duration-fast), border-color var(--duration-fast);
		width: 100%;
	}

	.cta-record {
		background: rgb(var(--color-accent-primary) / 0.1);
		border-color: rgb(var(--color-accent-primary) / 0.25);
		color: rgb(var(--color-accent-primary));
	}
	.cta-record:hover {
		background: rgb(var(--color-accent-primary) / 0.18);
		border-color: rgb(var(--color-accent-primary) / 0.5);
	}

	.cta-import {
		background: rgb(var(--color-surface-1));
		border-color: rgb(var(--color-border-default));
		color: rgb(var(--color-text-primary));
	}
	.cta-import:hover {
		background: rgb(var(--color-surface-2));
		border-color: rgb(var(--color-border-default));
	}

	.cta-icon {
		width: 18px;
		height: 18px;
		flex-shrink: 0;
	}
	.cta-text { display: flex; flex-direction: column; gap: 0.1rem; }
	.cta-title { font-size: 0.8rem; font-weight: 600; line-height: 1.2; }
	.cta-sub   { font-size: 0.65rem; opacity: 0.65; line-height: 1.2; }

	/* ── Library section ── */
	.library-section {
		display: flex;
		flex-direction: column;
		flex: 1;
		min-height: 0;
		overflow: hidden;
	}
	.section-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 0.6rem 1rem 0.4rem;
		flex-shrink: 0;
	}
	.section-title {
		font-size: 0.7rem;
		font-weight: 600;
		text-transform: uppercase;
		letter-spacing: 0.06em;
		color: rgb(var(--color-text-secondary));
	}
	.section-count {
		font-size: 0.65rem;
		color: rgb(var(--color-text-tertiary));
		background: rgb(var(--color-surface-1));
		border-radius: 999px;
		padding: 0.1rem 0.4rem;
	}

	.library-empty {
		display: flex;
		flex-direction: column;
		align-items: center;
		justify-content: center;
		flex: 1;
		padding: 1.5rem;
		text-align: center;
		gap: 0.4rem;
	}
	.empty-icon { width: 40px; height: 40px; opacity: 0.2; margin-bottom: 0.25rem; }
	.library-empty p    { font-size: 0.8rem; color: rgb(var(--color-text-secondary)); margin: 0; }
	.library-empty span { font-size: 0.7rem; color: rgb(var(--color-text-tertiary)); }

	.project-list {
		list-style: none;
		margin: 0;
		padding: 0.25rem 0;
		overflow-y: auto;
		flex: 1;
	}
	.project-row {
		display: flex;
		align-items: center;
		gap: 0.6rem;
		padding: 0.4rem 0.75rem;
		cursor: pointer;
		border-left: 2px solid transparent;
		transition: background var(--duration-fast), border-color var(--duration-fast);
	}
	.project-row:hover { background: rgb(var(--color-surface-1)); }
	.project-row.active {
		background: rgb(var(--color-accent-primary) / 0.08);
		border-left-color: rgb(var(--color-accent-primary));
	}
	.project-thumb {
		width: 44px;
		height: 28px;
		border-radius: 3px;
		overflow: hidden;
		background: rgb(var(--color-surface-1));
		flex-shrink: 0;
		display: flex;
		align-items: center;
		justify-content: center;
	}
	.project-thumb img { width: 100%; height: 100%; object-fit: cover; }
	.thumb-placeholder { font-size: 0.9rem; }
	.project-info { display: flex; flex-direction: column; gap: 0.1rem; flex: 1; min-width: 0; }
	.project-name {
		font-size: 0.75rem;
		font-weight: 500;
		color: rgb(var(--color-text-primary));
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}
	.project-meta { font-size: 0.65rem; color: rgb(var(--color-text-tertiary)); }
	.active-badge {
		font-size: 0.6rem;
		font-weight: 600;
		color: rgb(var(--color-accent-primary));
		background: rgb(var(--color-accent-primary) / 0.12);
		border-radius: var(--radius-sm);
		padding: 0.15rem 0.4rem;
		flex-shrink: 0;
	}

	/* ── Right column: preview ── */
	.preview-col {
		flex: 1;
		display: flex;
		flex-direction: column;
		align-items: center;
		justify-content: center;
		min-width: 0;
		overflow: hidden;
		background: rgb(var(--color-bg-primary));
	}

	/* ── Welcome state ── */
	.welcome-state {
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 1rem;
		text-align: center;
		max-width: 380px;
		padding: 2rem;
	}
	.welcome-graphic { margin-bottom: 0.5rem; }
	.welcome-svg { width: 160px; height: 120px; }
	.welcome-title {
		font-size: 1.5rem;
		font-weight: 700;
		color: rgb(var(--color-text-primary));
		margin: 0;
	}
	.welcome-sub {
		font-size: 0.875rem;
		color: rgb(var(--color-text-secondary));
		margin: 0;
		line-height: 1.5;
	}
	.welcome-actions {
		display: flex;
		gap: 0.75rem;
		margin-top: 0.5rem;
	}
	.welcome-btn {
		padding: 0.6rem 1.5rem;
		border-radius: var(--radius-md);
		font-size: 0.875rem;
		font-weight: 600;
		cursor: pointer;
		border: none;
		transition: all var(--duration-fast);
	}
	.welcome-btn.primary {
		background: rgb(var(--color-accent-primary));
		color: #fff;
	}
	.welcome-btn.primary:hover { background: rgb(var(--color-accent-hover)); }
	.welcome-btn.secondary {
		background: rgb(var(--color-surface-1));
		color: rgb(var(--color-text-primary));
		border: 1px solid rgb(var(--color-border-default));
	}
	.welcome-btn.secondary:hover { background: rgb(var(--color-surface-2)); }

	.native-badge {
		display: flex;
		align-items: center;
		gap: 0.4rem;
		font-size: 0.7rem;
		color: rgb(var(--color-success));
		background: rgb(var(--color-success) / 0.1);
		border: 1px solid rgb(var(--color-success) / 0.25);
		border-radius: 999px;
		padding: 0.25rem 0.75rem;
	}
	.badge-dot {
		width: 6px;
		height: 6px;
		border-radius: 50%;
		background: rgb(var(--color-success));
		animation: pulse 2s ease-in-out infinite;
	}
	@keyframes pulse {
		0%, 100% { opacity: 1; }
		50%       { opacity: 0.4; }
	}

	/* ── Clip preview (when loaded) ── */
	.clip-preview {
		display: flex;
		flex-direction: column;
		gap: 1rem;
		width: 100%;
		max-width: 700px;
		padding: 1.5rem;
		height: 100%;
		overflow-y: auto;
	}
	.preview-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		flex-shrink: 0;
	}
	.preview-title {
		font-size: 0.8rem;
		font-weight: 600;
		text-transform: uppercase;
		letter-spacing: 0.06em;
		color: rgb(var(--color-text-secondary));
	}
	.edit-btn {
		padding: 0.35rem 0.75rem;
		background: rgb(var(--color-accent-primary));
		color: #fff;
		border: none;
		border-radius: var(--radius-sm);
		font-size: 0.75rem;
		font-weight: 600;
		cursor: pointer;
	}
	.edit-btn:hover { background: rgb(var(--color-accent-hover)); }

	.video-preview-wrap {
		width: 100%;
		border-radius: var(--radius-md);
		overflow: hidden;
		background: #000;
		aspect-ratio: 16 / 9;
		flex-shrink: 0;
	}
	.video-preview { width: 100%; height: 100%; display: block; }

	.clip-meta-grid {
		display: grid;
		grid-template-columns: 1fr 1fr;
		gap: 0.5rem 1rem;
	}
	.meta-item { display: flex; flex-direction: column; gap: 0.1rem; }
	.meta-key  { font-size: 0.65rem; color: rgb(var(--color-text-tertiary)); text-transform: uppercase; letter-spacing: 0.05em; }
	.meta-val  {
		font-size: 0.8rem;
		color: rgb(var(--color-text-primary));
		font-family: monospace;
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
	}

	.quick-actions {
		display: flex;
		gap: 0.5rem;
		flex-shrink: 0;
	}
	.quick-btn {
		flex: 1;
		padding: 0.5rem;
		background: rgb(var(--color-surface-1));
		border: 1px solid rgb(var(--color-border-default));
		border-radius: var(--radius-md);
		color: rgb(var(--color-text-primary));
		font-size: 0.8rem;
		font-weight: 500;
		cursor: pointer;
		transition: all var(--duration-fast);
	}
	.quick-btn:hover {
		background: rgb(var(--color-surface-2));
		border-color: rgb(var(--color-accent-primary) / 0.4);
		color: rgb(var(--color-accent-primary));
	}
</style>
