<script lang="ts">
    /**
     * RegionChip — Step 11.
     *
     * Glass-morphism region chip for Zoom / Trim / Speed / Annotation rows.
     *
     * Drag interactions (pure pointer events, no library):
     *   - Drag BODY  → move region (preserve duration, clamp to [0, totalMs])
     *   - Drag LEFT HANDLE  → resize start edge (clamp to neighbour right boundary)
     *   - Drag RIGHT HANDLE → resize end edge   (clamp to neighbour left boundary)
     *
     * Overlap prevention (OpenScreen clampToNeighbours pattern):
     *   - Zoom/Trim/Speed: clamp to neighbours, min 100ms
     *   - Annotation: siblings=[] → no overlap check
     *
     * Tooltip: updated via showTooltip prop (DOM-direct in parent — no re-renders).
     */

    import { msToPixel, pixelToMs, clamp, clampToNeighbours, formatMs } from './timelineUtils';
    import type { Span } from './timelineUtils';

    type Variant = 'zoom' | 'trim' | 'speed' | 'annotation';

    let {
        id,
        startMs,
        endMs,
        clipOffsetMs = 0,
        scrollMs,
        pxPerMs,
        totalMs,
        minDurationMs = 100,
        siblings,
        variant,
        label,
        isSelected,
        onSelect,
        onSpanChange,
        showTooltip,
        hideTooltip,
    }: {
        id:             string;
        startMs:        number;
        endMs:          number;
        /** Global timeline offset of the clip that owns this region (ms).
         *  Added to startMs/endMs before pixel conversion so chips render
         *  at the correct absolute position on the global timeline. */
        clipOffsetMs?:  number;
        scrollMs:       number;
        pxPerMs:        number;
        totalMs:        number;
        minDurationMs?: number;
        siblings:       Span[];
        variant:        Variant;
        label:          string;
        isSelected:     boolean;
        onSelect:       () => void;
        onSpanChange:   (startMs: number, endMs: number) => void;
        showTooltip:    (text: string, screenX: number) => void;
        hideTooltip:    () => void;
    } = $props();

    // ── Derived pixel geometry ────────────────────────────────────────────────
    // clipOffsetMs converts clip-local ms → global timeline ms before pixel conversion.
    const left  = $derived(msToPixel(clipOffsetMs + startMs, scrollMs, pxPerMs));
    const width = $derived(Math.max(6, (endMs - startMs) * pxPerMs));

    // ── Drag state (not reactive — mutated directly during pointermove) ───────
    let dragType: 'body' | 'left' | 'right' | null = null;
    let dragStartX      = 0;
    let dragStartMs     = 0;
    let dragEndMs       = 0;
    let capturedPxPerMs = 0; // captured at drag-start; immune to zoom changes mid-drag
    let isDragging      = false; // used to suppress click-after-drag

    // Siblings excluding self (for overlap clamping)
    const sibsExcludingSelf = $derived(siblings.filter(s => s.id !== id));

    function startDrag(type: 'body' | 'left' | 'right', e: PointerEvent) {
        e.stopPropagation();
        e.preventDefault();
        dragType        = type;
        dragStartX      = e.clientX;
        dragStartMs     = startMs;
        dragEndMs       = endMs;
        capturedPxPerMs = pxPerMs; // capture — don't read pxPerMs inside onMove
        isDragging      = false;

        const originalDuration = endMs - startMs; // preserve for body drag clamping

        const onMove = (ev: PointerEvent) => {
            isDragging = true;
            const deltaPx = ev.clientX - dragStartX;
            const deltaMs = deltaPx / capturedPxPerMs;

            let newStart = dragStartMs;
            let newEnd   = dragEndMs;

            if (type === 'body') {
                const dur = originalDuration;
                newStart = clamp(dragStartMs + deltaMs, 0, totalMs - dur);
                newEnd   = newStart + dur;
            } else if (type === 'left') {
                newStart = clamp(dragStartMs + deltaMs, 0, dragEndMs - minDurationMs);
            } else {
                newEnd = clamp(dragEndMs + deltaMs, dragStartMs + minDurationMs, totalMs);
            }

            // clampToNeighbours (only when siblings provided)
            if (sibsExcludingSelf.length > 0) {
                const before = { startMs: newStart, endMs: newEnd };
                const clamped = clampToNeighbours(before, sibsExcludingSelf, minDurationMs, totalMs);

                // For body drags: if clamping changed the duration, discard the move
                // (chip stops at boundary instead of shrinking)
                if (type === 'body' && (clamped.endMs - clamped.startMs) < originalDuration - 1) {
                    return; // don't update — keep last valid position
                }

                newStart = clamped.startMs;
                newEnd   = clamped.endMs;
            }

            onSpanChange(newStart, newEnd);

            // DOM-direct tooltip
            showTooltip(`${formatMs(newStart)} – ${formatMs(newEnd)}`, ev.clientX);
        };

        const onUp = () => {
            dragType = null;
            hideTooltip();
            window.removeEventListener('pointermove', onMove);
            window.removeEventListener('pointerup', onUp);
            // Delay reset so the click event fires first and gets suppressed
            setTimeout(() => { isDragging = false; }, 50);
        };

        window.addEventListener('pointermove', onMove);
        window.addEventListener('pointerup', onUp);
    }

    function onClick(e: MouseEvent) {
        e.stopPropagation();
        if (isDragging) return; // suppress click-after-drag
        onSelect();
    }
</script>

<!-- svelte-ignore a11y_click_events_have_key_events a11y_no_static_element_interactions -->
<div
    class="chip chip-{variant}"
    class:selected={isSelected}
    style="left:{left}px; width:{width}px"
    onclick={onClick}
    role="button"
    tabindex="-1"
    aria-label="{variant} region {label}"
>
    <!-- Left resize handle -->
    <!-- svelte-ignore a11y_no_static_element_interactions -->
    <div
        class="handle handle-left"
        onpointerdown={(e) => startDrag('left', e)}
    ></div>

    <!-- Body drag + label -->
    <!-- svelte-ignore a11y_no_static_element_interactions -->
    <div
        class="chip-body"
        onpointerdown={(e) => startDrag('body', e)}
    >
        {#if width > 28}
            <span class="chip-label">{label}</span>
        {/if}
    </div>

    <!-- Right resize handle -->
    <!-- svelte-ignore a11y_no_static_element_interactions -->
    <div
        class="handle handle-right"
        onpointerdown={(e) => startDrag('right', e)}
    ></div>
</div>

<style>
    /* ── Base chip ── */
    .chip {
        position: absolute;
        top: 4px;
        height: calc(100% - 8px);
        border-radius: 4px;
        display: flex;
        align-items: stretch;
        overflow: hidden;
        cursor: default;
        transition: box-shadow 0.1s;
        min-width: 6px;
    }
    .chip:hover      { box-shadow: 0 0 0 1px rgb(255 255 255 / 0.2); }
    .chip.selected   { box-shadow: 0 0 0 1.5px rgb(255 255 255 / 0.5); }

    /* ── Variant colours (glass morphism) ── */
    .chip-zoom {
        background: linear-gradient(180deg, rgb(52 178 123 / 0.55) 0%, rgb(33 145 106 / 0.4) 100%);
        border: 1px solid rgb(52 178 123 / 0.6);
    }
    .chip-zoom.selected { border-color: rgb(52 178 123); }

    .chip-trim {
        background: linear-gradient(180deg, rgb(239 68 68 / 0.55) 0%, rgb(185 28 28 / 0.4) 100%);
        border: 1px solid rgb(239 68 68 / 0.6);
    }
    .chip-trim.selected { border-color: rgb(239 68 68); }

    .chip-speed {
        background: linear-gradient(180deg, rgb(217 119 6 / 0.55) 0%, rgb(180 83 9 / 0.4) 100%);
        border: 1px solid rgb(217 119 6 / 0.6);
    }
    .chip-speed.selected { border-color: rgb(217 119 6); }

    .chip-annotation {
        background: linear-gradient(180deg, rgb(180 160 70 / 0.55) 0%, rgb(133 115 40 / 0.4) 100%);
        border: 1px solid rgb(180 160 70 / 0.6);
    }
    .chip-annotation.selected { border-color: rgb(180 160 70); }

    /* ── Handles ── */
    .handle {
        width: 6px;
        flex-shrink: 0;
        cursor: col-resize;
        opacity: 0.7;
        transition: opacity 0.1s;
        display: flex;
        align-items: center;
        justify-content: center;
    }
    .handle:hover { opacity: 1; }
    .handle::after {
        content: '';
        display: block;
        width: 2px;
        height: 60%;
        border-radius: 1px;
        background: rgb(255 255 255 / 0.5);
    }
    .handle-left  { border-radius: 4px 0 0 4px; }
    .handle-right { border-radius: 0 4px 4px 0; }

    /* Handle accent colours */
    .chip-zoom .handle       { background: rgb(33 145 106 / 0.6); }
    .chip-trim .handle       { background: rgb(185 28 28 / 0.6); }
    .chip-speed .handle      { background: rgb(180 83 9 / 0.6); }
    .chip-annotation .handle { background: rgb(133 115 40 / 0.6); }

    /* ── Body ── */
    .chip-body {
        flex: 1;
        min-width: 0;
        display: flex;
        align-items: center;
        justify-content: center;
        cursor: grab;
        overflow: hidden;
    }
    .chip-body:active { cursor: grabbing; }

    .chip-label {
        font-size: 0.62rem;
        font-weight: 600;
        color: rgb(255 255 255 / 0.92);
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
        pointer-events: none;
        padding: 0 2px;
    }
</style>
