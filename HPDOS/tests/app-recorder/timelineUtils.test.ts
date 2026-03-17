/**
 * timelineUtils — pure function unit tests (no DOM, no Svelte).
 * Tests #1–61 from the HPD Video Editor test catalogue.
 */
import { describe, it, expect } from 'vitest';
import {
    pickAxisScale,
    buildAxisMarkers,
    formatTimeLabel,
    formatMs,
    msToPixel,
    pixelToMs,
    fitZoom,
    hasOverlap,
    clampToNeighbours,
    defaultRegionDuration,
    findGapAtPlayhead,
    smoothStep,
    computeRegionStrength,
    TRANSITION_WINDOW_MS,
} from '../../src/lib/apps/app-recorder/timeline/timelineUtils';

// ── pickAxisScale ─────────────────────────────────────────────────────────────

describe('pickAxisScale', () => {

    // #1
    it('returns 50ms interval for very zoomed-in view (300ms visible)', () => {
        const s = pickAxisScale(300);
        expect(s.intervalMs).toBe(50);
        expect(s.gridMs).toBe(10);
    });

    // #2
    it('returns 5000ms interval for 30s view', () => {
        const s = pickAxisScale(30_000);
        expect(s.intervalMs).toBe(5000);
        expect(s.gridMs).toBe(1000);
    });

    // #3
    it('returns last candidate for 2h view', () => {
        const s = pickAxisScale(7_200_000);
        expect(s.intervalMs).toBe(600_000);
        expect(s.gridMs).toBe(60_000);
    });

    // #4
    it('exactly 12 markers (boundary) uses finest scale', () => {
        // 600ms / 50ms = 12 markers exactly — ≤ 12, so pick 50ms
        const s = pickAxisScale(600);
        expect(s.intervalMs).toBe(50);
    });

    // #5
    it('13 markers triggers next scale', () => {
        // 650ms / 50ms = 13 > 12 → bump to 100ms
        const s = pickAxisScale(650);
        expect(s.intervalMs).toBe(100);
    });

    // #6
    it('zero visibleMs returns first candidate', () => {
        // 0 / 50 = 0 ≤ 12 → first candidate
        const s = pickAxisScale(0);
        expect(s.intervalMs).toBe(50);
    });
});

// ── buildAxisMarkers ──────────────────────────────────────────────────────────

describe('buildAxisMarkers', () => {

    // #7
    it('includes 0 at start when scrollMs=0', () => {
        const { major } = buildAxisMarkers(0, 5000, 10_000, 1000);
        expect(major.some(m => m.timeMs === 0)).toBe(true);
    });

    // #8
    it('excludes 0 when scrolled past it', () => {
        const { major } = buildAxisMarkers(2000, 5000, 10_000, 1000);
        expect(major.some(m => m.timeMs === 0)).toBe(false);
    });

    // #9
    it('includes duration end marker when in range', () => {
        const { major } = buildAxisMarkers(8000, 5000, 10_000, 1000);
        expect(major.some(m => m.timeMs === 10_000)).toBe(true);
    });

    // #10
    it('no major markers beyond durationMs', () => {
        const { major } = buildAxisMarkers(0, 20_000, 5000, 1000);
        expect(major.every(m => m.timeMs <= 5000)).toBe(true);
    });

    // #11
    it('minor ticks do not appear when only one second visible', () => {
        // scrollMs=0, visibleMs=1000, durationMs=5000, intervalMs=1000
        // minor interval = 1000/5 = 200ms; first = ceil(0/1000)*1000 = 0
        // ticks at 200,400,600,800 (4 non-major)
        const { minor } = buildAxisMarkers(0, 1000, 5000, 1000);
        expect(minor.length).toBe(4);
    });

    // #12
    it('minor ticks never coincide with major ticks', () => {
        const { major, minor } = buildAxisMarkers(0, 10_000, 30_000, 1000);
        const majorTimes = new Set(major.map(m => m.timeMs));
        const conflict = minor.some(m => majorTimes.has(m.timeMs));
        expect(conflict).toBe(false);
    });
});

// ── formatTimeLabel ───────────────────────────────────────────────────────────

describe('formatTimeLabel', () => {

    // #13
    it('whole seconds for interval ≥ 1000ms', () => {
        expect(formatTimeLabel(65_000, 1000)).toBe('1:05');
    });

    // #14
    it('1 decimal for interval 250–999ms', () => {
        expect(formatTimeLabel(1500, 500)).toBe('0:01.5');
    });

    // #15
    it('2 decimals for interval < 250ms', () => {
        expect(formatTimeLabel(1250, 100)).toBe('0:01.25');
    });

    // #16
    it('hours prefix for long clips', () => {
        expect(formatTimeLabel(3_661_000, 1000)).toBe('1:01:01');
    });

    // #17
    it('zero formats as 0:00', () => {
        expect(formatTimeLabel(0, 1000)).toBe('0:00');
    });

    // #18
    it('minute boundary 60s formats as 1:00', () => {
        expect(formatTimeLabel(60_000, 1000)).toBe('1:00');
    });
});

// ── formatMs ─────────────────────────────────────────────────────────────────

describe('formatMs', () => {

    // #19
    it('under 60s shows seconds with one decimal', () => {
        expect(formatMs(4500)).toBe('4.5s');
    });

    // #20
    it('over 60s shows M:SS.s', () => {
        expect(formatMs(90_000)).toBe('1:30.0');
    });

    // #21
    it('zero formats as 0.0s', () => {
        expect(formatMs(0)).toBe('0.0s');
    });
});

// ── msToPixel / pixelToMs ─────────────────────────────────────────────────────

describe('msToPixel / pixelToMs', () => {

    // #22
    it('msToPixel — no scroll', () => {
        expect(msToPixel(1000, 0, 0.1)).toBe(100);
    });

    // #23
    it('msToPixel — with scroll offsets result', () => {
        expect(msToPixel(2000, 1000, 0.1)).toBe(100);
    });

    // #24
    it('msToPixel — region before scroll is negative', () => {
        expect(msToPixel(500, 1000, 0.1)).toBe(-50);
    });

    // #25
    it('pixelToMs — round trip', () => {
        const px = msToPixel(1500, 200, 0.2);
        expect(pixelToMs(px, 200, 0.2)).toBeCloseTo(1500);
    });

    // #26
    it('pixelToMs — zero pxPerMs yields Infinity or NaN (documents behaviour)', () => {
        const result = pixelToMs(100, 0, 0);
        expect(!isFinite(result) || isNaN(result)).toBe(true);
    });
});

// ── fitZoom ───────────────────────────────────────────────────────────────────

describe('fitZoom', () => {

    // #27
    it('normal — fits duration in container', () => {
        const z = fitZoom(60_000, 632);
        expect(z).toBeCloseTo((632 - 32) / 60_000);
    });

    // #28
    it('zero duration — returns fallback 0.08', () => {
        expect(fitZoom(0, 800)).toBe(0.08);
    });

    // #29
    it('zero container — returns fallback 0.08', () => {
        expect(fitZoom(60_000, 0)).toBe(0.08);
    });
});

// ── hasOverlap ────────────────────────────────────────────────────────────────

describe('hasOverlap', () => {

    // #30
    it('clear gap — returns false', () => {
        expect(hasOverlap({ startMs: 0, endMs: 1000 }, [{ startMs: 2000, endMs: 3000, id: 'a' }])).toBe(false);
    });

    // #31
    it('touching edges — NOT an overlap', () => {
        expect(hasOverlap({ startMs: 0, endMs: 1000 }, [{ startMs: 1000, endMs: 2000, id: 'a' }])).toBe(false);
    });

    // #32
    it('partial overlap — returns true', () => {
        expect(hasOverlap({ startMs: 500, endMs: 1500 }, [{ startMs: 1000, endMs: 2000, id: 'a' }])).toBe(true);
    });

    // #33
    it('full containment — returns true', () => {
        expect(hasOverlap({ startMs: 100, endMs: 500 }, [{ startMs: 0, endMs: 1000, id: 'a' }])).toBe(true);
    });

    // #34
    it('excludeId self — does not self-conflict', () => {
        expect(hasOverlap(
            { startMs: 0, endMs: 1000 },
            [{ startMs: 0, endMs: 1000, id: 'me' }],
            'me',
        )).toBe(false);
    });

    // #35
    it('multiple spans — overlap with one of them', () => {
        expect(hasOverlap(
            { startMs: 900, endMs: 1100 },
            [{ startMs: 0, endMs: 500, id: 'a' }, { startMs: 1000, endMs: 2000, id: 'b' }],
        )).toBe(true);
    });
});

// ── clampToNeighbours ─────────────────────────────────────────────────────────

describe('clampToNeighbours', () => {

    // #36
    it('no siblings — span unchanged', () => {
        const r = clampToNeighbours({ startMs: 1000, endMs: 3000 }, [], 100, 10_000);
        expect(r.startMs).toBe(1000);
        expect(r.endMs).toBe(3000);
    });

    // #37
    it('right sibling pushes end back', () => {
        const r = clampToNeighbours(
            { startMs: 1000, endMs: 3000 },
            [{ startMs: 2000, endMs: 4000, id: 'r' }],
            100, 10_000,
        );
        expect(r.endMs).toBe(2000);
    });

    // #38
    it('left sibling pushes start forward', () => {
        const r = clampToNeighbours(
            { startMs: 500, endMs: 2000 },
            [{ startMs: 0, endMs: 1000, id: 'l' }],
            100, 10_000,
        );
        expect(r.startMs).toBe(1000);
    });

    // #39
    it('minimum duration enforced', () => {
        const r = clampToNeighbours({ startMs: 1000, endMs: 1050 }, [], 500, 10_000);
        expect(r.endMs - r.startMs).toBeGreaterThanOrEqual(500);
    });

    // #40
    it('minimum duration at end of timeline pulls start back', () => {
        const r = clampToNeighbours({ startMs: 9800, endMs: 9850 }, [], 500, 10_000);
        expect(r.endMs).toBe(10_000);
        expect(r.startMs).toBe(9500);
    });

    // #41
    it('start clamped to 0', () => {
        const r = clampToNeighbours({ startMs: -200, endMs: 500 }, [], 100, 10_000);
        expect(r.startMs).toBe(0);
    });

    // #42
    it('sandwiched between two neighbours', () => {
        const r = clampToNeighbours(
            { startMs: 900, endMs: 2100 },
            [
                { startMs: 0, endMs: 1000, id: 'l' },
                { startMs: 2000, endMs: 3000, id: 'r' },
            ],
            100, 10_000,
        );
        expect(r.startMs).toBe(1000);
        expect(r.endMs).toBe(2000);
    });
});

// ── defaultRegionDuration ─────────────────────────────────────────────────────

describe('defaultRegionDuration', () => {

    // #43
    it('normal — 5% of 60s = 3000ms', () => {
        expect(defaultRegionDuration(60_000)).toBe(3000);
    });

    // #44
    it('short clip — floor at 1000ms', () => {
        // 5% of 10000 = 500 → clamped to 1000
        expect(defaultRegionDuration(10_000)).toBe(1000);
    });

    // #45
    it('long clip — ceiling at 30000ms', () => {
        // 5% of 1_000_000 = 50000 → clamped to 30000
        expect(defaultRegionDuration(1_000_000)).toBe(30_000);
    });

    // #46
    it('zero — returns 1000ms floor', () => {
        expect(defaultRegionDuration(0)).toBe(1000);
    });
});

// ── findGapAtPlayhead ─────────────────────────────────────────────────────────

describe('findGapAtPlayhead', () => {

    // #47
    it('open space — full duration available, places at playhead', () => {
        const r = findGapAtPlayhead(0, [], 10_000, 3000);
        expect(r).toEqual({ startMs: 0, endMs: 3000 });
    });

    // #48
    it('playhead inside existing span — returns null', () => {
        expect(findGapAtPlayhead(500, [{ startMs: 0, endMs: 1000, id: 'a' }], 10_000, 3000)).toBeNull();
    });

    // #49
    it('gap smaller than wanted — truncated to available gap', () => {
        // playhead at 1000, spans [{0,1000},{3000,5000}], gap 1000–3000 = 2000ms, wanted 3000
        const r = findGapAtPlayhead(1000, [
            { startMs: 0, endMs: 1000, id: 'a' },
            { startMs: 3000, endMs: 5000, id: 'b' },
        ], 10_000, 3000);
        // gap is 2000ms so truncated
        expect(r).toEqual({ startMs: 1000, endMs: 3000 });
    });

    // #50
    it('gap after span — places between spans', () => {
        const r = findGapAtPlayhead(1000, [
            { startMs: 0, endMs: 1000, id: 'a' },
            { startMs: 4000, endMs: 6000, id: 'b' },
        ], 10_000, 2000);
        expect(r).toEqual({ startMs: 1000, endMs: 3000 });
    });

    // #51
    it('at end of timeline — truncated to remaining space', () => {
        const r = findGapAtPlayhead(9500, [], 10_000, 3000);
        // only 500ms available, so endMs = 10000
        expect(r).toEqual({ startMs: 9500, endMs: 10_000 });
    });
});

// ── smoothStep ────────────────────────────────────────────────────────────────

describe('smoothStep', () => {

    // #52
    it('midpoint t=0.5 → 0.5', () => {
        expect(smoothStep(0.5)).toBe(0.5);
    });

    // #53
    it('clamped below 0 → 0', () => {
        expect(smoothStep(-1)).toBe(0);
    });

    // #54
    it('clamped above 1 → 1', () => {
        expect(smoothStep(2)).toBe(1);
    });

    // #55
    it('t=0.25 → 0.15625', () => {
        expect(smoothStep(0.25)).toBeCloseTo(0.15625);
    });
});

// ── computeRegionStrength ─────────────────────────────────────────────────────

describe('computeRegionStrength', () => {

    const W = TRANSITION_WINDOW_MS; // 400ms

    // #56
    it('at centre of region — full strength 1.0', () => {
        expect(computeRegionStrength(1000, 5000, 3000)).toBe(1);
    });

    // #57
    it('before lead-in starts → 0', () => {
        // leadInStart = 1000 - 400 = 600; time 0 < 600
        expect(computeRegionStrength(1000, 5000, 0)).toBe(0);
    });

    // #58
    it('during fade-in — returns value between 0 and 1', () => {
        // time=800 is between leadInStart(600) and startMs(1000)
        const s = computeRegionStrength(1000, 5000, 800);
        expect(s).toBeGreaterThan(0);
        expect(s).toBeLessThan(1);
    });

    // #59
    it('during fade-out — returns value between 0 and 1', () => {
        // leadOutEnd = 5000 + 400 = 5400; time 5200 is in fade-out
        const s = computeRegionStrength(1000, 5000, 5200);
        expect(s).toBeGreaterThan(0);
        expect(s).toBeLessThan(1);
    });

    // #60
    it('after lead-out ends → 0', () => {
        // leadOutEnd = 5000 + 400 = 5400; time 6000 > 5400
        expect(computeRegionStrength(1000, 5000, 6000)).toBe(0);
    });

    // #61
    it('very short region — does not crash, returns value in [0,1]', () => {
        // region 1000–1200 (200ms < 2 * 400ms window)
        const s = computeRegionStrength(1000, 1200, 1100);
        expect(s).toBeGreaterThanOrEqual(0);
        expect(s).toBeLessThanOrEqual(1);
    });
});
