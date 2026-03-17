<script lang="ts">
    /**
     * Timeline — Step 8.
     *
     * Owns scrollMs + pxPerMs (zoom). Renders TimelineRuler + TimelineTrack.
     * Keyboard: Z=zoom, T=trim, A=annotation, S=speed, K=keyframe,
     *           Delete=delete selected, Space=play/pause, ←/→=nudge.
     * Wheel: Ctrl+scroll=zoom (around cursor), plain scroll=pan.
     */

    import type { AppRecorderState } from '../AppRecorderState.svelte';
    import { fitZoom, clamp, defaultRegionDuration, findGapAtPlayhead } from './timelineUtils';
    import TimelineRuler from './TimelineRuler.svelte';
    import TimelineTrack from './TimelineTrack.svelte';

    // Svelte action: registers a non-passive wheel listener so preventDefault works.
    // Plain onwheel in Svelte 5 is passive by default (browsers enforce this).
    function nonPassiveWheel(node: HTMLElement, handler: (e: WheelEvent) => void) {
        node.addEventListener('wheel', handler, { passive: false });
        return { destroy() { node.removeEventListener('wheel', handler); } };
    }

    let { editor }: { editor: AppRecorderState } = $props();

    // ── Visible range state ───────────────────────────────────────────────────
    let pxPerMs       = $state(0.08);
    let scrollMs      = $state(0);
    let containerEl   = $state<HTMLDivElement | null>(null);
    let trackWidth    = $state(0);
    let hasManualZoom = $state(false);
    let tooltipEl     = $state<HTMLDivElement | null>(null);

    $effect(() => {
        if (!hasManualZoom && editor.durationMs > 0 && trackWidth > 0) {
            pxPerMs  = fitZoom(editor.durationMs, trackWidth);
            scrollMs = 0;
        }
    });

    // Auto-scroll: keep playhead in view during playback.
    // When the playhead reaches 85% of the visible area, scroll forward.
    $effect(() => {
        if (!editor.isPlaying || trackWidth <= 0 || pxPerMs <= 0) return;
        const playheadPx = (editor.currentTimeMs - scrollMs) * pxPerMs;
        const rightThreshold = trackWidth * 0.85;
        if (playheadPx > rightThreshold) {
            scrollMs = clamp(
                editor.currentTimeMs - (trackWidth * 0.15) / pxPerMs,
                0,
                Math.max(0, editor.durationMs - visibleMs * 0.1),
            );
        }
    });

    const visibleMs = $derived(trackWidth > 0 && pxPerMs > 0 ? trackWidth / pxPerMs : 0);

    // ── DOM-direct tooltip ────────────────────────────────────────────────────
    function showTooltip(text: string, screenX: number) {
        if (!tooltipEl || !containerEl) return;
        tooltipEl.textContent = text;
        tooltipEl.style.opacity = '1';
        const rect = containerEl.getBoundingClientRect();
        const x = clamp(screenX - rect.left - 48, 0, rect.width - 100);
        tooltipEl.style.left = `${x}px`;
    }
    function hideTooltip() {
        if (tooltipEl) tooltipEl.style.opacity = '0';
    }

    // ── Wheel ─────────────────────────────────────────────────────────────────
    function onWheel(e: WheelEvent) {
        e.preventDefault();
        if (e.ctrlKey || e.metaKey) {
            hasManualZoom = true;
            const rect          = containerEl!.getBoundingClientRect();
            const mouseOffsetPx = e.clientX - rect.left;
            const mouseTimeMs   = mouseOffsetPx / pxPerMs + scrollMs;
            const factor        = e.deltaY < 0 ? 1.12 : 1 / 1.12;
            const newPx         = clamp(pxPerMs * factor, 0.005, 10);
            scrollMs = clamp(mouseTimeMs - mouseOffsetPx / newPx, 0,
                Math.max(0, editor.durationMs - visibleMs * 0.1));
            pxPerMs = newPx;
        } else {
            scrollMs = clamp(scrollMs + e.deltaY / pxPerMs, 0,
                Math.max(0, editor.durationMs - visibleMs * 0.1));
        }
    }

    // ── Keyboard ──────────────────────────────────────────────────────────────
    function onKeyDown(e: KeyboardEvent) {
        const tgt = e.target as HTMLElement;
        if (tgt.tagName === 'INPUT' || tgt.tagName === 'TEXTAREA') return;
        const total  = editor.durationMs;
        if (total <= 0) return;
        const playMs = editor.currentTimeMs;

        // Resolve the target clip for region additions.
        // Priority: active clip at playhead → selected clip → first clip.
        const targetClip = editor.activeClip
            ?? editor.selectedClip
            ?? (editor.clips.length > 0 ? editor.clips[0] : null);

        switch (e.key) {
            case 'z': case 'Z':
                if (e.metaKey || e.ctrlKey) break;
                if (!targetClip) break;
                e.preventDefault();
                {
                    const clipDur = targetClip.end - targetClip.start;
                    const localMs = playMs - targetClip.position;
                    const defDur  = defaultRegionDuration(clipDur);
                    const g = findGapAtPlayhead(localMs, targetClip.zoomRegions, clipDur, defDur);
                    if (g) editor.addZoomRegion(targetClip.id, g.startMs, g.endMs);
                }
                break;
            case 't': case 'T':
                if (!targetClip) break;
                e.preventDefault();
                {
                    const clipDur = targetClip.end - targetClip.start;
                    const localMs = playMs - targetClip.position;
                    const defDur  = defaultRegionDuration(clipDur);
                    const g = findGapAtPlayhead(localMs, targetClip.trimRegions, clipDur, defDur);
                    if (g) editor.addTrimRegion(targetClip.id, g.startMs, g.endMs);
                }
                break;
            case 'a': case 'A':
                if (e.metaKey || e.ctrlKey) break;
                if (!targetClip) break;
                e.preventDefault();
                {
                    const clipDur = targetClip.end - targetClip.start;
                    const localMs = playMs - targetClip.position;
                    const defDur  = defaultRegionDuration(clipDur);
                    editor.addAnnotation(targetClip.id, localMs, Math.min(localMs + defDur, clipDur), 'text');
                }
                break;
            case 's': case 'S':
                if (e.metaKey || e.ctrlKey) break;
                if (!targetClip) break;
                e.preventDefault();
                {
                    const clipDur = targetClip.end - targetClip.start;
                    const localMs = playMs - targetClip.position;
                    const defDur  = defaultRegionDuration(clipDur);
                    const g = findGapAtPlayhead(localMs, targetClip.speedRegions, clipDur, defDur);
                    if (g) editor.addSpeedRegion(targetClip.id, g.startMs, g.endMs, 1.5);
                }
                break;
            case 'k': case 'K':
                if (!targetClip) break;
                e.preventDefault();
                {
                    const localMs = playMs - targetClip.position;
                    editor.addKeyframe(targetClip.id, localMs);
                }
                break;
            case 'Delete': case 'Backspace':
                if (editor.selectedKeyframeId)        editor.removeKeyframe(editor.selectedKeyframeId);
                else if (editor.selectedZoomId)       editor.removeZoomRegion(editor.selectedZoomId);
                else if (editor.selectedTrimId)       editor.removeTrimRegion(editor.selectedTrimId);
                else if (editor.selectedAnnotationId) editor.removeAnnotation(editor.selectedAnnotationId);
                else if (editor.selectedSpeedId)      editor.removeSpeedRegion(editor.selectedSpeedId);
                else if (editor.selectedTransitionId) editor.removeTransition(editor.selectedTransitionId);
                else if (editor.selectedClipId)       editor.removeClip(editor.selectedClipId);
                break;
            case ' ':
                e.preventDefault();
                editor.togglePlayback();
                break;
            case 'ArrowLeft':
                e.preventDefault();
                editor.seekTo(Math.max(0, playMs - (e.shiftKey ? 1000 : 33)));
                break;
            case 'ArrowRight':
                e.preventDefault();
                editor.seekTo(Math.min(total, playMs + (e.shiftKey ? 1000 : 33)));
                break;
            case 'Tab': {
                // Tab cycles annotations within the active clip at the playhead
                if (!targetClip) break;
                const localMs = playMs - targetClip.position;
                const active = targetClip.annotationRegions
                    .filter(a => localMs >= a.startMs && localMs <= a.endMs)
                    .sort((a, b) => a.zIndex - b.zIndex);
                if (!active.length) break;
                e.preventDefault();
                if (!editor.selectedAnnotationId || !active.some(a => a.id === editor.selectedAnnotationId)) {
                    editor.selectAnnotation(active[0]!.id);
                } else {
                    const idx  = active.findIndex(a => a.id === editor.selectedAnnotationId);
                    const next = e.shiftKey
                        ? (idx - 1 + active.length) % active.length
                        : (idx + 1) % active.length;
                    editor.selectAnnotation(active[next]!.id);
                }
                break;
            }
        }
    }

    // ── Toolbar helpers ───────────────────────────────────────────────────────
    function targetClip() {
        return editor.activeClip ?? editor.selectedClip ?? (editor.clips.length > 0 ? editor.clips[0] : null);
    }

    function addZoom() {
        const clip = targetClip(); if (!clip) return;
        const clipDur = clip.end - clip.start;
        const localMs = editor.currentTimeMs - clip.position;
        const defDur  = defaultRegionDuration(clipDur);
        const g = findGapAtPlayhead(localMs, clip.zoomRegions, clipDur, defDur);
        if (g) editor.addZoomRegion(clip.id, g.startMs, g.endMs);
    }
    function addTrim() {
        const clip = targetClip(); if (!clip) return;
        const clipDur = clip.end - clip.start;
        const localMs = editor.currentTimeMs - clip.position;
        const defDur  = defaultRegionDuration(clipDur);
        const g = findGapAtPlayhead(localMs, clip.trimRegions, clipDur, defDur);
        if (g) editor.addTrimRegion(clip.id, g.startMs, g.endMs);
    }
    function addAnnotation() {
        const clip = targetClip(); if (!clip) return;
        const clipDur = clip.end - clip.start;
        const localMs = editor.currentTimeMs - clip.position;
        const defDur  = defaultRegionDuration(clipDur);
        editor.addAnnotation(clip.id, localMs, Math.min(localMs + defDur, clipDur), 'text');
    }
    function addSpeed() {
        const clip = targetClip(); if (!clip) return;
        const clipDur = clip.end - clip.start;
        const localMs = editor.currentTimeMs - clip.position;
        const defDur  = defaultRegionDuration(clipDur);
        const g = findGapAtPlayhead(localMs, clip.speedRegions, clipDur, defDur);
        if (g) editor.addSpeedRegion(clip.id, g.startMs, g.endMs, 1.5);
    }

    const hasVideo = $derived(editor.clips.length > 0);
</script>

<!-- svelte-ignore a11y_no_noninteractive_tabindex -->
<div
    class="timeline"
    bind:this={containerEl}
    use:nonPassiveWheel={onWheel}
    onkeydown={onKeyDown}
    tabindex="0"
    role="region"
    aria-label="Video timeline"
>
    {#if !hasVideo}
        <div class="empty-state">No video loaded — import or record to begin</div>
    {:else}
        <div class="toolbar">
            <div class="toolbar-actions">
                <button class="tool-btn zoom-btn" onclick={addZoom}       title="Add Zoom (Z)">
                    <svg width="13" height="13" viewBox="0 0 16 16" fill="none" aria-hidden="true">
                        <circle cx="7" cy="7" r="5" stroke="currentColor" stroke-width="1.5"/>
                        <line x1="11" y1="11" x2="15" y2="15" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
                        <line x1="7" y1="4.5" x2="7" y2="9.5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
                        <line x1="4.5" y1="7" x2="9.5" y2="7" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
                    </svg>
                    <span>Zoom</span>
                </button>
                <button class="tool-btn trim-btn" onclick={addTrim}       title="Add Trim (T)">
                    <svg width="13" height="13" viewBox="0 0 16 16" fill="none" aria-hidden="true">
                        <path d="M2 4 L8 8 L2 12" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
                        <path d="M14 4 L8 8 L14 12" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
                    </svg>
                    <span>Trim</span>
                </button>
                <button class="tool-btn ann-btn"  onclick={addAnnotation} title="Add Annotation (A)">
                    <svg width="13" height="13" viewBox="0 0 16 16" fill="none" aria-hidden="true">
                        <rect x="2" y="3" width="12" height="8" rx="1.5" stroke="currentColor" stroke-width="1.5"/>
                        <path d="M6 13 L8 11 L10 13" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
                    </svg>
                    <span>Note</span>
                </button>
                <button class="tool-btn spd-btn"  onclick={addSpeed}      title="Add Speed (S)">
                    <svg width="13" height="13" viewBox="0 0 16 16" fill="none" aria-hidden="true">
                        <path d="M2 12 Q5 3 8 8 Q11 13 14 4" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" fill="none"/>
                    </svg>
                    <span>Speed</span>
                </button>
            </div>
            <div class="toolbar-hints">
                <span><kbd>Ctrl+Scroll</kbd> Zoom</span>
                <span><kbd>Space</kbd> Play</span>
                <span><kbd>←/→</kbd> Nudge</span>
                <div class="track-height-ctrl" title="Track height">
                    <!-- rows icon -->
                    <svg width="11" height="11" viewBox="0 0 12 12" fill="none" aria-hidden="true">
                        <rect x="1" y="1.5" width="10" height="2.5" rx="0.75" fill="currentColor" opacity="0.6"/>
                        <rect x="1" y="5.5" width="10" height="2"   rx="0.75" fill="currentColor" opacity="0.45"/>
                        <rect x="1" y="9"   width="10" height="1.5" rx="0.75" fill="currentColor" opacity="0.3"/>
                    </svg>
                    <input
                        type="range"
                        class="track-height-slider"
                        min="28"
                        max="120"
                        step="4"
                        value={editor.trackHeight}
                        oninput={(e) => { editor.trackHeight = parseInt((e.target as HTMLInputElement).value, 10); }}
                        title="Track height: {editor.trackHeight}px"
                    />
                </div>
            </div>
        </div>

        <TimelineRuler
            {scrollMs}
            {visibleMs}
            {pxPerMs}
            durationMs={editor.durationMs}
            currentTimeMs={editor.currentTimeMs}
            onSeek={(ms) => editor.seekTo(ms)}
        />

        <TimelineTrack
            {editor}
            bind:scrollMs
            {pxPerMs}
            {visibleMs}
            bind:trackWidth
            onSeek={(ms) => editor.seekTo(ms)}
            {showTooltip}
            {hideTooltip}
        />

        <div class="drag-tooltip" bind:this={tooltipEl} style="opacity:0"></div>
    {/if}
</div>

<style>
    .timeline {
        display: flex;
        flex-direction: column;
        height: 100%;
        width: 100%;
        background: rgb(var(--color-bg-tertiary));
        overflow: hidden;
        outline: none;
        user-select: none;
        position: relative;
    }
    .empty-state {
        flex: 1;
        display: flex;
        align-items: center;
        justify-content: center;
        font-size: 0.75rem;
        color: rgb(var(--color-text-muted));
        opacity: 0.45;
        font-style: italic;
    }
    .toolbar {
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: 0 8px;
        height: 34px;
        flex-shrink: 0;
        border-bottom: 1px solid rgb(var(--color-border-default) / 0.5);
    }
    .toolbar-actions { display: flex; gap: 2px; align-items: center; }
    .tool-btn {
        display: flex;
        align-items: center;
        gap: 4px;
        padding: 3px 7px;
        border-radius: 4px;
        border: none;
        background: transparent;
        color: rgb(var(--color-text-muted));
        font-size: 0.68rem;
        font-weight: 500;
        cursor: pointer;
        transition: background 0.1s, color 0.1s;
        white-space: nowrap;
    }
    .tool-btn:hover  { background: rgb(var(--color-bg-secondary)); }
    .zoom-btn:hover  { color: rgb(52 178 123); }
    .trim-btn:hover  { color: rgb(239 68 68); }
    .ann-btn:hover   { color: rgb(180 160 70); }
    .spd-btn:hover   { color: rgb(217 119 6); }
    .toolbar-hints {
        display: flex;
        align-items: center;
        gap: 10px;
        font-size: 0.62rem;
        color: rgb(var(--color-text-muted));
        opacity: 0.55;
    }
    .toolbar-hints kbd {
        font-family: inherit;
        padding: 1px 4px;
        border-radius: 3px;
        background: rgb(var(--color-bg-secondary));
        border: 1px solid rgb(var(--color-border-default) / 0.5);
        font-size: 0.58rem;
        color: rgb(52 178 123);
    }
    .track-height-ctrl {
        display: flex;
        align-items: center;
        gap: 4px;
        color: rgb(var(--color-text-muted));
        opacity: 0.65;
        margin-left: 4px;
    }
    .track-height-ctrl:hover { opacity: 1; }
    .track-height-slider {
        -webkit-appearance: none;
        appearance: none;
        width: 60px;
        height: 3px;
        border-radius: 2px;
        background: rgb(var(--color-border-default));
        outline: none;
        cursor: pointer;
    }
    .track-height-slider::-webkit-slider-thumb {
        -webkit-appearance: none;
        appearance: none;
        width: 10px;
        height: 10px;
        border-radius: 50%;
        background: rgb(var(--color-text-muted));
        cursor: pointer;
        transition: background 0.1s;
    }
    .track-height-slider:hover::-webkit-slider-thumb {
        background: rgb(52 178 123);
    }
    .drag-tooltip {
        position: absolute;
        top: 38px;
        pointer-events: none;
        z-index: 60;
        padding: 2px 6px;
        border-radius: 4px;
        background: rgb(0 0 0 / 0.85);
        font-size: 0.65rem;
        color: rgb(255 255 255 / 0.9);
        font-weight: 500;
        white-space: nowrap;
        border: 1px solid rgb(255 255 255 / 0.1);
        box-shadow: 0 2px 8px rgb(0 0 0 / 0.4);
        transition: opacity 0.1s;
    }
</style>
