<script lang="ts">
    /**
     * TimelineTrack — multi-clip timeline with independently resizable rows.
     *
     * Horizontal sidebar width uses SplitPanel (one handle, simple case).
     * Vertical row heights use plain $state + mousedown drag — SplitPanel's
     * proportional flex distribution is the wrong model for independent
     * fixed-height rows that expand/collapse dynamically.
     *
     * Row height state lives in `rowHeights: Record<rowId, px>`.
     * rowId = `${clipId}:clip` | `${clipId}:zoom` | ... etc.
     */

    import { SplitPanel } from '@hpd/hpd-agent-headless-ui';
    import type { AppRecorderState, ClipModel } from '../AppRecorderState.svelte';
    import { msToPixel, pixelToMs, clamp, formatMs, pickAxisScale } from './timelineUtils';
    import RegionChip from './RegionChip.svelte';
    import Playhead from './Playhead.svelte';
    import { ZOOM_DEPTH_LABELS } from '../AppRecorderState.svelte';

    let {
        editor,
        scrollMs = $bindable(0),
        pxPerMs,
        visibleMs,
        trackWidth = $bindable(0),
        onSeek,
        showTooltip,
        hideTooltip,
    }: {
        editor:       AppRecorderState;
        scrollMs:     number;
        pxPerMs:      number;
        visibleMs:    number;
        trackWidth?:  number;
        onSeek:       (ms: number) => void;
        showTooltip:  (text: string, screenX: number) => void;
        hideTooltip:  () => void;
    } = $props();

    // ── Row height defaults (px) ──────────────────────────────────────────────
    const CLIP_H_DEFAULT = 48;
    const SUB_H_DEFAULT  = 28;
    const KEY_H_DEFAULT  = 18;
    const ROW_MIN_H      = 14;
    const CLIP_MIN_H     = 28;
    const HANDLE_H       = 4;
    const MIN_CHIP_MS    = 100;

    // ── Row heights — independent per row, not distributed proportionally ─────
    // Key: `${clipId}:clip` | `${clipId}:zoom` | etc.
    let rowHeights = $state<Record<string, number>>({});

    function rowH(key: string, fallback: number): number {
        return rowHeights[key] ?? fallback;
    }

    function defaultH(suffix: string): number {
        if (suffix === 'clip') return CLIP_H_DEFAULT;
        if (suffix === 'key')  return KEY_H_DEFAULT;
        return SUB_H_DEFAULT;
    }

    // ── Global trackHeight slider: reset all clip rows ────────────────────────
    $effect(() => {
        const h = editor.trackHeight;
        // Touch rowHeights so write is reactive
        for (const clip of editor.clips) {
            rowHeights[`${clip.id}:clip`] = h;
        }
    });

    // ── Row drag-to-resize ────────────────────────────────────────────────────
    let draggingKey = $state<string | null>(null);

    function onHandleMousedown(e: MouseEvent, key: string, suffix: string) {
        e.preventDefault();
        draggingKey = key;
        const startY   = e.clientY;
        const startH   = rowH(key, defaultH(suffix));
        const minH     = suffix === 'clip' ? CLIP_MIN_H : ROW_MIN_H;

        function onMove(ev: MouseEvent) {
            const delta = ev.clientY - startY;
            rowHeights[key] = Math.max(minH, startH + delta);
        }
        function onUp() {
            draggingKey = null;
            window.removeEventListener('mousemove', onMove);
            window.removeEventListener('mouseup', onUp);
        }
        window.addEventListener('mousemove', onMove);
        window.addEventListener('mouseup', onUp);
    }

    // ── Total track height (for lanes-rows container) ─────────────────────────
    const totalTrackH = $derived.by(() => {
        if (editor.clips.length === 0) return CLIP_H_DEFAULT;
        let h = 0;
        let rowCount = 0;
        for (const clip of sortedClips) {
            h += rowH(`${clip.id}:clip`, CLIP_H_DEFAULT);
            rowCount++;
            if (clip.expanded) {
                h += rowH(`${clip.id}:zoom`,  SUB_H_DEFAULT);
                h += rowH(`${clip.id}:trim`,  SUB_H_DEFAULT);
                h += rowH(`${clip.id}:speed`, SUB_H_DEFAULT);
                h += rowH(`${clip.id}:note`,  SUB_H_DEFAULT);
                h += rowH(`${clip.id}:key`,   KEY_H_DEFAULT);
                rowCount += 5;
            }
        }
        // handles sit between every adjacent row pair
        return h + Math.max(0, rowCount - 1) * HANDLE_H;
    });

    // ── Track area scroll sync ────────────────────────────────────────────────
    let lanesScrollEl = $state<HTMLDivElement | null>(null);
    let scrollSyncGen = 0;

    $effect(() => {
        if (!lanesScrollEl) return;
        const target = scrollMs * pxPerMs;
        if (Math.abs(lanesScrollEl.scrollLeft - target) > 1) {
            const gen = ++scrollSyncGen;
            lanesScrollEl.scrollLeft = target;
            void gen;
        }
    });

    function onLanesScroll() {
        if (!lanesScrollEl) return;
        const target = scrollMs * pxPerMs;
        if (Math.abs(lanesScrollEl.scrollLeft - target) <= 1) return;
        scrollMs = lanesScrollEl.scrollLeft / pxPerMs;
    }

    // ── Track area outer element (for trackWidth measurement) ─────────────────
    let trackAreaEl = $state<HTMLDivElement | null>(null);

    $effect(() => {
        if (!trackAreaEl) return;
        const ro = new ResizeObserver(entries => {
            const w = entries[0]?.contentRect.width ?? trackWidth;
            trackWidth = w;
        });
        ro.observe(trackAreaEl);
        return () => ro.disconnect();
    });

    // ── Derived ───────────────────────────────────────────────────────────────
    const trackPx = $derived(
        editor.durationMs > 0 ? editor.durationMs * pxPerMs : 1000
    );

    const gridPx = $derived.by(() => {
        const scale = pickAxisScale(visibleMs);
        return Math.max(2, scale.gridMs * pxPerMs);
    });

    const sortedClips = $derived(
        [...editor.clips].sort((a, b) => a.position - b.position)
    );

    // ── Click on empty space → seek ───────────────────────────────────────────
    function onLanesClick(e: MouseEvent) {
        if ((e.target as HTMLElement).closest('.chip')) return;
        if ((e.target as HTMLElement).closest('.clip-bar')) return;
        editor.clearSelection();
        const rect = lanesScrollEl!.getBoundingClientRect();
        const offsetX = e.clientX - rect.left + lanesScrollEl!.scrollLeft;
        if (offsetX < 0) return;
        const ms = clamp(pixelToMs(offsetX, scrollMs, pxPerMs), 0, editor.durationMs);
        onSeek(ms);
    }

    // ── Span change callbacks ─────────────────────────────────────────────────
    function onZoomSpanChange(id: string, s: number, e2: number) { editor.updateZoomRegion(id, { startMs: s, endMs: e2 }); }
    function onTrimSpanChange(id: string, s: number, e2: number) { editor.updateTrimRegion(id, { startMs: s, endMs: e2 }); }
    function onSpeedSpanChange(id: string, s: number, e2: number) { editor.updateSpeedRegion(id, { startMs: s, endMs: e2 }); }
    function onAnnotationSpanChange(id: string, s: number, e2: number) { editor.updateAnnotation(id, { startMs: s, endMs: e2 }); }

    // ── Helpers ───────────────────────────────────────────────────────────────
    function clipLeft(clip: ClipModel)  { return msToPixel(clip.position, scrollMs, pxPerMs); }
    function clipWidth(clip: ClipModel) { return Math.max(6, (clip.end - clip.start) * pxPerMs); }
    function clipFilename(clip: ClipModel) {
        return clip.path ? clip.path.split('/').pop() ?? clip.path : 'Recording';
    }
    function kfPositions(clip: ClipModel) {
        return clip.keyframes.map(kf => ({
            ...kf,
            x: msToPixel(clip.position + kf.timeMs, scrollMs, pxPerMs),
        }));
    }
</script>

<div class="track-area" bind:this={trackAreaEl}>

    <!-- ── Outer horizontal split: sidebar | lanes ── -->
    <SplitPanel.Root id="tl-h" class="track-h-root">
        <SplitPanel.Split axis="horizontal" class="track-h-split">

            <!-- ── Sidebar pane ── -->
            <SplitPanel.Pane
                id="tl-sidebar"
                initialSize={80}
                initialSizeUnit="pixels"
                minSize={56}
                maxSize={200}
                priority="low"
            >
                {#snippet child({ props, size })}
                    <div {...props}>
                        <div class="sidebar" style="width:{size}px">
                            {#if editor.clips.length === 0}
                                <div class="sidebar-row" style="height:{CLIP_H_DEFAULT}px">
                                    <span class="row-label clip-label">Clips</span>
                                </div>
                            {:else}
                                {#each sortedClips as clip, clipIdx (clip.id)}
                                    {#if clipIdx > 0}
                                        <div class="row-handle-h" style="height:{HANDLE_H}px"></div>
                                    {/if}
                                    <div
                                        class="sidebar-row sidebar-clip-row"
                                        style="height:{rowH(`${clip.id}:clip`, CLIP_H_DEFAULT)}px"
                                    >
                                        <button
                                            class="expand-btn"
                                            class:expanded={clip.expanded}
                                            onclick={(e) => { e.stopPropagation(); editor.toggleClipExpanded(clip.id); }}
                                            title={clip.expanded ? 'Collapse sub-lanes' : 'Expand sub-lanes'}
                                            aria-label={clip.expanded ? 'Collapse' : 'Expand'}
                                        >
                                            <svg width="8" height="8" viewBox="0 0 8 8" fill="currentColor">
                                                <path d={clip.expanded ? 'M1 2.5 L4 5.5 L7 2.5' : 'M2.5 1 L5.5 4 L2.5 7'} stroke="currentColor" stroke-width="1.2" stroke-linecap="round" stroke-linejoin="round" fill="none"/>
                                            </svg>
                                        </button>
                                        <span class="row-label clip-label" title={clipFilename(clip)}>{clipFilename(clip)}</span>
                                    </div>
                                    {#if clip.expanded}
                                        <div class="row-handle-h" style="height:{HANDLE_H}px"></div>
                                        <div class="sidebar-row" style="height:{rowH(`${clip.id}:zoom`,  SUB_H_DEFAULT)}px"><span class="row-label">Zoom</span><kbd class="row-hint">Z</kbd></div>
                                        <div class="row-handle-h" style="height:{HANDLE_H}px"></div>
                                        <div class="sidebar-row" style="height:{rowH(`${clip.id}:trim`,  SUB_H_DEFAULT)}px"><span class="row-label">Trim</span><kbd class="row-hint">T</kbd></div>
                                        <div class="row-handle-h" style="height:{HANDLE_H}px"></div>
                                        <div class="sidebar-row" style="height:{rowH(`${clip.id}:speed`, SUB_H_DEFAULT)}px"><span class="row-label">Speed</span><kbd class="row-hint">S</kbd></div>
                                        <div class="row-handle-h" style="height:{HANDLE_H}px"></div>
                                        <div class="sidebar-row" style="height:{rowH(`${clip.id}:note`,  SUB_H_DEFAULT)}px"><span class="row-label">Note</span><kbd class="row-hint">A</kbd></div>
                                        <div class="row-handle-h" style="height:{HANDLE_H}px"></div>
                                        <div class="sidebar-row" style="height:{rowH(`${clip.id}:key`,   KEY_H_DEFAULT)}px"><span class="row-label">Key</span></div>
                                    {/if}
                                {/each}
                            {/if}
                        </div>
                    </div>
                {/snippet}
            </SplitPanel.Pane>

            <!-- ── Sidebar/lanes resize handle ── -->
            <SplitPanel.Handle>
                {#snippet child({ props, isDragging })}
                    <div {...props} class="resize-handle-v" class:dragging={isDragging}></div>
                {/snippet}
            </SplitPanel.Handle>

            <!-- ── Lanes pane ── -->
            <SplitPanel.Pane id="tl-lanes" priority="high" minSize={200}>
                <!-- svelte-ignore a11y_no_static_element_interactions a11y_click_events_have_key_events -->
                <div
                    class="lanes-scroll"
                    bind:this={lanesScrollEl}
                    onclick={onLanesClick}
                    onscroll={onLanesScroll}
                >
                    <!-- Inner content wider than the pane = horizontal scroll -->
                    <div class="lanes-inner" style="width:{trackPx}px; position:relative; height:{totalTrackH}px">

                        <!-- Grid lines -->
                        <div class="grid-overlay" style="width:{trackPx}px; background-size:{gridPx}px 100%"></div>

                        <!-- ── Rows ── -->
                        <div class="lanes-rows">

                            {#if editor.clips.length === 0}
                                <div class="lane clip-lane" style="height:{CLIP_H_DEFAULT}px"></div>
                            {:else}
                                {#each sortedClips as clip, clipIdx (clip.id)}

                                    {#if clipIdx > 0}
                                        <!-- Handle resizes the PREVIOUS clip's clip row -->
                                        {@const prevId = sortedClips[clipIdx - 1]?.id ?? clip.id}
                                        <!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
                                        <div
                                            class="resize-handle-h"
                                            class:dragging={draggingKey === `${prevId}:clip`}
                                            style="height:{HANDLE_H}px"
                                            onmousedown={(e) => onHandleMousedown(e, `${prevId}:clip`, 'clip')}
                                            role="separator"
                                            aria-label="Resize clip row"
                                        ></div>
                                    {/if}

                                    <!-- ── Clip bar row ── -->
                                    <div class="lane clip-lane" style="height:{rowH(`${clip.id}:clip`, CLIP_H_DEFAULT)}px">
                                        <div
                                            class="clip-bar"
                                            class:clip-selected={editor.selectedClipId === clip.id}
                                            style="left:{clipLeft(clip)}px; width:{clipWidth(clip)}px"
                                            role="button"
                                            tabindex="0"
                                            onclick={(e) => { e.stopPropagation(); editor.selectClip(clip.id); }}
                                            onkeydown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.stopPropagation(); editor.selectClip(clip.id); } }}
                                            aria-label="Clip {clipFilename(clip)}"
                                        >
                                            <span class="clip-name">
                                                {clipFilename(clip)}&nbsp;·&nbsp;{formatMs(clip.end - clip.start)}
                                            </span>
                                        </div>
                                    </div>

                                    {#if clip.expanded}

                                        <!-- ── Zoom row ── -->
                                        <!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
                                        <div
                                            class="resize-handle-h"
                                            class:dragging={draggingKey === `${clip.id}:zoom`}
                                            style="height:{HANDLE_H}px"
                                            onmousedown={(e) => onHandleMousedown(e, `${clip.id}:zoom`, 'zoom')}
                                            role="separator"
                                            aria-label="Resize zoom row"
                                        ></div>
                                        <div class="lane" style="height:{rowH(`${clip.id}:zoom`, SUB_H_DEFAULT)}px">
                                            {#each clip.zoomRegions as region (region.id)}
                                                <RegionChip
                                                    id={region.id}
                                                    startMs={region.startMs} endMs={region.endMs}
                                                    clipOffsetMs={clip.position} {scrollMs} {pxPerMs}
                                                    totalMs={clip.end - clip.start} minDurationMs={MIN_CHIP_MS}
                                                    siblings={clip.zoomRegions} variant="zoom"
                                                    label={ZOOM_DEPTH_LABELS[region.depth]}
                                                    isSelected={editor.selectedZoomId === region.id}
                                                    onSelect={() => editor.selectZoom(region.id)}
                                                    onSpanChange={(s, e2) => onZoomSpanChange(region.id, s, e2)}
                                                    {showTooltip} {hideTooltip}
                                                />
                                            {/each}
                                        </div>

                                        <!-- ── Trim row ── -->
                                        <!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
                                        <div
                                            class="resize-handle-h"
                                            class:dragging={draggingKey === `${clip.id}:trim`}
                                            style="height:{HANDLE_H}px"
                                            onmousedown={(e) => onHandleMousedown(e, `${clip.id}:trim`, 'trim')}
                                            role="separator"
                                            aria-label="Resize trim row"
                                        ></div>
                                        <div class="lane" style="height:{rowH(`${clip.id}:trim`, SUB_H_DEFAULT)}px">
                                            {#each clip.trimRegions as region (region.id)}
                                                <RegionChip
                                                    id={region.id}
                                                    startMs={region.startMs} endMs={region.endMs}
                                                    clipOffsetMs={clip.position} {scrollMs} {pxPerMs}
                                                    totalMs={clip.end - clip.start} minDurationMs={MIN_CHIP_MS}
                                                    siblings={clip.trimRegions} variant="trim" label="Cut"
                                                    isSelected={editor.selectedTrimId === region.id}
                                                    onSelect={() => editor.selectTrim(region.id)}
                                                    onSpanChange={(s, e2) => onTrimSpanChange(region.id, s, e2)}
                                                    {showTooltip} {hideTooltip}
                                                />
                                            {/each}
                                        </div>

                                        <!-- ── Speed row ── -->
                                        <!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
                                        <div
                                            class="resize-handle-h"
                                            class:dragging={draggingKey === `${clip.id}:speed`}
                                            style="height:{HANDLE_H}px"
                                            onmousedown={(e) => onHandleMousedown(e, `${clip.id}:speed`, 'speed')}
                                            role="separator"
                                            aria-label="Resize speed row"
                                        ></div>
                                        <div class="lane" style="height:{rowH(`${clip.id}:speed`, SUB_H_DEFAULT)}px">
                                            {#each clip.speedRegions as region (region.id)}
                                                <RegionChip
                                                    id={region.id}
                                                    startMs={region.startMs} endMs={region.endMs}
                                                    clipOffsetMs={clip.position} {scrollMs} {pxPerMs}
                                                    totalMs={clip.end - clip.start} minDurationMs={MIN_CHIP_MS}
                                                    siblings={clip.speedRegions} variant="speed"
                                                    label="{region.multiplier}×"
                                                    isSelected={editor.selectedSpeedId === region.id}
                                                    onSelect={() => editor.selectSpeed(region.id)}
                                                    onSpanChange={(s, e2) => onSpeedSpanChange(region.id, s, e2)}
                                                    {showTooltip} {hideTooltip}
                                                />
                                            {/each}
                                        </div>

                                        <!-- ── Annotation row ── -->
                                        <!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
                                        <div
                                            class="resize-handle-h"
                                            class:dragging={draggingKey === `${clip.id}:note`}
                                            style="height:{HANDLE_H}px"
                                            onmousedown={(e) => onHandleMousedown(e, `${clip.id}:note`, 'note')}
                                            role="separator"
                                            aria-label="Resize annotation row"
                                        ></div>
                                        <div class="lane" style="height:{rowH(`${clip.id}:note`, SUB_H_DEFAULT)}px">
                                            {#each clip.annotationRegions as region (region.id)}
                                                <RegionChip
                                                    id={region.id}
                                                    startMs={region.startMs} endMs={region.endMs}
                                                    clipOffsetMs={clip.position} {scrollMs} {pxPerMs}
                                                    totalMs={clip.end - clip.start} minDurationMs={MIN_CHIP_MS}
                                                    siblings={[]} variant="annotation"
                                                    label={region.text?.trim() ? region.text.slice(0, 18) : (region.kind === 'arrow' ? '→' : region.kind === 'image' ? '⬛' : 'Text')}
                                                    isSelected={editor.selectedAnnotationId === region.id}
                                                    onSelect={() => editor.selectAnnotation(region.id)}
                                                    onSpanChange={(s, e2) => onAnnotationSpanChange(region.id, s, e2)}
                                                    {showTooltip} {hideTooltip}
                                                />
                                            {/each}
                                        </div>

                                        <!-- ── Keyframe row ── -->
                                        <!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
                                        <div
                                            class="resize-handle-h"
                                            class:dragging={draggingKey === `${clip.id}:key`}
                                            style="height:{HANDLE_H}px"
                                            onmousedown={(e) => onHandleMousedown(e, `${clip.id}:key`, 'key')}
                                            role="separator"
                                            aria-label="Resize keyframe row"
                                        ></div>
                                        <div class="lane kf-lane" style="height:{rowH(`${clip.id}:key`, KEY_H_DEFAULT)}px">
                                            {#each kfPositions(clip) as kf (kf.id)}
                                                <button
                                                    class="kf-marker"
                                                    class:kf-selected={editor.selectedKeyframeId === kf.id}
                                                    style="left:{kf.x}px"
                                                    onclick={(e) => { e.stopPropagation(); editor.selectKeyframe(kf.id); }}
                                                    aria-label="Keyframe at {formatMs(kf.timeMs)}"
                                                ></button>
                                            {/each}
                                        </div>

                                    {/if}
                                {/each}
                            {/if}

                        </div>

                        <!-- ── Playhead (absolute over all lanes) ── -->
                        <Playhead
                            currentTimeMs={editor.currentTimeMs}
                            durationMs={editor.durationMs}
                            {scrollMs} {pxPerMs} {visibleMs}
                            lanesEl={lanesScrollEl}
                            keyframes={editor.clips.flatMap(c => c.keyframes)}
                            {onSeek}
                        />

                    </div>
                </div>
            </SplitPanel.Pane>

        </SplitPanel.Split>
    </SplitPanel.Root>

</div>

<style>
    /* ── Outer container ── */
    .track-area {
        flex: 1;
        height: 0;
        display: flex;
        overflow: hidden;
        position: relative;
    }

    /* ── SplitPanel horizontal root fills container ── */
    :global(.track-h-root),
    :global(.track-h-split) {
        width: 100%;
        height: 100%;
        display: flex;
        flex-direction: row;
        overflow: hidden;
    }

    /* tl-lanes pane fills remaining width */
    :global([data-pane-id="tl-lanes"]) {
        flex: 1;
        min-width: 0;
        overflow: hidden;
        display: flex;
        flex-direction: column;
    }

    /* ── Sidebar ── */
    .sidebar {
        flex-shrink: 0;
        display: flex;
        flex-direction: column;
        background: rgb(var(--color-bg-tertiary));
        border-right: 1px solid rgb(var(--color-border-default) / 0.4);
        z-index: 10;
        overflow: hidden;
        height: 100%;
    }
    .sidebar-row {
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: 0 4px 0 6px;
        border-bottom: 1px solid rgb(var(--color-border-default) / 0.2);
        gap: 2px;
        flex-shrink: 0;
        overflow: hidden;
        box-sizing: border-box;
    }
    .sidebar-clip-row {
        background: rgb(var(--color-bg-secondary) / 0.6);
        border-bottom-color: rgb(var(--color-border-default) / 0.4);
    }
    .row-label {
        font-size: 0.58rem;
        color: rgb(var(--color-text-muted));
        opacity: 0.7;
        font-weight: 500;
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
        flex: 1;
        min-width: 0;
    }
    .clip-label { opacity: 1; color: rgb(var(--color-text-secondary)); font-size: 0.57rem; }
    .row-hint {
        font-family: inherit;
        font-size: 0.52rem;
        padding: 0 2px;
        border-radius: 2px;
        background: rgb(var(--color-bg-secondary));
        border: 1px solid rgb(var(--color-border-default) / 0.4);
        color: rgb(var(--color-text-muted));
        opacity: 0.6;
        flex-shrink: 0;
    }
    .expand-btn {
        flex-shrink: 0;
        background: none;
        border: none;
        padding: 1px;
        color: rgb(var(--color-text-muted));
        opacity: 0.55;
        cursor: pointer;
        display: flex;
        align-items: center;
        line-height: 1;
        border-radius: 2px;
        transition: opacity 0.1s;
    }
    .expand-btn:hover { opacity: 1; }

    /* ── Lanes scroll container ── */
    .lanes-scroll {
        flex: 1;
        min-width: 0;
        overflow-x: auto;
        overflow-y: hidden;
        cursor: default;
        position: relative;
    }
    .lanes-inner {
        position: relative;
    }
    .lanes-rows {
        display: flex;
        flex-direction: column;
        position: relative;
        z-index: 1;
    }

    /* ── Resize handle — vertical (sidebar width) ── */
    .resize-handle-v {
        width: 4px;
        height: 100%;
        cursor: col-resize;
        background: rgb(var(--color-border-default));
        transition: background 0.1s;
        flex-shrink: 0;
    }
    .resize-handle-v:hover,
    .resize-handle-v.dragging { background: rgb(var(--color-accent-primary)); }

    /* ── Resize handle — horizontal (row height) ── */
    .resize-handle-h {
        width: 100%;
        cursor: row-resize;
        background: rgb(var(--color-border-default) / 0.3);
        transition: background 0.1s;
        flex-shrink: 0;
        z-index: 5;
        box-sizing: border-box;
    }
    .resize-handle-h:hover,
    .resize-handle-h.dragging { background: rgb(var(--color-accent-primary)); }

    /* ── Sidebar handle spacer (non-interactive, just visual alignment) ── */
    .row-handle-h {
        flex-shrink: 0;
        background: rgb(var(--color-border-default) / 0.15);
        box-sizing: border-box;
    }

    /* ── Grid overlay ── */
    .grid-overlay {
        position: absolute;
        top: 0;
        left: 0;
        height: 100%;
        background-image: repeating-linear-gradient(
            to right,
            rgb(var(--color-text-muted) / 0.04) 0px,
            rgb(var(--color-text-muted) / 0.04) 1px,
            transparent 1px,
            transparent 100%
        );
        pointer-events: none;
        z-index: 0;
    }

    /* ── Lane ── */
    .lane {
        position: relative;
        border-bottom: 1px solid rgb(var(--color-border-default) / 0.2);
        overflow: visible;
        box-sizing: border-box;
        flex-shrink: 0;
    }
    .clip-lane { background: rgb(var(--color-bg-secondary) / 0.3); }

    /* ── Clip bar ── */
    .clip-bar {
        position: absolute;
        top: 5px;
        height: calc(100% - 10px);
        border-radius: 4px;
        background: linear-gradient(
            180deg,
            rgb(var(--color-accent-primary) / 0.28) 0%,
            rgb(var(--color-accent-primary) / 0.14) 100%
        );
        border: 1px solid rgb(var(--color-accent-primary) / 0.38);
        cursor: pointer;
        transition: border-color 0.1s, background 0.1s;
        display: flex;
        align-items: center;
        overflow: hidden;
    }
    .clip-bar:hover {
        background: linear-gradient(
            180deg,
            rgb(var(--color-accent-primary) / 0.38) 0%,
            rgb(var(--color-accent-primary) / 0.22) 100%
        );
    }
    .clip-bar.clip-selected {
        border-color: rgb(var(--color-accent-primary) / 0.7);
        box-shadow: 0 0 0 1px rgb(var(--color-accent-primary) / 0.25);
    }
    .clip-name {
        padding: 0 8px;
        font-size: 0.58rem;
        color: rgb(var(--color-text-secondary));
        opacity: 0.8;
        white-space: nowrap;
        pointer-events: none;
        font-variant-numeric: tabular-nums;
        overflow: hidden;
        text-overflow: ellipsis;
    }

    /* ── Keyframe row ── */
    .kf-lane { background: rgb(var(--color-bg-tertiary)); }
    .kf-marker {
        position: absolute;
        top: 50%;
        transform: translate(-50%, -50%) rotate(45deg);
        width: 7px;
        height: 7px;
        border-radius: 1px;
        background: rgb(var(--color-accent-primary) / 0.6);
        border: 1px solid rgb(var(--color-accent-primary));
        cursor: pointer;
        padding: 0;
        transition: background 0.1s, transform 0.1s;
    }
    .kf-marker:hover {
        background: rgb(var(--color-accent-primary));
        transform: translate(-50%, -50%) rotate(45deg) scale(1.3);
    }
    .kf-selected {
        background: rgb(var(--color-accent-primary));
        box-shadow: 0 0 6px rgb(var(--color-accent-primary) / 0.6);
    }
</style>
