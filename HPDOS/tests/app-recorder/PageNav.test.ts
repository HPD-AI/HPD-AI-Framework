/**
 * PageNav component — Group 9 (component tests, #51–55).
 */
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import { userEvent } from '@testing-library/user-event';
import { AppRecorderState } from '../../src/lib/apps/app-recorder/AppRecorderState.svelte';
import PageNav from '../../src/lib/apps/app-recorder/PageNav.svelte';

function makeEditor() {
    return new AppRecorderState();
}

describe('Group 9 — PageNav component', () => {

    // #51
    it('renders 6 tabs', () => {
        const editor = makeEditor();
        render(PageNav, { props: { editor } });
        expect(screen.getAllByRole('tab')).toHaveLength(6);
    });

    // #52
    it('active tab has aria-selected=true', async () => {
        const editor = makeEditor();
        editor.setActivePage('export');
        render(PageNav, { props: { editor } });
        const exportTab = screen.getByRole('tab', { name: /export/i });
        expect(exportTab).toHaveAttribute('aria-selected', 'true');
        const editTab = screen.getByRole('tab', { name: /^edit$/i });
        expect(editTab).toHaveAttribute('aria-selected', 'false');
    });

    // #53
    it('clicking Edit tab calls setActivePage("edit")', async () => {
        const editor = makeEditor();
        editor.setActivePage('export');
        render(PageNav, { props: { editor } });
        const user = userEvent.setup();
        await user.click(screen.getByRole('tab', { name: /^edit$/i }));
        expect(editor.activePage).toBe('edit');
    });

    // #54
    it('clicking Audio tab does nothing (coming soon)', async () => {
        const editor = makeEditor();
        render(PageNav, { props: { editor } });
        const user = userEvent.setup();
        await user.click(screen.getByRole('tab', { name: /audio/i }));
        expect(editor.activePage).toBe('media'); // unchanged (default is now 'media')
    });

    // #55
    it('coming-soon tabs have aria-disabled=true', () => {
        const editor = makeEditor();
        render(PageNav, { props: { editor } });
        const audioTab = screen.getByRole('tab', { name: /audio/i });
        const colorTab = screen.getByRole('tab', { name: /color/i });
        expect(audioTab).toHaveAttribute('aria-disabled', 'true');
        expect(colorTab).toHaveAttribute('aria-disabled', 'true');
    });
});
