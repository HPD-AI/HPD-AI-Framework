/**
 * MessageEdit Browser Tests
 *
 * Rendered DOM tests using vitest-browser-svelte / real Chromium.
 * Covers: uncontrolled editing toggle, controlled mode, textarea
 * keyboard interaction, save/cancel flows, data attributes, callbacks.
 */

import { describe, it, expect, vi } from 'vitest';
import { render } from 'vitest-browser-svelte';
import { page, userEvent } from 'vitest/browser';
import type { Workspace } from '../../workspace/types.ts';
import MessageEditTest from './message-edit-test.svelte';

// ============================================
// Helpers
// ============================================

function makeWorkspace(editImpl?: () => Promise<void>): Workspace {
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

// ============================================
// Uncontrolled — initial render
// ============================================

describe('MessageEdit (uncontrolled) — initial render', () => {
	it('renders in view mode: editing snippet prop is false', async () => {
		render(MessageEditTest, { workspace: makeWorkspace() });
		await expect.element(page.getByTestId('snippet-editing')).toHaveTextContent('false');
	});

	it('start-edit trigger is present', async () => {
		render(MessageEditTest, { workspace: makeWorkspace() });
		await expect.element(page.getByTestId('start-edit-trigger')).toBeVisible();
	});

	it('textarea is not rendered before startEdit', async () => {
		render(MessageEditTest, { workspace: makeWorkspace() });
		expect(page.getByTestId('textarea-root').elements()).toHaveLength(0);
	});

	it('save button is not rendered before startEdit', async () => {
		render(MessageEditTest, { workspace: makeWorkspace() });
		expect(page.getByTestId('save-btn').elements()).toHaveLength(0);
	});

	it('cancel button is not rendered before startEdit', async () => {
		render(MessageEditTest, { workspace: makeWorkspace() });
		expect(page.getByTestId('cancel-btn').elements()).toHaveLength(0);
	});

	it('root has data-message-edit-root attribute', async () => {
		render(MessageEditTest, { workspace: makeWorkspace() });
		await expect.element(page.getByTestId('root')).toHaveAttribute('data-message-edit-root', '');
	});

	it('data-editing attribute is absent in view mode', async () => {
		render(MessageEditTest, { workspace: makeWorkspace() });
		const root = page.getByTestId('root').element() as HTMLElement;
		expect(root.hasAttribute('data-editing')).toBe(false);
	});
});

// ============================================
// Uncontrolled — entering edit mode
// ============================================

describe('MessageEdit (uncontrolled) — entering edit mode', () => {
	it('clicking start-edit sets editing to true', async () => {
		render(MessageEditTest, { workspace: makeWorkspace(), initialValue: 'Hello' });
		await page.getByTestId('start-edit-trigger').click();
		await expect.element(page.getByTestId('snippet-editing')).toHaveTextContent('true');
	});

	it('textarea appears after startEdit', async () => {
		render(MessageEditTest, { workspace: makeWorkspace(), initialValue: 'Hello' });
		await page.getByTestId('start-edit-trigger').click();
		await expect.element(page.getByTestId('textarea-root')).toBeVisible();
	});

	it('save button appears after startEdit', async () => {
		render(MessageEditTest, { workspace: makeWorkspace(), initialValue: 'Hello' });
		await page.getByTestId('start-edit-trigger').click();
		await expect.element(page.getByTestId('save-btn')).toBeVisible();
	});

	it('cancel button appears after startEdit', async () => {
		render(MessageEditTest, { workspace: makeWorkspace(), initialValue: 'Hello' });
		await page.getByTestId('start-edit-trigger').click();
		await expect.element(page.getByTestId('cancel-btn')).toBeVisible();
	});

	it('draft is pre-filled with initialValue on startEdit', async () => {
		render(MessageEditTest, { workspace: makeWorkspace(), initialValue: 'Pre-filled text' });
		await page.getByTestId('start-edit-trigger').click();
		await expect.element(page.getByTestId('snippet-draft')).toHaveTextContent('Pre-filled text');
	});

	it('data-editing attribute is present after startEdit', async () => {
		render(MessageEditTest, { workspace: makeWorkspace(), initialValue: 'Hello' });
		await page.getByTestId('start-edit-trigger').click();
		await expect.element(page.getByTestId('root')).toHaveAttribute('data-editing', '');
	});

	it('textarea autofocuses on enter edit mode', async () => {
		render(MessageEditTest, { workspace: makeWorkspace(), initialValue: 'Hello' });
		await page.getByTestId('start-edit-trigger').click();
		await expect.element(page.getByRole('textbox')).toHaveFocus();
	});
});

// ============================================
// Uncontrolled — cancel
// ============================================

describe('MessageEdit (uncontrolled) — cancel', () => {
	it('clicking cancel exits edit mode', async () => {
		render(MessageEditTest, { workspace: makeWorkspace(), initialValue: 'Hello' });
		await page.getByTestId('start-edit-trigger').click();
		await expect.element(page.getByTestId('cancel-btn')).toBeEnabled();
		await page.getByTestId('cancel-btn').click();
		await expect.element(page.getByTestId('snippet-editing')).toHaveTextContent('false');
	});

	it('textarea is hidden after cancel', async () => {
		render(MessageEditTest, { workspace: makeWorkspace(), initialValue: 'Hello' });
		await page.getByTestId('start-edit-trigger').click();
		await page.getByTestId('cancel-btn').click();
		await expect.element(page.getByTestId('snippet-editing')).toHaveTextContent('false');
		expect(page.getByTestId('textarea-root').elements()).toHaveLength(0);
	});

	it('calls onCancel callback', async () => {
		const onCancel = vi.fn();
		render(MessageEditTest, { workspace: makeWorkspace(), initialValue: 'Hello', onCancel });
		await page.getByTestId('start-edit-trigger').click();
		await page.getByTestId('cancel-btn').click();
		await expect.element(page.getByTestId('snippet-editing')).toHaveTextContent('false');
		expect(onCancel).toHaveBeenCalledOnce();
	});

	it('does not call editMessage on cancel', async () => {
		const ws = makeWorkspace();
		render(MessageEditTest, { workspace: ws, initialValue: 'Hello' });
		await page.getByTestId('start-edit-trigger').click();
		await page.getByTestId('cancel-btn').click();
		expect(ws.editMessage).not.toHaveBeenCalled();
	});
});

// ============================================
// Uncontrolled — save via button
// ============================================

describe('MessageEdit (uncontrolled) — save via button', () => {
	it('clicking save calls workspace.editMessage', async () => {
		const ws = makeWorkspace();
		render(MessageEditTest, { workspace: ws, initialValue: 'Hello', messageIndex: 2 });
		await page.getByTestId('start-edit-trigger').click();
		await expect.element(page.getByTestId('save-btn')).toBeEnabled();
		await page.getByTestId('save-btn').click();
		// Wait for async save to complete
		await expect.element(page.getByTestId('snippet-editing')).toHaveTextContent('false');
		expect(ws.editMessage).toHaveBeenCalledWith(2, 'Hello');
	});

	it('exits edit mode after successful save', async () => {
		render(MessageEditTest, { workspace: makeWorkspace(), initialValue: 'Hello' });
		await page.getByTestId('start-edit-trigger').click();
		await page.getByTestId('save-btn').click();
		await expect.element(page.getByTestId('snippet-editing')).toHaveTextContent('false');
	});

	it('calls onSave callback', async () => {
		const onSave = vi.fn();
		render(MessageEditTest, { workspace: makeWorkspace(), initialValue: 'Hello', onSave });
		await page.getByTestId('start-edit-trigger').click();
		await page.getByTestId('save-btn').click();
		await expect.element(page.getByTestId('snippet-editing')).toHaveTextContent('false');
		expect(onSave).toHaveBeenCalledOnce();
	});

	it('save button is disabled when textarea is cleared', async () => {
		render(MessageEditTest, { workspace: makeWorkspace(), initialValue: 'Hello' });
		await page.getByTestId('start-edit-trigger').click();
		const textarea = page.getByRole('textbox');
		await textarea.clear();
		await expect.element(page.getByTestId('save-btn')).toBeDisabled();
	});
});

// ============================================
// Uncontrolled — keyboard: Escape
// ============================================

describe('MessageEdit (uncontrolled) — keyboard Escape', () => {
	it('Escape in textarea exits edit mode', async () => {
		render(MessageEditTest, { workspace: makeWorkspace(), initialValue: 'Hello' });
		await page.getByTestId('start-edit-trigger').click();
		await page.getByRole('textbox').click();
		await userEvent.keyboard('{Escape}');
		await expect.element(page.getByTestId('snippet-editing')).toHaveTextContent('false');
	});

	it('Escape calls onCancel', async () => {
		const onCancel = vi.fn();
		render(MessageEditTest, { workspace: makeWorkspace(), initialValue: 'Hello', onCancel });
		await page.getByTestId('start-edit-trigger').click();
		await page.getByRole('textbox').click();
		await userEvent.keyboard('{Escape}');
		await expect.element(page.getByTestId('snippet-editing')).toHaveTextContent('false');
		expect(onCancel).toHaveBeenCalledOnce();
	});
});

// ============================================
// Uncontrolled — keyboard: Enter
// ============================================

describe('MessageEdit (uncontrolled) — keyboard Enter', () => {
	it('Enter in textarea calls editMessage', async () => {
		const ws = makeWorkspace();
		render(MessageEditTest, { workspace: ws, initialValue: 'Hello' });
		await page.getByTestId('start-edit-trigger').click();
		await page.getByRole('textbox').click();
		await userEvent.keyboard('{Enter}');
		await expect.element(page.getByTestId('snippet-editing')).toHaveTextContent('false');
		expect(ws.editMessage).toHaveBeenCalled();
	});

	it('Shift+Enter does not call editMessage', async () => {
		const ws = makeWorkspace();
		render(MessageEditTest, { workspace: ws, initialValue: 'Hello' });
		await page.getByTestId('start-edit-trigger').click();
		await page.getByRole('textbox').click();
		await userEvent.keyboard('{Shift>}{Enter}{/Shift}');
		expect(ws.editMessage).not.toHaveBeenCalled();
	});
});

// ============================================
// Uncontrolled — error handling
// ============================================

describe('MessageEdit (uncontrolled) — error handling', () => {
	it('calls onError when editMessage rejects', async () => {
		const onError = vi.fn();
		const ws = makeWorkspace(() => Promise.reject(new Error('network error')));
		render(MessageEditTest, { workspace: ws, initialValue: 'Hello', onError });
		await page.getByTestId('start-edit-trigger').click();
		await expect.element(page.getByTestId('save-btn')).toBeEnabled();
		await page.getByTestId('save-btn').click();
		// Wait for save to settle (stays in edit mode on error)
		await expect.element(page.getByTestId('snippet-pending')).toHaveTextContent('false');
		expect(onError).toHaveBeenCalledWith(expect.any(Error));
	});

	it('stays in edit mode after error', async () => {
		const ws = makeWorkspace(() => Promise.reject(new Error('fail')));
		render(MessageEditTest, { workspace: ws, initialValue: 'Hello', onError: vi.fn() });
		await page.getByTestId('start-edit-trigger').click();
		await page.getByTestId('save-btn').click();
		await expect.element(page.getByTestId('snippet-pending')).toHaveTextContent('false');
		await expect.element(page.getByTestId('snippet-editing')).toHaveTextContent('true');
	});
});

// ============================================
// Controlled mode
// ============================================

describe('MessageEdit (controlled)', () => {
	it('renders in edit mode when editing=true', async () => {
		render(MessageEditTest, { workspace: makeWorkspace(), initialValue: 'Hello', editing: true });
		await expect.element(page.getByTestId('snippet-editing')).toHaveTextContent('true');
	});

	it('renders edit UI when editing=true', async () => {
		render(MessageEditTest, { workspace: makeWorkspace(), initialValue: 'Hello', editing: true });
		await expect.element(page.getByTestId('textarea-root')).toBeVisible();
	});

	it('renders in view mode when editing=false', async () => {
		render(MessageEditTest, { workspace: makeWorkspace(), initialValue: 'Hello', editing: false });
		await expect.element(page.getByTestId('snippet-editing')).toHaveTextContent('false');
	});

	it('startEdit calls onStartEdit, not internal toggle', async () => {
		const onStartEdit = vi.fn();
		render(MessageEditTest, {
			workspace: makeWorkspace(),
			initialValue: 'Hello',
			editing: false,
			onStartEdit,
		});
		await page.getByTestId('start-edit-trigger').click();
		// editing prop is still false — controlled
		await expect.element(page.getByTestId('snippet-editing')).toHaveTextContent('false');
		expect(onStartEdit).toHaveBeenCalledOnce();
	});

	it('cancel calls onCancel and editing stays true (consumer controls it)', async () => {
		const onCancel = vi.fn();
		render(MessageEditTest, {
			workspace: makeWorkspace(),
			initialValue: 'Hello',
			editing: true,
			onCancel,
		});
		// Need to call startEdit to populate draft so cancel button is enabled
		await page.getByTestId('start-edit-trigger').click();
		await expect.element(page.getByTestId('cancel-btn')).toBeEnabled();
		await page.getByTestId('cancel-btn').click();
		// editing prop still true — consumer must flip it via onCancel
		await expect.element(page.getByTestId('snippet-editing')).toHaveTextContent('true');
		expect(onCancel).toHaveBeenCalledOnce();
	});
});

// ============================================
// Data attributes
// ============================================

describe('MessageEdit — data attributes', () => {
	it('data-message-edit-root always present', async () => {
		render(MessageEditTest, { workspace: makeWorkspace() });
		await expect.element(page.getByTestId('root')).toHaveAttribute('data-message-edit-root', '');
	});

	it('data-editing present in edit mode', async () => {
		render(MessageEditTest, { workspace: makeWorkspace(), initialValue: 'Hello' });
		await page.getByTestId('start-edit-trigger').click();
		await expect.element(page.getByTestId('root')).toHaveAttribute('data-editing', '');
	});

	it('data-editing absent in view mode', async () => {
		render(MessageEditTest, { workspace: makeWorkspace() });
		const root = page.getByTestId('root').element() as HTMLElement;
		expect(root.hasAttribute('data-editing')).toBe(false);
	});

	it('textarea has data-message-edit-textarea', async () => {
		render(MessageEditTest, { workspace: makeWorkspace(), initialValue: 'Hello' });
		await page.getByTestId('start-edit-trigger').click();
		await expect.element(page.getByTestId('textarea-root')).toHaveAttribute('data-message-edit-textarea', '');
	});

	it('save button has data-message-edit-save', async () => {
		render(MessageEditTest, { workspace: makeWorkspace(), initialValue: 'Hello' });
		await page.getByTestId('start-edit-trigger').click();
		await expect.element(page.getByTestId('save-btn')).toHaveAttribute('data-message-edit-save', '');
	});

	it('cancel button has data-message-edit-cancel', async () => {
		render(MessageEditTest, { workspace: makeWorkspace(), initialValue: 'Hello' });
		await page.getByTestId('start-edit-trigger').click();
		await expect.element(page.getByTestId('cancel-btn')).toHaveAttribute('data-message-edit-cancel', '');
	});
});
