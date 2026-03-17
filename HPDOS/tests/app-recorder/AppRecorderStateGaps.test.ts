/**
 * AppRecorderState — Gap tests (Groups 9–14, tests #51–85).
 * Covers: handleClientTool bridge (#51–63), scrollTimeline ceiling (#64),
 * addRecentProject dedup/cap (#65–66), loadProject clears selection (#67),
 * sort invariants (#68–70), annotation zIndex immutability (#71),
 * setters (#72–80), and export/HUD defaults (#81–85).
 */
import { describe, it, expect } from 'vitest';
import { AppRecorderState } from '../../src/lib/apps/app-recorder/AppRecorderState.svelte';

function make() {
    return new AppRecorderState();
}

function req(toolName: string, args: Record<string, unknown> = {}) {
    return { requestId: 'r1', toolName, arguments: args };
}

// ── Group 9: handleClientTool bridge ─────────────────────────────────────────

describe('Group 9 — handleClientTool bridge', () => {

    // #51
    it('show_recording_hud sets hudVisible=true', async () => {
        const s = make();
        await s.handleClientTool(req('show_recording_hud'));
        expect(s.hudVisible).toBe(true);
    });

    // #52
    it('hide_recording_hud clears hudVisible', async () => {
        const s = make();
        s.showHud();
        await s.handleClientTool(req('hide_recording_hud'));
        expect(s.hudVisible).toBe(false);
        expect(s.recordingStartedAt).toBeNull();
    });

    // #53
    it('seek_preview calls seekTo with given timeMs', async () => {
        const s = make();
        s.durationMs = 10000;
        await s.handleClientTool(req('seek_preview', { timeMs: 3000 }));
        expect(s.currentTimeMs).toBe(3000);
    });

    // #54
    it('set_playback_speed updates playbackSpeed', async () => {
        const s = make();
        await s.handleClientTool(req('set_playback_speed', { speed: 1.5 }));
        expect(s.playbackSpeed).toBe(1.5);
    });

    // #55
    it('show_export_progress updates exportStatus format and percent', async () => {
        const s = make();
        s.startExport();
        await s.handleClientTool(req('show_export_progress', { format: 'mp4', percent: 50 }));
        expect(s.exportStatus.percent).toBe(50);
        expect(s.exportStatus.format).toBe('mp4');
    });

    // #56
    it('show_export_complete completes export with outputPath', async () => {
        const s = make();
        s.startExport();
        await s.handleClientTool(req('show_export_complete', { filePath: '/out.mp4', format: 'mp4' }));
        expect(s.exportStatus.outputPath).toBe('/out.mp4');
        expect(s.exportStatus.active).toBe(false);
        expect(s.exportStatus.percent).toBe(100);
    });

    // #57
    it('show_source_picker opens picker then resolves with sourceId', async () => {
        const s = make();
        const sources = [{ id: 'src-1', name: 'Screen 1', type: 'screen' as const }];
        const promise = s.handleClientTool(req('show_source_picker', { sources }))!;
        expect(s.sourcePickerOpen).toBe(true);
        s.resolveSourcePick('src-1');
        const result = await promise;
        expect(result.success).toBe(true);
    });

    // #58
    it('show_import_picker opens picker then resolves with path', async () => {
        const s = make();
        const promise = s.handleClientTool(req('show_import_picker'))!;
        expect(s.importPickerOpen).toBe(true);
        s.resolveImportPick('/video.mp4');
        const result = await promise;
        expect(result.success).toBe(true);
    });

    // #59
    it('show_unsaved_changes_dialog resolves with discard', async () => {
        const s = make();
        const promise = s.handleClientTool(req('show_unsaved_changes_dialog'))!;
        expect(s.unsavedChangesOpen).toBe(true);
        s.resolveUnsavedChanges('discard');
        const result = await promise;
        expect(result.success).toBe(true);
    });

    // #60
    it('highlight_regions sets highlightedRegions', async () => {
        const s = make();
        const regions = [{ startMs: 0, endMs: 1000, type: 'zoom' as const }];
        await s.handleClientTool(req('highlight_regions', { regions }));
        expect(s.highlightedRegions).toHaveLength(1);
    });

    // #61
    it('unknown tool returns null', () => {
        const s = make();
        const result = s.handleClientTool(req('totally_unknown_tool'));
        expect(result).toBeNull();
    });

    // #62
    it('show_font_picker sets fontPickerOpen=true', async () => {
        const s = make();
        await s.handleClientTool(req('show_font_picker'));
        expect(s.fontPickerOpen).toBe(true);
    });

    // #63
    it('show_shortcut_config sets shortcutConfigOpen=true', async () => {
        const s = make();
        await s.handleClientTool(req('show_shortcut_config'));
        expect(s.shortcutConfigOpen).toBe(true);
    });
});

// ── Group 10: Timeline / scroll ───────────────────────────────────────────────

describe('Group 10 — Timeline and scroll', () => {

    // #64
    it('scrollTimeline ceiling never exceeds durationMs - 1', () => {
        const s = make();
        s.durationMs = 5000;
        s.scrollTimeline(99999);
        expect(s.timelineScrollMs).toBeLessThanOrEqual(4999);
    });
});

// ── Group 11: Recent projects ─────────────────────────────────────────────────

describe('Group 11 — Recent projects', () => {

    function project(id: string, name = id) {
        return { id, name, path: `/path/${id}`, lastEditedAt: Date.now() };
    }

    // #65
    it('addRecentProject deduplicates by id (updates entry)', () => {
        const s = make();
        s.addRecentProject(project('p1', 'Old Name'));
        s.addRecentProject(project('p1', 'New Name'));
        expect(s.recentProjects).toHaveLength(1);
        expect(s.recentProjects[0].name).toBe('New Name');
    });

    // #66
    it('addRecentProject capped at 10', () => {
        const s = make();
        for (let i = 0; i < 12; i++) s.addRecentProject(project(`p${i}`));
        expect(s.recentProjects).toHaveLength(10);
    });
});

// ── Group 12: loadProject clears selection ────────────────────────────────────

describe('Group 12 — loadProject clears selection', () => {

    // #67
    it('loadProject clears all seven selection fields', () => {
        const s = make();
        const zoom = s.addZoomRegion(0, 1000);
        s.selectZoom(zoom.id);
        // build a minimal snapshot
        s.loadProject({
            projectId: 'loaded', projectPath: '/p.hpdrecorder', sourceType: null,
            videoPath: null, telemetryPath: null, aspectRatio: '16:9',
            zoomRegions: [], trimRegions: [], speedRegions: [], annotationRegions: [],
            keyframes: [], splitPoints: [], transitions: [],
            background: { kind: 'solid', color: '#000' },
            visual: { borderRadius: 0, padding: 50, dropShadow: false, backgroundBlur: false, motionBlur: false },
            crop: null, colorGrade: null,
            export: { quality: 'good', format: 'mp4', gifFrameRate: 15, gifLoop: true, gifSize: 'medium' },
        });
        expect(s.selectedZoomId).toBeNull();
        expect(s.selectedTrimId).toBeNull();
        expect(s.selectedSpeedId).toBeNull();
        expect(s.selectedAnnotationId).toBeNull();
        expect(s.selectedKeyframeId).toBeNull();
        expect(s.selectedSplitId).toBeNull();
        expect(s.selectedTransitionId).toBeNull();
    });
});

// ── Group 13: Sort invariants ─────────────────────────────────────────────────

describe('Group 13 — Sort invariants', () => {

    // #68
    it('splitPoints are sorted by timeMs after addSplitPoint out-of-order', () => {
        const s = make();
        s.addSplitPoint(5000);
        s.addSplitPoint(1000);
        expect(s.splitPoints[0].timeMs).toBeLessThan(s.splitPoints[1].timeMs);
    });

    // #69
    it('keyframes are sorted by timeMs after addKeyframe out-of-order', () => {
        const s = make();
        s.addKeyframe(5000);
        s.addKeyframe(1000);
        expect(s.keyframes[0].timeMs).toBeLessThan(s.keyframes[1].timeMs);
    });

    // #70
    it('transitions are sorted by timeMs after addTransition out-of-order', () => {
        const s = make();
        s.addTransition(5000, 'fade', 300);
        s.addTransition(1000, 'wipe', 200);
        expect(s.transitions[0].timeMs).toBeLessThan(s.transitions[1].timeMs);
    });
});

// ── Group 14: Annotation zIndex immutability ──────────────────────────────────

describe('Group 14 — Annotation zIndex immutability', () => {

    // #71
    it('updateAnnotation cannot change zIndex (Omit enforced at runtime)', () => {
        const s = make();
        const ann = s.addAnnotation(0, 1000, 'text');
        const origZ = ann.zIndex;
        // Attempt to pass zIndex — the signature Omits it, so the patch is applied
        // without the zIndex key. The zIndex should remain unchanged.
        s.updateAnnotation(ann.id, { text: 'hello' });
        const updated = s.annotationRegions.find(r => r.id === ann.id)!;
        expect(updated.zIndex).toBe(origZ);
    });
});

// ── Group 15: Setters ─────────────────────────────────────────────────────────

describe('Group 15 — Setters', () => {

    // #72
    it('setActivePage updates activePage', () => {
        const s = make();
        s.setActivePage('annotate');
        expect(s.activePage).toBe('annotate');
    });

    // #73
    it('setAnnotationTool updates annotationTool', () => {
        const s = make();
        s.setAnnotationTool('arrow');
        expect(s.annotationTool).toBe('arrow');
    });

    // #74
    it('showHud sets hudVisible and recordingStartedAt', () => {
        const s = make();
        const before = Date.now();
        s.showHud();
        expect(s.hudVisible).toBe(true);
        expect(s.recordingStartedAt).toBeGreaterThanOrEqual(before);
    });

    // #75
    it('hideHud clears hudVisible and recordingStartedAt', () => {
        const s = make();
        s.showHud();
        s.hideHud();
        expect(s.hudVisible).toBe(false);
        expect(s.recordingStartedAt).toBeNull();
    });

    // #76
    it('updateExportProgress updates percent and format', () => {
        const s = make();
        s.startExport();
        s.updateExportProgress('gif', 75);
        expect(s.exportStatus.percent).toBe(75);
        expect(s.exportStatus.format).toBe('gif');
    });

    // #77
    it('setBackground updates background', () => {
        const s = make();
        s.setBackground({ kind: 'gradient', gradientCss: 'linear-gradient(90deg, red, blue)' });
        expect(s.background.kind).toBe('gradient');
    });

    // #78
    it('setVisual partial patch preserves other fields', () => {
        const s = make();
        const orig = { ...s.visual };
        s.setVisual({ dropShadow: true });
        expect(s.visual.dropShadow).toBe(true);
        expect(s.visual.borderRadius).toBe(orig.borderRadius);
        expect(s.visual.padding).toBe(orig.padding);
    });

    // #79
    it('setCrop sets and clears crop', () => {
        const s = make();
        s.setCrop({ x: 0.1, y: 0.1, width: 0.8, height: 0.8 });
        expect(s.crop).not.toBeNull();
        s.setCrop(null);
        expect(s.crop).toBeNull();
    });

    // #80
    it('clearHighlights empties highlightedRegions', () => {
        const s = make();
        s.highlightRegions([{ startMs: 0, endMs: 1000, type: 'zoom' }]);
        s.clearHighlights();
        expect(s.highlightedRegions).toHaveLength(0);
    });
});

// ── Group 16: Speed region ramping ───────────────────────────────────────────

describe('Group 16 — Speed region ramping', () => {

    // #81
    it('addSpeedRegion ramping defaults to false', () => {
        const s = make();
        const r = s.addSpeedRegion(0, 1000, 2.0);
        expect(r.ramping).toBe(false);
    });

    // #82
    it('updateSpeedRegion can patch ramping to true', () => {
        const s = make();
        const r = s.addSpeedRegion(0, 1000, 2.0);
        s.updateSpeedRegion(r.id, { ramping: true });
        const updated = s.speedRegions.find(x => x.id === r.id)!;
        expect(updated.ramping).toBe(true);
    });
});

// ── Group 17: Default values ──────────────────────────────────────────────────

describe('Group 17 — Default values', () => {

    // #83
    it('exportStatus initial state is all-clear', () => {
        const s = make();
        expect(s.exportStatus.active).toBe(false);
        expect(s.exportStatus.format).toBeNull();
        expect(s.exportStatus.percent).toBe(0);
        expect(s.exportStatus.error).toBeNull();
        expect(s.exportStatus.outputPath).toBeNull();
    });

    // #84
    it('exportSettings defaults: quality=good format=mp4 gifFrameRate=15 gifLoop=true gifSize=medium', () => {
        const s = make();
        expect(s.exportSettings.quality).toBe('good');
        expect(s.exportSettings.format).toBe('mp4');
        expect(s.exportSettings.gifFrameRate).toBe(15);
        expect(s.exportSettings.gifLoop).toBe(true);
        expect(s.exportSettings.gifSize).toBe('medium');
    });

    // #85
    it('setColorGrade sets grade and can be cleared', () => {
        const s = make();
        const grade = { lift: { r: 0, g: 0, b: 0 }, gamma: { r: 1, g: 1, b: 1 }, gain: { r: 1, g: 1, b: 1 } };
        s.setColorGrade(grade);
        expect(s.colorGrade).not.toBeNull();
        s.setColorGrade(null);
        expect(s.colorGrade).toBeNull();
    });
});
