/**
 * AppRecorderState — Groups 1–8 (unit + state tests, no DOM).
 * Groups 1–4, 6–8 are pure logic — no reactive context required.
 * Groups with $derived values (Group 3 outputResolution, hasUnsavedChanges, effectiveDurationMs)
 * need the class instantiated inside flushSync to allow Svelte 5 runes to settle.
 */
import { describe, it, expect } from 'vitest';
import { AppRecorderState, DEFAULT_TEXT_STYLE, DEFAULT_FIGURE_DATA } from '../../src/lib/apps/app-recorder/AppRecorderState.svelte';

function make() {
    return new AppRecorderState();
}

// ── Group 1: Region CRUD ──────────────────────────────────────────────────────

describe('Group 1 — Region CRUD', () => {

    // #1
    it('addZoomRegion returns region with unique id', () => {
        const s = make();
        const a = s.addZoomRegion(0, 1000);
        const b = s.addZoomRegion(1000, 2000);
        const idA = parseInt(a.id.split('-')[1]);
        const idB = parseInt(b.id.split('-')[1]);
        expect(idB).toBeGreaterThan(idA);
    });

    // #2
    it('addZoomRegion auto-selects newly added region', () => {
        const s = make();
        const r = s.addZoomRegion(0, 2000);
        expect(s.selectedZoomId).toBe(r.id);
    });

    // #3
    it('removeZoomRegion clears selection if selected', () => {
        const s = make();
        const r = s.addZoomRegion(0, 1000);
        s.removeZoomRegion(r.id);
        expect(s.selectedZoomId).toBeNull();
    });

    // #4
    it('removeZoomRegion does not clear unrelated selection', () => {
        const s = make();
        const a = s.addZoomRegion(0, 1000);
        const b = s.addZoomRegion(1000, 2000);
        s.selectZoom(b.id);
        s.removeZoomRegion(a.id);
        expect(s.selectedZoomId).toBe(b.id);
    });

    // #5
    it('updateZoomRegion patches only specified fields', () => {
        const s = make();
        const r = s.addZoomRegion(0, 1000, 0.3, 0.7);
        s.updateZoomRegion(r.id, { depth: 5 });
        const updated = s.zoomRegions.find(x => x.id === r.id)!;
        expect(updated.depth).toBe(5);
        expect(updated.cx).toBe(0.3);
        expect(updated.cy).toBe(0.7);
    });

    // #6 — CRUD completeness for trim, speed, annotation, transition, splitPoint, keyframe

    it('trimRegion: add selects, remove clears selection', () => {
        const s = make();
        const r = s.addTrimRegion(0, 1000);
        expect(s.selectedTrimId).toBe(r.id);
        s.removeTrimRegion(r.id);
        expect(s.selectedTrimId).toBeNull();
    });

    it('trimRegion: updateTrimRegion patches only specified fields', () => {
        const s = make();
        const r = s.addTrimRegion(0, 1000);
        s.updateTrimRegion(r.id, { endMs: 2000 });
        expect(s.trimRegions.find(x => x.id === r.id)!.endMs).toBe(2000);
        expect(s.trimRegions.find(x => x.id === r.id)!.startMs).toBe(0);
    });

    it('speedRegion: add selects, remove clears selection', () => {
        const s = make();
        const r = s.addSpeedRegion(0, 1000, 2.0);
        expect(s.selectedSpeedId).toBe(r.id);
        s.removeSpeedRegion(r.id);
        expect(s.selectedSpeedId).toBeNull();
    });

    it('annotationRegion: add selects, remove clears selection', () => {
        const s = make();
        const r = s.addAnnotation(0, 1000, 'text');
        expect(s.selectedAnnotationId).toBe(r.id);
        s.removeAnnotation(r.id);
        expect(s.selectedAnnotationId).toBeNull();
    });

    it('keyframe: add selects, remove clears selection', () => {
        const s = make();
        const kf = s.addKeyframe(500);
        expect(s.selectedKeyframeId).toBe(kf.id);
        s.removeKeyframe(kf.id);
        expect(s.selectedKeyframeId).toBeNull();
    });

    it('splitPoint: add selects, remove clears selection', () => {
        const s = make();
        const sp = s.addSplitPoint(500);
        expect(s.selectedSplitId).toBe(sp.id);
        s.removeSplitPoint(sp.id);
        expect(s.selectedSplitId).toBeNull();
    });

    it('transition: add selects, update patches, remove clears selection', () => {
        const s = make();
        const t = s.addTransition(1000, 'fade', 300);
        expect(s.selectedTransitionId).toBe(t.id);
        s.updateTransition(t.id, { durationMs: 500 });
        expect(s.transitions.find(x => x.id === t.id)!.durationMs).toBe(500);
        s.removeTransition(t.id);
        expect(s.selectedTransitionId).toBeNull();
    });
});

// ── Group 2: Selection exclusivity ───────────────────────────────────────────

describe('Group 2 — Selection exclusivity', () => {

    // #7
    it('selectZoom clears all other selections', () => {
        const s = make();
        const trim = s.addTrimRegion(0, 1000);
        s.selectTrim(trim.id);
        const zoom = s.addZoomRegion(0, 1000);
        s.selectZoom(zoom.id);
        expect(s.selectedZoomId).toBe(zoom.id);
        expect(s.selectedTrimId).toBeNull();
    });

    // #8
    it('selectAnnotation clears zoom selection', () => {
        const s = make();
        const zoom = s.addZoomRegion(0, 1000);
        s.selectZoom(zoom.id);
        const ann = s.addAnnotation(0, 1000, 'text');
        s.selectAnnotation(ann.id);
        expect(s.selectedZoomId).toBeNull();
    });

    // #9
    it('clearSelection nulls all 7 selection fields', () => {
        const s = make();
        s.addZoomRegion(0, 1000);
        s.addAnnotation(0, 1000, 'text');
        s.clearSelection();
        expect(s.selectedZoomId).toBeNull();
        expect(s.selectedTrimId).toBeNull();
        expect(s.selectedSpeedId).toBeNull();
        expect(s.selectedAnnotationId).toBeNull();
        expect(s.selectedKeyframeId).toBeNull();
        expect(s.selectedSplitId).toBeNull();
        expect(s.selectedTransitionId).toBeNull();
    });

    // #10
    it('selectZoom(null) does not clear other selections', () => {
        const s = make();
        const zoom = s.addZoomRegion(0, 1000);
        s.selectZoom(zoom.id);
        const trim = s.addTrimRegion(0, 1000);
        // selectTrim clears zoom. Now re-select trim so it's set.
        s.selectTrim(trim.id);
        s.selectZoom(null); // deselect zoom only
        expect(s.selectedTrimId).toBe(trim.id);
    });
});

// ── Group 3: Derived values ───────────────────────────────────────────────────

describe('Group 3 — Derived values', () => {

    // #11
    it('selectedZoom resolves to region object', () => {
        const s = make();
        const r = s.addZoomRegion(0, 1000);
        expect(s.selectedZoom?.id).toBe(r.id);
    });

    // #12
    it('selectedZoom returns null when id not found', () => {
        const s = make();
        s.selectZoom('zoom-999');
        expect(s.selectedZoom).toBeNull();
    });

    // #13
    it('effectiveDurationMs subtracts trim regions', () => {
        const s = make();
        s.durationMs = 10000;
        s.addTrimRegion(0, 1000);
        s.addTrimRegion(5000, 6000);
        expect(s.effectiveDurationMs).toBe(8000);
    });

    // #14
    it('effectiveDurationMs is never negative', () => {
        const s = make();
        s.durationMs = 1000;
        s.addTrimRegion(0, 5000);
        expect(s.effectiveDurationMs).toBe(0);
    });

    // #15
    it('outputResolution 16:9 with no metadata returns 1920×1080', () => {
        const s = make();
        s.aspectRatio = '16:9';
        expect(s.outputResolution).toEqual({ width: 1920, height: 1080 });
    });

    // #16
    it('outputResolution 1:1 with no metadata returns equal width and height', () => {
        const s = make();
        s.aspectRatio = '1:1';
        const { width, height } = s.outputResolution;
        expect(width).toBe(height);
    });

    // #17
    it('outputResolution uses source dimensions when metadata set (4K 16:9)', () => {
        const s = make();
        s.setMediaMetadata({ width: 3840, height: 2160, fps: 60, fileSizeBytes: 0 });
        s.aspectRatio = '16:9';
        expect(s.outputResolution).toEqual({ width: 3840, height: 2160 });
    });

    // #18
    it('outputResolution pillarboxes portrait source into 16:9', () => {
        const s = make();
        s.setMediaMetadata({ width: 1080, height: 1920, fps: 30, fileSizeBytes: 0 });
        s.aspectRatio = '16:9';
        // sourceRatio = 1080/1920 = 0.5625 < targetRatio 16/9 = 1.777 → use source height
        const { width, height } = s.outputResolution;
        expect(height).toBe(1920);
        expect(width).toBe(Math.round(1920 * 16 / 9));
    });

    // #19
    it('hasUnsavedChanges is false after loadProject', () => {
        const s = make();
        s.projectPath = '/some/path.hpdproj';
        const snap = captureSnapshot(s);
        s.loadProject(snap);
        expect(s.hasUnsavedChanges).toBe(false);
    });

    // #20
    it('hasUnsavedChanges is true after mutation post-load', () => {
        const s = make();
        s.projectPath = '/some/path.hpdproj';
        const snap = captureSnapshot(s);
        s.loadProject(snap);
        s.addZoomRegion(0, 1000);
        expect(s.hasUnsavedChanges).toBe(true);
    });

    // #21
    it('hasUnsavedChanges is false when projectPath is null', () => {
        const s = make();
        expect(s.projectPath).toBeNull();
        s.addZoomRegion(0, 1000);
        expect(s.hasUnsavedChanges).toBe(false);
    });
});

// ── Group 4: Playback + timeline ─────────────────────────────────────────────

describe('Group 4 — Playback + timeline', () => {

    // #22
    it('seekTo clamps to 0', () => {
        const s = make();
        s.seekTo(-500);
        expect(s.currentTimeMs).toBe(0);
    });

    // #23
    it('seekTo clamps to durationMs', () => {
        const s = make();
        s.durationMs = 5000;
        s.seekTo(9999);
        expect(s.currentTimeMs).toBe(5000);
    });

    // #24
    it('setPlaybackSpeed clamps min 0.25', () => {
        const s = make();
        s.setPlaybackSpeed(0.1);
        expect(s.playbackSpeed).toBe(0.25);
    });

    // #25
    it('setPlaybackSpeed clamps max 2', () => {
        const s = make();
        s.setPlaybackSpeed(10);
        expect(s.playbackSpeed).toBe(2);
    });

    // #26
    it('togglePlayback flips isPlaying', () => {
        const s = make();
        const initial = s.isPlaying;
        s.togglePlayback();
        expect(s.isPlaying).toBe(!initial);
        s.togglePlayback();
        expect(s.isPlaying).toBe(initial);
    });

    // #27
    it('zoomTimeline clamps min 0.02', () => {
        const s = make();
        for (let i = 0; i < 100; i++) s.zoomTimeline(-10);
        expect(s.timelineZoom).toBeGreaterThanOrEqual(0.02);
    });

    // #28
    it('zoomTimeline clamps max 2.0', () => {
        const s = make();
        for (let i = 0; i < 100; i++) s.zoomTimeline(10);
        expect(s.timelineZoom).toBeLessThanOrEqual(2.0);
    });

    // #29
    it('scrollTimeline clamps to 0', () => {
        const s = make();
        s.scrollTimeline(-1000);
        expect(s.timelineScrollMs).toBe(0);
    });
});

// ── Group 5: Annotation specifics ────────────────────────────────────────────

describe('Group 5 — Annotation specifics', () => {

    // #30
    it('addAnnotation kind=text seeds textStyle', () => {
        const s = make();
        const r = s.addAnnotation(0, 1000, 'text');
        expect(r.textStyle).toEqual(DEFAULT_TEXT_STYLE);
    });

    // #31
    it('addAnnotation kind=arrow seeds figureData', () => {
        const s = make();
        const r = s.addAnnotation(0, 1000, 'arrow');
        expect(r.figureData).toEqual(DEFAULT_FIGURE_DATA);
    });

    // #32
    it('addAnnotation kind=image has no figureData', () => {
        const s = make();
        const r = s.addAnnotation(0, 1000, 'image');
        expect(r.figureData).toBeUndefined();
    });

    // #33
    it('bringAnnotationToFront increases zIndex above all others', () => {
        const s = make();
        const a = s.addAnnotation(0, 1000, 'text');
        const b = s.addAnnotation(0, 1000, 'text');
        s.bringAnnotationToFront(a.id);
        const aUpdated = s.annotationRegions.find(r => r.id === a.id)!;
        const bUpdated = s.annotationRegions.find(r => r.id === b.id)!;
        expect(aUpdated.zIndex).toBeGreaterThan(bUpdated.zIndex);
    });

    // #34
    it('sendAnnotationToBack decreases zIndex below all others', () => {
        const s = make();
        const a = s.addAnnotation(0, 1000, 'text');
        const b = s.addAnnotation(0, 1000, 'text');
        s.sendAnnotationToBack(b.id);
        const aUpdated = s.annotationRegions.find(r => r.id === a.id)!;
        const bUpdated = s.annotationRegions.find(r => r.id === b.id)!;
        expect(bUpdated.zIndex).toBeLessThan(aUpdated.zIndex);
    });
});

// ── Group 6: Export ───────────────────────────────────────────────────────────

describe('Group 6 — Export', () => {

    // #35
    it('startExport sets active=true', () => {
        const s = make();
        s.startExport();
        expect(s.exportStatus.active).toBe(true);
    });

    // #36
    it('startExport snapshots format from exportSettings', () => {
        const s = make();
        s.setExportSettings({ format: 'gif' });
        s.startExport();
        expect(s.exportStatus.format).toBe('gif');
    });

    // #37
    it('setExportSettings blocked during active export', () => {
        const s = make();
        s.setExportSettings({ format: 'mp4' });
        s.startExport();
        s.setExportSettings({ format: 'gif' });
        expect(s.exportSettings.format).toBe('mp4');
    });

    // #38
    it('completeExport clears active, sets outputPath and percent=100', () => {
        const s = make();
        s.startExport();
        s.completeExport('/out.mp4');
        expect(s.exportStatus.active).toBe(false);
        expect(s.exportStatus.outputPath).toBe('/out.mp4');
        expect(s.exportStatus.percent).toBe(100);
    });

    // #39
    it('completeExport appends to recentExports most-recent-first', () => {
        const s = make();
        for (let i = 1; i <= 3; i++) {
            s.startExport();
            s.completeExport(`/out${i}.mp4`);
        }
        expect(s.recentExports.length).toBe(3);
        expect(s.recentExports[0].outputPath).toBe('/out3.mp4');
    });

    // #40
    it('recentExports capped at 10', () => {
        const s = make();
        for (let i = 0; i < 12; i++) {
            s.startExport();
            s.completeExport(`/out${i}.mp4`);
        }
        expect(s.recentExports.length).toBe(10);
    });

    // #41
    it('failExport clears active, sets error', () => {
        const s = make();
        s.startExport();
        s.failExport('disk full');
        expect(s.exportStatus.active).toBe(false);
        expect(s.exportStatus.error).toBe('disk full');
    });
});

// ── Group 7: Overlay promise slots ───────────────────────────────────────────

describe('Group 7 — Overlay promise slots', () => {

    // #42
    it('openSourcePicker resolves with selected source id', async () => {
        const s = make();
        const p = s.openSourcePicker([]);
        s.resolveSourcePick('src-1');
        await expect(p).resolves.toBe('src-1');
    });

    // #43
    it('openSourcePicker resolves null on cancel', async () => {
        const s = make();
        const p = s.openSourcePicker([]);
        s.resolveSourcePick(null);
        await expect(p).resolves.toBeNull();
    });

    // #44
    it('resolveSourcePick closes picker', async () => {
        const s = make();
        s.openSourcePicker([]);
        s.resolveSourcePick(null);
        expect(s.sourcePickerOpen).toBe(false);
    });

    // #45
    it('openImportPicker / resolveImportPick resolves and closes', async () => {
        const s = make();
        const p = s.openImportPicker();
        s.resolveImportPick('/file.mp4');
        await expect(p).resolves.toBe('/file.mp4');
        expect(s.importPickerOpen).toBe(false);
    });

    // #46
    it('openUnsavedChangesDialog resolves with discard', async () => {
        const s = make();
        const p = s.openUnsavedChangesDialog();
        s.resolveUnsavedChanges('discard');
        await expect(p).resolves.toBe('discard');
    });
});

// ── Group 8: loadProject / ID counter re-sync ─────────────────────────────────

describe('Group 8 — loadProject / ID counter re-sync', () => {

    // #47
    it('loadProject restores all snapshot fields', () => {
        const s = make();
        s.videoPath = '/video.mp4';
        s.addZoomRegion(0, 1000);
        const snap = captureSnapshot(s);
        const s2 = make();
        s2.loadProject(snap);
        expect(s2.videoPath).toBe('/video.mp4');
        expect(s2.zoomRegions.length).toBe(1);
    });

    // #48
    it('loadProject resyncs zoom ID counter so new zoom has id zoom-6', () => {
        const s = make();
        // Add 5 zoom regions to push counter to 5
        for (let i = 0; i < 5; i++) s.addZoomRegion(i * 1000, (i + 1) * 1000);
        const snap = captureSnapshot(s);
        const s2 = make();
        s2.loadProject(snap);
        const newR = s2.addZoomRegion(5000, 6000);
        expect(newR.id).toBe('zoom-6');
    });

    // #49
    it('loadProject resyncs annotationZ counter', () => {
        const s = make();
        const a = s.addAnnotation(0, 1000, 'text');
        const snap = captureSnapshot(s);
        const s2 = make();
        s2.loadProject(snap);
        s2.bringAnnotationToFront(a.id);
        const updated = s2.annotationRegions.find(r => r.id === a.id)!;
        expect(updated.zIndex).toBeGreaterThan(a.zIndex);
    });

    // #50
    it('markSaved clears hasUnsavedChanges', () => {
        const s = make();
        s.projectPath = '/path.hpdproj';
        // First load a snapshot to establish a baseline
        const snap = captureSnapshot(s);
        s.loadProject(snap);
        s.addZoomRegion(0, 1000); // dirty
        expect(s.hasUnsavedChanges).toBe(true);
        s.markSaved('/path.hpdproj');
        expect(s.hasUnsavedChanges).toBe(false);
    });
});

// ── Snapshot helper ────────────────────────────────────────────────────────────

function captureSnapshot(s: AppRecorderState) {
    return {
        projectId:         s.projectId,
        projectPath:       s.projectPath,
        sourceType:        s.sourceType,
        videoPath:         s.videoPath,
        telemetryPath:     s.telemetryPath,
        aspectRatio:       s.aspectRatio,
        zoomRegions:       s.zoomRegions,
        trimRegions:       s.trimRegions,
        speedRegions:      s.speedRegions,
        annotationRegions: s.annotationRegions,
        keyframes:         s.keyframes,
        splitPoints:       s.splitPoints,
        transitions:       s.transitions,
        background:        s.background,
        visual:            s.visual,
        crop:              s.crop,
        colorGrade:        s.colorGrade,
        export:            s.exportSettings,
    };
}
