import { createExpandedToolKit } from '@hpd/hpd-agent-headless-ui';
import { isHybridWebView } from '../../ipc/bridge';

// ── Tool definitions ───────────────────────────────────────────────────────

const SHOW_SOURCE_PICKER = {
    name: 'show_source_picker',
    description: 'Opens the source picker so the user can choose a screen or window to record. Returns the sourceId chosen.',
    parametersSchema: {
        type: 'object',
        properties: {
            sources: {
                type: 'array',
                description: 'List of recording sources from ListSources.',
                items: {
                    type: 'object',
                    properties: {
                        id:   { type: 'string' },
                        name: { type: 'string' },
                        type: { type: 'string', enum: ['screen', 'window'] },
                    },
                    required: ['id', 'name', 'type'],
                },
            },
        },
        required: ['sources'],
    },
} as const;

const SHOW_RECORDING_HUD = {
    name: 'show_recording_hud',
    description: 'Shows the floating recording HUD overlay with a live timer and a stop button.',
    parametersSchema: { type: 'object', properties: {}, required: [] },
} as const;

const HIDE_RECORDING_HUD = {
    name: 'hide_recording_hud',
    description: 'Dismisses the recording HUD after the recording has stopped.',
    parametersSchema: { type: 'object', properties: {}, required: [] },
} as const;

const SEEK_PREVIEW = {
    name: 'seek_preview',
    description: 'Scrubs the canvas preview to the given timestamp.',
    parametersSchema: {
        type: 'object',
        properties: {
            timeMs: { type: 'number', description: 'Timestamp in milliseconds to seek to.' },
        },
        required: ['timeMs'],
    },
} as const;

const SET_PLAYBACK_SPEED = {
    name: 'set_playback_speed',
    description: 'Sets the preview playback rate (0.25x–2x). Does not add a speed region — this controls the preview only.',
    parametersSchema: {
        type: 'object',
        properties: {
            speed: { type: 'number', description: 'Playback speed multiplier (0.25–2.0).' },
        },
        required: ['speed'],
    },
} as const;

const HIGHLIGHT_REGIONS = {
    name: 'highlight_regions',
    description: 'Animates the timeline to draw attention to suggested zoom, trim, or speed regions.',
    parametersSchema: {
        type: 'object',
        properties: {
            regions: {
                type: 'array',
                description: 'Regions to highlight.',
                items: {
                    type: 'object',
                    properties: {
                        startMs: { type: 'number' },
                        endMs:   { type: 'number' },
                        type:    { type: 'string', enum: ['zoom', 'trim', 'speed'] },
                    },
                    required: ['startMs', 'endMs', 'type'],
                },
            },
        },
        required: ['regions'],
    },
} as const;

const SHOW_EXPORT_PROGRESS = {
    name: 'show_export_progress',
    description: 'Updates the export progress UI.',
    parametersSchema: {
        type: 'object',
        properties: {
            format:  { type: 'string', enum: ['mp4', 'gif'], description: 'Export format.' },
            percent: { type: 'number', description: 'Progress 0–100.' },
        },
        required: ['format', 'percent'],
    },
} as const;

const SHOW_EXPORT_COMPLETE = {
    name: 'show_export_complete',
    description: 'Shows the export complete toast with a reveal-in-finder button.',
    parametersSchema: {
        type: 'object',
        properties: {
            filePath: { type: 'string', description: 'Absolute path to the exported file.' },
            format:   { type: 'string', enum: ['mp4', 'gif'], description: 'Export format.' },
        },
        required: ['filePath', 'format'],
    },
} as const;

const SHOW_IMPORT_PICKER = {
    name: 'show_import_picker',
    description: 'Opens a file picker so the user can import an existing video file. Available in web mode.',
    parametersSchema: { type: 'object', properties: {}, required: [] },
} as const;

const SHOW_UNSAVED_CHANGES_DIALOG = {
    name: 'show_unsaved_changes_dialog',
    description: 'Warns the user about unsaved changes before closing or loading a new project.',
    parametersSchema: { type: 'object', properties: {}, required: [] },
} as const;

const SHOW_FONT_PICKER = {
    name: 'show_font_picker',
    description: 'Opens the Google Fonts import UI for use with annotation text.',
    parametersSchema: { type: 'object', properties: {}, required: [] },
} as const;

const SHOW_SHORTCUT_CONFIG = {
    name: 'show_shortcut_config',
    description: 'Opens the keyboard shortcut configuration panel.',
    parametersSchema: { type: 'object', properties: {}, required: [] },
} as const;

// ── Native-only tools (recording tools) ──────────────────────────────────

const NATIVE_TOOLS = [
    SHOW_SOURCE_PICKER,
    SHOW_RECORDING_HUD,
    HIDE_RECORDING_HUD,
];

// ── All tools (shared) ────────────────────────────────────────────────────

const SHARED_TOOLS = [
    SEEK_PREVIEW,
    SET_PLAYBACK_SPEED,
    HIGHLIGHT_REGIONS,
    SHOW_EXPORT_PROGRESS,
    SHOW_EXPORT_COMPLETE,
    SHOW_IMPORT_PICKER,
    SHOW_UNSAVED_CHANGES_DIALOG,
    SHOW_FONT_PICKER,
    SHOW_SHORTCUT_CONFIG,
];

// ── buildAppRecorderToolKit ───────────────────────────────────────────────

/**
 * Task 26 — builds the client toolkit based on runtime capabilities.
 * Native (MAUI): full recording + editing toolkit.
 * Web: edit-only toolkit — recording tools omitted.
 */
export function buildAppRecorderToolKit() {
    const tools = isHybridWebView()
        ? [...NATIVE_TOOLS, ...SHARED_TOOLS]
        : SHARED_TOOLS;

    return createExpandedToolKit('app-recorder', tools, {
        systemPrompt: isHybridWebView()
            ? `You have access to HPD Video client tools for recording, editing, and exporting video.
Recording tools (native only): show_source_picker, show_recording_hud, hide_recording_hud.
Editing tools: seek_preview, set_playback_speed, highlight_regions.
Export tools: show_export_progress, show_export_complete.
Other: show_import_picker, show_unsaved_changes_dialog, show_font_picker, show_shortcut_config.`
            : `You have access to HPD Video client tools for editing and exporting video.
Recording is not available in web mode — use show_import_picker to let the user import an existing video.
Available: seek_preview, set_playback_speed, highlight_regions, show_export_progress, show_export_complete,
show_import_picker, show_unsaved_changes_dialog, show_font_picker, show_shortcut_config.`,
    });
}
