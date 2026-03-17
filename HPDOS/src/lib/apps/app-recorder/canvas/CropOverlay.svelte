<script lang="ts">
	/**
	 * CropOverlay — Step 17
	 *
	 * Shown when editor.annotationTool === 'crop'.
	 * Darkened vignette outside crop region + 8 resize handles.
	 *
	 * Crop region defaults to editor.crop ?? { x:0, y:0, width:1, height:1 }.
	 * All values normalised 0–1.
	 *
	 * Drag handle → editor.setCrop({ x, y, width, height })
	 * "Apply Crop" → keeps crop, resets tool to 'select'
	 * "Reset"      → editor.setCrop(null)
	 */

	import type { AppRecorderState, CropOptions } from '../AppRecorderState.svelte';

	let { editor }: { editor: AppRecorderState } = $props();

	let rootEl = $state<HTMLDivElement>(null!);

	// Active crop (live while dragging)
	const crop = $derived<CropOptions>(
		editor.crop ?? { x: 0, y: 0, width: 1, height: 1 }
	);

	// ── Handle positions ──────────────────────────────────────────────────────
	type Handle = 'nw' | 'n' | 'ne' | 'e' | 'se' | 's' | 'sw' | 'w';

	const HANDLES: Handle[] = ['nw', 'n', 'ne', 'e', 'se', 's', 'sw', 'w'];

	function handleLeft(h: Handle): string {
		if (h === 'nw' || h === 'sw' || h === 'w') return `calc(${crop.x * 100}% - 5px)`;
		if (h === 'n'  || h === 's')               return `calc(${(crop.x + crop.width / 2) * 100}% - 5px)`;
		return `calc(${(crop.x + crop.width) * 100}% - 5px)`;
	}

	function handleTop(h: Handle): string {
		if (h === 'nw' || h === 'n' || h === 'ne') return `calc(${crop.y * 100}% - 5px)`;
		if (h === 'w'  || h === 'e')               return `calc(${(crop.y + crop.height / 2) * 100}% - 5px)`;
		return `calc(${(crop.y + crop.height) * 100}% - 5px)`;
	}

	function handleCursor(h: Handle): string {
		const map: Record<Handle, string> = {
			nw: 'nw-resize', n: 'n-resize', ne: 'ne-resize',
			e: 'e-resize', se: 'se-resize', s: 's-resize',
			sw: 'sw-resize', w: 'w-resize',
		};
		return map[h];
	}

	// ── Drag state ────────────────────────────────────────────────────────────
	let dragging: Handle | 'body' | null = null;
	let dragStartX = 0;
	let dragStartY = 0;
	let dragOrigin = { x: 0, y: 0, w: 0, h: 0 };

	const MIN = 0.05;

	function startHandleDrag(e: PointerEvent, h: Handle) {
		e.preventDefault();
		e.stopPropagation();
		dragging   = h;
		dragStartX = e.clientX;
		dragStartY = e.clientY;
		dragOrigin = { x: crop.x, y: crop.y, w: crop.width, h: crop.height };
		(e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
	}

	function startBodyDrag(e: PointerEvent) {
		e.preventDefault();
		dragging   = 'body';
		dragStartX = e.clientX;
		dragStartY = e.clientY;
		dragOrigin = { x: crop.x, y: crop.y, w: crop.width, h: crop.height };
		(e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
	}

	function onPointerMove(e: PointerEvent) {
		if (!dragging || !rootEl) return;
		const rect = rootEl.getBoundingClientRect();
		const dx   = (e.clientX - dragStartX) / rect.width;
		const dy   = (e.clientY - dragStartY) / rect.height;
		let { x, y, w, h } = dragOrigin;

		if (dragging === 'body') {
			x = Math.max(0, Math.min(1 - w, x + dx));
			y = Math.max(0, Math.min(1 - h, y + dy));
		} else {
			if (dragging.includes('e')) { w = Math.max(MIN, Math.min(1 - x, w + dx)); }
			if (dragging.includes('w')) { const nx = Math.max(0, Math.min(x + w - MIN, x + dx)); w = w - (nx - x); x = nx; }
			if (dragging.includes('s')) { h = Math.max(MIN, Math.min(1 - y, h + dy)); }
			if (dragging.includes('n')) { const ny = Math.max(0, Math.min(y + h - MIN, y + dy)); h = h - (ny - y); y = ny; }
		}

		editor.setCrop({ x, y, width: w, height: h });
	}

	function onPointerUp(e: PointerEvent) {
		if (!dragging) return;
		try { (e.currentTarget as HTMLElement).releasePointerCapture(e.pointerId); } catch {}
		dragging = null;
	}

	// ── SVG clip-path for vignette ────────────────────────────────────────────
	// Two rects: outer (full canvas) minus inner (crop window) = dark surround
	const vignetteClip = $derived(
		`polygon(
			0% 0%, 100% 0%, 100% 100%, 0% 100%, 0% 0%,
			${crop.x * 100}% ${crop.y * 100}%,
			${crop.x * 100}% ${(crop.y + crop.height) * 100}%,
			${(crop.x + crop.width) * 100}% ${(crop.y + crop.height) * 100}%,
			${(crop.x + crop.width) * 100}% ${crop.y * 100}%,
			${crop.x * 100}% ${crop.y * 100}%
		)`
	);
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div
	class="crop-overlay"
	bind:this={rootEl}
	onpointermove={onPointerMove}
	onpointerup={onPointerUp}
	onpointerleave={onPointerUp}
>
	<!-- Dark vignette outside crop region -->
	<div
		class="vignette"
		style="clip-path: {vignetteClip};"
	></div>

	<!-- Crop border rect -->
	<div
		class="crop-border"
		style="
			left:   {crop.x * 100}%;
			top:    {crop.y * 100}%;
			width:  {crop.width  * 100}%;
			height: {crop.height * 100}%;
		"
		onpointerdown={startBodyDrag}
	>
		<!-- Rule-of-thirds grid lines -->
		<div class="thirds-h" style="top: 33.33%;"></div>
		<div class="thirds-h" style="top: 66.66%;"></div>
		<div class="thirds-v" style="left: 33.33%;"></div>
		<div class="thirds-v" style="left: 66.66%;"></div>
	</div>

	<!-- Resize handles -->
	{#each HANDLES as h}
		<!-- svelte-ignore a11y_no_static_element_interactions -->
		<div
			class="crop-handle"
			style="left: {handleLeft(h)}; top: {handleTop(h)}; cursor: {handleCursor(h)};"
			onpointerdown={(e) => startHandleDrag(e, h)}
		></div>
	{/each}

	<!-- Action buttons (bottom-left of crop box) -->
	<div
		class="crop-actions"
		style="
			left: {crop.x * 100}%;
			top:  calc({(crop.y + crop.height) * 100}% + 8px);
		"
	>
		<button
			class="crop-btn crop-btn-apply"
			onclick={() => { editor.setAnnotationTool('select'); }}
		>
			Apply Crop
		</button>
		<button
			class="crop-btn crop-btn-reset"
			onclick={() => { editor.setCrop(null); editor.setAnnotationTool('select'); }}
		>
			Reset
		</button>
	</div>
</div>

<style>
	.crop-overlay {
		position: absolute;
		inset: 0;
		pointer-events: auto;
		user-select: none;
	}

	/* Dark surround */
	.vignette {
		position: absolute;
		inset: 0;
		background: rgba(0, 0, 0, 0.55);
		pointer-events: none;
	}

	/* Crop border */
	.crop-border {
		position: absolute;
		border: 1.5px solid rgba(255, 255, 255, 0.85);
		box-sizing: border-box;
		cursor: move;
	}

	/* Rule-of-thirds lines */
	.thirds-h {
		position: absolute;
		left: 0; right: 0;
		height: 1px;
		background: rgba(255, 255, 255, 0.25);
		pointer-events: none;
	}

	.thirds-v {
		position: absolute;
		top: 0; bottom: 0;
		width: 1px;
		background: rgba(255, 255, 255, 0.25);
		pointer-events: none;
	}

	/* Resize handles */
	.crop-handle {
		position: absolute;
		width: 10px;
		height: 10px;
		background: #fff;
		border: 1.5px solid rgba(0,0,0,0.4);
		border-radius: 1px;
		box-shadow: 0 1px 3px rgba(0,0,0,0.4);
		pointer-events: auto;
		z-index: 2;
	}

	/* Action buttons */
	.crop-actions {
		position: absolute;
		display: flex;
		gap: 6px;
		z-index: 3;
		pointer-events: auto;
	}

	.crop-btn {
		padding: 4px 12px;
		font-size: var(--font-size-xs);
		border-radius: var(--radius-sm);
		cursor: pointer;
		border: 1px solid transparent;
		font-weight: 500;
		transition: all var(--duration-fast);
		backdrop-filter: blur(6px);
	}

	.crop-btn-apply {
		background: rgb(var(--color-accent-primary));
		color: #fff;
		border-color: rgb(var(--color-accent-primary));
	}

	.crop-btn-apply:hover { opacity: 0.85; }

	.crop-btn-reset {
		background: rgb(var(--color-bg-secondary) / 0.85);
		color: rgb(var(--color-text-primary));
		border-color: rgb(var(--color-border-default));
	}

	.crop-btn-reset:hover { background: rgb(var(--color-surface-2)); }
</style>
