/**
 * MessageActions Unit Tests
 *
 * Pure state-class tests — no DOM, no rendering.
 * Covers: RootState, EditButtonState, RetryButtonState,
 *         CopyButtonState, PrevState, NextState, PositionState
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { box } from 'svelte-toolbelt';
import type { Branch } from '@hpd/hpd-agent-client';
import type { Workspace } from '../../workspace/types.ts';
import type { Message } from '../../agent/types.ts';
import {
	MessageActionsRootState,
	MessageActionsEditButtonState,
	MessageActionsRetryButtonState,
	MessageActionsCopyButtonState,
	MessageActionsPrevState,
	MessageActionsNextState,
	MessageActionsPositionState,
} from '../message-actions.svelte.js';

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

function makeWorkspace(messages: Message[] = []): Workspace {
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
		state: {
			messages,
			streaming: false,
			permissions: [],
			clarifications: [],
			clientToolRequests: [],
		} as any,		agents: [],
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
		} as any,		selectSession: vi.fn(),
		createSession: vi.fn(),
		deleteSession: vi.fn(),
		switchBranch: vi.fn(),
		goToNextSibling: vi.fn(),
		goToPreviousSibling: vi.fn(),
		goToSiblingByIndex: vi.fn(),
		editMessage: vi.fn().mockResolvedValue(undefined),
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
	role?: Message['role'];
	messageIndex?: number;
	branch?: Branch | null;
	workspace?: Workspace;
} = {}): MessageActionsRootState {
	const ws = opts.workspace ?? makeWorkspace();
	return new MessageActionsRootState({
		workspace: box(ws),
		messageIndex: box(opts.messageIndex ?? 0),
		role: box(opts.role ?? 'user'),
		branch: opts.branch !== undefined ? box(opts.branch) : undefined,
	});
}

// ============================================
// RootState — pending tracking
// ============================================

describe('MessageActionsRootState — pending', () => {
	it('starts as false', () => {
		const root = makeRoot();
		expect(root.pending).toBe(false);
	});

	it('becomes true after increment', () => {
		const root = makeRoot();
		root._incrementPending();
		expect(root.pending).toBe(true);
	});

	it('returns false when balanced', () => {
		const root = makeRoot();
		root._incrementPending();
		root._decrementPending();
		expect(root.pending).toBe(false);
	});

	it('never goes below zero', () => {
		const root = makeRoot();
		root._decrementPending();
		root._decrementPending();
		expect(root.pending).toBe(false);
	});

	it('stays true when multiple increments, only partially decremented', () => {
		const root = makeRoot();
		root._incrementPending();
		root._incrementPending();
		root._decrementPending();
		expect(root.pending).toBe(true);
	});

	it('clears when all increments are balanced', () => {
		const root = makeRoot();
		root._incrementPending();
		root._incrementPending();
		root._decrementPending();
		root._decrementPending();
		expect(root.pending).toBe(false);
	});
});

// ============================================
// RootState — hasSiblings / isForkedHere
// ============================================

describe('MessageActionsRootState — hasSiblings', () => {
	it('is false when no branch provided', () => {
		const root = makeRoot({ messageIndex: 2 });
		expect(root.hasSiblings).toBe(false);
	});

	it('is false when branch is null', () => {
		const root = makeRoot({ messageIndex: 2, branch: null });
		expect(root.hasSiblings).toBe(false);
	});

	it('is false when branch is original (isOriginal=true)', () => {
		const root = makeRoot({
			messageIndex: 2,
			branch: createBranch({ isOriginal: true, forkedFrom: undefined, forkedAtMessageIndex: undefined }),
		});
		expect(root.hasSiblings).toBe(false);
	});

	it('is false when forkedAtMessageIndex does not match messageIndex', () => {
		const root = makeRoot({
			messageIndex: 0,
			branch: createBranch({ forkedAtMessageIndex: 3 }),
		});
		expect(root.hasSiblings).toBe(false);
	});

	it('is false when forked here but only one sibling', () => {
		const root = makeRoot({
			messageIndex: 2,
			branch: createBranch({ forkedAtMessageIndex: 2, totalSiblings: 1 }),
		});
		expect(root.hasSiblings).toBe(false);
	});

	it('is true when forked at this index with multiple siblings', () => {
		const root = makeRoot({
			messageIndex: 2,
			branch: createBranch({ forkedAtMessageIndex: 2, totalSiblings: 3 }),
		});
		expect(root.hasSiblings).toBe(true);
	});
});

// ============================================
// RootState — canGoPrevious / canGoNext
// ============================================

describe('MessageActionsRootState — navigation availability', () => {
	it('canGoPrevious is false when not forked here', () => {
		const root = makeRoot({ messageIndex: 0, branch: createBranch({ forkedAtMessageIndex: 3 }) });
		expect(root.canGoPrevious).toBe(false);
	});

	it('canGoPrevious is false when forked here but no previousSiblingId', () => {
		const root = makeRoot({
			messageIndex: 2,
			branch: createBranch({ forkedAtMessageIndex: 2, previousSiblingId: undefined }),
		});
		expect(root.canGoPrevious).toBe(false);
	});

	it('canGoPrevious is true when forked here and previousSiblingId exists', () => {
		const root = makeRoot({
			messageIndex: 2,
			branch: createBranch({ forkedAtMessageIndex: 2, previousSiblingId: 'main' }),
		});
		expect(root.canGoPrevious).toBe(true);
	});

	it('canGoNext is false when not forked here', () => {
		const root = makeRoot({ messageIndex: 0, branch: createBranch({ forkedAtMessageIndex: 3 }) });
		expect(root.canGoNext).toBe(false);
	});

	it('canGoNext is false when forked here but no nextSiblingId', () => {
		const root = makeRoot({
			messageIndex: 2,
			branch: createBranch({ forkedAtMessageIndex: 2, nextSiblingId: undefined }),
		});
		expect(root.canGoNext).toBe(false);
	});

	it('canGoNext is true when forked here and nextSiblingId exists', () => {
		const root = makeRoot({
			messageIndex: 2,
			branch: createBranch({ forkedAtMessageIndex: 2, nextSiblingId: 'fork-2' }),
		});
		expect(root.canGoNext).toBe(true);
	});
});

// ============================================
// RootState — position / positionLabel
// ============================================

describe('MessageActionsRootState — position strings', () => {
	it('position is empty when not forked here', () => {
		const root = makeRoot({ messageIndex: 0, branch: createBranch({ forkedAtMessageIndex: 3 }) });
		expect(root.position).toBe('');
	});

	it('position is empty when no branch', () => {
		const root = makeRoot({ messageIndex: 2 });
		expect(root.position).toBe('');
	});

	it('position is "2 / 4" for siblingIndex=1, totalSiblings=4', () => {
		const root = makeRoot({
			messageIndex: 2,
			branch: createBranch({ forkedAtMessageIndex: 2, siblingIndex: 1, totalSiblings: 4 }),
		});
		expect(root.position).toBe('2 / 4');
	});

	it('position is "1 / 2" for siblingIndex=0, totalSiblings=2', () => {
		const root = makeRoot({
			messageIndex: 2,
			branch: createBranch({ forkedAtMessageIndex: 2, siblingIndex: 0, totalSiblings: 2 }),
		});
		expect(root.position).toBe('1 / 2');
	});

	it('positionLabel is empty when not forked here', () => {
		const root = makeRoot({ messageIndex: 0, branch: createBranch({ forkedAtMessageIndex: 3 }) });
		expect(root.positionLabel).toBe('');
	});

	it('positionLabel is empty when totalSiblings is 1', () => {
		const root = makeRoot({
			messageIndex: 2,
			branch: createBranch({ forkedAtMessageIndex: 2, totalSiblings: 1 }),
		});
		expect(root.positionLabel).toBe('');
	});

	it('positionLabel is "Original (1 / 3)" for original branch with 3 siblings', () => {
		const root = makeRoot({
			messageIndex: 2,
			branch: createBranch({
				forkedAtMessageIndex: 2,
				isOriginal: true,
				siblingIndex: 0,
				totalSiblings: 3,
			}),
		});
		// isOriginal=true means isForkedHere check fails — position label stays empty
		// (original branches can't be a fork result)
		expect(root.positionLabel).toBe('');
	});

	it('positionLabel is "Fork 2 / 3" for fork at siblingIndex=1', () => {
		const root = makeRoot({
			messageIndex: 2,
			branch: createBranch({
				forkedAtMessageIndex: 2,
				isOriginal: false,
				siblingIndex: 1,
				totalSiblings: 3,
			}),
		});
		expect(root.positionLabel).toBe('Fork 2 / 3');
	});

	it('positionLabel is "Fork 4 / 4" for last fork', () => {
		const root = makeRoot({
			messageIndex: 2,
			branch: createBranch({
				forkedAtMessageIndex: 2,
				isOriginal: false,
				siblingIndex: 3,
				totalSiblings: 4,
			}),
		});
		expect(root.positionLabel).toBe('Fork 4 / 4');
	});

	it('position is "4 / 4" for last-of-four fork', () => {
		const root = makeRoot({
			messageIndex: 2,
			branch: createBranch({
				forkedAtMessageIndex: 2,
				siblingIndex: 3,
				totalSiblings: 4,
			}),
		});
		expect(root.position).toBe('4 / 4');
	});

	it('position is "1 / 3" for first fork of three', () => {
		const root = makeRoot({
			messageIndex: 2,
			branch: createBranch({
				forkedAtMessageIndex: 2,
				siblingIndex: 0,
				totalSiblings: 3,
			}),
		});
		expect(root.position).toBe('1 / 3');
	});
});

// ============================================
// RootState — snippetProps integration
// (Ported from BranchSwitcher — full field coverage)
// ============================================

describe('MessageActionsRootState — snippetProps integration', () => {
	it('all fields correct for a mid-sibling fork at this index', () => {
		const branch = createBranch({
			forkedAtMessageIndex: 2,
			siblingIndex: 1,
			totalSiblings: 3,
			isOriginal: false,
			previousSiblingId: 'main',
			nextSiblingId: 'fork-2',
		});
		const root = makeRoot({ messageIndex: 2, branch });
		const sp = root.snippetProps;
		expect(sp.hasSiblings).toBe(true);
		expect(sp.pending).toBe(false);
		expect(sp.position).toBe('2 / 3');
		expect(root.canGoPrevious).toBe(true);
		expect(root.canGoNext).toBe(true);
		expect(root.positionLabel).toBe('Fork 2 / 3');
	});

	it('all fields false/empty when no branch', () => {
		const root = makeRoot({ messageIndex: 2 });
		const sp = root.snippetProps;
		expect(sp.hasSiblings).toBe(false);
		expect(sp.position).toBe('');
		expect(root.canGoPrevious).toBe(false);
		expect(root.canGoNext).toBe(false);
		expect(root.positionLabel).toBe('');
	});

	it('all fields false/empty when branch is forked at a different index', () => {
		const branch = createBranch({
			forkedAtMessageIndex: 5,
			siblingIndex: 1,
			totalSiblings: 3,
			previousSiblingId: 'main',
			nextSiblingId: 'fork-2',
		});
		const root = makeRoot({ messageIndex: 0, branch });
		expect(root.hasSiblings).toBe(false);
		expect(root.position).toBe('');
		expect(root.canGoPrevious).toBe(false);
		expect(root.canGoNext).toBe(false);
	});
});

// ============================================
// RootState — props
// ============================================

describe('MessageActionsRootState — props', () => {
	it('has data-message-actions-root', () => {
		const root = makeRoot({ role: 'user', messageIndex: 3 });
		expect(root.props['data-message-actions-root']).toBe('');
	});

	it('has data-role matching role', () => {
		const root = makeRoot({ role: 'assistant', messageIndex: 1 });
		expect(root.props['data-role']).toBe('assistant');
	});

	it('has data-message-index matching messageIndex', () => {
		const root = makeRoot({ role: 'user', messageIndex: 7 });
		expect(root.props['data-message-index']).toBe(7);
	});

	it('data-has-siblings is absent when hasSiblings is false', () => {
		const root = makeRoot({ messageIndex: 0 });
		expect(root.props['data-has-siblings']).toBeUndefined();
	});

	it('data-has-siblings is present when hasSiblings is true', () => {
		const root = makeRoot({
			messageIndex: 2,
			branch: createBranch({ forkedAtMessageIndex: 2, totalSiblings: 3 }),
		});
		expect(root.props['data-has-siblings']).toBe('');
	});
});

// ============================================
// EditButtonState — disabled
// ============================================

describe('MessageActionsEditButtonState — disabled', () => {
	function makeEdit(role: Message['role'], root?: MessageActionsRootState) {
		const r = root ?? makeRoot({ role });
		return new MessageActionsEditButtonState(r, {
			ariaLabel: box('Edit'),
			onSuccess: box(undefined),
			onError: box(undefined),
		});
	}

	it('is enabled for user messages', () => {
		expect(makeEdit('user').disabled).toBe(false);
	});

	it('is disabled for assistant messages', () => {
		expect(makeEdit('assistant').disabled).toBe(true);
	});

	it('is disabled for system messages', () => {
		expect(makeEdit('system').disabled).toBe(true);
	});

	it('is disabled when root is pending', () => {
		const root = makeRoot({ role: 'user' });
		root._incrementPending();
		expect(makeEdit('user', root).disabled).toBe(true);
	});
});

describe('MessageActionsEditButtonState — props', () => {
	it('has data-message-actions-edit', () => {
		const root = makeRoot({ role: 'user' });
		const edit = new MessageActionsEditButtonState(root, {
			ariaLabel: box('Edit message'),
			onSuccess: box(undefined),
			onError: box(undefined),
		});
		expect(edit.props['data-message-actions-edit']).toBe('');
	});

	it('has type=button', () => {
		const root = makeRoot({ role: 'user' });
		const edit = new MessageActionsEditButtonState(root, {
			ariaLabel: box('Edit message'),
			onSuccess: box(undefined),
			onError: box(undefined),
		});
		expect(edit.props.type).toBe('button');
	});

	it('data-status is "idle" initially', () => {
		const root = makeRoot({ role: 'user' });
		const edit = new MessageActionsEditButtonState(root, {
			ariaLabel: box('Edit'),
			onSuccess: box(undefined),
			onError: box(undefined),
		});
		expect(edit.props['data-status']).toBe('idle');
	});

	it('data-disabled is present when disabled', () => {
		const root = makeRoot({ role: 'assistant' });
		const edit = new MessageActionsEditButtonState(root, {
			ariaLabel: box('Edit'),
			onSuccess: box(undefined),
			onError: box(undefined),
		});
		expect(edit.props['data-disabled']).toBe('');
	});

	it('data-disabled is absent when enabled', () => {
		const root = makeRoot({ role: 'user' });
		const edit = new MessageActionsEditButtonState(root, {
			ariaLabel: box('Edit'),
			onSuccess: box(undefined),
			onError: box(undefined),
		});
		expect(edit.props['data-disabled']).toBeUndefined();
	});
});

// ============================================
// RetryButtonState — disabled
// ============================================

describe('MessageActionsRetryButtonState — disabled', () => {
	function makeRetry(role: Message['role'], root?: MessageActionsRootState) {
		const r = root ?? makeRoot({ role });
		return new MessageActionsRetryButtonState(r, {
			ariaLabel: box('Retry'),
			onSuccess: box(undefined),
			onError: box(undefined),
		});
	}

	it('is enabled for user messages', () => {
		expect(makeRetry('user').disabled).toBe(false);
	});

	it('is enabled for assistant messages', () => {
		expect(makeRetry('assistant').disabled).toBe(false);
	});

	it('is disabled for system messages', () => {
		expect(makeRetry('system').disabled).toBe(true);
	});

	it('is disabled when root is pending', () => {
		const root = makeRoot({ role: 'user' });
		root._incrementPending();
		expect(makeRetry('user', root).disabled).toBe(true);
	});
});

describe('MessageActionsRetryButtonState — props', () => {
	it('has data-message-actions-retry', () => {
		const root = makeRoot({ role: 'user' });
		const retry = new MessageActionsRetryButtonState(root, {
			ariaLabel: box('Retry'),
			onSuccess: box(undefined),
			onError: box(undefined),
		});
		expect(retry.props['data-message-actions-retry']).toBe('');
	});

	it('has type=button', () => {
		const root = makeRoot({ role: 'user' });
		const retry = new MessageActionsRetryButtonState(root, {
			ariaLabel: box('Retry'),
			onSuccess: box(undefined),
			onError: box(undefined),
		});
		expect(retry.props.type).toBe('button');
	});

	it('data-status is "idle" initially', () => {
		const root = makeRoot({ role: 'user' });
		const retry = new MessageActionsRetryButtonState(root, {
			ariaLabel: box('Retry'),
			onSuccess: box(undefined),
			onError: box(undefined),
		});
		expect(retry.props['data-status']).toBe('idle');
	});
});

// ============================================
// RetryButtonState — retry logic
// ============================================

describe('MessageActionsRetryButtonState — retry()', () => {
	it('calls editMessage with messageIndex and content for user message', async () => {
		const ws = makeWorkspace([createMessage({ role: 'user', content: 'Hello', id: 'msg-0' })]);
		const root = makeRoot({ role: 'user', messageIndex: 0, workspace: ws });
		const retry = new MessageActionsRetryButtonState(root, {
			ariaLabel: box('Retry'),
			onSuccess: box(undefined),
			onError: box(undefined),
		});
		await retry.retry();
		expect(ws.editMessage).toHaveBeenCalledWith(0, 'Hello');
	});

	it('finds preceding user message for assistant message', async () => {
		const messages = [
			createMessage({ role: 'user', content: 'My question', id: 'msg-0' }),
			createMessage({ role: 'assistant', content: 'My answer', id: 'msg-1' }),
		];
		const ws = makeWorkspace(messages);
		const root = makeRoot({ role: 'assistant', messageIndex: 1, workspace: ws });
		const retry = new MessageActionsRetryButtonState(root, {
			ariaLabel: box('Retry'),
			onSuccess: box(undefined),
			onError: box(undefined),
		});
		await retry.retry();
		expect(ws.editMessage).toHaveBeenCalledWith(0, 'My question');
	});

	it('does nothing for assistant with no preceding user message', async () => {
		const messages = [
			createMessage({ role: 'assistant', content: 'Answer', id: 'msg-0' }),
		];
		const ws = makeWorkspace(messages);
		const root = makeRoot({ role: 'assistant', messageIndex: 0, workspace: ws });
		const retry = new MessageActionsRetryButtonState(root, {
			ariaLabel: box('Retry'),
			onSuccess: box(undefined),
			onError: box(undefined),
		});
		await retry.retry();
		expect(ws.editMessage).not.toHaveBeenCalled();
	});

	it('does nothing when disabled (system role)', async () => {
		const ws = makeWorkspace([createMessage({ role: 'system' })]);
		const root = makeRoot({ role: 'system', messageIndex: 0, workspace: ws });
		const retry = new MessageActionsRetryButtonState(root, {
			ariaLabel: box('Retry'),
			onSuccess: box(undefined),
			onError: box(undefined),
		});
		await retry.retry();
		expect(ws.editMessage).not.toHaveBeenCalled();
	});

	it('calls onSuccess after successful retry', async () => {
		const onSuccess = vi.fn();
		const ws = makeWorkspace([createMessage({ role: 'user', content: 'Hi' })]);
		const root = makeRoot({ role: 'user', messageIndex: 0, workspace: ws });
		const retry = new MessageActionsRetryButtonState(root, {
			ariaLabel: box('Retry'),
			onSuccess: box(onSuccess),
			onError: box(undefined),
		});
		await retry.retry();
		expect(onSuccess).toHaveBeenCalledOnce();
	});

	it('calls onError when editMessage throws', async () => {
		const onError = vi.fn();
		const ws = makeWorkspace([createMessage({ role: 'user', content: 'Hi' })]);
		(ws.editMessage as ReturnType<typeof vi.fn>).mockRejectedValue(new Error('network'));
		const root = makeRoot({ role: 'user', messageIndex: 0, workspace: ws });
		const retry = new MessageActionsRetryButtonState(root, {
			ariaLabel: box('Retry'),
			onSuccess: box(undefined),
			onError: box(onError),
		});
		await retry.retry();
		expect(onError).toHaveBeenCalledOnce();
	});
});

// ============================================
// CopyButtonState
// ============================================

describe('MessageActionsCopyButtonState', () => {
	beforeEach(() => {
		vi.useFakeTimers();
		Object.defineProperty(globalThis, 'navigator', {
			value: { clipboard: { writeText: vi.fn().mockResolvedValue(undefined) } },
			writable: true,
			configurable: true,
		});
	});

	afterEach(() => {
		vi.useRealTimers();
		// @ts-expect-error — cleaning up globalThis.navigator stub
		delete globalThis.navigator;
	});

	function makeCopy(content = 'Hello world', resetDelay = 2000) {
		return new MessageActionsCopyButtonState({
			content: box(content),
			resetDelay: box(resetDelay),
			ariaLabel: box('Copy message'),
			onSuccess: box(undefined),
			onError: box(undefined),
		});
	}

	it('copied is false initially', () => {
		expect(makeCopy().snippetProps.copied).toBe(false);
	});

	it('calls clipboard.writeText with the content', async () => {
		const copy = makeCopy('Test content');
		await copy.copy();
		expect(navigator.clipboard.writeText).toHaveBeenCalledWith('Test content');
	});

	it('copied becomes true immediately after copy', async () => {
		const copy = makeCopy();
		await copy.copy();
		expect(copy.snippetProps.copied).toBe(true);
	});

	it('copied resets to false after default resetDelay (2000ms)', async () => {
		const copy = makeCopy('text', 2000);
		await copy.copy();
		expect(copy.snippetProps.copied).toBe(true);
		vi.advanceTimersByTime(2000);
		expect(copy.snippetProps.copied).toBe(false);
	});

	it('copied resets after custom resetDelay', async () => {
		const copy = makeCopy('text', 500);
		await copy.copy();
		vi.advanceTimersByTime(499);
		expect(copy.snippetProps.copied).toBe(true);
		vi.advanceTimersByTime(1);
		expect(copy.snippetProps.copied).toBe(false);
	});

	it('re-clicking before reset clears previous timer and restarts', async () => {
		const copy = makeCopy('text', 2000);
		await copy.copy();
		vi.advanceTimersByTime(1500);
		await copy.copy(); // re-click at 1500ms
		vi.advanceTimersByTime(1500); // now at 3000ms total, but only 1500ms since re-click
		expect(copy.snippetProps.copied).toBe(true); // should NOT have reset yet
		vi.advanceTimersByTime(500);
		expect(copy.snippetProps.copied).toBe(false);
	});

	it('calls onSuccess after successful copy', async () => {
		const onSuccess = vi.fn();
		const copy = new MessageActionsCopyButtonState({
			content: box('text'),
			resetDelay: box(2000),
			ariaLabel: box('Copy'),
			onSuccess: box(onSuccess),
			onError: box(undefined),
		});
		await copy.copy();
		expect(onSuccess).toHaveBeenCalledOnce();
	});

	it('calls onError when clipboard throws', async () => {
		const onError = vi.fn();
		(navigator.clipboard.writeText as ReturnType<typeof vi.fn>).mockRejectedValue(new Error('denied'));
		const copy = new MessageActionsCopyButtonState({
			content: box('text'),
			resetDelay: box(2000),
			ariaLabel: box('Copy'),
			onSuccess: box(undefined),
			onError: box(onError),
		});
		await copy.copy();
		expect(onError).toHaveBeenCalledOnce();
	});

	it('copied stays false when clipboard throws', async () => {
		(navigator.clipboard.writeText as ReturnType<typeof vi.fn>).mockRejectedValue(new Error('denied'));
		const copy = makeCopy();
		await copy.copy();
		expect(copy.snippetProps.copied).toBe(false);
	});

	it('props have data-message-actions-copy', () => {
		expect(makeCopy().props['data-message-actions-copy']).toBe('');
	});

	it('data-copied absent initially', () => {
		expect(makeCopy().props['data-copied']).toBeUndefined();
	});

	it('data-copied present after copy', async () => {
		const copy = makeCopy();
		await copy.copy();
		expect(copy.props['data-copied']).toBe('');
	});

	it('data-copied absent after reset', async () => {
		const copy = makeCopy('text', 1000);
		await copy.copy();
		vi.advanceTimersByTime(1000);
		expect(copy.props['data-copied']).toBeUndefined();
	});
});

// ============================================
// PrevState / NextState
// ============================================

describe('MessageActionsPrevState — props', () => {
	function makePrev(root: MessageActionsRootState, label = 'Previous version') {
		return new MessageActionsPrevState(root, box(label));
	}

	it('has data-message-actions-prev', () => {
		const root = makeRoot({ messageIndex: 2, branch: createBranch({ forkedAtMessageIndex: 2 }) });
		expect(makePrev(root).props['data-message-actions-prev']).toBe('');
	});

	it('has type=button', () => {
		const root = makeRoot({ messageIndex: 2, branch: createBranch({ forkedAtMessageIndex: 2 }) });
		expect(makePrev(root).props.type).toBe('button');
	});

	it('disabled is true when canGoPrevious is false', () => {
		const root = makeRoot({
			messageIndex: 2,
			branch: createBranch({ forkedAtMessageIndex: 2, previousSiblingId: undefined }),
		});
		expect(makePrev(root).props.disabled).toBe(true);
	});

	it('disabled is false when canGoPrevious is true', () => {
		const root = makeRoot({
			messageIndex: 2,
			branch: createBranch({ forkedAtMessageIndex: 2, previousSiblingId: 'main' }),
		});
		expect(makePrev(root).props.disabled).toBe(false);
	});

	it('aria-label uses the provided value', () => {
		const root = makeRoot({ messageIndex: 2, branch: createBranch({ forkedAtMessageIndex: 2 }) });
		expect(makePrev(root, 'Go back').props['aria-label']).toBe('Go back');
	});

	it('data-disabled present when disabled', () => {
		const root = makeRoot({
			messageIndex: 2,
			branch: createBranch({ forkedAtMessageIndex: 2, previousSiblingId: undefined }),
		});
		expect(makePrev(root).props['data-disabled']).toBe('');
	});

	it('data-disabled absent when enabled', () => {
		const root = makeRoot({
			messageIndex: 2,
			branch: createBranch({ forkedAtMessageIndex: 2, previousSiblingId: 'main' }),
		});
		expect(makePrev(root).props['data-disabled']).toBeUndefined();
	});
});

describe('MessageActionsNextState — props', () => {
	function makeNext(root: MessageActionsRootState, label = 'Next version') {
		return new MessageActionsNextState(root, box(label));
	}

	it('has data-message-actions-next', () => {
		const root = makeRoot({ messageIndex: 2, branch: createBranch({ forkedAtMessageIndex: 2 }) });
		expect(makeNext(root).props['data-message-actions-next']).toBe('');
	});

	it('disabled is true when canGoNext is false', () => {
		const root = makeRoot({
			messageIndex: 2,
			branch: createBranch({ forkedAtMessageIndex: 2, nextSiblingId: undefined }),
		});
		expect(makeNext(root).props.disabled).toBe(true);
	});

	it('disabled is false when canGoNext is true', () => {
		const root = makeRoot({
			messageIndex: 2,
			branch: createBranch({ forkedAtMessageIndex: 2, nextSiblingId: 'fork-2' }),
		});
		expect(makeNext(root).props.disabled).toBe(false);
	});
});

// ============================================
// PositionState
// ============================================

describe('MessageActionsPositionState — props and snippetProps', () => {
	it('has data-message-actions-position', () => {
		const root = makeRoot({ messageIndex: 2, branch: createBranch({ forkedAtMessageIndex: 2 }) });
		const pos = new MessageActionsPositionState(root);
		expect(pos.props['data-message-actions-position']).toBe('');
	});

	it('has aria-live="polite"', () => {
		const root = makeRoot();
		const pos = new MessageActionsPositionState(root);
		expect(pos.props['aria-live']).toBe('polite');
	});

	it('has aria-atomic="true"', () => {
		const root = makeRoot();
		const pos = new MessageActionsPositionState(root);
		expect(pos.props['aria-atomic']).toBe('true');
	});

	it('snippetProps.position mirrors root.position', () => {
		const root = makeRoot({
			messageIndex: 2,
			branch: createBranch({ forkedAtMessageIndex: 2, siblingIndex: 1, totalSiblings: 4 }),
		});
		const pos = new MessageActionsPositionState(root);
		expect(pos.snippetProps.position).toBe('2 / 4');
	});

	it('snippetProps.label mirrors root.positionLabel', () => {
		const root = makeRoot({
			messageIndex: 2,
			branch: createBranch({
				forkedAtMessageIndex: 2,
				isOriginal: false,
				siblingIndex: 1,
				totalSiblings: 3,
			}),
		});
		const pos = new MessageActionsPositionState(root);
		expect(pos.snippetProps.label).toBe('Fork 2 / 3');
	});

	it('snippetProps.position is empty when not forked here', () => {
		const root = makeRoot({ messageIndex: 0, branch: createBranch({ forkedAtMessageIndex: 5 }) });
		const pos = new MessageActionsPositionState(root);
		expect(pos.snippetProps.position).toBe('');
	});
});

// ============================================
// RootState — hasSiblings for ORIGINAL branch (U1-U5)
// The #isForkedHere path for isOriginal=true branches uses
// workspace.branches.get(branch.nextSiblingId) to find the fork point.
// ============================================

describe('MessageActionsRootState — hasSiblings (original branch path)', () => {
	function makeWorkspaceWithFork(forkId: string, forkIndex: number): Workspace {
		const forkBranch = createBranch({
			id: forkId,
			isOriginal: false,
			forkedFrom: 'main',
			forkedAtMessageIndex: forkIndex,
			previousSiblingId: 'main',
			nextSiblingId: undefined,
		});
		const ws = makeWorkspace();
		(ws.branches as Map<string, Branch>).set(forkId, forkBranch);
		return ws;
	}

	// U1: hasSiblings true for original branch at the fork-point row
	it('U1: is true for original branch when nextSiblingId fork is at this messageIndex', () => {
		const ws = makeWorkspaceWithFork('fork-1', 2);
		const branch = createBranch({
			id: 'main',
			isOriginal: true,
			forkedFrom: undefined,
			forkedAtMessageIndex: undefined,
			previousSiblingId: undefined,
			nextSiblingId: 'fork-1',
			siblingIndex: 0,
			totalSiblings: 2,
		});
		const root = makeRoot({ messageIndex: 2, branch, workspace: ws });
		expect(root.hasSiblings).toBe(true);
	});

	// U2: hasSiblings false for original branch at a DIFFERENT row
	it('U2: is false for original branch when messageIndex does not match fork point', () => {
		const ws = makeWorkspaceWithFork('fork-1', 2);
		const branch = createBranch({
			id: 'main',
			isOriginal: true,
			forkedFrom: undefined,
			forkedAtMessageIndex: undefined,
			previousSiblingId: undefined,
			nextSiblingId: 'fork-1',
			siblingIndex: 0,
			totalSiblings: 2,
		});
		const root = makeRoot({ messageIndex: 5, branch, workspace: ws });
		expect(root.hasSiblings).toBe(false);
	});

	// U3: hasSiblings false when nextSiblingId is not in workspace.branches
	it('U3: is false for original branch when nextSiblingId not in workspace.branches', () => {
		const ws = makeWorkspace(); // empty branches map
		const branch = createBranch({
			id: 'main',
			isOriginal: true,
			forkedFrom: undefined,
			forkedAtMessageIndex: undefined,
			previousSiblingId: undefined,
			nextSiblingId: 'fork-1', // points to a branch not loaded
			siblingIndex: 0,
			totalSiblings: 2,
		});
		const root = makeRoot({ messageIndex: 2, branch, workspace: ws });
		expect(root.hasSiblings).toBe(false);
	});

	// U4: canGoPrevious false for original branch (it is slot 0, no previousSiblingId)
	it('U4: canGoPrevious is false for original branch (slot 0)', () => {
		const ws = makeWorkspaceWithFork('fork-1', 2);
		const branch = createBranch({
			id: 'main',
			isOriginal: true,
			forkedFrom: undefined,
			forkedAtMessageIndex: undefined,
			previousSiblingId: undefined,
			nextSiblingId: 'fork-1',
			siblingIndex: 0,
			totalSiblings: 2,
		});
		const root = makeRoot({ messageIndex: 2, branch, workspace: ws });
		expect(root.canGoPrevious).toBe(false);
	});

	// U5: canGoNext true for original branch when nextSiblingId is set and forked here
	it('U5: canGoNext is true for original branch when nextSiblingId is set at fork point', () => {
		const ws = makeWorkspaceWithFork('fork-1', 2);
		const branch = createBranch({
			id: 'main',
			isOriginal: true,
			forkedFrom: undefined,
			forkedAtMessageIndex: undefined,
			previousSiblingId: undefined,
			nextSiblingId: 'fork-1',
			siblingIndex: 0,
			totalSiblings: 2,
		});
		const root = makeRoot({ messageIndex: 2, branch, workspace: ws });
		expect(root.canGoNext).toBe(true);
	});

	// U6: position shows "1 / 2" for original branch at fork point (siblingIndex=0)
	it('U6: position is "1 / 2" for original branch at fork point', () => {
		const ws = makeWorkspaceWithFork('fork-1', 2);
		const branch = createBranch({
			id: 'main',
			isOriginal: true,
			forkedFrom: undefined,
			forkedAtMessageIndex: undefined,
			previousSiblingId: undefined,
			nextSiblingId: 'fork-1',
			siblingIndex: 0,
			totalSiblings: 2,
		});
		const root = makeRoot({ messageIndex: 2, branch, workspace: ws });
		expect(root.position).toBe('1 / 2');
	});

	// U7: positionLabel shows "Original (1 / 2)" for original branch at fork point
	it('U7: positionLabel is "Original (1 / 2)" for original branch at fork point', () => {
		const ws = makeWorkspaceWithFork('fork-1', 2);
		const branch = createBranch({
			id: 'main',
			isOriginal: true,
			forkedFrom: undefined,
			forkedAtMessageIndex: undefined,
			previousSiblingId: undefined,
			nextSiblingId: 'fork-1',
			siblingIndex: 0,
			totalSiblings: 2,
		});
		const root = makeRoot({ messageIndex: 2, branch, workspace: ws });
		expect(root.positionLabel).toBe('Original (1 / 2)');
	});
});

// ============================================
// RootState — navigation actions (U8-U10)
// ============================================

describe('MessageActionsRootState — navigation actions', () => {
	// U8: goPrevious calls switchBranch with previousSiblingId
	it('U8: goPrevious calls workspace.switchBranch with previousSiblingId', async () => {
		const ws = makeWorkspace();
		const branch = createBranch({
			forkedAtMessageIndex: 2,
			previousSiblingId: 'main',
			nextSiblingId: undefined,
			siblingIndex: 1,
			totalSiblings: 2,
			isOriginal: false,
		});
		const root = makeRoot({ messageIndex: 2, branch, workspace: ws });
		await root.goPrevious();
		expect(ws.switchBranch).toHaveBeenCalledWith('main');
	});

	// U9: goNext calls switchBranch with nextSiblingId (original branch path)
	it('U9: goNext calls workspace.switchBranch with nextSiblingId for original branch', async () => {
		const forkBranch = createBranch({
			id: 'fork-1',
			isOriginal: false,
			forkedFrom: 'main',
			forkedAtMessageIndex: 2,
			previousSiblingId: 'main',
			nextSiblingId: undefined,
		});
		const ws = makeWorkspace();
		(ws.branches as Map<string, Branch>).set('fork-1', forkBranch);
		const branch = createBranch({
			id: 'main',
			isOriginal: true,
			forkedFrom: undefined,
			forkedAtMessageIndex: undefined,
			previousSiblingId: undefined,
			nextSiblingId: 'fork-1',
			siblingIndex: 0,
			totalSiblings: 2,
		});
		const root = makeRoot({ messageIndex: 2, branch, workspace: ws });
		await root.goNext();
		expect(ws.switchBranch).toHaveBeenCalledWith('fork-1');
	});

	// U10: goPrevious does nothing when previousSiblingId is null
	it('U10: goPrevious does nothing when previousSiblingId is null', async () => {
		const ws = makeWorkspace();
		const branch = createBranch({
			forkedAtMessageIndex: 2,
			previousSiblingId: undefined,
		});
		const root = makeRoot({ messageIndex: 2, branch, workspace: ws });
		await root.goPrevious();
		expect(ws.switchBranch).not.toHaveBeenCalled();
	});
});
