<script lang="ts">
    /**
     * Playhead — Step 12.
     *
     * Red vertical line spanning all track rows.
     * Draggable handle (diamond) at top.
     * Snaps to keyframes within 150ms (OpenScreen pattern).
     * Hidden when currentTimeMs is outside the visible range.
     *
     * Tooltip shown during drag (MM:SS.f format).
     */

    import { msToPixel, pixelToMs, clamp, formatMs } from './timelineUtils';
    import type { Keyframe } from '../AppRecorderState.svelte';

    // Snap threshold scales with zoom: ~8px worth of time, clamped to [20ms, 500ms].
    const snapThresholdMs = $derived(clamp(8 / pxPerMs, 20, 500));

    let {
        currentTimeMs,
        durationMs,
        scrollMs,
        pxPerMs,
        visibleMs,
        lanesEl,
        keyframes,
        onSeek,
    }: {
        currentTimeMs: number;
        durationMs:    number;
        scrollMs:      number;
        pxPerMs:       number;
        visibleMs:     number;
        lanesEl:       HTMLDivElement | null;
        keyframes:     Keyframe[];
        onSeek:        (ms: number) => void;
    } = $props();

    let isDragging = $state(false);

    const offsetX  = $derived(msToPixel(currentTimeMs, scrollMs, pxPerMs));
    const isVisible = $derived(
        durationMs > 0 &&
        currentTimeMs >= scrollMs &&
        currentTimeMs <= scrollMs + visibleMs
    );

    function onPointerDown(e: PointerEvent) {
        e.stopPropagation();
        e.preventDefault();
        isDragging = true;

        const onMove = (ev: PointerEvent) => {
            if (!lanesEl) return;
            const rect     = lanesEl.getBoundingClientRect();
            const offsetPx = ev.clientX - rect.left;
            let timeMs     = clamp(pixelToMs(offsetPx, scrollMs, pxPerMs), 0, durationMs);

            // Snap to nearby keyframe (threshold scales with zoom)
            const snap = keyframes.find(kf => Math.abs(kf.timeMs - timeMs) <= snapThresholdMs);
            if (snap) timeMs = snap.timeMs;

            onSeek(timeMs);
        };

        const onUp = () => {
            isDragging = false;
            window.removeEventListener('pointermove', onMove);
            window.removeEventListener('pointerup', onUp);
        };

        window.addEventListener('pointermove', onMove);
        window.addEventListener('pointerup', onUp);
    }
</script>

{#if isVisible}
    <div
        class="playhead"
        class:dragging={isDragging}
        style="left:{offsetX}px"
    >
        <!-- Draggable diamond handle -->
        <div
            class="handle"
            onpointerdown={onPointerDown}
            role="slider"
            tabindex="-1"
            aria-label="Playhead at {formatMs(currentTimeMs)}"
            aria-valuenow={currentTimeMs}
            aria-valuemin={0}
            aria-valuemax={durationMs}
        >
            <div class="diamond"></div>
        </div>

        <!-- Vertical line -->
        <div class="line"></div>

        <!-- Time tooltip shown while dragging -->
        {#if isDragging}
            <div class="time-label">{formatMs(currentTimeMs)}</div>
        {/if}
    </div>
{/if}

<style>
    .playhead {
        position: absolute;
        top: 0;
        bottom: 0;
        width: 0;
        z-index: 40;
        pointer-events: none;
    }

    /* ── Vertical line ── */
    .line {
        position: absolute;
        top: 0;
        left: 0;
        width: 2px;
        height: 100%;
        background: rgb(52 178 123);
        box-shadow: 0 0 8px rgb(52 178 123 / 0.5);
        transform: translateX(-50%);
        pointer-events: none;
        transition: box-shadow 0.1s;
    }
    .dragging .line {
        box-shadow: 0 0 14px rgb(52 178 123 / 0.7);
    }

    /* ── Diamond handle ── */
    .handle {
        position: absolute;
        top: -2px;
        left: 50%;
        transform: translateX(-50%);
        width: 20px;
        height: 20px;
        display: flex;
        align-items: center;
        justify-content: center;
        cursor: ew-resize;
        pointer-events: auto;
        z-index: 41;
    }
    .handle:hover .diamond {
        transform: rotate(45deg) scale(1.2);
        background: rgb(52 178 123);
    }

    .diamond {
        width: 10px;
        height: 10px;
        border-radius: 2px;
        background: rgb(52 178 123);
        border: 1.5px solid rgb(255 255 255 / 0.3);
        transform: rotate(45deg);
        box-shadow: 0 1px 4px rgb(0 0 0 / 0.4);
        transition: transform 0.1s, background 0.1s;
    }

    /* ── Drag time label ── */
    .time-label {
        position: absolute;
        top: -22px;
        left: 50%;
        transform: translateX(-50%);
        padding: 1px 5px;
        border-radius: 3px;
        background: rgb(0 0 0 / 0.85);
        color: rgb(255 255 255 / 0.9);
        font-size: 0.6rem;
        font-weight: 600;
        white-space: nowrap;
        border: 1px solid rgb(255 255 255 / 0.1);
        pointer-events: none;
        font-variant-numeric: tabular-nums;
    }
</style>
