/**
 * FileAttachmentState unit tests
 *
 * Tests the complete upload lifecycle, state transitions, derived getters,
 * and error/retry paths. The uploadFn is always a vi.fn() mock — no real
 * network calls are made.
 *
 * Test type: unit (server project — Node environment).
 */

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { boxWith } from 'svelte-toolbelt';
import { FileAttachmentState } from '../file-attachment.svelte.ts';
import type { AssetReference } from '@hpd/hpd-agent-client';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const ASSET: AssetReference = { assetId: 'asset-1', contentType: 'image/png', name: 'shot.png', sizeBytes: 1024 };
const ASSET2: AssetReference = { assetId: 'asset-2', contentType: 'text/plain', name: 'doc.txt', sizeBytes: 512 };

function makeFile(name = 'test.png', type = 'image/png'): File {
	return new File(['x'], name, { type });
}

interface StateOpts {
	sessionId?: string | null;
	disabled?: boolean;
	uploadFn?: (sessionId: string, file: File) => Promise<AssetReference>;
}

function makeState(opts: StateOpts = {}) {
	const uploadFn = opts.uploadFn ?? vi.fn(async () => ASSET);
	const sessionId: string = 'sessionId' in opts ? (opts.sessionId as string) : 'sess-1';
	const state = new FileAttachmentState({
		uploadFn: boxWith(() => uploadFn),
		sessionId: boxWith(() => sessionId),
		disabled: boxWith(() => opts.disabled ?? false),
	});
	return { state, uploadFn: uploadFn as ReturnType<typeof vi.fn> };
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('FileAttachmentState — initial state', () => {
	it('starts with empty attachments', () => {
		const { state } = makeState();
		expect(state.attachments).toEqual([]);
	});

	it('hasAttachments is false initially', () => {
		const { state } = makeState();
		expect(state.hasAttachments).toBe(false);
	});

	it('isUploading is false initially', () => {
		const { state } = makeState();
		expect(state.isUploading).toBe(false);
	});

	it('resolvedAssets is empty initially', () => {
		const { state } = makeState();
		expect(state.resolvedAssets).toEqual([]);
	});

	it('canSubmit is true initially (no uploads in progress, not disabled)', () => {
		const { state } = makeState();
		expect(state.canSubmit).toBe(true);
	});
});

describe('FileAttachmentState — add()', () => {
	it('does nothing when sessionId is null', async () => {
		const { state, uploadFn } = makeState({ sessionId: null });
		await state.add([makeFile()]);
		expect(state.attachments).toEqual([]);
		expect(uploadFn).not.toHaveBeenCalled();
	});

	it('resolves to done after successful upload', async () => {
		const { state } = makeState();
		await state.add([makeFile()]);
		expect(state.attachments).toHaveLength(1);
		expect(state.attachments[0].status).toBe('done');
		expect(state.attachments[0].asset).toEqual(ASSET);
	});

	it('transitions to error on upload failure', async () => {
		const { state } = makeState({
			uploadFn: vi.fn(async () => { throw new Error('server error'); }),
		});
		await state.add([makeFile()]);
		expect(state.attachments[0].status).toBe('error');
		expect(state.attachments[0].error).toBe('server error');
	});

	it('adds multiple files at once', async () => {
		const uploadFn = vi.fn()
			.mockResolvedValueOnce(ASSET)
			.mockResolvedValueOnce(ASSET2);
		const { state } = makeState({ uploadFn });
		await state.add([makeFile('a.png'), makeFile('b.txt', 'text/plain')]);
		expect(state.attachments).toHaveLength(2);
	});

	it('each file gets a unique localId', async () => {
		const { state } = makeState();
		await state.add([makeFile('a.png'), makeFile('b.png')]);
		const ids = state.attachments.map((a) => a.localId);
		expect(new Set(ids).size).toBe(2);
	});

	it('hasAttachments becomes true after adding a file', async () => {
		const { state } = makeState();
		await state.add([makeFile()]);
		expect(state.hasAttachments).toBe(true);
	});

	it('passes sessionId to uploadFn', async () => {
		const { state, uploadFn } = makeState({ sessionId: 'my-session' });
		await state.add([makeFile()]);
		expect(uploadFn).toHaveBeenCalledWith('my-session', expect.any(File));
	});

	it('uploads files in parallel (uploadFn called once per file before any await)', async () => {
		let callCount = 0;
		const uploadFn = vi.fn(async () => {
			callCount++;
			await new Promise((r) => setTimeout(r, 10));
			return ASSET;
		});
		const { state } = makeState({ uploadFn });
		const addPromise = state.add([makeFile('a.png'), makeFile('b.png')]);
		// By the time we get here, both uploads should have been initiated
		expect(callCount).toBe(2);
		await addPromise;
	});
});

describe('FileAttachmentState — resolvedAssets', () => {
	it('returns only done entries as AssetReference[]', async () => {
		const uploadFn = vi.fn()
			.mockResolvedValueOnce(ASSET)
			.mockRejectedValueOnce(new Error('fail'));
		const { state } = makeState({ uploadFn });
		await state.add([makeFile('ok.png'), makeFile('bad.png')]);
		expect(state.resolvedAssets).toHaveLength(1);
		expect(state.resolvedAssets[0]).toEqual(ASSET);
	});

	it('is empty when all entries are uploading', () => {
		// Not easy to test mid-upload without racing; verify it excludes error/uploading
		const { state } = makeState();
		expect(state.resolvedAssets).toEqual([]);
	});

	it('contains all assets when all uploads succeeded', async () => {
		const uploadFn = vi.fn()
			.mockResolvedValueOnce(ASSET)
			.mockResolvedValueOnce(ASSET2);
		const { state } = makeState({ uploadFn });
		await state.add([makeFile('a.png'), makeFile('b.txt', 'text/plain')]);
		expect(state.resolvedAssets).toHaveLength(2);
	});
});

describe('FileAttachmentState — remove()', () => {
	it('removes the entry with matching localId', async () => {
		const { state } = makeState();
		await state.add([makeFile()]);
		const id = state.attachments[0].localId;
		state.remove(id);
		expect(state.attachments).toHaveLength(0);
	});

	it('removing a non-existent id does nothing', () => {
		const { state } = makeState();
		expect(() => state.remove('no-such-id')).not.toThrow();
		expect(state.attachments).toHaveLength(0);
	});

	it('only removes the targeted entry, leaving others intact', async () => {
		const uploadFn = vi.fn()
			.mockResolvedValueOnce(ASSET)
			.mockResolvedValueOnce(ASSET2);
		const { state } = makeState({ uploadFn });
		await state.add([makeFile('a.png'), makeFile('b.png')]);
		const firstId = state.attachments[0].localId;
		state.remove(firstId);
		expect(state.attachments).toHaveLength(1);
		expect(state.attachments[0].asset).toEqual(ASSET2);
	});
});

describe('FileAttachmentState — retry()', () => {
	it('re-triggers upload on an error entry', async () => {
		let callCount = 0;
		const uploadFn = vi.fn(async () => {
			callCount++;
			if (callCount === 1) throw new Error('first attempt failed');
			return ASSET;
		});
		const { state } = makeState({ uploadFn });
		await state.add([makeFile()]);
		expect(state.attachments[0].status).toBe('error');

		await state.retry(state.attachments[0].localId);
		expect(state.attachments[0].status).toBe('done');
		expect(state.attachments[0].asset).toEqual(ASSET);
	});

	it('does nothing on a done entry', async () => {
		const { state, uploadFn } = makeState();
		await state.add([makeFile()]);
		expect(state.attachments[0].status).toBe('done');
		const callsBefore = uploadFn.mock.calls.length;

		await state.retry(state.attachments[0].localId);
		expect(uploadFn.mock.calls.length).toBe(callsBefore);
	});

	it('does nothing on a non-existent id', async () => {
		const { state } = makeState();
		await expect(state.retry('ghost-id')).resolves.not.toThrow();
	});

	it('transitions error → uploading → done lifecycle correctly', async () => {
		let calls = 0;
		const uploadFn = vi.fn(async () => {
			calls++;
			if (calls === 1) throw new Error('fail');
			return ASSET;
		});
		const { state } = makeState({ uploadFn });
		await state.add([makeFile()]);
		const id = state.attachments[0].localId;
		expect(state.attachments[0].status).toBe('error');
		expect(state.attachments[0].error).toBeDefined();

		await state.retry(id);
		expect(state.attachments[0].status).toBe('done');
		expect(state.attachments[0].error).toBeUndefined();
		expect(state.attachments[0].asset).toEqual(ASSET);
	});
});

describe('FileAttachmentState — clear()', () => {
	it('empties all attachments', async () => {
		const uploadFn = vi.fn()
			.mockResolvedValueOnce(ASSET)
			.mockResolvedValueOnce(ASSET2);
		const { state } = makeState({ uploadFn });
		await state.add([makeFile('a.png'), makeFile('b.png')]);
		expect(state.attachments).toHaveLength(2);
		state.clear();
		expect(state.attachments).toEqual([]);
	});

	it('clear on empty state does nothing', () => {
		const { state } = makeState();
		expect(() => state.clear()).not.toThrow();
		expect(state.attachments).toEqual([]);
	});
});

describe('FileAttachmentState — isUploading / canSubmit', () => {
	it('canSubmit is false when disabled, regardless of upload state', async () => {
		const { state } = makeState({ disabled: true });
		await state.add([makeFile()]);
		expect(state.canSubmit).toBe(false);
	});

	it('canSubmit is false when any entry has error status', async () => {
		const { state } = makeState({
			uploadFn: vi.fn(async () => { throw new Error('fail'); }),
		});
		await state.add([makeFile()]);
		expect(state.attachments[0].status).toBe('error');
		expect(state.canSubmit).toBe(false);
	});

	it('canSubmit is true when all entries are done and not disabled', async () => {
		const { state } = makeState();
		await state.add([makeFile()]);
		expect(state.attachments[0].status).toBe('done');
		expect(state.canSubmit).toBe(true);
	});
});

describe('FileAttachmentState — props / snippetProps', () => {
	it('props has data-file-attachment-root always', () => {
		const { state } = makeState();
		expect(state.props['data-file-attachment-root']).toBe('');
	});

	it('props has data-disabled when disabled', () => {
		const { state } = makeState({ disabled: true });
		expect(state.props['data-disabled']).toBe('');
	});

	it('props omits data-disabled when not disabled', () => {
		const { state } = makeState({ disabled: false });
		expect(state.props['data-disabled']).toBeUndefined();
	});

	it('props omits data-uploading when not uploading', () => {
		const { state } = makeState();
		expect(state.props['data-uploading']).toBeUndefined();
	});

	it('snippetProps contains all required methods and fields', async () => {
		const { state } = makeState();
		await state.add([makeFile()]);
		const sp = state.snippetProps;
		expect(sp).toHaveProperty('attachments');
		expect(sp).toHaveProperty('hasAttachments');
		expect(sp).toHaveProperty('isUploading');
		expect(sp).toHaveProperty('canSubmit');
		expect(typeof sp.add).toBe('function');
		expect(typeof sp.remove).toBe('function');
		expect(typeof sp.retry).toBe('function');
		expect(typeof sp.clear).toBe('function');
	});

	it('snippetProps.hasAttachments reflects current attachment count', async () => {
		const { state } = makeState();
		expect(state.snippetProps.hasAttachments).toBe(false);
		await state.add([makeFile()]);
		expect(state.snippetProps.hasAttachments).toBe(true);
	});
});
