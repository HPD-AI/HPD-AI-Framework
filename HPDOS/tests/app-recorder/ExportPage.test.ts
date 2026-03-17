/**
 * ExportPage component — Group 10 (component tests, #56–66).
 */
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import { userEvent } from '@testing-library/user-event';
import { AppRecorderState } from '../../src/lib/apps/app-recorder/AppRecorderState.svelte';
import ExportPage from '../../src/lib/apps/app-recorder/pages/ExportPage.svelte';

function makeEditor() {
    return new AppRecorderState();
}

describe('Group 10 — ExportPage component', () => {

    // #56
    it('renders format chips MP4 and GIF', () => {
        const editor = makeEditor();
        render(ExportPage, { props: { editor } });
        expect(screen.getByRole('button', { name: /^MP4$/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /^GIF$/i })).toBeInTheDocument();
    });

    // #57
    it('MP4 chip has class active when format=mp4', () => {
        const editor = makeEditor();
        editor.setExportSettings({ format: 'mp4' });
        render(ExportPage, { props: { editor } });
        expect(screen.getByRole('button', { name: /^MP4$/i })).toHaveClass('active');
    });

    // #58
    it('quality card hidden when format=gif', () => {
        const editor = makeEditor();
        editor.setExportSettings({ format: 'gif' });
        render(ExportPage, { props: { editor } });
        // Quality options (Medium/High/Source) should not be present
        expect(screen.queryByRole('button', { name: /^high$/i })).not.toBeInTheDocument();
    });

    // #59
    it('GIF options card shown when format=gif', () => {
        const editor = makeEditor();
        editor.setExportSettings({ format: 'gif' });
        render(ExportPage, { props: { editor } });
        // GIF fps options should appear
        expect(screen.getByRole('button', { name: /15 fps/i })).toBeInTheDocument();
    });

    // #60
    it('clicking GIF chip calls setExportSettings with format=gif', async () => {
        const editor = makeEditor();
        render(ExportPage, { props: { editor } });
        const user = userEvent.setup();
        await user.click(screen.getByRole('button', { name: /^GIF$/i }));
        expect(editor.exportSettings.format).toBe('gif');
    });

    // #61
    it('export button disabled when no videoPath', () => {
        const editor = makeEditor();
        editor.videoPath = null as any;
        render(ExportPage, { props: { editor } });
        const btn = screen.getByRole('button', { name: /export/i });
        expect(btn).toBeDisabled();
    });

    // #62
    it('export button disabled during active export', () => {
        const editor = makeEditor();
        editor.videoPath = '/video.mp4';
        editor.startExport();
        render(ExportPage, { props: { editor } });
        const btn = screen.getByRole('button', { name: /export/i });
        expect(btn).toBeDisabled();
    });

    // #63
    it('export button shows spinner during active export', () => {
        const editor = makeEditor();
        editor.videoPath = '/video.mp4';
        editor.startExport();
        render(ExportPage, { props: { editor } });
        expect(document.querySelector('.spinner')).toBeInTheDocument();
    });

    // #64
    it('resolution hint updates when aspectRatio changes to 1:1', () => {
        const editor = makeEditor();
        editor.setAspectRatio('1:1');
        render(ExportPage, { props: { editor } });
        const hint = document.querySelector('.resolution-hint');
        expect(hint?.textContent).toMatch(/1920\s*×\s*1920/);
    });

    // #65
    it('recent exports list shows empty hint when empty', () => {
        const editor = makeEditor();
        editor.recentExports = [];
        render(ExportPage, { props: { editor } });
        expect(screen.getByText(/no exports yet/i)).toBeInTheDocument();
    });

    // #66
    it('recent exports list renders rows for each entry', () => {
        const editor = makeEditor();
        // Directly set recentExports to avoid Date.now() duplicate key issue in tests
        editor.recentExports = [
            { id: 'export-1', filename: 'out1.mp4', format: 'mp4', fileSizeBytes: 0, exportedAt: Date.now() - 2000, outputPath: '/out1.mp4' },
            { id: 'export-2', filename: 'out2.mp4', format: 'mp4', fileSizeBytes: 0, exportedAt: Date.now() - 1000, outputPath: '/out2.mp4' },
        ];
        render(ExportPage, { props: { editor } });
        expect(screen.getByText(/out1\.mp4/)).toBeInTheDocument();
        expect(screen.getByText(/out2\.mp4/)).toBeInTheDocument();
    });
});
