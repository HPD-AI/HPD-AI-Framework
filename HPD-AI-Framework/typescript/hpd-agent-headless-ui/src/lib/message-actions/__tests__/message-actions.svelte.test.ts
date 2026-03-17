/**
 * MessageActions Browser Tests
 *
 * Rendered DOM tests using vitest-browser-svelte.
 * Covers: data attributes, ARIA, disabled state, snippet props,
 *         async action behaviour, callbacks, reactivity.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render } from 'vitest-browser-svelte';
import { page } from 'vitest/browser';
import type { Branch } from '@hpd/hpd-agent-client';
import type { Workspace } from '../../workspace/types.ts';
import type { Message } from '../../agent/types.ts';
import MessageActionsTest from './message-actions-test.svelte';

// ============================================
// Helpers
// ============================================

const createBranch = (overrides: Partial<Branch> = {}): Branch => ({
	id: 'fork-1',
	sessionId: 'session-1',
	name: 'Fork',
	createdAt: new Date().toISOString(),
	lastActivity: new Date().toISOString(),
	messageCount: 2,
	siblingIndex: 1,
	totalSiblings: 3,
	isOriginal: false,
	forkedFrom: 'main',
	forkedAtMessageIndex: 2,
	childBranches: [],
	totalForks: 0,
	previousSiblingId: 'main',
	nextSiblingId: 'fork-2',
	...overrides,
});

const createMessage = (overrides: Partial<Message> = {}): Message => ({
	id: `msg-${Math.random()}`,
	role: 'user',
	content: 'Hello',
	streaming: false,
	thinking: false,
	toolCalls: [],
	timestamp: new Date(),
	...overrides,
});

function makeWorkspace(messages: Message[] = [], editImpl?: () => Promise<void>): Workspace {
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
		state: { messages, streaming: false, permissions: [], clarifications: [], clientToolRequests: [] } as any,
		selectSession: vi.fn(),
		createSession: vi.fn(),
		deleteSession: vi.fn(),
		switchBranch: vi.fn(),
		goToNextSibling: vi.fn(),
		goToPreviousSibling: vi.fn(),
		goToSiblingByIndex: vi.fn(),
		editMessage: editImpl ? vi.fn().mockImplementation(editImpl) : vi.fn().mockResolvedValue(undefined),
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
		agents: [],
		activeAgentId: null,
		selectAgent: vi.fn(),
		listAgents: vi.fn().mockResolvedValue([]),
		client: null as any,
	};
}

function setup(props: {
	workspace?: Workspace;
	messageIndex?: number;
	role?: Message['role'];
	branch?: Branch | null;
	copyContent?: string;
	onPrev?: () => void;
	onNext?: () => void;
	onEditSuccess?: () => void;
	onEditError?: (err: unknown) => void;
	onRetrySuccess?: () => void;
	onRetryError?: (err: unknown) => void;
	onCopySuccess?: () => void;
	onCopyError?: (err: unknown) => void;
	editAriaLabel?: string;
	retryAriaLabel?: string;
	copyAriaLabel?: string;
	prevAriaLabel?: string;
	nextAriaLabel?: string;
	copyResetDelay?: number;
	editContent?: string;
} = {}) {
	const workspace = props.workspace ?? makeWorkspace();
	render(MessageActionsTest, { props: { workspace, ...props } } as any);

	return {
		root: page.getByTestId('root'),
		editBtn: page.getByTestId('edit-btn'),
		retryBtn: page.getByTestId('retry-btn'),
		copyBtn: page.getByTestId('copy-btn'),
		editTrigger: page.getByTestId('edit-trigger'),
		retryTrigger: page.getByTestId('retry-trigger'),
		copyTrigger: page.getByTestId('copy-trigger'),
		prevBtn: page.getByTestId('prev-btn'),
		nextBtn: page.getByTestId('next-btn'),
		positionEl: page.getByTestId('position-el'),
		snippetHasSiblings: page.getByTestId('snippet-has-siblings'),
		snippetPending: page.getByTestId('snippet-pending'),
		snippetPosition: page.getByTestId('snippet-position'),
		editStatus: page.getByTestId('edit-status'),
		editDisabled: page.getByTestId('edit-disabled'),
		retryStatus: page.getByTestId('retry-status'),
		retryDisabled: page.getByTestId('retry-disabled'),
		copyCopied: page.getByTestId('copy-copied'),
		workspace,
	};
}

// ============================================
// Data Attributes — Root
// ============================================

describe('MessageActions — Root data attributes', () => {
	it('has data-message-actions-root', async () => {
		const t = setup();
		await expect.element(t.root).toHaveAttribute('data-message-actions-root');
	});

	it('has data-role matching role prop', async () => {
		const t = setup({ role: 'assistant' });
		await expect.element(t.root).toHaveAttribute('data-role', 'assistant');
	});

	it('has data-message-index matching messageIndex prop', async () => {
		const t = setup({ messageIndex: 5 });
		await expect.element(t.root).toHaveAttribute('data-message-index', '5');
	});

	it('does not have data-has-siblings when no branch', async () => {
		const t = setup({ branch: null });
		await expect.element(t.root).not.toHaveAttribute('data-has-siblings');
	});

	it('does not have data-has-siblings when branch is original', async () => {
		const t = setup({
			messageIndex: 2,
			branch: createBranch({ isOriginal: true, forkedFrom: undefined, forkedAtMessageIndex: undefined }),
		});
		await expect.element(t.root).not.toHaveAttribute('data-has-siblings');
	});

	it('does not have data-has-siblings when forkedAtMessageIndex does not match', async () => {
		const t = setup({ messageIndex: 0, branch: createBranch({ forkedAtMessageIndex: 3 }) });
		await expect.element(t.root).not.toHaveAttribute('data-has-siblings');
	});

	it('has data-has-siblings when forked at this index with siblings in workspace.branches', async () => {
		const { ws, forks } = makeWorkspaceWithSiblings([
			{ id: 'main', siblingIndex: 0 },
			{ id: 'fork-1', siblingIndex: 1 },
			{ id: 'fork-2', siblingIndex: 2 },
		]);
		const t = setup({ workspace: ws, messageIndex: 2, branch: forks[0] });
		await expect.element(t.root).toHaveAttribute('data-has-siblings');
	});
});

// ============================================
// Data Attributes — EditButton
// ============================================

describe('MessageActions — EditButton data attributes', () => {
	it('has data-message-actions-edit', async () => {
		const t = setup({ role: 'user' });
		await expect.element(t.editBtn).toHaveAttribute('data-message-actions-edit');
	});

	it('has type=button', async () => {
		const t = setup({ role: 'user' });
		await expect.element(t.editBtn).toHaveAttribute('type', 'button');
	});

	it('has data-status="idle" initially', async () => {
		const t = setup({ role: 'user' });
		await expect.element(t.editBtn).toHaveAttribute('data-status', 'idle');
	});

	it('has data-disabled when role is assistant', async () => {
		const t = setup({ role: 'assistant' });
		await expect.element(t.editBtn).toHaveAttribute('data-disabled');
	});

	it('has data-disabled when role is system', async () => {
		const t = setup({ role: 'system' });
		await expect.element(t.editBtn).toHaveAttribute('data-disabled');
	});

	it('does not have data-disabled when role is user', async () => {
		const t = setup({ role: 'user' });
		await expect.element(t.editBtn).not.toHaveAttribute('data-disabled');
	});

	it('is disabled (HTML) when role is assistant', async () => {
		const t = setup({ role: 'assistant' });
		await expect.element(t.editBtn).toBeDisabled();
	});

	it('is not disabled (HTML) when role is user', async () => {
		const t = setup({ role: 'user' });
		await expect.element(t.editBtn).not.toBeDisabled();
	});
});

// ============================================
// Data Attributes — RetryButton
// ============================================

describe('MessageActions — RetryButton data attributes', () => {
	it('has data-message-actions-retry', async () => {
		const t = setup();
		await expect.element(t.retryBtn).toHaveAttribute('data-message-actions-retry');
	});

	it('has type=button', async () => {
		const t = setup();
		await expect.element(t.retryBtn).toHaveAttribute('type', 'button');
	});

	it('has data-status="idle" initially', async () => {
		const t = setup();
		await expect.element(t.retryBtn).toHaveAttribute('data-status', 'idle');
	});

	it('has data-disabled when role is system', async () => {
		const t = setup({ role: 'system' });
		await expect.element(t.retryBtn).toHaveAttribute('data-disabled');
	});

	it('does not have data-disabled when role is user', async () => {
		const t = setup({ role: 'user' });
		await expect.element(t.retryBtn).not.toHaveAttribute('data-disabled');
	});

	it('does not have data-disabled when role is assistant', async () => {
		const t = setup({ role: 'assistant' });
		await expect.element(t.retryBtn).not.toHaveAttribute('data-disabled');
	});

	it('is disabled (HTML) when role is system', async () => {
		const t = setup({ role: 'system' });
		await expect.element(t.retryBtn).toBeDisabled();
	});
});

// ============================================
// Data Attributes — CopyButton
// ============================================

describe('MessageActions — CopyButton data attributes', () => {
	beforeEach(() => {
		Object.defineProperty(navigator, 'clipboard', {
			value: { writeText: vi.fn().mockResolvedValue(undefined) },
			configurable: true,
		});
	});

	it('has data-message-actions-copy', async () => {
		const t = setup();
		await expect.element(t.copyBtn).toHaveAttribute('data-message-actions-copy');
	});

	it('has type=button', async () => {
		const t = setup();
		await expect.element(t.copyBtn).toHaveAttribute('type', 'button');
	});

	it('does not have data-copied initially', async () => {
		const t = setup();
		await expect.element(t.copyBtn).not.toHaveAttribute('data-copied');
	});

	it('has data-copied after clicking copy trigger', async () => {
		const t = setup();
		await t.copyTrigger.click();
		await expect.element(t.copyBtn).toHaveAttribute('data-copied');
	});
});

// ============================================
// Helpers for sibling-group tests
//
// #siblingsAtThisRow now derives navigation state purely from workspace.branches —
// navigation pointer fields (previousSiblingId / nextSiblingId) on Branch are no
// longer used for enablement decisions. Tests must populate workspace.branches.
// ============================================

function makeWorkspaceWithSiblings(
	siblings: Array<{ id: string; siblingIndex: number; isOriginal?: boolean }>
): { ws: Workspace; mainBranch: Branch; forks: Branch[] } {
	const ws = makeWorkspace();
	const mainBranch = createBranch({
		id: 'main',
		isOriginal: true,
		forkedFrom: undefined,
		forkedAtMessageIndex: undefined,
		siblingIndex: 0,
		totalSiblings: siblings.length,
		previousSiblingId: undefined,
		nextSiblingId: siblings.length > 1 ? siblings[1].id : undefined,
	});
	const forks = siblings.slice(1).map((s, i) =>
		createBranch({
			id: s.id,
			isOriginal: false,
			forkedFrom: 'main',
			forkedAtMessageIndex: 1, // forkAtIndex=1 → row 2 (messageIndex=2)
			siblingIndex: i + 1,
			totalSiblings: siblings.length,
			previousSiblingId: i === 0 ? 'main' : siblings[i].id,
			nextSiblingId: i + 2 < siblings.length ? siblings[i + 2].id : undefined,
		})
	);
	(ws.branches as Map<string, Branch>).set('main', mainBranch);
	for (const f of forks) (ws.branches as Map<string, Branch>).set(f.id, f);
	return { ws, mainBranch, forks };
}

// ============================================
// Data Attributes — Prev / Next / Position
// ============================================

describe('MessageActions — Prev/Next/Position data attributes', () => {
	it('prev has data-message-actions-prev', async () => {
		const t = setup();
		await expect.element(t.prevBtn).toHaveAttribute('data-message-actions-prev');
	});

	it('next has data-message-actions-next', async () => {
		const t = setup();
		await expect.element(t.nextBtn).toHaveAttribute('data-message-actions-next');
	});

	it('position has data-message-actions-position', async () => {
		const t = setup();
		await expect.element(t.positionEl).toHaveAttribute('data-message-actions-position');
	});

	it('position has aria-live="polite"', async () => {
		const t = setup();
		await expect.element(t.positionEl).toHaveAttribute('aria-live', 'polite');
	});

	it('position has aria-atomic="true"', async () => {
		const t = setup();
		await expect.element(t.positionEl).toHaveAttribute('aria-atomic', 'true');
	});

	it('prev and next have type=button', async () => {
		const t = setup();
		await expect.element(t.prevBtn).toHaveAttribute('type', 'button');
		await expect.element(t.nextBtn).toHaveAttribute('type', 'button');
	});

	it('prev is disabled when not forked here (branch has different forkedAtMessageIndex)', async () => {
		// Branch is forked at index 5, but we're rendering at messageIndex=0
		const { ws } = makeWorkspaceWithSiblings([{ id: 'main', siblingIndex: 0 }, { id: 'fork-1', siblingIndex: 1 }]);
		const fork = createBranch({ id: 'fork-x', forkedFrom: 'main', forkedAtMessageIndex: 5, siblingIndex: 1 });
		(ws.branches as Map<string, Branch>).set('fork-x', fork);
		const t = setup({ workspace: ws, messageIndex: 0, branch: fork });
		await expect.element(t.prevBtn).toBeDisabled();
	});

	it('next is disabled when not forked here', async () => {
		const t = setup({ messageIndex: 0, branch: createBranch({ forkedAtMessageIndex: 5 }) });
		await expect.element(t.nextBtn).toBeDisabled();
	});

	it('prev is disabled when fork is first in its group (no branch before it)', async () => {
		// fork-1 is the only fork at forkAtIndex=1, so group = [main, fork-1]
		// Currently on fork-1 which is at index 1 — prev=main should exist
		// But if we render main (index 0), prev is disabled
		const { ws, mainBranch } = makeWorkspaceWithSiblings([
			{ id: 'main', siblingIndex: 0 },
			{ id: 'fork-1', siblingIndex: 1 },
		]);
		const t = setup({ workspace: ws, messageIndex: 2, branch: mainBranch });
		await expect.element(t.prevBtn).toBeDisabled();
	});

	it('prev is enabled when branch has a predecessor in its sibling group', async () => {
		const { ws, forks } = makeWorkspaceWithSiblings([
			{ id: 'main', siblingIndex: 0 },
			{ id: 'fork-1', siblingIndex: 1 },
		]);
		// fork-1 is at index 1, main is at index 0 → prev exists
		const t = setup({ workspace: ws, messageIndex: 2, branch: forks[0] });
		await expect.element(t.prevBtn).not.toBeDisabled();
	});

	it('next is enabled when branch has a successor in its sibling group', async () => {
		const { ws, mainBranch } = makeWorkspaceWithSiblings([
			{ id: 'main', siblingIndex: 0 },
			{ id: 'fork-1', siblingIndex: 1 },
		]);
		// main is at index 0, fork-1 is at index 1 → next exists
		const t = setup({ workspace: ws, messageIndex: 2, branch: mainBranch });
		await expect.element(t.nextBtn).not.toBeDisabled();
	});

	it('position shows correct text when forked here', async () => {
		const { ws, forks } = makeWorkspaceWithSiblings([
			{ id: 'main', siblingIndex: 0 },
			{ id: 'fork-1', siblingIndex: 1 },
			{ id: 'fork-2', siblingIndex: 2 },
		]);
		// forks[0] = fork-1, siblingIndex=1 → position 2/3
		const t = setup({ workspace: ws, messageIndex: 2, branch: forks[0] });
		await expect.element(t.positionEl).toHaveTextContent('2 / 3');
	});

	it('position is empty when not forked here', async () => {
		const t = setup({ messageIndex: 0, branch: null });
		await expect.element(t.positionEl).toHaveTextContent('');
	});

	it('position shows "3 / 3" for last-of-three fork', async () => {
		const { ws, forks } = makeWorkspaceWithSiblings([
			{ id: 'main', siblingIndex: 0 },
			{ id: 'fork-1', siblingIndex: 1 },
			{ id: 'fork-2', siblingIndex: 2 },
		]);
		// forks[1] = fork-2, last → position 3/3
		const t = setup({ workspace: ws, messageIndex: 2, branch: forks[1] });
		await expect.element(t.positionEl).toHaveTextContent('3 / 3');
	});

	it('position shows "1 / 3" for first (original) branch of three', async () => {
		const { ws, mainBranch } = makeWorkspaceWithSiblings([
			{ id: 'main', siblingIndex: 0 },
			{ id: 'fork-1', siblingIndex: 1 },
			{ id: 'fork-2', siblingIndex: 2 },
		]);
		const t = setup({ workspace: ws, messageIndex: 2, branch: mainBranch });
		await expect.element(t.positionEl).toHaveTextContent('1 / 3');
	});
});

// ============================================
// ARIA labels
// ============================================

describe('MessageActions — ARIA labels', () => {
	it('edit button has default aria-label', async () => {
		const t = setup();
		await expect.element(t.editBtn).toHaveAttribute('aria-label', 'Edit message');
	});

	it('retry button has default aria-label', async () => {
		const t = setup();
		await expect.element(t.retryBtn).toHaveAttribute('aria-label', 'Retry message');
	});

	it('copy button has default aria-label', async () => {
		const t = setup();
		await expect.element(t.copyBtn).toHaveAttribute('aria-label', 'Copy message');
	});

	it('prev has default aria-label', async () => {
		const t = setup();
		await expect.element(t.prevBtn).toHaveAttribute('aria-label', 'Previous version');
	});

	it('next has default aria-label', async () => {
		const t = setup();
		await expect.element(t.nextBtn).toHaveAttribute('aria-label', 'Next version');
	});

	it('edit aria-label is overridable', async () => {
		const t = setup({ editAriaLabel: 'Modify this' });
		await expect.element(t.editBtn).toHaveAttribute('aria-label', 'Modify this');
	});

	it('retry aria-label is overridable', async () => {
		const t = setup({ retryAriaLabel: 'Try again' });
		await expect.element(t.retryBtn).toHaveAttribute('aria-label', 'Try again');
	});

	it('prev aria-label is overridable', async () => {
		const t = setup({ prevAriaLabel: 'Older version' });
		await expect.element(t.prevBtn).toHaveAttribute('aria-label', 'Older version');
	});

	it('next aria-label is overridable', async () => {
		const t = setup({ nextAriaLabel: 'Newer version' });
		await expect.element(t.nextBtn).toHaveAttribute('aria-label', 'Newer version');
	});
});

// ============================================
// Snippet props
// ============================================

describe('MessageActions — Root snippet props', () => {
	it('hasSiblings is false when no branch', async () => {
		const t = setup({ branch: null });
		await expect.element(t.snippetHasSiblings).toHaveTextContent('false');
	});

	it('hasSiblings is true when forked at this index (siblings in workspace.branches)', async () => {
		const { ws, forks } = makeWorkspaceWithSiblings([
			{ id: 'main', siblingIndex: 0 },
			{ id: 'fork-1', siblingIndex: 1 },
			{ id: 'fork-2', siblingIndex: 2 },
		]);
		const t = setup({ workspace: ws, messageIndex: 2, branch: forks[0] });
		await expect.element(t.snippetHasSiblings).toHaveTextContent('true');
	});

	it('pending is false initially', async () => {
		const t = setup();
		await expect.element(t.snippetPending).toHaveTextContent('false');
	});

	it('position is empty when no branch', async () => {
		const t = setup({ branch: null });
		await expect.element(t.snippetPosition).toHaveTextContent('');
	});

	it('position shows "2 / 3" when branch is second in a three-sibling group', async () => {
		const { ws, forks } = makeWorkspaceWithSiblings([
			{ id: 'main', siblingIndex: 0 },
			{ id: 'fork-1', siblingIndex: 1 },
			{ id: 'fork-2', siblingIndex: 2 },
		]);
		// forks[0] = fork-1, index 1 in group → "2 / 3"
		const t = setup({ workspace: ws, messageIndex: 2, branch: forks[0] });
		await expect.element(t.snippetPosition).toHaveTextContent('2 / 3');
	});
});

describe('MessageActions — EditButton snippet props', () => {
	it('edit-disabled is true for assistant role', async () => {
		const t = setup({ role: 'assistant' });
		await expect.element(t.editDisabled).toHaveTextContent('true');
	});

	it('edit-disabled is false for user role', async () => {
		const t = setup({ role: 'user' });
		await expect.element(t.editDisabled).toHaveTextContent('false');
	});

	it('edit-status is "idle" initially', async () => {
		const t = setup({ role: 'user' });
		await expect.element(t.editStatus).toHaveTextContent('idle');
	});
});

describe('MessageActions — RetryButton snippet props', () => {
	it('retry-disabled is true for system role', async () => {
		const t = setup({ role: 'system' });
		await expect.element(t.retryDisabled).toHaveTextContent('true');
	});

	it('retry-disabled is false for user role', async () => {
		const t = setup({ role: 'user' });
		await expect.element(t.retryDisabled).toHaveTextContent('false');
	});

	it('retry-disabled is false for assistant role', async () => {
		const t = setup({ role: 'assistant' });
		await expect.element(t.retryDisabled).toHaveTextContent('false');
	});

	it('retry-status is "idle" initially', async () => {
		const t = setup();
		await expect.element(t.retryStatus).toHaveTextContent('idle');
	});
});

describe('MessageActions — CopyButton snippet props', () => {
	beforeEach(() => {
		Object.defineProperty(navigator, 'clipboard', {
			value: { writeText: vi.fn().mockResolvedValue(undefined) },
			configurable: true,
		});
	});

	it('copy-copied is false initially', async () => {
		const t = setup();
		await expect.element(t.copyCopied).toHaveTextContent('false');
	});

	it('copy-copied is true after clicking copy', async () => {
		const t = setup();
		await t.copyTrigger.click();
		await expect.element(t.copyCopied).toHaveTextContent('true');
	});

	it('copy trigger text changes to "Copied!" after copy', async () => {
		const t = setup();
		await t.copyTrigger.click();
		await expect.element(t.copyTrigger).toHaveTextContent('Copied!');
	});
});

// ============================================
// Async action behaviour — EditButton
// ============================================

describe('MessageActions — EditButton async behaviour', () => {
	it('calls workspace.editMessage with messageIndex and editContent', async () => {
		const workspace = makeWorkspace([createMessage({ role: 'user', content: 'Hello' })]);
		const t = setup({ workspace, messageIndex: 0, role: 'user', editContent: 'New text' });
		await t.editTrigger.click();
		expect(workspace.editMessage).toHaveBeenCalledWith(0, 'New text');
	});

	it('calls onEditSuccess after success', async () => {
		const onEditSuccess = vi.fn();
		const workspace = makeWorkspace();
		const t = setup({ workspace, role: 'user', onEditSuccess });
		await t.editTrigger.click();
		expect(onEditSuccess).toHaveBeenCalledOnce();
	});

	it('calls onEditError when editMessage throws', async () => {
		const onEditError = vi.fn();
		const workspace = makeWorkspace([], async () => { throw new Error('fail'); });
		const t = setup({ workspace, role: 'user', onEditError });
		await t.editTrigger.click();
		expect(onEditError).toHaveBeenCalledOnce();
	});

	it('edit-status becomes "error" when editMessage throws', async () => {
		const workspace = makeWorkspace([], async () => { throw new Error('fail'); });
		const t = setup({ workspace, role: 'user' });
		await t.editTrigger.click();
		await expect.element(t.editStatus).toHaveTextContent('error');
	});

	it('disabled button does not call workspace.editMessage when force-clicked', async () => {
		const workspace = makeWorkspace();
		const t = setup({ workspace, role: 'assistant' });
		await t.editBtn.click({ force: true });
		expect(workspace.editMessage).not.toHaveBeenCalled();
	});
});

// ============================================
// Async action behaviour — RetryButton
// ============================================

describe('MessageActions — RetryButton async behaviour', () => {
	it('calls workspace.editMessage for user message retry', async () => {
		const messages = [createMessage({ role: 'user', content: 'Hello', id: 'msg-0' })];
		const workspace = makeWorkspace(messages);
		const t = setup({ workspace, messageIndex: 0, role: 'user' });
		await t.retryTrigger.click();
		expect(workspace.editMessage).toHaveBeenCalledWith(0, 'Hello');
	});

	it('calls workspace.editMessage with preceding user msg for assistant retry', async () => {
		const messages = [
			createMessage({ role: 'user', content: 'My question', id: 'msg-0' }),
			createMessage({ role: 'assistant', content: 'My answer', id: 'msg-1' }),
		];
		const workspace = makeWorkspace(messages);
		const t = setup({ workspace, messageIndex: 1, role: 'assistant' });
		await t.retryTrigger.click();
		expect(workspace.editMessage).toHaveBeenCalledWith(0, 'My question');
	});

	it('calls onRetrySuccess after success', async () => {
		const onRetrySuccess = vi.fn();
		const messages = [createMessage({ role: 'user', content: 'Hi' })];
		const workspace = makeWorkspace(messages);
		const t = setup({ workspace, messageIndex: 0, role: 'user', onRetrySuccess });
		await t.retryTrigger.click();
		expect(onRetrySuccess).toHaveBeenCalledOnce();
	});

	it('calls onRetryError when editMessage throws', async () => {
		const onRetryError = vi.fn();
		const messages = [createMessage({ role: 'user', content: 'Hi' })];
		const workspace = makeWorkspace(messages, async () => { throw new Error('fail'); });
		const t = setup({ workspace, messageIndex: 0, role: 'user', onRetryError });
		await t.retryTrigger.click();
		expect(onRetryError).toHaveBeenCalledOnce();
	});

	it('retry-status becomes "error" when editMessage throws', async () => {
		const messages = [createMessage({ role: 'user', content: 'Hi' })];
		const workspace = makeWorkspace(messages, async () => { throw new Error('fail'); });
		const t = setup({ workspace, messageIndex: 0, role: 'user' });
		await t.retryTrigger.click();
		await expect.element(t.retryStatus).toHaveTextContent('error');
	});

	it('system role retry button does not call editMessage when force-clicked', async () => {
		const workspace = makeWorkspace();
		const t = setup({ workspace, role: 'system' });
		await t.retryBtn.click({ force: true });
		expect(workspace.editMessage).not.toHaveBeenCalled();
	});
});

// ============================================
// CopyButton interaction
// ============================================

describe('MessageActions — CopyButton interaction', () => {
	beforeEach(() => {
		Object.defineProperty(navigator, 'clipboard', {
			value: { writeText: vi.fn().mockResolvedValue(undefined) },
			configurable: true,
		});
	});

	it('copies the copyContent prop to clipboard', async () => {
		const t = setup({ copyContent: 'The message text' });
		await t.copyTrigger.click();
		expect(navigator.clipboard.writeText).toHaveBeenCalledWith('The message text');
	});

	it('calls onCopySuccess after successful copy', async () => {
		const onCopySuccess = vi.fn();
		const t = setup({ onCopySuccess });
		await t.copyTrigger.click();
		expect(onCopySuccess).toHaveBeenCalledOnce();
	});

	it('calls onCopyError when clipboard throws', async () => {
		const onCopyError = vi.fn();
		(navigator.clipboard.writeText as ReturnType<typeof vi.fn>).mockRejectedValue(new Error('denied'));
		const t = setup({ onCopyError });
		await t.copyTrigger.click();
		expect(onCopyError).toHaveBeenCalledOnce();
	});

	it('copied stays false when clipboard throws', async () => {
		(navigator.clipboard.writeText as ReturnType<typeof vi.fn>).mockRejectedValue(new Error('denied'));
		const t = setup();
		await t.copyTrigger.click();
		await expect.element(t.copyCopied).toHaveTextContent('false');
	});
});

// ============================================
// Branch nav callbacks
// ============================================

describe('MessageActions — Prev/Next callbacks', () => {
	it('calls onPrev when prev button is clicked and enabled', async () => {
		const onPrev = vi.fn();
		const { ws, forks } = makeWorkspaceWithSiblings([
			{ id: 'main', siblingIndex: 0 },
			{ id: 'fork-1', siblingIndex: 1 },
		]);
		// fork-1 is at index 1 → prev (main) exists
		const t = setup({ workspace: ws, messageIndex: 2, branch: forks[0], onPrev });
		await t.prevBtn.click();
		expect(onPrev).toHaveBeenCalledOnce();
	});

	it('calls onNext when next button is clicked and enabled', async () => {
		const onNext = vi.fn();
		const { ws, mainBranch } = makeWorkspaceWithSiblings([
			{ id: 'main', siblingIndex: 0 },
			{ id: 'fork-1', siblingIndex: 1 },
		]);
		// main is at index 0 → next (fork-1) exists
		const t = setup({ workspace: ws, messageIndex: 2, branch: mainBranch, onNext });
		await t.nextBtn.click();
		expect(onNext).toHaveBeenCalledOnce();
	});

	it('does not call onPrev when prev is disabled (first in group)', async () => {
		const onPrev = vi.fn();
		const { ws, mainBranch } = makeWorkspaceWithSiblings([
			{ id: 'main', siblingIndex: 0 },
			{ id: 'fork-1', siblingIndex: 1 },
		]);
		// main is at index 0 → no prev
		const t = setup({ workspace: ws, messageIndex: 2, branch: mainBranch, onPrev });
		await t.prevBtn.click({ force: true });
		expect(onPrev).not.toHaveBeenCalled();
	});

	it('does not call onNext when next is disabled (last in group)', async () => {
		const onNext = vi.fn();
		const { ws, forks } = makeWorkspaceWithSiblings([
			{ id: 'main', siblingIndex: 0 },
			{ id: 'fork-1', siblingIndex: 1 },
		]);
		// fork-1 is the last → no next
		const t = setup({ workspace: ws, messageIndex: 2, branch: forks[0], onNext });
		await t.nextBtn.click({ force: true });
		expect(onNext).not.toHaveBeenCalled();
	});
});

// ============================================
// Original-branch switcher (S1-S7)
// Tests the #isForkedHere / #siblingsAtThisRow path for isOriginal=true branches.
// All navigation is derived from workspace.branches — pointer fields are only
// used for siblingIndex ordering; stale pointers from a different fork group
// do NOT affect which row the switcher appears at.
// ============================================

describe('MessageActions — original branch switcher (S1-S7)', () => {
	/** Build a workspace where main is forked at forkAtIndex (row = forkAtIndex + 1). */
	function makeWorkspaceWithFork(forkAtIndex: number): { ws: Workspace; mainBranch: Branch; fork1: Branch } {
		const ws = makeWorkspace();
		const mainBranch = createBranch({
			id: 'main',
			isOriginal: true,
			forkedFrom: undefined,
			forkedAtMessageIndex: undefined,
			previousSiblingId: undefined,
			nextSiblingId: 'fork-1',
			siblingIndex: 0,
			totalSiblings: 2,
		});
		const fork1 = createBranch({
			id: 'fork-1',
			isOriginal: false,
			forkedFrom: 'main',
			forkedAtMessageIndex: forkAtIndex,
			previousSiblingId: 'main',
			nextSiblingId: undefined,
			siblingIndex: 1,
			totalSiblings: 2,
		});
		(ws.branches as Map<string, Branch>).set('main', mainBranch);
		(ws.branches as Map<string, Branch>).set('fork-1', fork1);
		return { ws, mainBranch, fork1 };
	}

	// S1: data-has-siblings present for original branch at its fork-point row
	it('S1: has data-has-siblings for original branch at fork-point messageIndex', async () => {
		// forkAtIndex=0 → row messageIndex=1
		const { ws, mainBranch } = makeWorkspaceWithFork(0);
		const t = setup({ workspace: ws, messageIndex: 1, branch: mainBranch });
		await expect.element(t.root).toHaveAttribute('data-has-siblings');
	});

	// S2: data-has-siblings absent for original branch at a different row
	it('S2: does not have data-has-siblings for original branch at a different messageIndex', async () => {
		// forkAtIndex=0 → row 1; checking row 2 → should NOT show
		const { ws, mainBranch } = makeWorkspaceWithFork(0);
		const t = setup({ workspace: ws, messageIndex: 2, branch: mainBranch });
		await expect.element(t.root).not.toHaveAttribute('data-has-siblings');
	});

	// S3: position shows "Original (1 / 2)" when original branch is at fork point
	it('S3: position shows "Original (1 / 2)" for original branch at fork point', async () => {
		const { ws, mainBranch } = makeWorkspaceWithFork(0);
		const t = setup({ workspace: ws, messageIndex: 1, branch: mainBranch });
		await expect.element(t.positionEl).toHaveTextContent('Original (1 / 2)');
	});

	// S4: prev is disabled for original branch (it is slot 0 — first in sibling group)
	it('S4: prev is disabled for original branch (slot 0, first in sibling group)', async () => {
		const { ws, mainBranch } = makeWorkspaceWithFork(0);
		const t = setup({ workspace: ws, messageIndex: 1, branch: mainBranch });
		await expect.element(t.prevBtn).toBeDisabled();
	});

	// S5: next is enabled for original branch (it has a fork after it)
	it('S5: next is enabled for original branch pointing to fork at this row', async () => {
		const { ws, mainBranch } = makeWorkspaceWithFork(0);
		const t = setup({ workspace: ws, messageIndex: 1, branch: mainBranch });
		await expect.element(t.nextBtn).not.toBeDisabled();
	});

	// S6: clicking prev navigates to the previous branch in the sibling group
	it('S6: clicking prev calls workspace.switchBranch with previous sibling in group', async () => {
		const { ws, fork1 } = makeWorkspaceWithFork(1);
		// fork-1 is at index 1, main is at index 0 → prev = main
		const t = setup({ workspace: ws, messageIndex: 2, branch: fork1 });
		await t.prevBtn.click();
		expect(ws.switchBranch).toHaveBeenCalledWith('main');
	});

	// S7: clicking next navigates to the next branch in the sibling group (original → fork)
	it('S7: clicking next calls workspace.switchBranch with next sibling in group', async () => {
		const { ws, mainBranch } = makeWorkspaceWithFork(0);
		// main is at index 0, fork-1 is at index 1 → next = fork-1
		const t = setup({ workspace: ws, messageIndex: 1, branch: mainBranch });
		await t.nextBtn.click();
		expect(ws.switchBranch).toHaveBeenCalledWith('fork-1');
	});
});

// ============================================
// Multi-fork-group (M1-M4)
// Tests the edge case where the same source branch (main) has been forked at
// TWO different message indices. The backend overwrites navigation pointer
// fields with the last group written, so #siblingsAtThisRow must use
// workspace.branches directly (not nextSiblingId / totalSiblings).
// ============================================

describe('MessageActions — multi-fork-group invariants (M1-M4)', () => {
	/**
	 * Setup: main forked at index 3 (group A, row 4) and at index 1 (group B, row 2).
	 * After both forks, main.nextSiblingId reflects whichever group was forked last —
	 * it no longer reliably identifies which group to use for a given row.
	 */
	function makeMultiForkWorkspace() {
		const ws = makeWorkspace();

		const mainBranch = createBranch({
			id: 'main', isOriginal: true,
			forkedFrom: undefined, forkedAtMessageIndex: undefined,
			siblingIndex: 0, totalSiblings: 2,
			previousSiblingId: undefined,
			// nextSiblingId is stale — points to whichever fork was created last
			nextSiblingId: 'fork-b1',  // group B was created after group A
		});

		// Group A: forked at index 3 → shows at row 4
		const forkA1 = createBranch({
			id: 'fork-a1', isOriginal: false,
			forkedFrom: 'main', forkedAtMessageIndex: 3,
			siblingIndex: 1, totalSiblings: 2,
			previousSiblingId: 'main', nextSiblingId: undefined,
		});

		// Group B: forked at index 1 → shows at row 2
		const forkB1 = createBranch({
			id: 'fork-b1', isOriginal: false,
			forkedFrom: 'main', forkedAtMessageIndex: 1,
			siblingIndex: 1, totalSiblings: 2,
			previousSiblingId: 'main', nextSiblingId: undefined,
		});

		(ws.branches as Map<string, Branch>).set('main', mainBranch);
		(ws.branches as Map<string, Branch>).set('fork-a1', forkA1);
		(ws.branches as Map<string, Branch>).set('fork-b1', forkB1);

		return { ws, mainBranch, forkA1, forkB1 };
	}

	// M1: original branch shows switcher at GROUP A row (4) despite nextSiblingId pointing to group B
	it('M1: original branch shows switcher at group-A row even when nextSiblingId is from group-B', async () => {
		const { ws, mainBranch } = makeMultiForkWorkspace();
		// Group A: forkAtIndex=3 → row messageIndex=4
		const t = setup({ workspace: ws, messageIndex: 4, branch: mainBranch });
		await expect.element(t.root).toHaveAttribute('data-has-siblings');
	});

	// M2: original branch shows switcher at GROUP B row (2)
	it('M2: original branch also shows switcher at group-B row', async () => {
		const { ws, mainBranch } = makeMultiForkWorkspace();
		// Group B: forkAtIndex=1 → row messageIndex=2
		const t = setup({ workspace: ws, messageIndex: 2, branch: mainBranch });
		await expect.element(t.root).toHaveAttribute('data-has-siblings');
	});

	// M3: position at group-A row is correct (1/2) regardless of stale nextSiblingId
	it('M3: position at group-A row shows "Original (1 / 2)"', async () => {
		const { ws, mainBranch } = makeMultiForkWorkspace();
		const t = setup({ workspace: ws, messageIndex: 4, branch: mainBranch });
		await expect.element(t.positionEl).toHaveTextContent('Original (1 / 2)');
	});

	// M4: clicking next at group-A row navigates to fork-a1, not fork-b1
	it('M4: clicking next at group-A row navigates to fork-a1 (not the stale nextSiblingId fork-b1)', async () => {
		const { ws, mainBranch } = makeMultiForkWorkspace();
		const t = setup({ workspace: ws, messageIndex: 4, branch: mainBranch });
		await t.nextBtn.click();
		expect(ws.switchBranch).toHaveBeenCalledWith('fork-a1');
	});
});

// ============================================
// Stateless switcher (ST1-ST3)
// The switcher must remain visible at a row even when the active branch
// (workspace.activeBranch / branch prop) has navigated away from that row's
// fork group. As long as workspace.branches contains siblings at a given
// messageIndex, the switcher shows there — always.
// ============================================

describe('MessageActions — stateless switcher (ST1-ST3)', () => {
	/**
	 * Two fork groups from main:
	 *   Group A: forked at index 3 → shows switcher at row 4
	 *   Group B: forked at index 1 → shows switcher at row 2
	 *
	 * Active branch is fork-b1 (in group B, at row 2).
	 * When rendering row 4, branch prop = fork-b1 (not in group A).
	 * The switcher at row 4 must still appear and show the right position.
	 */
	function makeStatelessWorkspace() {
		const ws = makeWorkspace();

		const mainBranch = createBranch({
			id: 'main', isOriginal: true,
			forkedFrom: undefined, forkedAtMessageIndex: undefined,
			siblingIndex: 0, totalSiblings: 2,
			previousSiblingId: undefined, nextSiblingId: 'fork-b1',
			ancestors: {},
		});
		const forkA1 = createBranch({
			id: 'fork-a1', isOriginal: false,
			forkedFrom: 'main', forkedAtMessageIndex: 3,
			siblingIndex: 1, totalSiblings: 2,
			previousSiblingId: 'main', nextSiblingId: undefined,
			// fork-a1 descends from main
			ancestors: { '0': 'main' },
		});
		const forkB1 = createBranch({
			id: 'fork-b1', isOriginal: false,
			forkedFrom: 'main', forkedAtMessageIndex: 1,
			siblingIndex: 1, totalSiblings: 2,
			previousSiblingId: 'main', nextSiblingId: undefined,
			ancestors: { '0': 'main' },
		});

		(ws.branches as Map<string, Branch>).set('main', mainBranch);
		(ws.branches as Map<string, Branch>).set('fork-a1', forkA1);
		(ws.branches as Map<string, Branch>).set('fork-b1', forkB1);
		// Override activeBranchId on the workspace mock
		Object.defineProperty(ws, 'activeBranchId', { get: () => 'fork-b1', configurable: true });
		Object.defineProperty(ws, 'activeBranch', { get: () => forkB1, configurable: true });

		return { ws, mainBranch, forkA1, forkB1 };
	}

	// ST1: switcher at row 4 (group A) is visible even though active branch is fork-b1 (group B)
	it('ST1: switcher at group-A row visible even when active branch is in group-B', async () => {
		const { ws, forkB1 } = makeStatelessWorkspace();
		// branch prop = forkB1 (active branch), but messageIndex=4 (group A row)
		const t = setup({ workspace: ws, messageIndex: 4, branch: forkB1 });
		await expect.element(t.root).toHaveAttribute('data-has-siblings');
	});

	// ST2: switcher at row 2 (group B) still works normally
	it('ST2: switcher at group-B row shows correct active sibling', async () => {
		const { ws, forkB1 } = makeStatelessWorkspace();
		const t = setup({ workspace: ws, messageIndex: 2, branch: forkB1 });
		await expect.element(t.positionEl).toHaveTextContent('2 / 2');
	});

	// ST3: at group-A row, active sibling resolved via ancestor lookup (main is ancestor of fork-b1)
	it('ST3: group-A row resolves active sibling via ancestor chain, shows "Original (1 / 2)"', async () => {
		const { ws, forkB1 } = makeStatelessWorkspace();
		// fork-b1's ancestor is main. main is in group A. So active sibling at row 4 = main.
		const t = setup({ workspace: ws, messageIndex: 4, branch: forkB1 });
		await expect.element(t.positionEl).toHaveTextContent('Original (1 / 2)');
	});
});
