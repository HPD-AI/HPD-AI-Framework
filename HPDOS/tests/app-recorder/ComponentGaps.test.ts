/**
 * Component gap tests — Groups 18–20 (#86–96).
 * ExportPage rendering gaps, PageNav active class + annotate tab,
 * RecordingHud, SourcePickerOverlay.
 */
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import { userEvent } from '@testing-library/user-event';
import { AppRecorderState } from '../../src/lib/apps/app-recorder/AppRecorderState.svelte';
import ExportPage from '../../src/lib/apps/app-recorder/pages/ExportPage.svelte';
import PageNav from '../../src/lib/apps/app-recorder/PageNav.svelte';
import RecordingHud from '../../src/lib/apps/app-recorder/overlays/RecordingHud.svelte';
import SourcePickerOverlay from '../../src/lib/apps/app-recorder/overlays/SourcePickerOverlay.svelte';

function makeEditor() {
    return new AppRecorderState();
}

// ── Group 18: ExportPage rendering gaps ──────────────────────────────────────

describe('Group 18 — ExportPage rendering gaps', () => {

    // #86
    it('quality card shown when format=mp4', () => {
        const editor = makeEditor();
        editor.setExportSettings({ format: 'mp4' });
        render(ExportPage, { props: { editor } });
        // Quality options: Medium / High / Source
        expect(screen.getByRole('button', { name: /^medium$/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /^high$/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /^source$/i })).toBeInTheDocument();
    });

    // #87
    it('all five aspect ratio chips are present', () => {
        const editor = makeEditor();
        render(ExportPage, { props: { editor } });
        expect(screen.getByRole('button', { name: /^16:9$/ })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /^4:3$/ })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /^1:1$/ })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /^9:16$/ })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /^21:9$/ })).toBeInTheDocument();
    });

    // #88
    it('clicking quality chip Medium updates exportSettings.quality', async () => {
        const editor = makeEditor();
        editor.setExportSettings({ format: 'mp4', quality: 'good' });
        render(ExportPage, { props: { editor } });
        const user = userEvent.setup();
        await user.click(screen.getByRole('button', { name: /^medium$/i }));
        expect(editor.exportSettings.quality).toBe('medium');
    });

    // #89
    it('no videoPath shows no-video hint text', () => {
        const editor = makeEditor();
        editor.videoPath = null;
        render(ExportPage, { props: { editor } });
        expect(screen.getByText(/import or record a video first/i)).toBeInTheDocument();
    });

    // #90
    it('videoPath set shows a video element with the src', () => {
        const editor = makeEditor();
        editor.videoPath = '/path/to/video.mp4';
        render(ExportPage, { props: { editor } });
        const video = document.querySelector('video');
        expect(video).toBeInTheDocument();
        expect(video?.getAttribute('src')).toBe('/path/to/video.mp4');
    });
});

// ── Group 19: PageNav active class + annotate tab ────────────────────────────

describe('Group 19 — PageNav active class and annotate tab', () => {

    // #91
    it('active tab has CSS class "active"', () => {
        const editor = makeEditor();
        editor.setActivePage('export');
        render(PageNav, { props: { editor } });
        const exportTab = screen.getByRole('tab', { name: /export/i });
        expect(exportTab).toHaveClass('active');
    });

    // #92
    it('clicking Annotate tab sets activePage to annotate', async () => {
        const editor = makeEditor();
        render(PageNav, { props: { editor } });
        const user = userEvent.setup();
        await user.click(screen.getByRole('tab', { name: /annotate/i }));
        expect(editor.activePage).toBe('annotate');
    });
});

// ── Group 20: RecordingHud ────────────────────────────────────────────────────

describe('Group 20 — RecordingHud', () => {

    // #93
    it('renders timer element when startedAt is set', () => {
        render(RecordingHud, { props: { startedAt: Date.now() } });
        expect(document.querySelector('.timer')).toBeInTheDocument();
    });

    // #94
    it('renders hud with null startedAt (initial state)', () => {
        render(RecordingHud, { props: { startedAt: null } });
        expect(document.querySelector('.hud')).toBeInTheDocument();
    });
});

// ── Group 21: SourcePickerOverlay ─────────────────────────────────────────────

describe('Group 21 — SourcePickerOverlay', () => {

    const sources = [
        { id: 'src-1', name: 'Screen 1', type: 'screen' as const },
        { id: 'src-2', name: 'Window A', type: 'window' as const },
    ];

    // #95
    it('renders two source items', () => {
        render(SourcePickerOverlay, {
            props: { sources, onpick: vi.fn(), oncancel: vi.fn() },
        });
        expect(screen.getByText('Screen 1')).toBeInTheDocument();
        expect(screen.getByText('Window A')).toBeInTheDocument();
    });

    // #96
    it('clicking a source calls onpick with its id', async () => {
        const onpick = vi.fn();
        render(SourcePickerOverlay, {
            props: { sources, onpick, oncancel: vi.fn() },
        });
        const user = userEvent.setup();
        await user.click(screen.getByText('Screen 1'));
        expect(onpick).toHaveBeenCalledWith('src-1');
    });

    // #97
    it('clicking Cancel button calls oncancel', async () => {
        const oncancel = vi.fn();
        render(SourcePickerOverlay, {
            props: { sources, onpick: vi.fn(), oncancel },
        });
        const user = userEvent.setup();
        await user.click(screen.getByRole('button', { name: /cancel/i }));
        expect(oncancel).toHaveBeenCalled();
    });
});
