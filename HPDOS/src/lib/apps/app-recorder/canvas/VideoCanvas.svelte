<script lang="ts">
	/**
	 * VideoCanvas — Step 13
	 *
	 * PixiJS application wrapping a hidden <video> element.
	 * Architecture mirrors OpenScreen's VideoPlayback.tsx, translated to Svelte 5 runes.
	 *
	 * Layer hierarchy:
	 *   app.stage
	 *   └── cameraContainer   ← scaled/translated for zoom
	 *       └── videoContainer ← masked to crop rect, has BlurFilter
	 *           ├── videoSprite
	 *           └── maskGraphics
	 *
	 * Transport controls are plain HTML below the canvas (not PixiJS).
	 * Overlay children (ZoomHandleOverlay etc.) will be added in Steps 14–17 as
	 * absolutely-positioned divs rendered over .canvas-overlay.
	 */

	import {
		Application,
		BlurFilter,
		Container,
		Graphics,
		Sprite,
		Texture,
		VideoSource,
	} from 'pixi.js';
	import type { AppRecorderState, ZoomRegion, ClipModel } from '../AppRecorderState.svelte';
	import { ZOOM_DEPTH_SCALES } from '../AppRecorderState.svelte';
	import ZoomHandleOverlay  from './ZoomHandleOverlay.svelte';
	import AnnotationOverlay  from './AnnotationOverlay.svelte';
	import CropOverlay        from './CropOverlay.svelte';

	// ── Props ─────────────────────────────────────────────────────────────────

	let { editor }: { editor: AppRecorderState } = $props();

	// ── DOM refs ──────────────────────────────────────────────────────────────

	let containerEl = $state<HTMLDivElement>(null!);
	let overlayEl   = $state<HTMLDivElement>(null!);
	let videoEl     = $state<HTMLVideoElement>(null!);

	// ── Constants (from OpenScreen) ───────────────────────────────────────────

	const SMOOTHING_FACTOR  = 0.12;
	const MIN_DELTA         = 0.0001;
	const TRANSITION_WINDOW = 320; // ms — zoom fade-in/out envelope

	// ── Zoom region helpers ───────────────────────────────────────────────────

	function smoothStep(t: number): number {
		const c = Math.max(0, Math.min(1, t));
		return c * c * (3 - 2 * c);
	}

	function regionStrength(r: ZoomRegion, nowMs: number): number {
		if (nowMs < r.startMs || nowMs > r.endMs) return 0;
		return Math.min(
			smoothStep((nowMs - r.startMs) / TRANSITION_WINDOW),
			smoothStep((r.endMs - nowMs)   / TRANSITION_WINDOW),
		);
	}

	function findDominantZoom(regions: ZoomRegion[], nowMs: number) {
		let best: ZoomRegion | null = null;
		let bestStr = 0;
		for (const r of regions) {
			const s = regionStrength(r, nowMs);
			if (s > bestStr) { best = r; bestStr = s; }
		}
		return { region: best, strength: bestStr };
	}

	// Keep focus point within stage so video never scrolls out of view
	function clampFocus(cx: number, cy: number, zoomScale: number) {
		const margin = 0.5 / zoomScale;
		return {
			cx: Math.max(margin, Math.min(1 - margin, cx)),
			cy: Math.max(margin, Math.min(1 - margin, cy)),
		};
	}

	// ── Time formatting ───────────────────────────────────────────────────────

	function fmtTime(ms: number): string {
		if (!isFinite(ms) || ms < 0) return '0:00.0';
		const s   = ms / 1000;
		const m   = Math.floor(s / 60);
		const sec = Math.floor(s % 60);
		const f   = Math.floor((s % 1) * 10);
		return `${m}:${sec.toString().padStart(2, '0')}.${f}`;
	}

	// ── Pixi lifecycle (single $effect owns the entire Pixi app) ──────────────

	$effect(() => {
		// Svelte tracks these reads — effect re-runs if activeClip changes
		const activeClip = editor.activeClip;
		const videoPath  = activeClip?.path ?? null;
		if (!containerEl || !videoEl || !overlayEl) return;

		let mounted = true;

		// Mutable animation state — never reactive, mutated each ticker frame
		const anim = { scale: 1, focusX: 0.5, focusY: 0.5 };

		// Pixi nodes
		let app:             Application | null = null;
		let cameraContainer: Container   | null = null;
		let videoContainer:  Container   | null = null;
		let videoSprite:     Sprite      | null = null;
		let maskGraphics:    Graphics    | null = null;
		let blurFilter:      BlurFilter  | null = null;

		// Locked video intrinsic size (set once to prevent jitter on resize)
		let lockedW = 0, lockedH = 0;

		// Stage size updated by layout()
		let stageW = 0, stageH = 0;

		// Playback guard refs (avoid stale closures on RAF)
		let isPlayingLocal = false;
		let allowPlay      = false;
		let isSeeking      = false;
		let rafId: number | null = null;

		// ── RAF time-update loop ────────────────────────────────────────────
		function updateTime() {
			if (!videoEl || videoEl.paused || videoEl.ended) return;

			// videoEl.currentTime is clip-local (source time).
			// Convert to global timeline ms via activeClip.
			const clip      = editor.activeClip;
			const sourceMs  = videoEl.currentTime * 1000;
			const globalMs  = clip ? clip.position + (sourceMs - clip.start) : sourceMs;

			// Skip trim regions (clip-local)
			const trim = clip?.trimRegions.find(r => sourceMs >= r.startMs && sourceMs < r.endMs);
			if (trim) {
				const skipSource = trim.endMs / 1000;
				if (skipSource >= videoEl.duration) {
					videoEl.pause();
				} else {
					videoEl.currentTime = skipSource;
					const skipGlobal = clip ? clip.position + (trim.endMs - clip.start) : trim.endMs;
					editor.seekTo(skipGlobal);
				}
			} else {
				// Apply speed region (clip-local)
				const speed = clip?.speedRegions.find(r => sourceMs >= r.startMs && sourceMs < r.endMs);
				videoEl.playbackRate = speed ? speed.multiplier : editor.playbackSpeed;
				editor.seekTo(globalMs);
			}

			if (!videoEl.paused && !videoEl.ended) {
				rafId = requestAnimationFrame(updateTime);
			}
		}

		// ── Video event handlers ────────────────────────────────────────────
		function onPlay() {
			if (isSeeking || !allowPlay) { videoEl.pause(); return; }
			isPlayingLocal = true;
			if (rafId) cancelAnimationFrame(rafId);
			rafId = requestAnimationFrame(updateTime);
		}

		function onPause() {
			isPlayingLocal = false;
			if (rafId) { cancelAnimationFrame(rafId); rafId = null; }
			editor.seekTo(videoEl.currentTime * 1000);
		}

		function onSeeking() {
			isSeeking = true;
			if (!isPlayingLocal && !videoEl.paused) videoEl.pause();
			editor.seekTo(videoEl.currentTime * 1000);
		}

		function onSeeked() {
			isSeeking = false;
			editor.seekTo(videoEl.currentTime * 1000);
		}

		function onLoadedMetadata() {
			// Store metadata keyed by clip id so each clip has its own resolution/fps
			if (activeClip) {
				editor.setClipMetadata(activeClip.id, {
					width:         videoEl.videoWidth,
					height:        videoEl.videoHeight,
					fps:           30,
					fileSizeBytes: 0,
				});
			}
			videoEl.currentTime = (activeClip?.start ?? 0) / 1000;
			videoEl.pause();
			allowPlay = false;
			lockedW = 0; lockedH = 0;

			// Poll until first renderable frame is available
			function waitForFrame() {
				if (!mounted) return;
				if (videoEl.videoWidth > 0 && videoEl.readyState >= HTMLMediaElement.HAVE_CURRENT_DATA) {
					if (!lockedW) { lockedW = videoEl.videoWidth; lockedH = videoEl.videoHeight; }
					setupSprite();
				} else {
					requestAnimationFrame(waitForFrame);
				}
			}
			requestAnimationFrame(waitForFrame);
		}

		videoEl.addEventListener('play',           onPlay);
		videoEl.addEventListener('pause',          onPause);
		videoEl.addEventListener('ended',          onPause);
		videoEl.addEventListener('seeking',        onSeeking);
		videoEl.addEventListener('seeked',         onSeeked);
		videoEl.addEventListener('loadedmetadata', onLoadedMetadata);

		// ── Layout: resize renderer + recalculate sprite/mask ──────────────
		function layout() {
			if (!app || !videoSprite || !maskGraphics) return;
			const W = containerEl.clientWidth;
			const H = containerEl.clientHeight;
			if (!W || !H || !lockedW || !lockedH) return;

			app.renderer.resize(W, H);
			const canvas = app.canvas as HTMLCanvasElement;
			canvas.style.width  = '100%';
			canvas.style.height = '100%';
			stageW = W; stageH = H;

			// editor.visual.padding: 0–100; 50 → same as OpenScreen VIEWPORT_SCALE 0.8
			const pad    = editor.visual?.padding ?? 50;
			const pScale = 1.0 - (pad / 100) * 0.4;
			const scale  = Math.min((W * pScale) / lockedW, (H * pScale) / lockedH, 1);

			const dispW  = lockedW * scale;
			const dispH  = lockedH * scale;
			const offX   = (W - dispW) / 2;
			const offY   = (H - dispH) / 2;

			videoSprite.scale.set(scale);
			videoSprite.position.set(offX, offY);

			// Rounded mask
			const br = editor.visual?.borderRadius ?? 0;
			maskGraphics.clear();
			maskGraphics.roundRect(offX, offY, dispW, dispH, br);
			maskGraphics.fill({ color: 0xffffff });

			// Reset camera to identity after layout
			if (cameraContainer) {
				cameraContainer.scale.set(1);
				cameraContainer.position.set(0, 0);
			}

			}

		// ── Sprite setup (called once video intrinsic size is known) ────────
		function setupSprite() {
			if (!app || !videoContainer) return;

			// Tear down previous sprite
			if (videoSprite) { videoContainer.removeChild(videoSprite); videoSprite.destroy(); videoSprite = null; }
			if (maskGraphics) { videoContainer.removeChild(maskGraphics); maskGraphics.destroy(); maskGraphics = null; videoContainer.mask = null; }
			if (blurFilter)   { videoContainer.filters = []; blurFilter.destroy(); blurFilter = null; }

			const src = VideoSource.from(videoEl);
			// Pixi v8 VideoSource fields
			(src as unknown as Record<string, unknown>).autoPlay   = false;
			(src as unknown as Record<string, unknown>).autoUpdate = true;
			const tex = Texture.from(src);

			videoSprite  = new Sprite(tex);
			maskGraphics = new Graphics();
			videoContainer.addChild(videoSprite);
			videoContainer.addChild(maskGraphics);
			videoContainer.mask = maskGraphics;

			blurFilter            = new BlurFilter();
			blurFilter.quality    = 3;
			blurFilter.resolution = app.renderer.resolution;
			blurFilter.blur       = 0;
			videoContainer.filters = [blurFilter];

			// Reset animation state
			anim.scale = 1; anim.focusX = 0.5; anim.focusY = 0.5;

			layout();
			videoEl.pause();
		}

		// ── Pixi ticker: smooth zoom interpolation each frame ───────────────
		function tick() {
			if (!cameraContainer || !stageW) return;

			// Zoom regions are clip-local; convert global playhead to source ms for lookup
			const clip    = editor.activeClip;
			const nowMs   = clip
				? clip.start + (editor.currentTimeMs - clip.position)
				: editor.currentTimeMs;
			const regions = clip?.zoomRegions ?? [];
			const { region, strength } = findDominantZoom(regions, nowMs);

			// While a zoom is selected but paused → stay un-zoomed so focus overlay is visible
			const shouldShowUnzoomed = editor.selectedZoomId !== null && !isPlayingLocal;

			let targetScale  = 1;
			let targetFocusX = 0.5;
			let targetFocusY = 0.5;

			if (region && strength > 0 && !shouldShowUnzoomed) {
				const zScale   = ZOOM_DEPTH_SCALES[region.depth];
				const clamped  = clampFocus(region.cx, region.cy, zScale);
				targetScale    = 1 + (zScale - 1) * strength;
				targetFocusX   = 0.5 + (clamped.cx - 0.5) * strength;
				targetFocusY   = 0.5 + (clamped.cy - 0.5) * strength;
			}

			// Lerp toward target
			const sd = targetScale  - anim.scale;
			const xd = targetFocusX - anim.focusX;
			const yd = targetFocusY - anim.focusY;

			const prevScale  = anim.scale;
			const prevFocusX = anim.focusX;
			const prevFocusY = anim.focusY;

			anim.scale  = Math.abs(sd) > MIN_DELTA ? anim.scale  + sd * SMOOTHING_FACTOR : targetScale;
			anim.focusX = Math.abs(xd) > MIN_DELTA ? anim.focusX + xd * SMOOTHING_FACTOR : targetFocusX;
			anim.focusY = Math.abs(yd) > MIN_DELTA ? anim.focusY + yd * SMOOTHING_FACTOR : targetFocusY;

			const motionIntensity = Math.max(
				Math.abs(anim.scale  - prevScale),
				Math.abs(anim.focusX - prevFocusX),
				Math.abs(anim.focusY - prevFocusY),
			);

			// Apply camera transform
			cameraContainer.scale.set(anim.scale);
			cameraContainer.position.set(
				stageW / 2 - anim.focusX * stageW * anim.scale,
				stageH / 2 - anim.focusY * stageH * anim.scale,
			);

			// Motion blur
			if (blurFilter) {
				const shouldBlur = editor.visual?.motionBlur && isPlayingLocal && motionIntensity > 0.0005;
				blurFilter.blur  = shouldBlur ? Math.min(6, motionIntensity * 120) : 0;
			}
		}

		// ── Pixi init (async) ───────────────────────────────────────────────
		let resizeObserver: ResizeObserver | null = null;

		(async () => {
			app = new Application();
			await app.init({
				width:           containerEl.clientWidth  || 640,
				height:          containerEl.clientHeight || 360,
				backgroundAlpha: 0,
				antialias:       true,
				resolution:      window.devicePixelRatio || 1,
				autoDensity:     true,
			});
			app.ticker.maxFPS = 60;

			if (!mounted) {
				app.destroy(true, { children: true, texture: true, textureSource: true });
				return;
			}

			containerEl.appendChild(app.canvas as HTMLCanvasElement);

			cameraContainer = new Container();
			videoContainer  = new Container();
			cameraContainer.addChild(videoContainer);
			app.stage.addChild(cameraContainer);

			app.ticker.add(tick);

			resizeObserver = new ResizeObserver(() => layout());
			resizeObserver.observe(containerEl);

			// Set video source now that Pixi is ready
			if (videoPath) {
				videoEl.src = videoPath;
				videoEl.load();
			}

			// If video already decoded (e.g. same path re-mount), set up sprite immediately
			if (videoEl.videoWidth > 0 && videoEl.readyState >= HTMLMediaElement.HAVE_CURRENT_DATA) {
				lockedW = videoEl.videoWidth;
				lockedH = videoEl.videoHeight;
				setupSprite();
			}
		})();

		// ── Cleanup ─────────────────────────────────────────────────────────
		return () => {
			mounted = false;
			if (rafId) cancelAnimationFrame(rafId);
			resizeObserver?.disconnect();

			videoEl.removeEventListener('play',           onPlay);
			videoEl.removeEventListener('pause',          onPause);
			videoEl.removeEventListener('ended',          onPause);
			videoEl.removeEventListener('seeking',        onSeeking);
			videoEl.removeEventListener('seeked',         onSeeked);
			videoEl.removeEventListener('loadedmetadata', onLoadedMetadata);

			if (app?.renderer) {
				app.destroy(true, { children: true, texture: true, textureSource: true });
			}
		};
	});

	// ── Sync: editor.isPlaying → video.play / video.pause ───────────────────
	// Separate effect so it runs after the Pixi effect initialises the video element.
	$effect(() => {
		const playing = editor.isPlaying;
		if (!videoEl) return;
		if (playing) {
			// Signal the guard inside the Pixi effect
			videoEl.dataset.hpdAllowPlay = '1';
			videoEl.play().catch(() => { editor.isPlaying = false; });
		} else {
			videoEl.dataset.hpdAllowPlay = '0';
			videoEl.pause();
		}
	});

	// ── Sync: activeClipSourceMs → video.currentTime (seek while paused) ────
	$effect(() => {
		const sourceMs = editor.activeClipSourceMs;
		if (!videoEl || editor.isPlaying) return;
		const target = sourceMs / 1000;
		if (Math.abs(videoEl.currentTime - target) > 0.05) {
			videoEl.currentTime = target;
		}
	});

	// ── Sync: active clip source switch ──────────────────────────────────────
	// When the active clip changes (different path), update <video> src.
	$effect(() => {
		const clip = editor.activeClip;
		const path = clip?.path ?? null;
		if (!videoEl || !path) return;
		if (videoEl.src !== path) {
			videoEl.src = path;
			videoEl.load();
		}
	});

	// ── Focus drag on overlay ─────────────────────────────────────────────────
	let isDraggingFocus = false;

	function overlayPointerDown(e: PointerEvent) {
		if (editor.isPlaying || !editor.selectedZoom) return;
		e.preventDefault();
		isDraggingFocus = true;
		(e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
		applyFocusDrag(e);
	}

	function overlayPointerMove(e: PointerEvent) {
		if (!isDraggingFocus) return;
		applyFocusDrag(e);
	}

	function overlayPointerUp(e: PointerEvent) {
		if (!isDraggingFocus) return;
		isDraggingFocus = false;
		try { (e.currentTarget as HTMLElement).releasePointerCapture(e.pointerId); } catch {}
	}

	function applyFocusDrag(e: PointerEvent) {
		if (!overlayEl || !editor.selectedZoom) return;
		const rect   = overlayEl.getBoundingClientRect();
		const rawCx  = (e.clientX - rect.left) / rect.width;
		const rawCy  = (e.clientY - rect.top)  / rect.height;
		const zScale = ZOOM_DEPTH_SCALES[editor.selectedZoom.depth];
		const { cx, cy } = clampFocus(rawCx, rawCy, zScale);
		editor.updateZoomRegion(editor.selectedZoom.id, { cx, cy });
	}

	// ── Transport: derived state ──────────────────────────────────────────────
	const overlayCursor    = $derived(editor.selectedZoom && !editor.isPlaying ? 'grab' : 'default');
	const overlayPointers  = $derived(editor.selectedZoom && !editor.isPlaying ? 'auto' : 'none');
	const progressPct      = $derived(editor.durationMs > 0 ? (editor.currentTimeMs / editor.durationMs) * 100 : 0);

	const SPEED_CHIPS  = [0.25, 0.5, 1, 1.5, 2] as const;
	const ASPECT_CHIPS = ['16:9', '4:3', '1:1', '9:16'] as const;

	function onScrub(e: Event) {
		editor.seekTo(parseFloat((e.target as HTMLInputElement).value));
	}
</script>

<!-- ── Root ───────────────────────────────────────────────────────────────── -->
<div class="video-canvas-root">

	{#if false && editor.clips.length === 0}
		<!-- Empty state -->
		<div class="empty-state">
			<div class="empty-icon">▶</div>
			<p class="empty-title">No video loaded</p>
			<p class="empty-sub">Import a video or record your screen to get started.</p>
			<button class="empty-cta" onclick={() => { editor.activePage = 'media'; }}>
				Go to Media
			</button>
		</div>
	{:else}
		<!-- Canvas + overlay wrapper -->
		<div class="canvas-area">
			<!-- Pixi injects its <canvas> here -->
			<div class="pixi-host" bind:this={containerEl}></div>

			<!-- Overlay: focus indicator + future Steps 14–17 children -->
			<!-- svelte-ignore a11y_no_static_element_interactions -->
			<div
				class="canvas-overlay"
				bind:this={overlayEl}
				style="cursor: {overlayCursor}; pointer-events: {overlayPointers};"
				onpointerdown={overlayPointerDown}
				onpointermove={overlayPointerMove}
				onpointerup={overlayPointerUp}
				onpointerleave={overlayPointerUp}
			>
				<!-- Step 14: zoom crosshair + depth ring (shown while paused + zoom selected) -->
				{#if editor.selectedZoom && !editor.isPlaying}
					<ZoomHandleOverlay {editor} />
				{/if}

				<!-- Step 16: annotation drag/resize (Edit + Annotate pages) -->
				{#if editor.activePage === 'edit' || editor.activePage === 'annotate'}
					<AnnotationOverlay {editor} />
				{/if}

				<!-- Step 17: crop overlay (when crop tool is active) -->
				{#if editor.annotationTool === 'crop'}
					<CropOverlay {editor} />
				{/if}
			</div>
		</div>

		<!-- Transport controls (HTML, not PixiJS) -->
		<div class="transport">

			<!-- Play / Pause -->
			<button
				class="play-btn"
				onclick={() => editor.togglePlayback()}
				aria-label={editor.isPlaying ? 'Pause' : 'Play'}
			>
				{#if editor.isPlaying}
					<svg viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
						<rect x="3" y="2" width="4" height="12" rx="1"/>
						<rect x="9" y="2" width="4" height="12" rx="1"/>
					</svg>
				{:else}
					<svg viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
						<path d="M4 2.5l10 5.5-10 5.5V2.5z"/>
					</svg>
				{/if}
			</button>

			<!-- Current time -->
			<span class="time-label">{fmtTime(editor.currentTimeMs)}</span>

			<!-- Scrub bar -->
			<div class="scrub-wrap">
				<input
					type="range"
					class="scrub-input"
					min="0"
					max={editor.durationMs}
					step="1"
					value={editor.currentTimeMs}
					oninput={onScrub}
					aria-label="Seek"
				/>
				<div class="scrub-track"></div>
				<div class="scrub-fill" style="width: {progressPct}%;"></div>
			</div>

			<!-- Duration -->
			<span class="time-label muted">{fmtTime(editor.durationMs)}</span>

			<!-- Speed chips -->
			<div class="chip-row" role="group" aria-label="Playback speed">
				{#each SPEED_CHIPS as s}
					<button
						class="chip {editor.playbackSpeed === s ? 'chip-active' : ''}"
						onclick={() => {
							editor.setPlaybackSpeed(s);
							if (videoEl) videoEl.playbackRate = s;
						}}
					>{s}×</button>
				{/each}
			</div>

			<!-- Aspect ratio chips -->
			<div class="chip-row" role="group" aria-label="Aspect ratio">
				{#each ASPECT_CHIPS as ar}
					<button
						class="chip {editor.aspectRatio === ar ? 'chip-active' : ''}"
						onclick={() => editor.setAspectRatio(ar)}
					>{ar}</button>
				{/each}
			</div>

		</div>
	{/if}

	<!-- Hidden video element — always in DOM so Pixi can attach VideoSource.
	     Source is set programmatically inside the $effect above. -->
	<!-- svelte-ignore a11y_media_has_caption -->
	<video bind:this={videoEl} class="hidden-video" preload="metadata" playsinline></video>
</div>

<style>
	.video-canvas-root {
		flex: 1;
		min-height: 0;
		display: flex;
		flex-direction: column;
		width: 100%;
		height: 100%;
		background: rgb(var(--color-bg-primary));
		overflow: hidden;
	}

	/* ── Canvas area ──────────────────────────────────────────────────────── */
	.canvas-area {
		flex: 1;
		min-height: 0;
		position: relative;
	}

	.pixi-host {
		position: absolute;
		inset: 0;
	}

	/* Pixi injects a <canvas> child — fill the host */
	.pixi-host :global(canvas) {
		display: block;
		width: 100% !important;
		height: 100% !important;
	}

	.canvas-overlay {
		position: absolute;
		inset: 0;
		user-select: none;
	}

	/* ── Empty state ──────────────────────────────────────────────────────── */
	.empty-state {
		flex: 1;
		display: flex;
		flex-direction: column;
		align-items: center;
		justify-content: center;
		gap: 8px;
		color: rgb(var(--color-text-secondary));
	}

	.empty-icon {
		font-size: 40px;
		opacity: 0.25;
		margin-bottom: 4px;
	}

	.empty-title {
		font-size: var(--font-size-base);
		font-weight: 500;
		color: rgb(var(--color-text-primary));
		margin: 0;
	}

	.empty-sub {
		font-size: var(--font-size-sm);
		margin: 0;
		text-align: center;
		max-width: 240px;
	}

	.empty-cta {
		margin-top: 12px;
		padding: 6px 16px;
		background: rgb(var(--color-accent-primary) / 0.12);
		border: 1px solid rgb(var(--color-accent-primary) / 0.4);
		border-radius: var(--radius-sm);
		color: rgb(var(--color-accent-primary));
		font-size: var(--font-size-sm);
		cursor: pointer;
		transition: background var(--duration-fast);
	}

	.empty-cta:hover { background: rgb(var(--color-accent-primary) / 0.22); }

	/* ── Transport controls ───────────────────────────────────────────────── */
	.transport {
		flex-shrink: 0;
		display: flex;
		align-items: center;
		gap: 8px;
		padding: 0 12px;
		height: 44px;
		background: rgb(var(--color-bg-secondary));
		border-top: 1px solid rgb(var(--color-border-default));
		overflow: hidden;
	}

	.play-btn {
		flex-shrink: 0;
		width: 30px;
		height: 30px;
		border-radius: var(--radius-sm);
		background: rgb(var(--color-accent-primary));
		border: none;
		color: #fff;
		cursor: pointer;
		display: flex;
		align-items: center;
		justify-content: center;
		transition: opacity var(--duration-fast);
	}

	.play-btn:hover { opacity: 0.82; }
	.play-btn svg   { width: 13px; height: 13px; }

	.time-label {
		flex-shrink: 0;
		font-size: var(--font-size-xs);
		color: rgb(var(--color-text-primary));
		font-variant-numeric: tabular-nums;
		min-width: 50px;
	}

	.time-label.muted { color: rgb(var(--color-text-secondary)); }

	/* Scrub bar */
	.scrub-wrap {
		flex: 1;
		position: relative;
		height: 20px;
		display: flex;
		align-items: center;
		min-width: 60px;
	}

	.scrub-input {
		position: absolute;
		inset: 0;
		width: 100%;
		height: 100%;
		opacity: 0;
		cursor: pointer;
		z-index: 2;
		margin: 0;
	}

	.scrub-track {
		position: absolute;
		left: 0; right: 0;
		height: 3px;
		background: rgb(var(--color-surface-3));
		border-radius: 2px;
	}

	.scrub-fill {
		position: absolute;
		left: 0;
		height: 3px;
		background: rgb(var(--color-accent-primary));
		border-radius: 2px;
		pointer-events: none;
		transition: width 0.05s linear;
		max-width: 100%;
	}

	/* Chips */
	.chip-row {
		display: flex;
		gap: 2px;
		flex-shrink: 0;
	}

	.chip {
		padding: 2px 5px;
		font-size: 10px;
		line-height: 1.7;
		border-radius: var(--radius-sm);
		background: rgb(var(--color-surface-2));
		border: 1px solid rgb(var(--color-border-default));
		color: rgb(var(--color-text-secondary));
		cursor: pointer;
		transition: all var(--duration-fast);
	}

	.chip:hover {
		background: rgb(var(--color-surface-3));
		color: rgb(var(--color-text-primary));
	}

	.chip-active {
		background: rgb(var(--color-accent-primary) / 0.12);
		border-color: rgb(var(--color-accent-primary) / 0.5);
		color: rgb(var(--color-accent-primary));
	}

	/* Hidden video element */
	.hidden-video { display: none; }
</style>
