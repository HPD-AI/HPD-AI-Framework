<script lang="ts">
	/**
	 * AnnotatePage — Step 15
	 *
	 * Layout:
	 * ┌──────┬─────────────────────────────┬──────────────┐
	 * │      │        VideoCanvas          │  Properties  │
	 * │ Ann. │                             │  Panel       │
	 * │ Tool │                             │              │
	 * │ bar  ├─────────────────────────────┴──────────────┤
	 * │      │     MiniTimeline (40px)                    │
	 * └──────┴────────────────────────────────────────────┘
	 *
	 * AnnotationToolbar — 48px fixed left strip
	 * VideoCanvas       — flex:1 centre
	 * PropertiesPanel   — 280px fixed right
	 * MiniTimeline      — 40px scrub-only bar below canvas + props
	 *
	 * MiniTimeline: click/drag → seekTo. Shows a coloured pip per annotation region
	 * visible at its time position. No resize handles (those are in Timeline.svelte).
	 */

	import type { AppRecorderState } from '../AppRecorderState.svelte';
	import AnnotationToolbar from '../panels/AnnotationToolbar.svelte';
	import VideoCanvas from '../canvas/VideoCanvas.svelte';
	import PropertiesPanel from '../panels/PropertiesPanel.svelte';
	import { msToPixel, pixelToMs, fitZoom } from '../timeline/timelineUtils';

	let { editor }: { editor: AppRecorderState } = $props();

	// ── MiniTimeline ──────────────────────────────────────────────────────────

	let miniEl   = $state<HTMLDivElement>(null!);
	let miniW    = $state(0);
	let dragging = $state(false);

	// px per ms — fit entire duration into mini timeline width
	const pxPerMs = $derived(
		miniW > 0 && editor.durationMs > 0
			? fitZoom(editor.durationMs, miniW)
			: 0
	);

	// Playhead position in the mini timeline
	const playheadPx = $derived(
		pxPerMs > 0
			? Math.max(0, Math.min(miniW - 1, msToPixel(editor.currentTimeMs, 0, pxPerMs)))
			: 0
	);

	$effect(() => {
		if (!miniEl) return;
		const ro = new ResizeObserver((entries) => {
			const e = entries[0];
			if (!e) return;
			miniW = e.contentRect.width;
		});
		ro.observe(miniEl);
		miniW = miniEl.clientWidth;
		return () => ro.disconnect();
	});

	function seekFromEvent(e: PointerEvent) {
		if (!miniEl || !pxPerMs) return;
		const rect = miniEl.getBoundingClientRect();
		const px   = Math.max(0, Math.min(rect.width, e.clientX - rect.left));
		editor.seekTo(pixelToMs(px, 0, pxPerMs));
	}

	function miniPointerDown(e: PointerEvent) {
		e.preventDefault();
		dragging = true;
		(e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
		seekFromEvent(e);
	}

	function miniPointerMove(e: PointerEvent) {
		if (!dragging) return;
		seekFromEvent(e);
	}

	function miniPointerUp(e: PointerEvent) {
		if (!dragging) return;
		dragging = false;
		try { (e.currentTarget as HTMLElement).releasePointerCapture(e.pointerId); } catch {}
	}

	// Annotation pips — one dot per annotation region, positioned at startMs
	// Colour: same accent tint used in RegionChip for annotations (yellow)
	const annotationPips = $derived(
		pxPerMs > 0
			? (editor.activeClip?.annotationRegions ?? []).map(a => ({
				id:  a.id,
				x:   Math.max(0, Math.min(miniW - 4, msToPixel(editor.activeClip!.position + a.startMs, 0, pxPerMs))),
				sel: a.id === editor.selectedAnnotationId,
			}))
			: []
	);

	// Props panel collapsed state — wire same toggle as EditPage
	let propsCollapsed = $state(false);
</script>

<div class="annotate-page">

	<!-- Left: annotation tool strip -->
	<AnnotationToolbar {editor} />

	<!-- Centre + right column -->
	<div class="main-col">

		<!-- Top row: canvas + properties -->
		<div class="top-row">

			<!-- VideoCanvas -->
			<div class="canvas-wrap">
				<VideoCanvas {editor} />
			</div>

			<!-- Properties panel (280px) -->
			{#if !propsCollapsed}
				<div class="props-wrap">
					<PropertiesPanel {editor} isCollapsed={false} toggle={() => { propsCollapsed = true; }} />
				</div>
			{:else}
				<button
					class="props-show-btn"
					onclick={() => { propsCollapsed = false; }}
					title="Show properties"
					aria-label="Show properties"
				>
					<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5">
						<polyline points="10,4 6,8 10,12"/>
					</svg>
				</button>
			{/if}

		</div>

		<!-- MiniTimeline — scrub only, annotation pips -->
		<!-- svelte-ignore a11y_no_static_element_interactions -->
		<div
			class="mini-timeline"
			bind:this={miniEl}
			onpointerdown={miniPointerDown}
			onpointermove={miniPointerMove}
			onpointerup={miniPointerUp}
			onpointerleave={miniPointerUp}
			role="slider"
			aria-label="Scrub timeline"
			aria-valuemin={0}
			aria-valuemax={editor.durationMs}
			aria-valuenow={editor.currentTimeMs}
			tabindex="0"
		>
			<!-- Background track -->
			<div class="mini-track"></div>

			<!-- Progress fill -->
			<div
				class="mini-progress"
				style="width: {playheadPx}px;"
			></div>

			<!-- Annotation pips -->
			{#each annotationPips as pip (pip.id)}
				<div
					class="ann-pip {pip.sel ? 'ann-pip-sel' : ''}"
					style="left: {pip.x}px;"
				></div>
			{/each}

			<!-- Playhead line -->
			<div class="mini-playhead" style="left: {playheadPx}px;"></div>

		</div>

	</div>

</div>

<style>
	.annotate-page {
		flex: 1;
		min-height: 0;
		display: flex;
		flex-direction: row;
		width: 100%;
		height: 100%;
		background: rgb(var(--color-bg-primary));
		overflow: hidden;
	}

	/* ── Main column (canvas + mini timeline) ─────────────────────────────── */
	.main-col {
		flex: 1;
		min-width: 0;
		display: flex;
		flex-direction: column;
		overflow: hidden;
	}

	/* ── Top row ──────────────────────────────────────────────────────────── */
	.top-row {
		flex: 1;
		min-height: 0;
		display: flex;
		flex-direction: row;
		overflow: hidden;
	}

	/* ── Canvas wrap ──────────────────────────────────────────────────────── */
	.canvas-wrap {
		flex: 1;
		min-width: 0;
		display: flex;
		flex-direction: column;
		overflow: hidden;
	}

	/* ── Properties panel ─────────────────────────────────────────────────── */
	.props-wrap {
		width: 280px;
		flex-shrink: 0;
		height: 100%;
		border-left: 1px solid rgb(var(--color-border-default));
		overflow: hidden;
		display: flex;
		flex-direction: column;
	}

	.props-show-btn {
		flex-shrink: 0;
		align-self: flex-start;
		margin: 8px 4px;
		width: 26px;
		height: 26px;
		background: rgb(var(--color-surface-2));
		border: 1px solid rgb(var(--color-border-default));
		border-radius: var(--radius-sm);
		color: rgb(var(--color-text-secondary));
		cursor: pointer;
		display: flex;
		align-items: center;
		justify-content: center;
		padding: 0;
		transition: all var(--duration-fast);
	}

	.props-show-btn:hover {
		background: rgb(var(--color-surface-3));
		color: rgb(var(--color-text-primary));
	}

	.props-show-btn svg { width: 13px; height: 13px; }

	/* ── MiniTimeline ─────────────────────────────────────────────────────── */
	.mini-timeline {
		flex-shrink: 0;
		height: 40px;
		position: relative;
		background: rgb(var(--color-bg-tertiary));
		border-top: 1px solid rgb(var(--color-border-default));
		cursor: pointer;
		user-select: none;
		display: flex;
		align-items: center;
	}

	.mini-track {
		position: absolute;
		left: 0;
		right: 0;
		height: 4px;
		background: rgb(var(--color-surface-3));
		border-radius: 2px;
	}

	.mini-progress {
		position: absolute;
		left: 0;
		height: 4px;
		background: rgb(var(--color-accent-primary) / 0.5);
		border-radius: 2px;
		pointer-events: none;
		max-width: 100%;
	}

	/* Annotation pip — small vertical bar at startMs position */
	.ann-pip {
		position: absolute;
		top: 8px;
		width: 3px;
		height: 24px;
		border-radius: 1.5px;
		background: rgb(234 179 8 / 0.55); /* yellow — matches annotation region colour */
		pointer-events: none;
		transform: translateX(-1px);
	}

	.ann-pip-sel {
		background: rgb(234 179 8 / 0.9);
		box-shadow: 0 0 0 1px rgb(234 179 8 / 0.4);
	}

	/* Playhead line */
	.mini-playhead {
		position: absolute;
		top: 0;
		bottom: 0;
		width: 2px;
		background: rgb(var(--color-accent-primary));
		pointer-events: none;
		transform: translateX(-1px);
		border-radius: 1px;
	}
</style>
