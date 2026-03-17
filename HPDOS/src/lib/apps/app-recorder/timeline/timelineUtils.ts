/**
 * Timeline utility functions — ported and adapted from OpenScreen's TimelineEditor.tsx.
 *
 * All time values are in milliseconds unless noted.
 * "pixels per millisecond" = zoom level (pxPerMs).
 */

// ── Scale candidates (OpenScreen SCALE_CANDIDATES, verbatim) ──────────────────

interface ScaleCandidate {
    intervalMs: number;
    gridMs: number;
}

const TARGET_MARKER_COUNT = 12;

export const SCALE_CANDIDATES: ScaleCandidate[] = [
    { intervalMs:    50, gridMs:    10 },
    { intervalMs:   100, gridMs:    20 },
    { intervalMs:   250, gridMs:    50 },
    { intervalMs:   500, gridMs:   100 },
    { intervalMs:  1000, gridMs:   250 },
    { intervalMs:  2000, gridMs:   500 },
    { intervalMs:  5000, gridMs:  1000 },
    { intervalMs: 10000, gridMs:  2000 },
    { intervalMs: 15000, gridMs:  3000 },
    { intervalMs: 30000, gridMs:  5000 },
    { intervalMs: 60000, gridMs: 10000 },
    { intervalMs: 120000, gridMs: 20000 },
    { intervalMs: 300000, gridMs: 30000 },
    { intervalMs: 600000, gridMs: 60000 },
];

/**
 * Pick the finest scale that shows ≤ TARGET_MARKER_COUNT markers
 * in the currently visible range. Re-run on every zoom change.
 */
export function pickAxisScale(visibleMs: number): ScaleCandidate {
    const found = SCALE_CANDIDATES.find(c => visibleMs / c.intervalMs <= TARGET_MARKER_COUNT);
    return found ?? SCALE_CANDIDATES[SCALE_CANDIDATES.length - 1]!;
}

// ── Axis marker generation ────────────────────────────────────────────────────

export interface AxisMarker {
    timeMs: number;
    label: string;
    isMajor: true;
}

export interface MinorTick {
    timeMs: number;
    isMajor: false;
}

export interface AxisMarkers {
    major: AxisMarker[];
    minor: MinorTick[];
}

export function buildAxisMarkers(
    scrollMs: number,
    visibleMs: number,
    durationMs: number,
    intervalMs: number,
): AxisMarkers {
    const maxTime = durationMs > 0 ? durationMs : scrollMs + visibleMs;
    const visStart = Math.max(0, scrollMs);
    const visEnd   = Math.min(scrollMs + visibleMs, maxTime);

    const majorSet = new Set<number>();
    const first = Math.ceil(visStart / intervalMs) * intervalMs;
    for (let t = first; t <= maxTime; t += intervalMs) {
        if (t >= visStart && t <= visEnd) majorSet.add(Math.round(t));
    }
    // Include 0 only when it's in view (not every arbitrary scroll position)
    if (visStart === 0) majorSet.add(0);
    // Include the end marker when it's visible
    if (durationMs > 0 && durationMs <= visEnd) majorSet.add(Math.round(durationMs));

    const major: AxisMarker[] = Array.from(majorSet)
        .filter(t => t <= maxTime)
        .sort((a, b) => a - b)
        .map(t => ({ timeMs: t, label: formatTimeLabel(t, intervalMs), isMajor: true }));

    // Minor ticks: 5 subdivisions per interval
    const minorInterval = intervalMs / 5;
    const minor: MinorTick[] = [];
    for (let t = first; t <= maxTime; t += minorInterval) {
        if (t >= visStart && t <= visEnd) {
            if (Math.abs(t % intervalMs) > 1) {
                minor.push({ timeMs: t, isMajor: false });
            }
        }
    }

    return { major, minor };
}

// ── Time label formatting ─────────────────────────────────────────────────────

export function formatTimeLabel(ms: number, intervalMs: number): string {
    const totalSec = ms / 1000;
    const hours   = Math.floor(totalSec / 3600);
    const minutes = Math.floor((totalSec % 3600) / 60);
    const seconds = totalSec % 60;

    const frac = intervalMs < 250 ? 2 : intervalMs < 1000 ? 1 : 0;

    if (hours > 0) {
        if (frac > 0) {
            const secStr = seconds.toFixed(frac);
            const [whole, fraction] = secStr.split('.');
            return `${hours}:${pad2(minutes)}:${pad2(Number(whole))}.${fraction}`;
        }
        return `${hours}:${pad2(minutes)}:${pad2(Math.floor(seconds))}`;
    }
    if (frac > 0) {
        const secStr = seconds.toFixed(frac);
        const [whole, fraction] = secStr.split('.');
        return `${minutes}:${pad2(Number(whole))}.${fraction}`;
    }
    return `${minutes}:${pad2(Math.floor(seconds))}`;
}

/** Format ms for playhead tooltip / chip label */
export function formatMs(ms: number): string {
    const s = ms / 1000;
    const min = Math.floor(s / 60);
    const sec = s % 60;
    if (min > 0) return `${min}:${sec.toFixed(1).padStart(4, '0')}`;
    return `${sec.toFixed(1)}s`;
}

function pad2(n: number): string {
    return n.toString().padStart(2, '0');
}

// ── Coordinate helpers ────────────────────────────────────────────────────────

/** Convert a timeline time (ms) to pixel offset from the left edge of the track area. */
export function msToPixel(timeMs: number, scrollMs: number, pxPerMs: number): number {
    return (timeMs - scrollMs) * pxPerMs;
}

/** Convert a pixel offset (from left edge of track area) to a timeline time (ms). */
export function pixelToMs(px: number, scrollMs: number, pxPerMs: number): number {
    return px / pxPerMs + scrollMs;
}

/** Clamp a value between min and max. */
export function clamp(value: number, min: number, max: number): number {
    return Math.min(max, Math.max(min, value));
}

// ── Default zoom / scroll ─────────────────────────────────────────────────────

/**
 * Given video duration, return a pxPerMs that fits the whole timeline
 * into a given container width with some padding.
 */
export function fitZoom(durationMs: number, containerWidthPx: number): number {
    if (durationMs <= 0 || containerWidthPx <= 0) return 0.08;
    // Leave 32px padding on right
    return (containerWidthPx - 32) / durationMs;
}

// ── Overlap detection ─────────────────────────────────────────────────────────

export interface Span { startMs: number; endMs: number; id: string; }

/**
 * Check if a candidate span overlaps any of the existing spans (excluding one by id).
 * Annotations are allowed to overlap — pass skipOverlapCheck=true for them.
 */
export function hasOverlap(
    candidate: { startMs: number; endMs: number },
    spans: Span[],
    excludeId?: string,
): boolean {
    return spans.some(s => {
        if (s.id === excludeId) return false;
        return candidate.endMs > s.startMs && candidate.startMs < s.endMs;
    });
}

/**
 * Clamp a dragged span so it doesn't overlap its neighbours.
 * Instead of rejecting, slide the edge to the nearest boundary (OpenScreen pattern).
 */
export function clampToNeighbours(
    span: { startMs: number; endMs: number },
    siblings: Span[],
    minDurationMs: number,
    totalMs: number,
): { startMs: number; endMs: number } {
    let { startMs, endMs } = span;

    for (const r of siblings) {
        // Right edge crossed into region to the right
        if (endMs > r.startMs && startMs < r.startMs) endMs = r.startMs;
        // Left edge crossed into region to the left
        if (startMs < r.endMs && endMs > r.endMs) startMs = r.endMs;
    }

    // Enforce minimum duration (guard against minDurationMs > totalMs)
    const effectiveMin = Math.min(minDurationMs, totalMs);
    if (endMs - startMs < effectiveMin) {
        endMs = startMs + effectiveMin;
        if (endMs > totalMs) {
            endMs = totalMs;
            startMs = Math.max(0, endMs - effectiveMin);
        }
    }

    return {
        startMs: clamp(startMs, 0, totalMs),
        endMs:   clamp(endMs,   0, totalMs),
    };
}

// ── Default region duration ────────────────────────────────────────────────────

/** 5% of total duration, clamped to [1000ms, 30000ms]. */
export function defaultRegionDuration(totalMs: number): number {
    return Math.max(1000, Math.min(Math.round(totalMs * 0.05), 30000));
}

/** Find a gap at or after `playheadMs` in a sorted list of spans. Returns {startMs, endMs} or null. */
export function findGapAtPlayhead(
    playheadMs: number,
    spans: Span[],
    totalMs: number,
    wantedDuration: number,
): { startMs: number; endMs: number } | null {
    const sorted = [...spans].sort((a, b) => a.startMs - b.startMs);

    // Is playhead inside an existing span?
    const inside = sorted.find(r => playheadMs >= r.startMs && playheadMs < r.endMs);
    if (inside) return null;

    const startPos = clamp(playheadMs, 0, totalMs);
    const nextRegion = sorted.find(r => r.startMs > startPos);
    const gapToNext = nextRegion ? nextRegion.startMs - startPos : totalMs - startPos;

    if (gapToNext <= 0) return null;

    const duration = Math.min(wantedDuration, gapToNext);
    return { startMs: startPos, endMs: startPos + duration };
}

// ── Zoom smoothstep / region strength (for VideoCanvas use) ──────────────────

export function smoothStep(t: number): number {
    const c = clamp(t, 0, 1);
    return c * c * (3 - 2 * c);
}

export const TRANSITION_WINDOW_MS = 400;

export function computeRegionStrength(
    startMs: number, endMs: number, timeMs: number,
): number {
    const leadInStart  = startMs - TRANSITION_WINDOW_MS;
    const leadOutEnd   = endMs   + TRANSITION_WINDOW_MS;
    if (timeMs < leadInStart || timeMs > leadOutEnd) return 0;
    const fadeIn  = smoothStep((timeMs - leadInStart) / TRANSITION_WINDOW_MS);
    const fadeOut = smoothStep((leadOutEnd - timeMs)  / TRANSITION_WINDOW_MS);
    return Math.min(fadeIn, fadeOut);
}
