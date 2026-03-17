/**
 * MessageEdit Unit Tests
 *
 * Pure state-class tests — no DOM, no rendering.
 * Covers: RootState (uncontrolled + controlled), TextareaState,
 *         SaveButtonState, CancelButtonState
 */

import { describe, it, expect, vi } from 'vitest';
import { box } from 'svelte-toolbelt';
import type { Workspace } from '../../workspace/types.ts';
import {
	MessageEditRootState,
	MessageEditTextareaState,
	MessageEditSaveButtonState,
	MessageEditCancelButtonState,
} from '../message-edit.svelte.js';

// ============================================
// Helpers
// ============================================

function makeWorkspace(editImpl?: (index: number, content: string) => Promise<void>): Workspace {
	return {
		sessions: [],
		activeSessionId: 'session-1',
		loading: false,
		error: null,
		branches: new Map(),
		activeBranchId: 'main',
		activeBranch: null,
		activeSiblings: [],
		canGoNext: false,
		canGoPrevious: false,
		currentSiblingPosition: { current: 1, total: 1 },
		state: { messages: [], streaming: false, permissions: [], clarifications: [], clientToolRequests: [] } as any,
		agents: [],
		activeAgentId: null,
		selectAgent: vi.fn(),
		listAgents: vi.fn().mockResolvedValue([]),
		client: {
			stream: vi.fn(),
			abort: vi.fn(),
			listSessions: vi.fn(),
			getSession: vi.fn(),
			createSession: vi.fn(),
			updateSession: vi.fn(),
			deleteSession: vi.fn(),
			listBranches: vi.fn(),
			getBranch: vi.fn(),
			createBranch: vi.fn(),
			forkBranch: vi.fn(),
			deleteBranch: vi.fn(),
			getBranchMessages: vi.fn(),
			getBranchSiblings: vi.fn(),
			getNextSibling: vi.fn(),
			getPreviousSibling: vi.fn(),
			listAgents: vi.fn(),
			getAgent: vi.fn(),
			createAgent: vi.fn(),
			updateAgent: vi.fn(),
			deleteAgent: vi.fn(),
			uploadAsset: vi.fn(),
		} as any,
		selectSession: vi.fn(),
		createSession: vi.fn(),
		deleteSession: vi.fn(),
		switchBranch: vi.fn(),
		goToNextSibling: vi.fn(),
		goToPreviousSibling: vi.fn(),
		goToSiblingByIndex: vi.fn(),
		editMessage: editImpl
			? vi.fn().mockImplementation(editImpl)
			: vi.fn().mockResolvedValue(undefined),
		deleteBranch: vi.fn(),
		createBranch: vi.fn(),
		refreshBranch: vi.fn(),
		invalidateBranch: vi.fn(),
		send: vi.fn(),
		abort: vi.fn(),
		approve: vi.fn(),
		deny: vi.fn(),
		clarify: vi.fn(),
		clear: vi.fn(),
	};
}

function makeRoot(opts: {
	ws?: Workspace;
	messageIndex?: number;
	initialValue?: string;
	editing?: boolean;
	onStartEdit?: () => void;
	onSave?: () => void;
	onCancel?: () => void;
	onError?: (err: unknown) => void;
} = {}): MessageEditRootState {
	return new MessageEditRootState({
		workspace: box(opts.ws ?? makeWorkspace()),
		messageIndex: box(opts.messageIndex ?? 0),
		initialValue: box(opts.initialValue ?? 'Original content'),
		editing: box(opts.editing),
		onStartEdit: box(opts.onStartEdit),
		onSave: box(opts.onSave),
		onCancel: box(opts.onCancel),
		onError: box(opts.onError),
	});
}

// ============================================
// RootState — uncontrolled: editing toggle
// ============================================

describe('MessageEditRootState (uncontrolled) — editing toggle', () => {
	it('starts in view mode (editing = false)', () => {
		const root = makeRoot();
		expect(root.editing).toBe(false);
	});

	it('startEdit sets editing to true', () => {
		const root = makeRoot();
		root.startEdit();
		expect(root.editing).toBe(true);
	});

	it('cancel after startEdit sets editing back to false', () => {
		const root = makeRoot();
		root.startEdit();
		root.cancel();
		expect(root.editing).toBe(false);
	});

	it('save completes without error and calls onSave (editing toggle verified in browser tests)', async () => {
		const onSave = vi.fn();
		const root = makeRoot({ initialValue: 'Hello', onSave });
		root.startEdit();
		await root.save();
		// Verify side effects: onSave called, pending reset, no throw
		expect(onSave).toHaveBeenCalledOnce();
		expect(root.pending).toBe(false);
	});

	it('save does not set editing to false when editMessage throws', async () => {
		const ws = makeWorkspace(() => Promise.reject(new Error('network')));
		const root = makeRoot({ ws, initialValue: 'Hello', onError: vi.fn() });
		root.startEdit();
		await root.save();
		expect(root.editing).toBe(true);
	});
});

// ============================================
// RootState — uncontrolled: draft management
// ============================================

describe('MessageEditRootState (uncontrolled) — draft management', () => {
	it('draft starts empty, not from initialValue', () => {
		const root = makeRoot({ initialValue: 'Original' });
		expect(root.draft).toBe('');
	});

	it('startEdit resets draft to initialValue', () => {
		const root = makeRoot({ initialValue: 'Hello world' });
		root.startEdit();
		expect(root.draft).toBe('Hello world');
	});

	it('startEdit always picks up the latest initialValue', () => {
		// box is static here but we verify the draft reflects what was current at startEdit time
		const root = makeRoot({ initialValue: 'First' });
		root.startEdit();
		expect(root.draft).toBe('First');
	});

	it('updateDraft changes the draft value', () => {
		const root = makeRoot();
		root.startEdit();
		root.updateDraft('New content');
		expect(root.draft).toBe('New content');
	});

	it('startEdit resets draft even if it was previously modified', () => {
		const root = makeRoot({ initialValue: 'Original' });
		root.startEdit();
		root.updateDraft('Typed something');
		root.cancel();
		root.startEdit();
		expect(root.draft).toBe('Original');
	});
});

// ============================================
// RootState — uncontrolled: isEmpty / canSave
// ============================================

describe('MessageEditRootState (uncontrolled) — isEmpty / canSave', () => {
	it('isEmpty is true when draft is empty string', () => {
		const root = makeRoot();
		root.startEdit();
		root.updateDraft('');
		expect(root.isEmpty).toBe(true);
	});

	it('isEmpty is true for whitespace-only draft', () => {
		const root = makeRoot();
		root.startEdit();
		root.updateDraft('   ');
		expect(root.isEmpty).toBe(true);
	});

	it('isEmpty is false when draft has content', () => {
		const root = makeRoot({ initialValue: 'Hello' });
		root.startEdit();
		expect(root.isEmpty).toBe(false);
	});

	it('canSave is false when draft is empty', () => {
		const root = makeRoot();
		root.startEdit();
		root.updateDraft('');
		expect(root.canSave).toBe(false);
	});

	it('canSave is true when draft has content and not pending', () => {
		const root = makeRoot({ initialValue: 'Hello' });
		root.startEdit();
		expect(root.canSave).toBe(true);
	});
});

// ============================================
// RootState — uncontrolled: pending
// ============================================

describe('MessageEditRootState (uncontrolled) — pending', () => {
	it('pending is false initially', () => {
		const root = makeRoot();
		expect(root.pending).toBe(false);
	});

	it('pending is false after successful save', async () => {
		const root = makeRoot({ initialValue: 'Hello' });
		root.startEdit();
		await root.save();
		expect(root.pending).toBe(false);
	});

	it('pending is false after save that throws', async () => {
		const ws = makeWorkspace(() => Promise.reject(new Error('fail')));
		const root = makeRoot({ ws, initialValue: 'Hello', onError: vi.fn() });
		root.startEdit();
		await root.save();
		expect(root.pending).toBe(false);
	});

	it('pending is true while save is in flight', async () => {
		let resolveSave!: () => void;
		const ws = makeWorkspace(() => new Promise<void>((r) => (resolveSave = r)));
		const root = makeRoot({ ws, initialValue: 'Hello' });
		root.startEdit();
		const savePromise = root.save();
		expect(root.pending).toBe(true);
		resolveSave();
		await savePromise;
	});
});

// ============================================
// RootState — uncontrolled: save behaviour
// ============================================

describe('MessageEditRootState (uncontrolled) — save()', () => {
	it('calls workspace.editMessage with correct messageIndex', async () => {
		const ws = makeWorkspace();
		const root = makeRoot({ ws, messageIndex: 3, initialValue: 'Hello' });
		root.startEdit();
		await root.save();
		expect(ws.editMessage).toHaveBeenCalledWith(3, 'Hello');
	});

	it('trims whitespace from draft before calling editMessage', async () => {
		const ws = makeWorkspace();
		const root = makeRoot({ ws, initialValue: '  trimmed  ' });
		root.startEdit();
		await root.save();
		expect(ws.editMessage).toHaveBeenCalledWith(0, 'trimmed');
	});

	it('calls onSave callback after success', async () => {
		const onSave = vi.fn();
		const root = makeRoot({ initialValue: 'Hello', onSave });
		root.startEdit();
		await root.save();
		expect(onSave).toHaveBeenCalledOnce();
	});

	it('calls onError when editMessage throws', async () => {
		const onError = vi.fn();
		const ws = makeWorkspace(() => Promise.reject(new Error('boom')));
		const root = makeRoot({ ws, initialValue: 'Hello', onError });
		root.startEdit();
		await root.save();
		expect(onError).toHaveBeenCalledWith(expect.any(Error));
	});

	it('does not call onSave when editMessage throws', async () => {
		const onSave = vi.fn();
		const ws = makeWorkspace(() => Promise.reject(new Error('boom')));
		const root = makeRoot({ ws, initialValue: 'Hello', onSave, onError: vi.fn() });
		root.startEdit();
		await root.save();
		expect(onSave).not.toHaveBeenCalled();
	});

	it('save is a no-op when draft is empty', async () => {
		const ws = makeWorkspace();
		const root = makeRoot({ ws });
		root.startEdit();
		root.updateDraft('');
		await root.save();
		expect(ws.editMessage).not.toHaveBeenCalled();
	});

	it('save is a no-op when canSave is false (whitespace)', async () => {
		const ws = makeWorkspace();
		const root = makeRoot({ ws });
		root.startEdit();
		root.updateDraft('   ');
		await root.save();
		expect(ws.editMessage).not.toHaveBeenCalled();
	});
});

// ============================================
// RootState — uncontrolled: cancel behaviour
// ============================================

describe('MessageEditRootState (uncontrolled) — cancel()', () => {
	it('calls onCancel callback', () => {
		const onCancel = vi.fn();
		const root = makeRoot({ onCancel });
		root.startEdit();
		root.cancel();
		expect(onCancel).toHaveBeenCalledOnce();
	});

	it('does not call editMessage on cancel', () => {
		const ws = makeWorkspace();
		const root = makeRoot({ ws });
		root.startEdit();
		root.cancel();
		expect(ws.editMessage).not.toHaveBeenCalled();
	});
});

// ============================================
// RootState — uncontrolled: props / snippetProps
// ============================================

describe('MessageEditRootState (uncontrolled) — props', () => {
	it('has data-message-edit-root', () => {
		const root = makeRoot();
		expect(root.props['data-message-edit-root']).toBe('');
	});

	it('data-editing is absent when not editing', () => {
		const root = makeRoot();
		expect(root.props['data-editing']).toBeUndefined();
	});

	it('data-editing is present when editing', () => {
		const root = makeRoot();
		root.startEdit();
		expect(root.props['data-editing']).toBe('');
	});

	it('data-pending is absent when not pending', () => {
		const root = makeRoot();
		expect(root.props['data-pending']).toBeUndefined();
	});

	it('snippetProps contains all expected fields', () => {
		const root = makeRoot({ initialValue: 'Hello' });
		root.startEdit();
		const sp = root.snippetProps;
		expect(sp.editing).toBe(true);
		expect(sp.draft).toBe('Hello');
		expect(sp.pending).toBe(false);
		expect(sp.canSave).toBe(true);
		expect(typeof sp.startEdit).toBe('function');
		expect(typeof sp.save).toBe('function');
		expect(typeof sp.cancel).toBe('function');
	});
});

// ============================================
// RootState — controlled mode
// ============================================

describe('MessageEditRootState (controlled) — editing driven by prop', () => {
	it('editing reflects the prop value when true', () => {
		const root = makeRoot({ editing: true });
		expect(root.editing).toBe(true);
	});

	it('editing reflects the prop value when false', () => {
		const root = makeRoot({ editing: false });
		expect(root.editing).toBe(false);
	});

	it('startEdit calls onStartEdit, not internal toggle', () => {
		const onStartEdit = vi.fn();
		const root = makeRoot({ editing: false, onStartEdit });
		root.startEdit();
		// Internal state not touched — editing prop still drives it
		expect(root.editing).toBe(false);
		expect(onStartEdit).toHaveBeenCalledOnce();
	});

	it('cancel calls onCancel, does not flip internal state', () => {
		const onCancel = vi.fn();
		const root = makeRoot({ editing: true, onCancel });
		root.cancel();
		// editing still true because prop drives it
		expect(root.editing).toBe(true);
		expect(onCancel).toHaveBeenCalledOnce();
	});

	it('save calls onSave after success, does not change internal editing', async () => {
		const onSave = vi.fn();
		const root = makeRoot({ editing: true, initialValue: 'Hello', onSave });
		root.startEdit(); // resets draft
		await root.save();
		// editing still true because prop drives it
		expect(root.editing).toBe(true);
		expect(onSave).toHaveBeenCalledOnce();
	});
});

// ============================================
// TextareaState — keyboard handling
// ============================================

describe('MessageEditTextareaState — keyboard', () => {
	function makeTextareaWithRoot(initialValue = 'Hello') {
		const root = makeRoot({ initialValue });
		root.startEdit();
		const textarea = new MessageEditTextareaState(root, {
			placeholder: box('Edit…'),
			ariaLabel: box('Edit message'),
		});
		return { root, textarea };
	}

	function makeKeyEvent(key: string, shiftKey = false, isComposing = false): KeyboardEvent {
		return { key, shiftKey, isComposing, preventDefault: vi.fn() } as unknown as KeyboardEvent;
	}

	it('Enter key calls save', async () => {
		const ws = makeWorkspace();
		const root = makeRoot({ ws, initialValue: 'Hello' });
		root.startEdit();
		const textarea = new MessageEditTextareaState(root, {
			placeholder: box('Edit…'),
			ariaLabel: box('Edit message'),
		});
		const event = makeKeyEvent('Enter');
		textarea.handleKeyDown(event);
		await new Promise((r) => setTimeout(r, 0));
		expect(ws.editMessage).toHaveBeenCalled();
	});

	it('Enter key prevents default', () => {
		const { textarea } = makeTextareaWithRoot();
		const event = makeKeyEvent('Enter');
		textarea.handleKeyDown(event);
		expect(event.preventDefault).toHaveBeenCalled();
	});

	it('Shift+Enter does not call save', async () => {
		const ws = makeWorkspace();
		const root = makeRoot({ ws, initialValue: 'Hello' });
		root.startEdit();
		const textarea = new MessageEditTextareaState(root, {
			placeholder: box('Edit…'),
			ariaLabel: box('Edit message'),
		});
		const event = makeKeyEvent('Enter', true);
		textarea.handleKeyDown(event);
		await new Promise((r) => setTimeout(r, 0));
		expect(ws.editMessage).not.toHaveBeenCalled();
	});

	it('isComposing Enter does not call save', async () => {
		const ws = makeWorkspace();
		const root = makeRoot({ ws, initialValue: 'Hello' });
		root.startEdit();
		const textarea = new MessageEditTextareaState(root, {
			placeholder: box('Edit…'),
			ariaLabel: box('Edit message'),
		});
		const event = makeKeyEvent('Enter', false, true);
		textarea.handleKeyDown(event);
		await new Promise((r) => setTimeout(r, 0));
		expect(ws.editMessage).not.toHaveBeenCalled();
	});

	it('Escape key calls cancel', () => {
		const onCancel = vi.fn();
		const root = makeRoot({ onCancel });
		root.startEdit();
		const textarea = new MessageEditTextareaState(root, {
			placeholder: box('Edit…'),
			ariaLabel: box('Edit message'),
		});
		const event = makeKeyEvent('Escape');
		textarea.handleKeyDown(event);
		expect(onCancel).toHaveBeenCalledOnce();
	});

	it('Escape key prevents default', () => {
		const { textarea } = makeTextareaWithRoot();
		const event = makeKeyEvent('Escape');
		textarea.handleKeyDown(event);
		expect(event.preventDefault).toHaveBeenCalled();
	});

	it('handleChange updates root draft', () => {
		const { root, textarea } = makeTextareaWithRoot();
		textarea.handleChange('New text');
		expect(root.draft).toBe('New text');
	});

	it('props.disabled is false when not pending', () => {
		const { textarea } = makeTextareaWithRoot();
		expect(textarea.props.disabled).toBe(false);
	});

	it('props has data-message-edit-textarea', () => {
		const { textarea } = makeTextareaWithRoot();
		expect(textarea.props['data-message-edit-textarea']).toBe('');
	});

	it('snippetProps.value mirrors root draft', () => {
		const root = makeRoot({ initialValue: 'Hi' });
		root.startEdit();
		const textarea = new MessageEditTextareaState(root, {
			placeholder: box('Edit…'),
			ariaLabel: box('Edit message'),
		});
		expect(textarea.snippetProps.value).toBe('Hi');
		root.updateDraft('Updated');
		expect(textarea.snippetProps.value).toBe('Updated');
	});

	it('snippetProps.placeholder reflects the prop', () => {
		const root = makeRoot();
		root.startEdit();
		const textarea = new MessageEditTextareaState(root, {
			placeholder: box('Custom placeholder'),
			ariaLabel: box('Edit message'),
		});
		expect(textarea.snippetProps.placeholder).toBe('Custom placeholder');
	});
});

// ============================================
// SaveButtonState
// ============================================

describe('MessageEditSaveButtonState', () => {
	function makeSave(root: MessageEditRootState, label = 'Save edit') {
		return new MessageEditSaveButtonState(root, { ariaLabel: box(label) });
	}

	it('disabled is true when draft is empty', () => {
		const root = makeRoot();
		root.startEdit();
		root.updateDraft('');
		const save = makeSave(root);
		expect(save.props.disabled).toBe(true);
	});

	it('disabled is false when draft has content', () => {
		const root = makeRoot({ initialValue: 'Hello' });
		root.startEdit();
		const save = makeSave(root);
		expect(save.props.disabled).toBe(false);
	});

	it('has data-message-edit-save', () => {
		const root = makeRoot();
		root.startEdit();
		expect(makeSave(root).props['data-message-edit-save']).toBe('');
	});

	it('has type=button', () => {
		const root = makeRoot();
		root.startEdit();
		expect(makeSave(root).props.type).toBe('button');
	});

	it('data-disabled present when disabled', () => {
		const root = makeRoot();
		root.startEdit();
		root.updateDraft('');
		expect(makeSave(root).props['data-disabled']).toBe('');
	});

	it('data-disabled absent when enabled', () => {
		const root = makeRoot({ initialValue: 'Hello' });
		root.startEdit();
		expect(makeSave(root).props['data-disabled']).toBeUndefined();
	});

	it('aria-label reflects the prop', () => {
		const root = makeRoot();
		root.startEdit();
		expect(makeSave(root, 'Confirm edit').props['aria-label']).toBe('Confirm edit');
	});

	it('snippetProps.save delegates to root.save', async () => {
		const ws = makeWorkspace();
		const root = makeRoot({ ws, initialValue: 'Hello' });
		root.startEdit();
		const save = makeSave(root);
		await save.snippetProps.save();
		expect(ws.editMessage).toHaveBeenCalledOnce();
	});

	it('snippetProps.disabled matches props.disabled', () => {
		const root = makeRoot({ initialValue: 'Hello' });
		root.startEdit();
		const save = makeSave(root);
		expect(save.snippetProps.disabled).toBe(save.props.disabled);
	});

	it('snippetProps.pending mirrors root.pending', () => {
		const root = makeRoot();
		root.startEdit();
		const save = makeSave(root);
		expect(save.snippetProps.pending).toBe(false);
	});
});

// ============================================
// CancelButtonState
// ============================================

describe('MessageEditCancelButtonState', () => {
	function makeCancel(root: MessageEditRootState, label = 'Cancel edit') {
		return new MessageEditCancelButtonState(root, { ariaLabel: box(label) });
	}

	it('disabled is false when not pending', () => {
		const root = makeRoot();
		root.startEdit();
		expect(makeCancel(root).props.disabled).toBe(false);
	});

	it('has data-message-edit-cancel', () => {
		const root = makeRoot();
		root.startEdit();
		expect(makeCancel(root).props['data-message-edit-cancel']).toBe('');
	});

	it('has type=button', () => {
		const root = makeRoot();
		root.startEdit();
		expect(makeCancel(root).props.type).toBe('button');
	});

	it('data-disabled absent when not pending', () => {
		const root = makeRoot();
		root.startEdit();
		expect(makeCancel(root).props['data-disabled']).toBeUndefined();
	});

	it('aria-label reflects the prop', () => {
		const root = makeRoot();
		root.startEdit();
		expect(makeCancel(root, 'Discard').props['aria-label']).toBe('Discard');
	});

	it('snippetProps.cancel delegates to root.cancel', () => {
		const onCancel = vi.fn();
		const root = makeRoot({ onCancel });
		root.startEdit();
		const cancel = makeCancel(root);
		cancel.snippetProps.cancel();
		expect(onCancel).toHaveBeenCalledOnce();
	});

	it('snippetProps.pending mirrors root.pending', () => {
		const root = makeRoot();
		root.startEdit();
		expect(makeCancel(root).snippetProps.pending).toBe(false);
	});
});
