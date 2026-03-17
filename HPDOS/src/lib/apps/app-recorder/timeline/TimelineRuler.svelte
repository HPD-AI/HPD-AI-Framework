<script lang="ts">
    /**
     * TimelineRuler — Step 9.
     *
     * Draws the time axis with adaptive major/minor ticks.
     * Uses the SCALE_CANDIDATES algorithm from OpenScreen:
     * pick the finest interval that shows ≤ 12 markers in the visible range.
     *
     * Receives layout params from Timeline.svelte (scrollMs, pxPerMs, visibleMs).
     * Does NOT own any state — purely derived from props.
     */

    import { pickAxisScale, buildAxisMarkers, msToPixel, pixelToMs, clamp } from './timelineUtils';

    let {
        scrollMs,
        visibleMs,
        pxPerMs,
        durationMs,
        currentTimeMs,
        onSeek,
    }: {
        scrollMs:      number;
        visibleMs:     number;
        pxPerMs:       number;
        durationMs:    number;
        currentTimeMs: number;
        onSeek:        (ms: number) => void;
    } = $props();

    const SIDEBAR_WIDTH = 56; // px — row label column width (matches TimelineTrack)

    const scale = $derived(pickAxisScale(visibleMs));

    const markers = $derived(
        buildAxisMarkers(scrollMs, visibleMs, durationMs, scale.intervalMs)
    );

    let rulerEl = $state<HTMLDivElement | null>(null);
    let isScrubbing = false;

    function seekFromPointer(e: PointerEvent) {
        if (!rulerEl) return;
        const rect = rulerEl.getBoundingClientRect();
        const offsetX = e.clientX - rect.left - SIDEBAR_WIDTH;
        if (offsetX < 0) return;
        const ms = clamp(pixelToMs(offsetX, scrollMs, pxPerMs), 0, durationMs);
        onSeek(ms);
    }

    function onPointerMove(e: PointerEvent) {
        if (!isScrubbing) return;
        seekFromPointer(e);
    }

    function onPointerUp() {
        isScrubbing = false;
        window.removeEventListener('pointermove', onPointerMove);
        window.removeEventListener('pointerup', onPointerUp);
    }

    function onPointerDown(e: PointerEvent) {
        isScrubbing = true;
        seekFromPointer(e);
        window.addEventListener('pointermove', onPointerMove);
        window.addEventListener('pointerup', onPointerUp);
    }

    $effect(() => {
        return () => {
            // Clean up listeners if component is destroyed mid-drag
            window.removeEventListener('pointermove', onPointerMove);
            window.removeEventListener('pointerup', onPointerUp);
        };
    });
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div
    class="ruler"
    style="padding-left: {SIDEBAR_WIDTH}px"
    bind:this={rulerEl}
    onpointerdown={onPointerDown}
>
    <!-- Minor ticks -->
    {#each markers.minor as tick (tick.timeMs)}
        {@const x = msToPixel(tick.timeMs, scrollMs, pxPerMs)}
        <div class="minor-tick" style="left:{x}px"></div>
    {/each}

    <!-- Major markers -->
    {#each markers.major as marker (marker.timeMs)}
        {@const x = msToPixel(marker.timeMs, scrollMs, pxPerMs)}
        <div class="major-marker" style="left:{x}px">
            <div class="tick-line"></div>
            <span
                class="tick-label"
                class:active={Math.abs(marker.timeMs - currentTimeMs) < scale.intervalMs * 0.5}
            >{marker.label}</span>
        </div>
    {/each}
</div>

<style>
    .ruler {
        position: relative;
        height: 28px;
        flex-shrink: 0;
        background: rgb(var(--color-bg-tertiary));
        border-bottom: 1px solid rgb(var(--color-border-default) / 0.4);
        overflow: hidden;
        user-select: none;
        cursor: pointer;
    }

    .minor-tick {
        position: absolute;
        bottom: 0;
        width: 1px;
        height: 4px;
        background: rgb(var(--color-text-muted) / 0.2);
    }

    .major-marker {
        position: absolute;
        bottom: 0;
        height: 100%;
        display: flex;
        flex-direction: column;
        align-items: flex-start;
        justify-content: flex-end;
        padding-bottom: 3px;
    }

    .tick-line {
        position: absolute;
        bottom: 0;
        left: 0;
        width: 1px;
        height: 8px;
        background: rgb(var(--color-text-muted) / 0.35);
    }

    .tick-label {
        font-size: 0.6rem;
        font-weight: 500;
        color: rgb(var(--color-text-muted));
        white-space: nowrap;
        padding-left: 3px;
        font-variant-numeric: tabular-nums;
        letter-spacing: -0.02em;
        transition: color 0.1s;
    }
    .tick-label.active {
        color: rgb(52 178 123);
    }
</style>
