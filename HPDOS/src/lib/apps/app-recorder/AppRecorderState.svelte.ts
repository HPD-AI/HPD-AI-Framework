/**
 * AppRecorderState — single source of truth for the HPD Video editor.
 *
 * Design rules (learned from OpenScreen fragmentation analysis):
 *   1. One class, $state fields only. No external refs. No effect-synced copies.
 *   2. Every region lives in exactly one place. Canvas and timeline read the same array.
 *   3. Selection is mutually exclusive. selectZoom() clears all other selections.
 *   4. Export is an atomic object — settings + status together. Settings snapshot on startExport().
 *   5. $derived for all computed values. No dep arrays. No missed deps.
 *   6. Dirty detection via $derived — automatically tracks every project field.
 *   7. UI overlays (pickers, dialogs) resolved via promise slots — one promise per overlay type.
 *   8. No ID collisions — private counters, typed prefixes, globally unique per session.
 *
 * Multi-clip model (libopenshot invariants):
 *   - Each ClipModel owns its sub-lane regions (zoom/trim/speed/annotation/keyframes).
 *   - Region startMs/endMs are clip-LOCAL (0 = clip source start, not global timeline).
 *   - clip.position = global timeline offset in ms (left edge of clip on the timeline).
 *   - clip.start/end = trim in/out on source (ms). Duration = end - start.
 *   - Global time T maps to clip source time: clip.start + (T - clip.position).
 *   - durationMs is computed: max(clip.position + clip.end - clip.start) over all clips.
 *   - Gaps produce black frames. No auto-slide.
 *   - clip.layer = compositing order (default 0). Reserved for future Resolve-style multi-layer.
 */

import { createSuccessResponse, createErrorResponse } from '@hpd/hpd-agent-headless-ui';

// ── Page navigation ───────────────────────────────────────────────────────────

export type ActivePage = 'media' | 'edit' | 'annotate' | 'audio' | 'color' | 'export';

// ── Annotation tool ───────────────────────────────────────────────────────────

export type AnnotationTool = 'select' | 'text' | 'arrow' | 'image' | 'zoom-point' | 'crop';

// ── Shared types (mirror C# models) ──────────────────────────────────────────

export type SourceType = 'screen' | 'camera' | 'import';

export interface RecordingSource {
    id: string;
    name: string;
    type: 'screen' | 'window';
    width?: number;
    height?: number;
}

export interface MediaMetadata {
    width: number;
    height: number;
    fps: number;
    fileSizeBytes: number;
}

export interface RecentProject {
    id: string;
    name: string;
    path: string;
    thumbnailUrl?: string;
    lastEditedAt: number; // Unix ms
}

export interface RecentExport {
    id: string;
    filename: string;
    format: ExportFormat;
    fileSizeBytes: number;
    exportedAt: number; // Unix ms
    outputPath: string;
}

// ── Zoom depth (mirrors openscreen ZOOM_DEPTH_SCALES) ─────────────────────────
// Discrete levels 1–6 mapping to scale multipliers.

export type ZoomDepth = 1 | 2 | 3 | 4 | 5 | 6;

export const ZOOM_DEPTH_SCALES: Record<ZoomDepth, number> = {
    1: 1.25,
    2: 1.5,
    3: 1.8,
    4: 2.2,
    5: 3.5,
    6: 5.0,
};

export const ZOOM_DEPTH_LABELS: Record<ZoomDepth, string> = {
    1: '1.25×',
    2: '1.5×',
    3: '1.8×',
    4: '2.2×',
    5: '3.5×',
    6: '5×',
};

// ── Region types (mirror ProjectCommands.cs) ─────────────────────────────────
// All startMs/endMs are CLIP-LOCAL (relative to clip.start, not global timeline).

export interface ZoomRegion {
    id: string;
    startMs: number;
    endMs: number;
    /** Discrete zoom level 1–6 (maps to ZOOM_DEPTH_SCALES) */
    depth: ZoomDepth;
    /** Normalised focus point (0–1) */
    cx: number;
    cy: number;
}

export interface TrimRegion {
    id: string;
    startMs: number;
    endMs: number;
}

export interface SpeedRegion {
    id: string;
    startMs: number;
    endMs: number;
    /** >1 = faster, <1 = slower. Mirrors libopenshot Clip.time keyframe. */
    multiplier: number;
    /** Ease in/out ramping via keyframe curve */
    ramping: boolean;
}

// ── Annotation types (richer model from openscreen reference) ─────────────────

export type AnnotationKind = 'text' | 'arrow' | 'image';

export type ArrowDirection =
    | 'up' | 'down' | 'left' | 'right'
    | 'up-right' | 'up-left' | 'down-right' | 'down-left';

export interface AnnotationTextStyle {
    color: string;
    backgroundColor: string;
    fontSize: number;
    fontFamily: string;
    fontWeight: 'normal' | 'bold';
    fontStyle: 'normal' | 'italic';
    textDecoration: 'none' | 'underline';
    textAlign: 'left' | 'center' | 'right';
}

export interface AnnotationFigureData {
    arrowDirection: ArrowDirection;
    color: string;
    strokeWidth: number;
}

export interface AnnotationRegion {
    id: string;
    startMs: number;
    endMs: number;
    kind: AnnotationKind;
    /** Normalised 0–1 canvas position */
    x: number;
    y: number;
    width: number;
    height: number;
    /** Stacking order. Higher = on top. Never reuse values. */
    zIndex: number;
    opacity: number;
    /** Text content (kind === 'text') */
    text?: string;
    textStyle?: AnnotationTextStyle;
    /** Image src / data URL (kind === 'image') */
    imageSrc?: string;
    /** Arrow data (kind === 'arrow') */
    figureData?: AnnotationFigureData;
}

export const DEFAULT_TEXT_STYLE: AnnotationTextStyle = {
    color: '#ffffff',
    backgroundColor: 'transparent',
    fontSize: 32,
    fontFamily: 'Inter',
    fontWeight: 'bold',
    fontStyle: 'normal',
    textDecoration: 'none',
    textAlign: 'center',
};

export const DEFAULT_FIGURE_DATA: AnnotationFigureData = {
    arrowDirection: 'right',
    color: '#34B27B',
    strokeWidth: 4,
};

export interface Keyframe {
    id: string;
    timeMs: number;
}

export interface SplitPoint {
    id: string;
    timeMs: number;
}

export type TransitionType = string; // e.g. "fade", "wipe-right"

export interface Transition {
    id: string;
    timeMs: number;
    type: TransitionType;
    durationMs: number;
}

// ── Clip model (libopenshot ClipBase + sub-lanes) ─────────────────────────────

export interface ClipModel {
    id: string;
    /** Absolute path to source video file */
    path: string;
    /** Global timeline offset — left edge of clip, ms */
    position: number;
    /** Trim in-point on source, ms */
    start: number;
    /** Trim out-point on source, ms */
    end: number;
    /** Compositing layer. Default 0. Higher = rendered on top. */
    layer: number;
    /** Whether sub-lanes are visible in the timeline UI */
    expanded: boolean;
    // Sub-lane regions — all ms values are CLIP-LOCAL (0 = source start)
    zoomRegions:       ZoomRegion[];
    trimRegions:       TrimRegion[];
    speedRegions:      SpeedRegion[];
    annotationRegions: AnnotationRegion[];
    keyframes:         Keyframe[];
}

// ── Visual options (mirror VisualOptions record in C#) ───────────────────────

export type BackgroundKind = 'solid' | 'gradient' | 'image' | 'preset';

export interface BackgroundOptions {
    kind: BackgroundKind;
    color?: string;
    gradientCss?: string;
    imagePath?: string;
    presetId?: string;
}

export interface VisualOptions {
    borderRadius: number;
    padding: number;
    dropShadow: boolean;
    backgroundBlur: boolean;
    motionBlur: boolean;
}

export interface CropOptions {
    /** All normalised 0–1 */
    x: number;
    y: number;
    width: number;
    height: number;
}

// ── Color grade (Color page — Phase 5) ───────────────────────────────────────

export interface ColorGrade {
    lift: { r: number; g: number; b: number };
    gamma: { r: number; g: number; b: number };
    gain: { r: number; g: number; b: number };
    lutPath?: string;
}

// ── Export ───────────────────────────────────────────────────────────────────

export type ExportQuality = 'medium' | 'good' | 'source';
export type ExportFormat = 'mp4' | 'gif';
export type GifFrameRate = 10 | 15 | 20 | 24;
export type GifSizePreset = 'small' | 'medium' | 'large';
export type AspectRatio = '16:9' | '4:3' | '1:1' | '9:16' | '21:9';

export const ASPECT_RATIO_DIMENSIONS: Record<AspectRatio, { w: number; h: number }> = {
    '16:9':  { w: 16, h: 9  },
    '4:3':   { w: 4,  h: 3  },
    '1:1':   { w: 1,  h: 1  },
    '9:16':  { w: 9,  h: 16 },
    '21:9':  { w: 21, h: 9  },
};

export interface ExportSettings {
    quality: ExportQuality;
    format: ExportFormat;
    gifFrameRate: GifFrameRate;
    gifLoop: boolean;
    gifSize: GifSizePreset;
}

export interface ExportStatus {
    active: boolean;
    format: ExportFormat | null;
    percent: number;
    error: string | null;
    outputPath: string | null;
}

// ── Project snapshot (what gets serialised / saved) ──────────────────────────

export interface ProjectSnapshot {
    projectId: string | null;
    projectPath: string | null;
    sourceType: SourceType | null;
    aspectRatio: AspectRatio;
    clips: ClipModel[];
    splitPoints: SplitPoint[];
    transitions: Transition[];
    background: BackgroundOptions;
    visual: VisualOptions;
    crop: CropOptions | null;
    colorGrade: ColorGrade | null;
    export: ExportSettings;
}

// ── Overlay promise slot ─────────────────────────────────────────────────────

interface OverlaySlot<T> {
    resolve: ((value: T) => void) | null;
}

// ── Default values ────────────────────────────────────────────────────────────

const DEFAULT_VISUAL: VisualOptions = {
    borderRadius: 0,
    padding: 50,
    dropShadow: false,
    backgroundBlur: false,
    motionBlur: false,
};

const DEFAULT_BACKGROUND: BackgroundOptions = {
    kind: 'solid',
    color: '#1a1a2e',
};

const DEFAULT_EXPORT: ExportSettings = {
    quality: 'good',
    format: 'mp4',
    gifFrameRate: 15,
    gifLoop: true,
    gifSize: 'medium',
};

// ── AppRecorderState ──────────────────────────────────────────────────────────

export class AppRecorderState {

    // ── Page / tool navigation ────────────────────────────────────────────────
    activePage        = $state<ActivePage>('media');
    annotationTool    = $state<AnnotationTool>('select');

    // ── Project identity ──────────────────────────────────────────────────────
    projectId         = $state<string | null>(null);
    projectPath       = $state<string | null>(null);
    sourceType        = $state<SourceType | null>(null);

    // ── Canvas / aspect ratio (top-level — affects live preview AND export) ───
    aspectRatio       = $state<AspectRatio>('16:9');

    // ── Playback ──────────────────────────────────────────────────────────────
    currentTimeMs     = $state(0);
    isPlaying         = $state(false);
    playbackSpeed     = $state(1);

    // ── Timeline view state ───────────────────────────────────────────────────
    /** Pixels per millisecond. Default: 0.1 = 100px per second. */
    timelineZoom      = $state(0.1);
    /** Height of the clip bar row in px. Sub-lanes scale from this. Range 28–120. */
    trackHeight       = $state(48);
    /** Left edge of visible timeline in ms. */
    timelineScrollMs  = $state(0);

    // ── Clips — single source of truth ───────────────────────────────────────
    clips             = $state<ClipModel[]>([]);

    // ── Per-clip media metadata (keyed by clip.id) ────────────────────────────
    clipMetadata      = $state<Map<string, MediaMetadata>>(new Map());

    // ── Recording sources (populated by show_source_picker) ──────────────────
    recordingSources  = $state<RecordingSource[]>([]);

    // ── Recent projects + exports ─────────────────────────────────────────────
    recentProjects    = $state<RecentProject[]>([]);
    recentExports     = $state<RecentExport[]>([]);

    // ── Project-level regions (global timeline, not clip-scoped) ─────────────
    splitPoints       = $state<SplitPoint[]>([]);
    transitions       = $state<Transition[]>([]);

    // ── Selection ─────────────────────────────────────────────────────────────
    // Clip selection is the outermost; region selections are mutually exclusive
    // within the selected clip.
    #selectedClipId        = $state<string | null>(null);
    #selectedZoomId        = $state<string | null>(null);
    #selectedTrimId        = $state<string | null>(null);
    #selectedSpeedId       = $state<string | null>(null);
    #selectedAnnotationId  = $state<string | null>(null);
    #selectedKeyframeId    = $state<string | null>(null);
    #selectedSplitId       = $state<string | null>(null);
    #selectedTransitionId  = $state<string | null>(null);

    get selectedClipId()       { return this.#selectedClipId; }
    get selectedZoomId()       { return this.#selectedZoomId; }
    get selectedTrimId()       { return this.#selectedTrimId; }
    get selectedSpeedId()      { return this.#selectedSpeedId; }
    get selectedAnnotationId() { return this.#selectedAnnotationId; }
    get selectedKeyframeId()   { return this.#selectedKeyframeId; }
    get selectedSplitId()      { return this.#selectedSplitId; }
    get selectedTransitionId() { return this.#selectedTransitionId; }

    // ── Visual / visual options ───────────────────────────────────────────────
    background        = $state<BackgroundOptions>({ ...DEFAULT_BACKGROUND });
    visual            = $state<VisualOptions>({ ...DEFAULT_VISUAL });
    crop              = $state<CropOptions | null>(null);
    colorGrade        = $state<ColorGrade | null>(null);

    // ── Export — settings + status grouped to prevent mid-export drift ────────
    exportSettings    = $state<ExportSettings>({ ...DEFAULT_EXPORT });
    exportStatus      = $state<ExportStatus>({
        active: false,
        format: null,
        percent: 0,
        error: null,
        outputPath: null,
    });
    showExportDialog  = $state(false);

    // ── Project persistence ───────────────────────────────────────────────────
    #savedSnapshot    = $state<string>('');

    // ── Loading / error ───────────────────────────────────────────────────────
    loading           = $state(false);
    error             = $state<string | null>(null);

    // ── Recording HUD ─────────────────────────────────────────────────────────
    hudVisible        = $state(false);
    recordingStartedAt = $state<number | null>(null);

    // ── Overlay visibility ────────────────────────────────────────────────────
    sourcePickerOpen        = $state(false);
    sourcePickerSources     = $state<RecordingSource[]>([]);
    importPickerOpen        = $state(false);
    unsavedChangesOpen      = $state(false);
    fontPickerOpen          = $state(false);
    shortcutConfigOpen      = $state(false);

    // ── Highlighted regions (timeline animation) ──────────────────────────────
    highlightedRegions = $state<{ startMs: number; endMs: number; type: 'zoom' | 'trim' | 'speed' }[]>([]);

    // ── Private: ID counters (never expose; prevents collisions) ─────────────
    #ids = {
        clip: 1,
        zoom: 1,
        trim: 1,
        speed: 1,
        annotation: 1,
        annotationZ: 1,
        keyframe: 1,
        split: 1,
        transition: 1,
    };

    // ── Private: overlay promise slots ───────────────────────────────────────
    #sourcePick:  OverlaySlot<string | null>        = { resolve: null };
    #importPick:  OverlaySlot<string | null>        = { resolve: null };
    #unsavedPick: OverlaySlot<'discard' | 'cancel'> = { resolve: null };

    // ── $derived: computed timeline duration ──────────────────────────────────
    // max(clip.position + clip.end - clip.start) over all clips — no explicit field.

    readonly durationMs = $derived.by(() => {
        if (this.clips.length === 0) return 0;
        return this.clips.reduce((max, c) => Math.max(max, c.position + (c.end - c.start)), 0);
    });

    // ── $derived: clip active at current playhead ─────────────────────────────

    readonly activeClip = $derived.by(() =>
        this.clips.find(c =>
            this.currentTimeMs >= c.position &&
            this.currentTimeMs < c.position + (c.end - c.start)
        ) ?? null
    );

    // ── $derived: source time within the active clip ──────────────────────────

    readonly activeClipSourceMs = $derived.by(() => {
        const c = this.activeClip;
        return c ? c.start + (this.currentTimeMs - c.position) : 0;
    });

    // ── $derived: media metadata for the active clip ──────────────────────────

    readonly activeClipMetadata = $derived.by(() =>
        this.activeClip ? (this.clipMetadata.get(this.activeClip.id) ?? null) : null
    );

    // ── $derived: output resolution from aspect ratio + source ────────────────

    readonly outputResolution = $derived.by(() => {
        const meta = this.activeClipMetadata;
        const ratio = ASPECT_RATIO_DIMENSIONS[this.aspectRatio];
        if (!meta) {
            const baseW = 1920;
            const baseH = Math.round(baseW * ratio.h / ratio.w);
            return { width: baseW, height: baseH };
        }
        const sourceRatio = meta.width / meta.height;
        const targetRatio = ratio.w / ratio.h;
        if (sourceRatio > targetRatio) {
            return { width: meta.width, height: Math.round(meta.width * ratio.h / ratio.w) };
        } else {
            return { width: Math.round(meta.height * ratio.w / ratio.h), height: meta.height };
        }
    });

    // ── $derived: resolved selections (from the selected clip's sub-lanes) ────

    readonly selectedClip = $derived.by(() =>
        this.#selectedClipId
            ? this.clips.find(c => c.id === this.#selectedClipId) ?? null
            : null
    );

    readonly selectedZoom = $derived.by(() => {
        if (!this.#selectedZoomId) return null;
        for (const c of this.clips) {
            const r = c.zoomRegions.find(r => r.id === this.#selectedZoomId);
            if (r) return r;
        }
        return null;
    });

    readonly selectedTrim = $derived.by(() => {
        if (!this.#selectedTrimId) return null;
        for (const c of this.clips) {
            const r = c.trimRegions.find(r => r.id === this.#selectedTrimId);
            if (r) return r;
        }
        return null;
    });

    readonly selectedSpeed = $derived.by(() => {
        if (!this.#selectedSpeedId) return null;
        for (const c of this.clips) {
            const r = c.speedRegions.find(r => r.id === this.#selectedSpeedId);
            if (r) return r;
        }
        return null;
    });

    readonly selectedAnnotation = $derived.by(() => {
        if (!this.#selectedAnnotationId) return null;
        for (const c of this.clips) {
            const r = c.annotationRegions.find(r => r.id === this.#selectedAnnotationId);
            if (r) return r;
        }
        return null;
    });

    readonly selectedKeyframe = $derived.by(() => {
        if (!this.#selectedKeyframeId) return null;
        for (const c of this.clips) {
            const k = c.keyframes.find(k => k.id === this.#selectedKeyframeId);
            if (k) return k;
        }
        return null;
    });

    readonly selectedTransition = $derived.by(() =>
        this.#selectedTransitionId
            ? this.transitions.find(t => t.id === this.#selectedTransitionId) ?? null
            : null
    );

    // ── $derived: effective duration (sum minus trimmed segments across all clips) ──

    readonly effectiveDurationMs = $derived.by(() => {
        let total = this.durationMs;
        for (const c of this.clips) {
            const trimmed = c.trimRegions.reduce((sum, r) => sum + (r.endMs - r.startMs), 0);
            total -= trimmed;
        }
        return Math.max(0, total);
    });

    // ── $derived: dirty detection ──────────────────────────────────────────────

    readonly #projectHash = $derived.by(() => JSON.stringify(this.#projectData()));

    readonly hasUnsavedChanges = $derived.by(() =>
        this.projectPath !== null && this.#projectHash !== this.#savedSnapshot
    );

    // ── Page / tool navigation ────────────────────────────────────────────────

    setActivePage(page: ActivePage)         { this.activePage = page; }
    setAnnotationTool(tool: AnnotationTool) { this.annotationTool = tool; }

    // ── Timeline view ─────────────────────────────────────────────────────────

    zoomTimeline(delta: number) {
        this.timelineZoom = Math.max(0.02, Math.min(2.0, this.timelineZoom * (1 + delta * 0.1)));
    }

    scrollTimeline(ms: number) {
        this.timelineScrollMs = Math.max(0, Math.min(ms, Math.max(0, this.durationMs - 1)));
    }

    // ── Clip management ───────────────────────────────────────────────────────

    addClip(path: string, positionMs: number, durationMs: number): ClipModel {
        const clip: ClipModel = {
            id: `clip-${this.#ids.clip++}`,
            path,
            position: positionMs,
            start: 0,
            end: durationMs,
            layer: 0,
            expanded: false,
            zoomRegions:       [],
            trimRegions:       [],
            speedRegions:      [],
            annotationRegions: [],
            keyframes:         [],
        };
        this.clips = [...this.clips, clip].sort((a, b) => a.position - b.position);
        this.selectClip(clip.id);
        return clip;
    }

    removeClip(id: string) {
        this.clips = this.clips.filter(c => c.id !== id);
        if (this.#selectedClipId === id) this.#selectedClipId = null;
        this.clearSelection();
    }

    moveClip(id: string, positionMs: number) {
        this.clips = this.clips
            .map(c => c.id === id ? { ...c, position: Math.max(0, positionMs) } : c)
            .sort((a, b) => a.position - b.position);
    }

    trimClip(id: string, start: number, end: number) {
        this.clips = this.clips.map(c =>
            c.id === id ? { ...c, start: Math.max(0, start), end: Math.max(start + 1, end) } : c
        );
    }

    toggleClipExpanded(id: string) {
        this.clips = this.clips.map(c =>
            c.id === id ? { ...c, expanded: !c.expanded } : c
        );
    }

    // ── Selection (mutually exclusive) ────────────────────────────────────────

    selectClip(id: string | null) {
        this.#selectedClipId = id;
        // Don't clear region selections — keep context when clicking clips
    }

    selectZoom(id: string | null) {
        this.#selectedZoomId = id;
        if (id) this.#clearOtherSelections('zoom');
    }

    selectTrim(id: string | null) {
        this.#selectedTrimId = id;
        if (id) this.#clearOtherSelections('trim');
    }

    selectSpeed(id: string | null) {
        this.#selectedSpeedId = id;
        if (id) this.#clearOtherSelections('speed');
    }

    selectAnnotation(id: string | null) {
        this.#selectedAnnotationId = id;
        if (id) this.#clearOtherSelections('annotation');
    }

    selectKeyframe(id: string | null) {
        this.#selectedKeyframeId = id;
        if (id) this.#clearOtherSelections('keyframe');
    }

    selectSplit(id: string | null) {
        this.#selectedSplitId = id;
        if (id) this.#clearOtherSelections('split');
    }

    selectTransition(id: string | null) {
        this.#selectedTransitionId = id;
        if (id) this.#clearOtherSelections('transition');
    }

    clearSelection() {
        this.#selectedClipId = null;
        this.#selectedZoomId = null;
        this.#selectedTrimId = null;
        this.#selectedSpeedId = null;
        this.#selectedAnnotationId = null;
        this.#selectedKeyframeId = null;
        this.#selectedSplitId = null;
        this.#selectedTransitionId = null;
    }

    #clearOtherSelections(keep: string) {
        if (keep !== 'zoom')       this.#selectedZoomId = null;
        if (keep !== 'trim')       this.#selectedTrimId = null;
        if (keep !== 'speed')      this.#selectedSpeedId = null;
        if (keep !== 'annotation') this.#selectedAnnotationId = null;
        if (keep !== 'keyframe')   this.#selectedKeyframeId = null;
        if (keep !== 'split')      this.#selectedSplitId = null;
        if (keep !== 'transition') this.#selectedTransitionId = null;
    }

    // ── Helper: get clip by id (throws if not found) ──────────────────────────

    #getClip(clipId: string): ClipModel {
        const c = this.clips.find(c => c.id === clipId);
        if (!c) throw new Error(`Clip ${clipId} not found`);
        return c;
    }

    #updateClip(clipId: string, fn: (c: ClipModel) => ClipModel) {
        this.clips = this.clips.map(c => c.id === clipId ? fn(c) : c);
    }

    // ── Zoom regions ──────────────────────────────────────────────────────────

    addZoomRegion(clipId: string, startMs: number, endMs: number, cx = 0.5, cy = 0.5, depth: ZoomDepth = 3): ZoomRegion {
        const region: ZoomRegion = { id: `zoom-${this.#ids.zoom++}`, startMs, endMs, depth, cx, cy };
        this.#updateClip(clipId, c => ({ ...c, zoomRegions: [...c.zoomRegions, region] }));
        this.selectZoom(region.id);
        return region;
    }

    updateZoomRegion(id: string, patch: Partial<Omit<ZoomRegion, 'id'>>) {
        this.clips = this.clips.map(c => ({
            ...c,
            zoomRegions: c.zoomRegions.map(r => r.id === id ? { ...r, ...patch } : r),
        }));
    }

    removeZoomRegion(id: string) {
        this.clips = this.clips.map(c => ({
            ...c,
            zoomRegions: c.zoomRegions.filter(r => r.id !== id),
        }));
        if (this.#selectedZoomId === id) this.#selectedZoomId = null;
    }

    // ── Trim regions ──────────────────────────────────────────────────────────

    addTrimRegion(clipId: string, startMs: number, endMs: number): TrimRegion {
        const region: TrimRegion = { id: `trim-${this.#ids.trim++}`, startMs, endMs };
        this.#updateClip(clipId, c => ({ ...c, trimRegions: [...c.trimRegions, region] }));
        this.selectTrim(region.id);
        return region;
    }

    updateTrimRegion(id: string, patch: Partial<Omit<TrimRegion, 'id'>>) {
        this.clips = this.clips.map(c => ({
            ...c,
            trimRegions: c.trimRegions.map(r => r.id === id ? { ...r, ...patch } : r),
        }));
    }

    removeTrimRegion(id: string) {
        this.clips = this.clips.map(c => ({
            ...c,
            trimRegions: c.trimRegions.filter(r => r.id !== id),
        }));
        if (this.#selectedTrimId === id) this.#selectedTrimId = null;
    }

    // ── Speed regions ─────────────────────────────────────────────────────────

    addSpeedRegion(clipId: string, startMs: number, endMs: number, multiplier: number): SpeedRegion {
        const region: SpeedRegion = { id: `speed-${this.#ids.speed++}`, startMs, endMs, multiplier, ramping: false };
        this.#updateClip(clipId, c => ({ ...c, speedRegions: [...c.speedRegions, region] }));
        this.selectSpeed(region.id);
        return region;
    }

    updateSpeedRegion(id: string, patch: Partial<Omit<SpeedRegion, 'id'>>) {
        this.clips = this.clips.map(c => ({
            ...c,
            speedRegions: c.speedRegions.map(r => r.id === id ? { ...r, ...patch } : r),
        }));
    }

    removeSpeedRegion(id: string) {
        this.clips = this.clips.map(c => ({
            ...c,
            speedRegions: c.speedRegions.filter(r => r.id !== id),
        }));
        if (this.#selectedSpeedId === id) this.#selectedSpeedId = null;
    }

    // ── Annotation regions ────────────────────────────────────────────────────

    addAnnotation(
        clipId: string,
        startMs: number,
        endMs: number,
        kind: AnnotationKind,
        x = 0.1,
        y = 0.1,
        width = 0.3,
        height = 0.1,
    ): AnnotationRegion {
        const region: AnnotationRegion = {
            id: `annotation-${this.#ids.annotation++}`,
            startMs, endMs, kind, x, y, width, height,
            zIndex: this.#ids.annotationZ++,
            opacity: 1,
            text: kind === 'text' ? '' : undefined,
            textStyle: kind === 'text' ? { ...DEFAULT_TEXT_STYLE } : undefined,
            figureData: kind === 'arrow' ? { ...DEFAULT_FIGURE_DATA } : undefined,
        };
        this.#updateClip(clipId, c => ({ ...c, annotationRegions: [...c.annotationRegions, region] }));
        this.selectAnnotation(region.id);
        return region;
    }

    updateAnnotation(id: string, patch: Partial<Omit<AnnotationRegion, 'id' | 'zIndex'>>) {
        this.clips = this.clips.map(c => ({
            ...c,
            annotationRegions: c.annotationRegions.map(r => r.id === id ? { ...r, ...patch } : r),
        }));
    }

    removeAnnotation(id: string) {
        this.clips = this.clips.map(c => ({
            ...c,
            annotationRegions: c.annotationRegions.filter(r => r.id !== id),
        }));
        if (this.#selectedAnnotationId === id) this.#selectedAnnotationId = null;
    }

    bringAnnotationToFront(id: string) {
        this.clips = this.clips.map(c => ({
            ...c,
            annotationRegions: c.annotationRegions.map(r =>
                r.id === id ? { ...r, zIndex: this.#ids.annotationZ++ } : r
            ),
        }));
    }

    sendAnnotationToBack(id: string) {
        // Find the global minimum zIndex across all clips
        let minZ = Infinity;
        for (const c of this.clips) {
            for (const r of c.annotationRegions) minZ = Math.min(minZ, r.zIndex);
        }
        this.clips = this.clips.map(c => ({
            ...c,
            annotationRegions: c.annotationRegions.map(r =>
                r.id === id ? { ...r, zIndex: Math.max(0, minZ - 1) } : r
            ),
        }));
    }

    // ── Keyframes ─────────────────────────────────────────────────────────────

    addKeyframe(clipId: string, timeMs: number): Keyframe {
        const kf: Keyframe = { id: `kf-${this.#ids.keyframe++}`, timeMs };
        this.#updateClip(clipId, c => ({
            ...c,
            keyframes: [...c.keyframes, kf].sort((a, b) => a.timeMs - b.timeMs),
        }));
        this.selectKeyframe(kf.id);
        return kf;
    }

    removeKeyframe(id: string) {
        this.clips = this.clips.map(c => ({
            ...c,
            keyframes: c.keyframes.filter(k => k.id !== id),
        }));
        if (this.#selectedKeyframeId === id) this.#selectedKeyframeId = null;
    }

    // ── Split points (project-level) ──────────────────────────────────────────

    addSplitPoint(timeMs: number): SplitPoint {
        const sp: SplitPoint = { id: `split-${this.#ids.split++}`, timeMs };
        this.splitPoints = [...this.splitPoints, sp].sort((a, b) => a.timeMs - b.timeMs);
        this.selectSplit(sp.id);
        return sp;
    }

    removeSplitPoint(id: string) {
        this.splitPoints = this.splitPoints.filter(s => s.id !== id);
        if (this.#selectedSplitId === id) this.#selectedSplitId = null;
    }

    // ── Transitions (project-level) ───────────────────────────────────────────

    addTransition(timeMs: number, type: TransitionType, durationMs: number): Transition {
        const t: Transition = { id: `transition-${this.#ids.transition++}`, timeMs, type, durationMs };
        this.transitions = [...this.transitions, t].sort((a, b) => a.timeMs - b.timeMs);
        this.selectTransition(t.id);
        return t;
    }

    updateTransition(id: string, patch: Partial<Omit<Transition, 'id'>>) {
        this.transitions = this.transitions.map(t => t.id === id ? { ...t, ...patch } : t);
    }

    removeTransition(id: string) {
        this.transitions = this.transitions.filter(t => t.id !== id);
        if (this.#selectedTransitionId === id) this.#selectedTransitionId = null;
    }

    // ── Visual / crop ─────────────────────────────────────────────────────────

    setBackground(opts: BackgroundOptions)  { this.background = opts; }
    setVisual(opts: Partial<VisualOptions>) { this.visual = { ...this.visual, ...opts }; }
    setCrop(opts: CropOptions | null)       { this.crop = opts; }
    setColorGrade(grade: ColorGrade | null) { this.colorGrade = grade; }
    setAspectRatio(ratio: AspectRatio)      { this.aspectRatio = ratio; }

    // ── Export ────────────────────────────────────────────────────────────────

    setExportSettings(patch: Partial<ExportSettings>) {
        if (!this.exportStatus.active) {
            this.exportSettings = { ...this.exportSettings, ...patch };
        }
    }

    startExport() {
        this.exportStatus = {
            active: true,
            format: this.exportSettings.format,
            percent: 0,
            error: null,
            outputPath: null,
        };
    }

    updateExportProgress(format: ExportFormat, percent: number) {
        this.exportStatus = { ...this.exportStatus, format, percent };
    }

    completeExport(outputPath: string) {
        this.exportStatus = { ...this.exportStatus, active: false, percent: 100, outputPath };
        const filename = outputPath.split('/').pop() ?? outputPath;
        const entry: RecentExport = {
            id: `export-${Date.now()}`,
            filename,
            format: this.exportStatus.format ?? this.exportSettings.format,
            fileSizeBytes: 0,
            exportedAt: Date.now(),
            outputPath,
        };
        this.recentExports = [entry, ...this.recentExports].slice(0, 10);
    }

    failExport(error: string) {
        this.exportStatus = { ...this.exportStatus, active: false, error };
    }

    // ── Recording HUD ─────────────────────────────────────────────────────────

    showHud()  { this.hudVisible = true;  this.recordingStartedAt = Date.now(); }
    hideHud()  { this.hudVisible = false; this.recordingStartedAt = null; }

    // ── Playback ──────────────────────────────────────────────────────────────

    seekTo(timeMs: number)          { this.currentTimeMs = Math.max(0, Math.min(timeMs, this.durationMs)); }
    setPlaybackSpeed(speed: number) { this.playbackSpeed = Math.max(0.25, Math.min(speed, 2)); }
    togglePlayback()                { this.isPlaying = !this.isPlaying; }

    // ── Highlight regions (timeline animation) ────────────────────────────────

    highlightRegions(regions: { startMs: number; endMs: number; type: 'zoom' | 'trim' | 'speed' }[]) {
        this.highlightedRegions = regions;
    }

    clearHighlights() { this.highlightedRegions = []; }

    // ── Clip media metadata ───────────────────────────────────────────────────

    setClipMetadata(clipId: string, meta: MediaMetadata) {
        const next = new Map(this.clipMetadata);
        next.set(clipId, meta);
        this.clipMetadata = next;
    }

    // ── Recent projects ───────────────────────────────────────────────────────

    addRecentProject(project: RecentProject) {
        this.recentProjects = [
            project,
            ...this.recentProjects.filter(p => p.id !== project.id),
        ].slice(0, 10);
    }

    // ── Project load ──────────────────────────────────────────────────────────

    loadProject(snapshot: ProjectSnapshot) {
        this.projectId      = snapshot.projectId;
        this.projectPath    = snapshot.projectPath;
        this.sourceType     = snapshot.sourceType;
        this.aspectRatio    = snapshot.aspectRatio;
        this.clips          = snapshot.clips;
        this.splitPoints    = snapshot.splitPoints;
        this.transitions    = snapshot.transitions;
        this.background     = snapshot.background;
        this.visual         = snapshot.visual;
        this.crop           = snapshot.crop;
        this.colorGrade     = snapshot.colorGrade;
        this.exportSettings = snapshot.export;
        this.clearSelection();

        // Re-sync ID counters so new additions don't collide with loaded data
        this.#ids.clip       = this.#maxId(snapshot.clips) + 1;
        this.#ids.split      = this.#maxId(snapshot.splitPoints) + 1;
        this.#ids.transition = this.#maxId(snapshot.transitions) + 1;

        // Per-clip counters
        for (const c of snapshot.clips) {
            this.#ids.zoom       = Math.max(this.#ids.zoom,       this.#maxId(c.zoomRegions) + 1);
            this.#ids.trim       = Math.max(this.#ids.trim,       this.#maxId(c.trimRegions) + 1);
            this.#ids.speed      = Math.max(this.#ids.speed,      this.#maxId(c.speedRegions) + 1);
            this.#ids.annotation = Math.max(this.#ids.annotation, this.#maxId(c.annotationRegions) + 1);
            this.#ids.keyframe   = Math.max(this.#ids.keyframe,   this.#maxId(c.keyframes) + 1);
            this.#ids.annotationZ = Math.max(
                this.#ids.annotationZ,
                c.annotationRegions.reduce((m, a) => Math.max(m, a.zIndex), 0) + 1
            );
        }

        this.#savedSnapshot = JSON.stringify(this.#projectData());
    }

    markSaved(projectPath: string) {
        this.projectPath    = projectPath;
        this.#savedSnapshot = JSON.stringify(this.#projectData());
    }

    // ── Overlay: source picker ────────────────────────────────────────────────

    openSourcePicker(sources: RecordingSource[]): Promise<string | null> {
        this.sourcePickerSources = sources;
        this.sourcePickerOpen    = true;
        return new Promise(resolve => { this.#sourcePick.resolve = resolve; });
    }

    resolveSourcePick(sourceId: string | null) {
        this.sourcePickerOpen = false;
        this.#sourcePick.resolve?.(sourceId);
        this.#sourcePick.resolve = null;
    }

    // ── Overlay: import picker ────────────────────────────────────────────────

    openImportPicker(): Promise<string | null> {
        this.importPickerOpen = true;
        return new Promise(resolve => { this.#importPick.resolve = resolve; });
    }

    resolveImportPick(filePath: string | null) {
        this.importPickerOpen = false;
        this.#importPick.resolve?.(filePath);
        this.#importPick.resolve = null;
    }

    // ── Overlay: unsaved changes dialog ──────────────────────────────────────

    openUnsavedChangesDialog(): Promise<'discard' | 'cancel'> {
        this.unsavedChangesOpen = true;
        return new Promise(resolve => { this.#unsavedPick.resolve = resolve; });
    }

    resolveUnsavedChanges(choice: 'discard' | 'cancel') {
        this.unsavedChangesOpen = false;
        this.#unsavedPick.resolve?.(choice);
        this.#unsavedPick.resolve = null;
    }

    // ── Overlay: font picker / shortcut config ────────────────────────────────

    openFontPicker()      { this.fontPickerOpen = true; }
    closeFontPicker()     { this.fontPickerOpen = false; }
    openShortcutConfig()  { this.shortcutConfigOpen = true; }
    closeShortcutConfig() { this.shortcutConfigOpen = false; }

    // ── onClientToolInvoke handler ────────────────────────────────────────────

    handleClientTool(
        request: { requestId: string; toolName: string; arguments: Record<string, unknown> },
    ): Promise<{ requestId: string; content: unknown; success: boolean }> | null {
        switch (request.toolName) {

            case 'show_source_picker': {
                const { sources } = request.arguments as { sources: RecordingSource[] };
                return this.openSourcePicker(sources).then(sourceId =>
                    sourceId
                        ? createSuccessResponse(request.requestId, sourceId)
                        : createErrorResponse(request.requestId, 'User cancelled source selection.')
                ) as any;
            }

            case 'show_recording_hud': {
                this.showHud();
                return Promise.resolve(createSuccessResponse(request.requestId, 'Recording HUD shown.') as any);
            }

            case 'hide_recording_hud': {
                this.hideHud();
                return Promise.resolve(createSuccessResponse(request.requestId, 'Recording HUD hidden.') as any);
            }

            case 'seek_preview': {
                const { timeMs } = request.arguments as { timeMs: number };
                this.seekTo(timeMs);
                return Promise.resolve(createSuccessResponse(request.requestId, `Seeked to ${timeMs}ms.`) as any);
            }

            case 'set_playback_speed': {
                const { speed } = request.arguments as { speed: number };
                this.setPlaybackSpeed(speed);
                return Promise.resolve(createSuccessResponse(request.requestId, `Speed set to ${speed}x.`) as any);
            }

            case 'highlight_regions': {
                const { regions } = request.arguments as { regions: { startMs: number; endMs: number; type: 'zoom' | 'trim' | 'speed' }[] };
                this.highlightRegions(regions);
                return Promise.resolve(createSuccessResponse(request.requestId, `Highlighted ${regions.length} regions.`) as any);
            }

            case 'show_export_progress': {
                const { format, percent } = request.arguments as { format: ExportFormat; percent: number };
                this.updateExportProgress(format, percent);
                return Promise.resolve(createSuccessResponse(request.requestId, `Export ${percent}%.`) as any);
            }

            case 'show_export_complete': {
                const { filePath } = request.arguments as { filePath: string; format: ExportFormat };
                this.completeExport(filePath);
                return Promise.resolve(createSuccessResponse(request.requestId, `Export complete: ${filePath}`) as any);
            }

            case 'show_import_picker': {
                return this.openImportPicker().then(filePath =>
                    filePath
                        ? createSuccessResponse(request.requestId, filePath)
                        : createErrorResponse(request.requestId, 'User cancelled import.')
                ) as any;
            }

            case 'show_unsaved_changes_dialog': {
                return this.openUnsavedChangesDialog().then(choice =>
                    createSuccessResponse(request.requestId, choice)
                ) as any;
            }

            case 'show_font_picker': {
                this.openFontPicker();
                return Promise.resolve(createSuccessResponse(request.requestId, 'Font picker opened.') as any);
            }

            case 'show_shortcut_config': {
                this.openShortcutConfig();
                return Promise.resolve(createSuccessResponse(request.requestId, 'Shortcut config opened.') as any);
            }

            default:
                return null;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    #projectData(): ProjectSnapshot {
        return {
            projectId:   this.projectId,
            projectPath: this.projectPath,
            sourceType:  this.sourceType,
            aspectRatio: this.aspectRatio,
            clips:       this.clips,
            splitPoints: this.splitPoints,
            transitions: this.transitions,
            background:  this.background,
            visual:      this.visual,
            crop:        this.crop,
            colorGrade:  this.colorGrade,
            export:      this.exportSettings,
        };
    }

    #maxId(items: { id: string }[]): number {
        return items.reduce((max, item) => {
            const n = parseInt(item.id.split('-').pop() ?? '0', 10);
            return isNaN(n) ? max : Math.max(max, n);
        }, 0);
    }

    // ── Dev mode ──────────────────────────────────────────────────────────────
    // Seeds the editor with fake clips + regions so the UI is fully explorable
    // without importing a real video file. Call once after construction.

    seedDev() {
        const DUR_A = 10_000; // 10s
        const DUR_B = 8_000;  // 8s

        const clipA: ClipModel = {
            id: 'dev-clip-1',
            path: '',
            position: 0,
            start: 0,
            end: DUR_A,
            layer: 0,
            expanded: true,
            zoomRegions: [
                { id: 'dev-z1', startMs: 1000, endMs: 4000, depth: 2, cx: 0.5, cy: 0.45 },
            ],
            trimRegions: [
                { id: 'dev-t1', startMs: 7500, endMs: 9000 },
            ],
            speedRegions: [
                { id: 'dev-s1', startMs: 4500, endMs: 6500, multiplier: 2, ramping: false },
            ],
            annotationRegions: [
                { id: 'dev-a1', startMs: 1500, endMs: 3500, kind: 'text', text: 'Hello world', x: 0.2, y: 0.15, width: 0.3, height: 0.12, zIndex: 1, opacity: 1, textStyle: { fontSize: 24, color: '#ffffff', backgroundColor: 'transparent', fontFamily: 'sans-serif', fontWeight: 'normal', fontStyle: 'normal', textDecoration: 'none', textAlign: 'left' } },
            ],
            keyframes: [
                { id: 'dev-k1', timeMs: 2000 },
                { id: 'dev-k2', timeMs: 5000 },
            ],
        };

        const clipB: ClipModel = {
            id: 'dev-clip-2',
            path: '',
            position: DUR_A + 500,
            start: 0,
            end: DUR_B,
            layer: 0,
            expanded: false,
            zoomRegions: [],
            trimRegions: [],
            speedRegions: [],
            annotationRegions: [],
            keyframes: [],
        };

        this.clips = [clipA, clipB];
        this.clipMetadata = new Map([
            ['dev-clip-1', { width: 1920, height: 1080, fps: 60, fileSizeBytes: 0 }],
            ['dev-clip-2', { width: 1280, height: 720,  fps: 30, fileSizeBytes: 0 }],
        ]);
        this.activePage = 'edit';
        this.#ids.clip       = 10;
        this.#ids.zoom       = 10;
        this.#ids.trim       = 10;
        this.#ids.speed      = 10;
        this.#ids.annotation = 10;
        this.#ids.keyframe   = 10;
    }
}
