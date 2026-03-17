<script lang="ts">
	/**
	 * AnnotationOverlay — Step 16
	 *
	 * Absolutely-positioned div rendered over VideoCanvas.
	 * Shows all annotation regions visible at editor.currentTimeMs.
	 *
	 * Selected annotation gets 8 resize handles (corners + midpoints).
	 * Drag body  → updateAnnotation({ x, y })
	 * Drag handle → updateAnnotation({ x, y, width, height })
	 *
	 * When annotationTool === 'text' | 'arrow' | 'image' and user clicks
	 * an empty area → addAnnotation at normalised click position.
	 *
	 * Annotation kinds:
	 *   text  — <div> with text content + textStyle
	 *   arrow — <svg> line + arrowhead
	 *   image — <img> with imageSrc
	 *
	 * All coordinates are normalised 0–1; converted to % for CSS.
	 */

	import type { AppRecorderState, AnnotationRegion, ArrowDirection } from '../AppRecorderState.svelte';

	let { editor }: { editor: AppRecorderState } = $props();

	// ── Root element ref (for pointer coordinate math) ────────────────────────
	let rootEl = $state<HTMLDivElement>(null!);

	// ── Visible annotations at current time ───────────────────────────────────
	const visible = $derived.by(() => {
		const clip = editor.activeClip;
		if (!clip) return [];
		const nowMs = editor.activeClipSourceMs;
		return clip.annotationRegions
			.filter(a => nowMs >= a.startMs && nowMs < a.endMs || a.id === editor.selectedAnnotationId)
			.sort((a, b) => a.zIndex - b.zIndex);
	});

	// ── Drag state ────────────────────────────────────────────────────────────
	type HandlePos = 'nw' | 'n' | 'ne' | 'e' | 'se' | 's' | 'sw' | 'w' | 'body';

	let dragId:       string | null   = null;
	let dragHandle:   HandlePos       = 'body';
	let dragStartX    = 0;
	let dragStartY    = 0;
	let dragOrigin    = { x: 0, y: 0, w: 0, h: 0 };

	const HANDLES: HandlePos[] = ['nw', 'n', 'ne', 'e', 'se', 's', 'sw', 'w'];

	function handleCursor(h: HandlePos): string {
		const map: Record<HandlePos, string> = {
			nw: 'nw-resize', n: 'n-resize', ne: 'ne-resize',
			e: 'e-resize',
			se: 'se-resize', s: 's-resize', sw: 'sw-resize',
			w: 'w-resize', body: 'move',
		};
		return map[h];
	}

	function handleStyle(h: HandlePos): string {
		const pct: Record<HandlePos, { left: string; top: string }> = {
			nw: { left: '-4px',  top: '-4px'  },
			n:  { left: 'calc(50% - 4px)', top: '-4px' },
			ne: { left: 'calc(100% - 4px)', top: '-4px' },
			e:  { left: 'calc(100% - 4px)', top: 'calc(50% - 4px)' },
			se: { left: 'calc(100% - 4px)', top: 'calc(100% - 4px)' },
			s:  { left: 'calc(50% - 4px)',  top: 'calc(100% - 4px)' },
			sw: { left: '-4px',  top: 'calc(100% - 4px)' },
			w:  { left: '-4px',  top: 'calc(50% - 4px)' },
			body: { left: '0', top: '0' },
		};
		const p = pct[h];
		return `left:${p.left}; top:${p.top};`;
	}

	function startDrag(e: PointerEvent, id: string, handle: HandlePos) {
		e.preventDefault();
		e.stopPropagation();
		editor.selectAnnotation(id);

		const ann = editor.activeClip?.annotationRegions.find(a => a.id === id);
		if (!ann) return;

		dragId      = id;
		dragHandle  = handle;
		dragStartX  = e.clientX;
		dragStartY  = e.clientY;
		dragOrigin  = { x: ann.x, y: ann.y, w: ann.width, h: ann.height };

		(e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
	}

	function onPointerMove(e: PointerEvent) {
		if (!dragId || !rootEl) return;

		const rect  = rootEl.getBoundingClientRect();
		const dx    = (e.clientX - dragStartX) / rect.width;
		const dy    = (e.clientY - dragStartY) / rect.height;
		const MIN   = 0.02; // minimum width/height (normalised)

		let { x, y, w, h } = dragOrigin;

		if (dragHandle === 'body') {
			x = clamp01(x + dx);
			y = clamp01(y + dy);
		} else {
			// Resize: each handle affects different edges
			if (dragHandle.includes('e')) { w = Math.max(MIN, w + dx); }
			if (dragHandle.includes('w')) { x = x + dx; w = Math.max(MIN, w - dx); }
			if (dragHandle.includes('s')) { h = Math.max(MIN, h + dy); }
			if (dragHandle.includes('n')) { y = y + dy; h = Math.max(MIN, h - dy); }
			// Clamp to canvas bounds
			x = Math.max(0, x); y = Math.max(0, y);
			w = Math.min(w, 1 - x); h = Math.min(h, 1 - y);
		}

		editor.updateAnnotation(dragId, { x, y, width: w, height: h });
	}

	function onPointerUp(e: PointerEvent) {
		if (!dragId) return;
		try { (e.currentTarget as HTMLElement).releasePointerCapture(e.pointerId); } catch {}
		dragId = null;
	}

	function clamp01(v: number) { return Math.max(0, Math.min(1, v)); }

	// ── Click on empty area → place new annotation ────────────────────────────
	function onRootClick(e: MouseEvent) {
		const tool = editor.annotationTool;
		if (tool === 'select' || tool === 'zoom-point' || tool === 'crop') return;
		// Only fire if click wasn't on an existing annotation
		if ((e.target as HTMLElement) !== rootEl) return;
		if (!rootEl) return;

		const rect  = rootEl.getBoundingClientRect();
		const cx    = (e.clientX - rect.left) / rect.width;
		const cy    = (e.clientY - rect.top)  / rect.height;
		const nowMs = editor.currentTimeMs;

		const kind = tool === 'text' ? 'text' : tool === 'image' ? 'image' : 'arrow';
		editor.addAnnotation(nowMs, nowMs + 3000, kind, cx - 0.15, cy - 0.05, 0.3, 0.1);
		// Reset to select after placing
		editor.setAnnotationTool('select');
	}

	// ── Arrow SVG path helpers ────────────────────────────────────────────────
	function arrowPoints(dir: ArrowDirection, w: number, h: number): {
		x1: number; y1: number; x2: number; y2: number;
	} {
		// Returns start → end of arrow line in local pixel coordinates
		const cx = w / 2, cy = h / 2;
		const dirs: Record<ArrowDirection, [number, number, number, number]> = {
			right:      [4,  cy, w - 4, cy],
			left:       [w - 4, cy, 4,  cy],
			down:       [cx, 4,  cx,  h - 4],
			up:         [cx, h - 4, cx,  4],
			'up-right': [4,  h - 4, w - 4, 4],
			'up-left':  [w - 4, h - 4, 4,  4],
			'down-right': [4, 4, w - 4, h - 4],
			'down-left':  [w - 4, 4, 4, h - 4],
		};
		const [x1, y1, x2, y2] = dirs[dir];
		return { x1, y1, x2, y2 };
	}
</script>

<!-- svelte-ignore a11y_click_events_have_key_events -->
<!-- svelte-ignore a11y_no_static_element_interactions -->
<div
	class="ann-overlay"
	bind:this={rootEl}
	onpointermove={onPointerMove}
	onpointerup={onPointerUp}
	onpointerleave={onPointerUp}
	onclick={onRootClick}
	style="cursor: {editor.annotationTool !== 'select' ? 'crosshair' : 'default'};"
>
	{#each visible as ann (ann.id)}
		{@const selected = ann.id === editor.selectedAnnotationId}
		{@const ts = ann.textStyle}
		{@const fd = ann.figureData}

		<!-- svelte-ignore a11y_no_static_element_interactions -->
		<div
			class="ann-item {selected ? 'ann-selected' : ''}"
			style="
				left:    {ann.x * 100}%;
				top:     {ann.y * 100}%;
				width:   {ann.width  * 100}%;
				height:  {ann.height * 100}%;
				z-index: {ann.zIndex};
				opacity: {ann.opacity};
			"
			onpointerdown={(e) => startDrag(e, ann.id, 'body')}
		>
			<!-- ── Text annotation ─────────────────────────────────── -->
			{#if ann.kind === 'text' && ts}
				<div
					class="ann-text"
					style="
						color:           {ts.color};
						background:      {ts.backgroundColor};
						font-size:       {ts.fontSize}px;
						font-family:     {ts.fontFamily}, sans-serif;
						font-weight:     {ts.fontWeight};
						font-style:      {ts.fontStyle};
						text-decoration: {ts.textDecoration};
						text-align:      {ts.textAlign};
					"
				>
					{ann.text ?? ''}
				</div>

			<!-- ── Arrow annotation ────────────────────────────────── -->
			{:else if ann.kind === 'arrow' && fd}
				{@const aw = 100}
				{@const ah = 100}
				{@const p  = arrowPoints(fd.arrowDirection, aw, ah)}
				<svg
					class="ann-arrow"
					viewBox="0 0 {aw} {ah}"
					preserveAspectRatio="none"
					aria-hidden="true"
				>
					<defs>
						<marker
							id="arrowhead-{ann.id}"
							markerWidth="6" markerHeight="6"
							refX="5" refY="3"
							orient="auto"
						>
							<path d="M0,0 L6,3 L0,6 Z" fill={fd.color}/>
						</marker>
					</defs>
					<line
						x1={p.x1} y1={p.y1} x2={p.x2} y2={p.y2}
						stroke={fd.color}
						stroke-width={fd.strokeWidth}
						stroke-linecap="round"
						marker-end="url(#arrowhead-{ann.id})"
					/>
				</svg>

			<!-- ── Image annotation ────────────────────────────────── -->
			{:else if ann.kind === 'image'}
				{#if ann.imageSrc}
					<img
						class="ann-image"
						src={ann.imageSrc}
						alt=""
						draggable="false"
					/>
				{:else}
					<div class="ann-image-placeholder">
						<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.2">
							<rect x="2" y="3" width="12" height="10" rx="1"/>
							<circle cx="5.5" cy="6.5" r="1" fill="currentColor" stroke="none"/>
							<path d="M2 11l3.5-3.5 3 3 2-2 3 3" stroke-linecap="round"/>
						</svg>
						<span>No image</span>
					</div>
				{/if}
			{/if}

			<!-- ── Resize handles (selected only) ──────────────────── -->
			{#if selected}
				{#each HANDLES as h}
					<!-- svelte-ignore a11y_no_static_element_interactions -->
					<div
						class="handle"
						style="{handleStyle(h)} cursor: {handleCursor(h)};"
						onpointerdown={(e) => startDrag(e, ann.id, h)}
					></div>
				{/each}
			{/if}

		</div>
	{/each}
</div>

<style>
	.ann-overlay {
		position: absolute;
		inset: 0;
		pointer-events: auto;
		user-select: none;
	}

	/* ── Annotation item ──────────────────────────────────────────────────── */
	.ann-item {
		position: absolute;
		cursor: move;
		box-sizing: border-box;
	}

	.ann-selected {
		outline: 1.5px solid rgb(var(--color-accent-primary) / 0.8);
		outline-offset: 1px;
	}

	/* ── Text ─────────────────────────────────────────────────────────────── */
	.ann-text {
		width: 100%;
		height: 100%;
		display: flex;
		align-items: center;
		justify-content: center;
		padding: 4px 8px;
		box-sizing: border-box;
		white-space: pre-wrap;
		word-break: break-word;
		border-radius: 4px;
		line-height: 1.3;
		pointer-events: none;
	}

	/* ── Arrow ────────────────────────────────────────────────────────────── */
	.ann-arrow {
		width: 100%;
		height: 100%;
		display: block;
		overflow: visible;
		pointer-events: none;
	}

	/* ── Image ────────────────────────────────────────────────────────────── */
	.ann-image {
		width: 100%;
		height: 100%;
		object-fit: contain;
		display: block;
		pointer-events: none;
		border-radius: 2px;
	}

	.ann-image-placeholder {
		width: 100%;
		height: 100%;
		display: flex;
		flex-direction: column;
		align-items: center;
		justify-content: center;
		gap: 4px;
		background: rgb(var(--color-surface-2));
		border: 1px dashed rgb(var(--color-border-default));
		border-radius: 4px;
		color: rgb(var(--color-text-secondary));
		font-size: 10px;
		pointer-events: none;
	}

	.ann-image-placeholder svg {
		width: 20px;
		height: 20px;
		opacity: 0.5;
	}

	/* ── Resize handles ───────────────────────────────────────────────────── */
	.handle {
		position: absolute;
		width: 8px;
		height: 8px;
		background: #fff;
		border: 1.5px solid rgb(var(--color-accent-primary));
		border-radius: 1px;
		box-shadow: 0 0 0 1px rgb(var(--color-accent-primary) / 0.3);
		z-index: 1;
		pointer-events: auto;
	}

	.handle:hover {
		background: rgb(var(--color-accent-primary));
	}
</style>
