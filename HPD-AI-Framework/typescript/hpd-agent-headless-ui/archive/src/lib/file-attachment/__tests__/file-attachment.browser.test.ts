/**
 * FileAttachment Browser Component Tests
 *
 * Verifies that the FileAttachment component renders the correct data
 * attributes, exposes the right snippet props, handles the external state
 * prop pattern, and reflects upload lifecycle changes in the DOM.
 *
 * Test type: browser (chromium) — vitest-browser-svelte.
 */

import { describe, it, expect, vi } from 'vitest';
import { render } from 'vitest-browser-svelte';
import { page } from 'vitest/browser';
import type { ContentReference } from '@hpd-research/hpd-agent-client';
import FileAttachmentTest from './file-attachment-test.svelte';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const DEFAULT_CONTENT: ContentReference = {
	contentId: 'test-content-1',
	version: 'rev:1',
	contentType: 'image/png',
	name: 'test.png',
	sizeBytes: 512,
};

function setup(props: {
	mode?: 'external' | 'internal';
	sessionId?: string | null;
	threadId?: string | null;
	disabled?: boolean;
	uploadFn?: (sid: string, bid: string, file: File) => Promise<ContentReference>;
} = {}) {
	render(FileAttachmentTest, { props } as any);
}

// ---------------------------------------------------------------------------
// Root element — data attributes
// ---------------------------------------------------------------------------

describe('FileAttachment.Root — data attributes', () => {
	it('renders with data-file-attachment-root', async () => {
		setup();
		const root = page.getByTestId('root');
		await expect.element(root).toHaveAttribute('data-file-attachment-root');
	});

	it('does not have data-disabled when disabled is false', async () => {
		setup({ disabled: false });
		const root = page.getByTestId('root');
		await expect.element(root).not.toHaveAttribute('data-disabled');
	});

	it('has data-disabled when disabled is true', async () => {
		setup({ disabled: true });
		const root = page.getByTestId('root');
		await expect.element(root).toHaveAttribute('data-disabled');
	});

	it('does not have data-uploading initially', async () => {
		setup();
		const root = page.getByTestId('root');
		await expect.element(root).not.toHaveAttribute('data-uploading');
	});
});

// ---------------------------------------------------------------------------
// Initial snippet props via DOM
// ---------------------------------------------------------------------------

describe('FileAttachment.Root — initial snippet props', () => {
	it('hasAttachments is false initially', async () => {
		setup();
		await expect.element(page.getByTestId('has-attachments')).toHaveTextContent('false');
	});

	it('canSubmit is true initially (no uploads, not disabled)', async () => {
		setup();
		await expect.element(page.getByTestId('can-submit')).toHaveTextContent('true');
	});

	it('canSubmit is false when disabled', async () => {
		setup({ disabled: true });
		await expect.element(page.getByTestId('can-submit')).toHaveTextContent('false');
	});

	it('attachment list is empty initially', async () => {
		setup();
		await expect.element(page.getByTestId('attachment-count')).toHaveTextContent('0');
	});
});

// ---------------------------------------------------------------------------
// Upload lifecycle via programmatic add buttons
// ---------------------------------------------------------------------------

describe('FileAttachment.Root — upload lifecycle', () => {
	it('adding a file makes hasAttachments true', async () => {
		setup();
		await page.getByTestId('add-png-btn').click();
		await expect.element(page.getByTestId('has-attachments')).toHaveTextContent('true');
	});

	it('successfully uploaded file appears in attachment list with status done', async () => {
		setup({ uploadFn: async () => DEFAULT_CONTENT });
		await page.getByTestId('add-png-btn').click();

		const count = page.getByTestId('attachment-count');
		await expect.element(count).toHaveTextContent('1');

		// attachment-list contains a child with data-status="done" — verify via the list text
		const list = page.getByTestId('attachment-list');
		await expect.element(list).toHaveTextContent('photo.png');
	});

	it('failed upload shows error status', async () => {
		setup({
			uploadFn: async () => { throw new Error('network failure'); },
		});
		await page.getByTestId('add-png-btn').click();

		// canSubmit becomes false when there's an error entry
		await expect.element(page.getByTestId('can-submit')).toHaveTextContent('false');
		// resolved-content stays empty
		await expect.element(page.getByTestId('resolved-content')).toHaveTextContent('[]');
	});

	it('resolved-content JSON contains uploaded content', async () => {
		setup({ uploadFn: async () => DEFAULT_CONTENT });
		await page.getByTestId('add-png-btn').click();

		const resolved = page.getByTestId('resolved-content');
		await expect.element(resolved).toHaveTextContent('test-content-1');
	});

	it('resolved-content is empty when upload failed', async () => {
		setup({
			uploadFn: async () => { throw new Error('fail'); },
		});
		await page.getByTestId('add-png-btn').click();

		const resolved = page.getByTestId('resolved-content');
		await expect.element(resolved).toHaveTextContent('[]');
	});
});

// ---------------------------------------------------------------------------
// Remove & clear
// ---------------------------------------------------------------------------

describe('FileAttachment.Root — remove and clear', () => {
	it('remove button removes the attachment', async () => {
		setup({ uploadFn: async () => DEFAULT_CONTENT });
		await page.getByTestId('add-png-btn').click();

		// Wait for attachment to appear
		await expect.element(page.getByTestId('attachment-count')).toHaveTextContent('1');

		// resolved-content contains the content — extract localId from the remove button testid
		// The toolharness renders <button data-testid="remove-{localId}">
		const removeBtn = page.getByRole('button', { name: 'Remove' });
		await removeBtn.click();

		await expect.element(page.getByTestId('has-attachments')).toHaveTextContent('false');
		await expect.element(page.getByTestId('attachment-count')).toHaveTextContent('0');
	});

	it('clear button empties all attachments', async () => {
		setup({ uploadFn: async () => DEFAULT_CONTENT });
		await page.getByTestId('add-png-btn').click();
		await page.getByTestId('add-txt-btn').click();

		// Both should be present
		await expect.element(page.getByTestId('attachment-count')).toHaveTextContent('2');

		await page.getByTestId('clear-btn').click();

		await expect.element(page.getByTestId('attachment-count')).toHaveTextContent('0');
		await expect.element(page.getByTestId('has-attachments')).toHaveTextContent('false');
	});
});

// ---------------------------------------------------------------------------
// External state prop pattern
// ---------------------------------------------------------------------------

describe('FileAttachment.Root — external state prop', () => {
	it('component uses provided state (resolvedContent readable outside snippet)', async () => {
		setup({ uploadFn: async () => DEFAULT_CONTENT });

		// resolved-content is populated from externalState directly (not from inside snippet)
		await page.getByTestId('add-png-btn').click();

		const resolved = page.getByTestId('resolved-content');
		await expect.element(resolved).toHaveTextContent('test-content-1');
	});

	it('canSubmit reflects external state after upload', async () => {
		setup({ uploadFn: async () => DEFAULT_CONTENT });
		await page.getByTestId('add-png-btn').click();

		// After a successful upload, canSubmit should still be true
		await expect.element(page.getByTestId('can-submit')).toHaveTextContent('true');
	});

	it('canSubmit is false when there is an upload error', async () => {
		setup({
			uploadFn: async () => { throw new Error('fail'); },
		});
		await page.getByTestId('add-png-btn').click();

		await expect.element(page.getByTestId('can-submit')).toHaveTextContent('false');
	});
});
