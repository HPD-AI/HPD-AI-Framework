<script lang="ts">
	/**
	 * ZoomHandleOverlay — Step 14
	 *
	 * Absolutely-positioned div rendered over VideoCanvas when a zoom region is selected.
	 *
	 * Elements:
	 *   1. Crosshair  — two lines intersecting at (cx, cy), shows/moves focus point
	 *   2. Depth ring — circle centred on focus, radius = (1 / zoomScale) × half canvas min-dim
	 *      Visualises how much of the frame is visible at the current zoom depth.
	 *
	 * Interactions:
	 *   - Drag crosshair centre → updateZoomRegion({ cx, cy })
	 *   - Drag ring edge → maps drag distance to new depth (1–6)
	 *
	 * All coordinates are normalised 0–1 against the container bounding rect.
	 */

	import type { AppRecorderState } from '../AppRecorderState.svelte';
	import { ZOOM_DEPTH_SCALES } from '../AppRecorderState.svelte';
	import type { ZoomDepth } from '../AppRecorderState.svelte';

	let { editor }: { editor: AppRecorderState } = $props();

	// ── Container ref (parent passes its bounding rect) ───────────────────────
	let rootEl = $state<HTMLDivElement>(null!);

	// ── Derived: which zoom is selected and its pixel coordinates ────────────
	const zoom = $derived(editor.selectedZoom);

	// Container size — updated by ResizeObserver
	let cW = $state(0);
	let cH = $state(0);

	$effect(() => {
		if (!rootEl) return;
		const ro = new ResizeObserver((entries) => {
			const e = entries[0];
			if (!e) return;
			cW = e.contentRect.width;
			cH = e.contentRect.height;
		});
		ro.observe(rootEl);
		// Initial measurement
		cW = rootEl.clientWidth;
		cH = rootEl.clientHeight;
		return () => ro.disconnect();
	});

	// Focus point in pixels
	const fx = $derived(zoom ? zoom.cx * cW : 0);
	const fy = $derived(zoom ? zoom.cy * cH : 0);

	// Ring radius: viewport window size at current zoom = 1/scale × half min-dim
	const ringRadius = $derived(() => {
		if (!zoom || !cW || !cH) return 0;
		const scale = ZOOM_DEPTH_SCALES[zoom.depth];
		// Half of the smaller canvas dimension / scale → shows the zoomed viewport window
		return (Math.min(cW, cH) / 2) / scale;
	});

	// ── Clamp focus within bounds accounting for zoom level ──────────────────
	function clampFocus(rawCx: number, rawCy: number, depth: ZoomDepth) {
		const scale  = ZOOM_DEPTH_SCALES[depth];
		const margin = 0.5 / scale;
		return {
			cx: Math.max(margin, Math.min(1 - margin, rawCx)),
			cy: Math.max(margin, Math.min(1 - margin, rawCy)),
		};
	}

	// ── Crosshair drag ────────────────────────────────────────────────────────
	let draggingCrosshair = false;

	function crosshairPointerDown(e: PointerEvent) {
		if (!zoom) return;
		e.preventDefault();
		e.stopPropagation();
		draggingCrosshair = true;
		(e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
	}

	function crosshairPointerMove(e: PointerEvent) {
		if (!draggingCrosshair || !zoom || !rootEl) return;
		const rect = rootEl.getBoundingClientRect();
		const { cx, cy } = clampFocus(
			(e.clientX - rect.left)  / rect.width,
			(e.clientY - rect.top)   / rect.height,
			zoom.depth,
		);
		editor.updateZoomRegion(zoom.id, { cx, cy });
	}

	function crosshairPointerUp(e: PointerEvent) {
		if (!draggingCrosshair) return;
		draggingCrosshair = false;
		try { (e.currentTarget as HTMLElement).releasePointerCapture(e.pointerId); } catch {}
	}

	// ── Ring drag: maps drag distance from centre → new depth ────────────────
	let draggingRing      = false;
	let ringDragStartDist = 0; // pixel distance from centre at drag start
	let ringDragStartDepth: ZoomDepth = 1;

	const DEPTH_LEVELS: ZoomDepth[] = [1, 2, 3, 4, 5, 6];

	function ringPointerDown(e: PointerEvent) {
		if (!zoom || !rootEl) return;
		e.preventDefault();
		e.stopPropagation();
		draggingRing       = true;
		ringDragStartDepth = zoom.depth;
		const rect         = rootEl.getBoundingClientRect();
		const dx           = e.clientX - rect.left - fx;
		const dy           = e.clientY - rect.top  - fy;
		ringDragStartDist  = Math.sqrt(dx * dx + dy * dy);
		(e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
	}

	function ringPointerMove(e: PointerEvent) {
		if (!draggingRing || !zoom || !rootEl) return;
		const rect  = rootEl.getBoundingClientRect();
		const dx    = e.clientX - rect.left - fx;
		const dy    = e.clientY - rect.top  - fy;
		const dist  = Math.sqrt(dx * dx + dy * dy);

		// Larger radius = lower zoom = lower depth index
		// Smaller radius = higher zoom = higher depth index
		const ratio   = ringDragStartDist > 0 ? dist / ringDragStartDist : 1;
		// ratio > 1 → dragging out → less zoom → lower depth
		// ratio < 1 → dragging in  → more zoom → higher depth
		const startIdx  = DEPTH_LEVELS.indexOf(ringDragStartDepth);
		const deltaIdx  = Math.round((1 - ratio) * 3); // scale sensitivity
		const newIdx    = Math.max(0, Math.min(DEPTH_LEVELS.length - 1, startIdx + deltaIdx));
		const newDepth  = DEPTH_LEVELS[newIdx];

		if (newDepth !== zoom.depth) {
			editor.updateZoomRegion(zoom.id, { depth: newDepth });
		}
	}

	function ringPointerUp(e: PointerEvent) {
		if (!draggingRing) return;
		draggingRing = false;
		try { (e.currentTarget as HTMLElement).releasePointerCapture(e.pointerId); } catch {}
	}

	// Crosshair arm length
	const ARM = 20;
	// Ring stroke width
	const RING_STROKE = 1.5;
</script>

{#if zoom && cW > 0}
	<!-- svelte-ignore a11y_no_static_element_interactions -->
	<div class="zoom-overlay" bind:this={rootEl}>

		<!-- Depth ring — drag edge to change depth -->
		<!-- svelte-ignore a11y_no_static_element_interactions -->
		<div
			class="ring-hit"
			style="
				left: {fx - ringRadius()}px;
				top:  {fy - ringRadius()}px;
				width:  {ringRadius() * 2}px;
				height: {ringRadius() * 2}px;
			"
			onpointerdown={ringPointerDown}
			onpointermove={ringPointerMove}
			onpointerup={ringPointerUp}
			onpointerleave={ringPointerUp}
		>
			<svg
				class="ring-svg"
				viewBox="0 0 100 100"
				preserveAspectRatio="none"
				aria-hidden="true"
			>
				<circle
					cx="50" cy="50" r="48"
					fill="none"
					stroke="rgb(var(--color-accent-primary))"
					stroke-width="{RING_STROKE}"
					stroke-dasharray="4 3"
					opacity="0.7"
				/>
			</svg>
		</div>

		<!-- Crosshair — drag to reposition focus -->
		<!-- svelte-ignore a11y_no_static_element_interactions -->
		<div
			class="crosshair"
			style="left: {fx}px; top: {fy}px;"
			onpointerdown={crosshairPointerDown}
			onpointermove={crosshairPointerMove}
			onpointerup={crosshairPointerUp}
			onpointerleave={crosshairPointerUp}
		>
			<!-- Horizontal arm -->
			<div class="arm arm-h" style="width: {ARM * 2}px; left: {-ARM}px;"></div>
			<!-- Vertical arm -->
			<div class="arm arm-v" style="height: {ARM * 2}px; top: {-ARM}px;"></div>
			<!-- Centre dot -->
			<div class="centre-dot"></div>
		</div>

		<!-- Depth label badge -->
		<div
			class="depth-badge"
			style="left: {fx + ringRadius() + 6}px; top: {fy - 10}px;"
		>
			{ZOOM_DEPTH_SCALES[zoom.depth]}×
		</div>

	</div>
{:else}
	<!-- Still need rootEl for ResizeObserver even when zoom is null -->
	<div class="zoom-overlay zoom-overlay--hidden" bind:this={rootEl}></div>
{/if}

<style>
	.zoom-overlay {
		position: absolute;
		inset: 0;
		pointer-events: none; /* children opt-in individually */
		overflow: hidden;
	}

	.zoom-overlay--hidden {
		display: contents; /* invisible but measurable */
	}

	/* ── Ring ─────────────────────────────────────────────────────────────── */
	.ring-hit {
		position: absolute;
		cursor: ew-resize;
		pointer-events: auto;
		border-radius: 50%;
	}

	.ring-svg {
		width: 100%;
		height: 100%;
		display: block;
	}

	/* ── Crosshair ────────────────────────────────────────────────────────── */
	.crosshair {
		position: absolute;
		transform: translate(-50%, -50%); /* centre on focus point */
		cursor: grab;
		pointer-events: auto;
		width: 0;
		height: 0;
	}

	.crosshair:active { cursor: grabbing; }

	.arm {
		position: absolute;
		background: rgb(var(--color-accent-primary));
		opacity: 0.9;
	}

	.arm-h {
		height: 1.5px;
		top: -0.75px;
	}

	.arm-v {
		width: 1.5px;
		left: -0.75px;
	}

	.centre-dot {
		position: absolute;
		width: 7px;
		height: 7px;
		border-radius: 50%;
		background: rgb(var(--color-accent-primary));
		border: 1.5px solid #fff;
		transform: translate(-50%, -50%);
		box-shadow: 0 0 0 1px rgb(var(--color-accent-primary) / 0.4);
	}

	/* ── Depth badge ──────────────────────────────────────────────────────── */
	.depth-badge {
		position: absolute;
		pointer-events: none;
		font-size: 10px;
		font-variant-numeric: tabular-nums;
		color: rgb(var(--color-accent-primary));
		background: rgb(var(--color-bg-primary) / 0.75);
		border: 1px solid rgb(var(--color-accent-primary) / 0.4);
		border-radius: var(--radius-sm);
		padding: 1px 5px;
		line-height: 1.6;
		white-space: nowrap;
		backdrop-filter: blur(4px);
	}
</style>
