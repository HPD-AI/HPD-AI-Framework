/**
 * MessageActions State Management
 *
 * Compound headless component for message-level actions: Edit, Retry, and
 * branch navigation (Prev / Next / Position).
 *
 * The branch navigator becomes active at the user message row where siblings diverge.
 * Siblings are branches that share the same preceding context (forked at messageIndex - 1).
 * The ◀ ▶ counter is naturally scoped to the right bubble without any manual condition
 * in the consumer template.
 *
 * Parts: Root, EditButton, RetryButton, Prev, Next, Position
 *
 * @example
 * ```svelte
 * <MessageActions.Root {workspace} {messageIndex} role="user" branch={workspace.activeBranch}>
 *   <MessageActions.EditButton>
 *     {#snippet children({ edit, status })}
 *       <button onclick={() => edit(draft)} disabled={status === 'pending'}>Edit</button>
 *     {/snippet}
 *   </MessageActions.EditButton>
 *   <MessageActions.RetryButton>
 *     {#snippet children({ retry, status })}
 *       <button onclick={retry} disabled={status === 'pending'}>Retry</button>
 *     {/snippet}
 *   </MessageActions.RetryButton>
 *   {#snippet children({ hasSiblings })}
 *     {#if hasSiblings}
 *       <MessageActions.Prev />
 *       <MessageActions.Position />
 *       <MessageActions.Next />
 *     {/if}
 *   {/snippet}
 * </MessageActions.Root>
 * ```
 */

import { Context } from 'runed';
import { type ReadableBox } from 'svelte-toolbelt';
import { createHPDAttrs, boolToEmptyStrOrUndef } from '$lib/internal/attrs.js';
import type { Workspace } from '../workspace/types.ts';
import type { MessageRole } from '../agent/types.ts';
import type { Branch } from '@hpd/hpd-agent-client';
import type {
	MessageActionStatus,
	MessageActionsRootHTMLProps,
	MessageActionsRootSnippetProps,
	MessageActionsEditButtonHTMLProps,
	MessageActionsEditButtonSnippetProps,
	MessageActionsRetryButtonHTMLProps,
	MessageActionsRetryButtonSnippetProps,
	MessageActionsCopyButtonHTMLProps,
	MessageActionsCopyButtonSnippetProps,
	MessageActionsPrevHTMLProps,
	MessageActionsNextHTMLProps,
	MessageActionsPositionHTMLProps,
	MessageActionsPositionSnippetProps,
} from './types.js';

// ============================================
// Data Attributes
// ============================================

export const messageActionsAttrs = createHPDAttrs({
	component: 'message-actions',
	parts: ['root', 'edit', 'retry', 'prev', 'next', 'position'] as const,
});

// ============================================
// Root Context
// ============================================

const MessageActionsRootContext = new Context<MessageActionsRootState>('MessageActions.Root');

// ============================================
// Root State
// ============================================

interface MessageActionsRootStateOpts {
	workspace: ReadableBox<Workspace>;
	messageIndex: ReadableBox<number>;
	role: ReadableBox<MessageRole>;
	branch?: ReadableBox<Branch | null | undefined>;
}

export class MessageActionsRootState {
	readonly #opts: MessageActionsRootStateOpts;

	// Tracks how many actions are currently in-flight
	#pendingCount = $state(0);

	constructor(opts: MessageActionsRootStateOpts) {
		this.#opts = opts;
	}

	static create(opts: MessageActionsRootStateOpts): MessageActionsRootState {
		return MessageActionsRootContext.set(new MessageActionsRootState(opts));
	}

	// ============================================
	// Derived State
	// ============================================

	readonly workspace = $derived.by(() => this.#opts.workspace.current);
	readonly messageIndex = $derived.by(() => this.#opts.messageIndex.current);
	readonly role = $derived.by(() => this.#opts.role.current);
	readonly pending = $derived.by(() => this.#pendingCount > 0);

	// (branch prop kept for API compatibility but no longer drives sibling detection)
	readonly #branch = $derived.by(() => this.#opts.branch?.current ?? null);

	// Sibling group at this message row — computed purely from workspace.branches.
	//
	// A sibling group exists at messageIndex when there are forks that share the
	// same preceding context: forkedAtMessageIndex === messageIndex - 1.
	// We find any such fork, look up its source (the original branch in the group),
	// then collect all forks at that same (source, forkAtIndex) point.
	//
	// This is entirely independent of which branch is currently active — so the
	// switcher stays visible even when you navigate to a branch whose fork point
	// is at a different message index.
	readonly #siblingsAtThisRow = $derived.by(() => {
		const forkAtIndex = this.messageIndex - 1;
		const all = this.workspace.branches;

		// Find any fork at this row to identify the source branch.
		const anyFork = Array.from(all.values()).find(
			b => !b.isOriginal && b.forkedAtMessageIndex === forkAtIndex
		);
		if (!anyFork) return [];

		const sourceId = anyFork.forkedFrom!;
		const source = all.get(sourceId);
		const forks = Array.from(all.values()).filter(
			b => b.forkedFrom === sourceId && b.forkedAtMessageIndex === forkAtIndex
		);
		const group = source ? [source, ...forks] : forks;
		return group.sort((a, b) => a.siblingIndex - b.siblingIndex);
	});

	// The currently active sibling in this row's group.
	//
	// Resolution order:
	// 1. If the branch prop is a direct member of this row's sibling group, use it.
	//    This handles the common case where MessageActions.Root is rendered with
	//    branch={workspace.activeBranch} and the active branch is in this group.
	// 2. Otherwise find which sibling is an ancestor-or-equal of workspace.activeBranch.
	//    This handles the "stateless switcher" case: even when the active branch is
	//    at a deeper fork (different message), the switcher at this row shows which
	//    path the user took through this group.
	// 3. Fall back to the first sibling (original branch) if no match.
	readonly #activeSiblingInRow = $derived.by(() => {
		const siblings = this.#siblingsAtThisRow;
		if (siblings.length === 0) return null;

		// 1. Branch prop direct match
		const branchProp = this.#branch;
		if (branchProp) {
			const direct = siblings.find(b => b.id === branchProp.id);
			if (direct) return direct;
		}

		// 2. workspace.activeBranch ancestry match
		const activeBranchId = this.workspace.activeBranchId;
		const activeBranch = this.workspace.activeBranch;
		if (activeBranchId && activeBranch) {
			const direct = siblings.find(b => b.id === activeBranchId);
			if (direct) return direct;
			const ancestorIds = new Set(Object.values(activeBranch.ancestors ?? {}));
			const ancestor = siblings.find(b => ancestorIds.has(b.id));
			if (ancestor) return ancestor;
		}

		// 3. Default to original (first in group)
		return siblings[0];
	});

	readonly #isForkedHere = $derived.by(() => this.#siblingsAtThisRow.length > 1);

	readonly hasSiblings = $derived.by(() => this.#siblingsAtThisRow.length > 1);

	readonly canGoPrevious = $derived.by(() => {
		if (!this.#isForkedHere) return false;
		const active = this.#activeSiblingInRow;
		if (!active) return false;
		const idx = this.#siblingsAtThisRow.findIndex(b => b.id === active.id);
		return idx > 0;
	});

	readonly canGoNext = $derived.by(() => {
		if (!this.#isForkedHere) return false;
		const siblings = this.#siblingsAtThisRow;
		const active = this.#activeSiblingInRow;
		if (!active) return false;
		const idx = siblings.findIndex(b => b.id === active.id);
		return idx >= 0 && idx < siblings.length - 1;
	});

	readonly position = $derived.by(() => {
		if (!this.#isForkedHere) return '';
		const siblings = this.#siblingsAtThisRow;
		const active = this.#activeSiblingInRow;
		if (!active) return '';
		const idx = siblings.findIndex(b => b.id === active.id);
		if (idx < 0) return '';
		return `${idx + 1} / ${siblings.length}`;
	});

	readonly positionLabel = $derived.by(() => {
		if (!this.#isForkedHere) return '';
		const siblings = this.#siblingsAtThisRow;
		if (siblings.length <= 1) return '';
		const active = this.#activeSiblingInRow;
		if (!active) return '';
		const idx = siblings.findIndex(b => b.id === active.id);
		if (idx < 0) return '';
		if (active.isOriginal) return `Original (1 / ${siblings.length})`;
		return `Fork ${idx + 1} / ${siblings.length}`;
	});

	// ============================================
	// Navigation actions
	// ============================================

	readonly goPrevious = async (): Promise<void> => {
		const siblings = this.#siblingsAtThisRow;
		const active = this.#activeSiblingInRow;
		if (!active) return;
		const idx = siblings.findIndex(b => b.id === active.id);
		if (idx <= 0) return;
		await this.workspace.switchBranch(siblings[idx - 1].id);
	};

	readonly goNext = async (): Promise<void> => {
		const siblings = this.#siblingsAtThisRow;
		const active = this.#activeSiblingInRow;
		if (!active) return;
		const idx = siblings.findIndex(b => b.id === active.id);
		if (idx < 0 || idx >= siblings.length - 1) return;
		await this.workspace.switchBranch(siblings[idx + 1].id);
	};

	// ============================================
	// Internal helpers for child states
	// ============================================

	_incrementPending() {
		this.#pendingCount++;
	}

	_decrementPending() {
		this.#pendingCount = Math.max(0, this.#pendingCount - 1);
	}

	// ============================================
	// Props
	// ============================================

	get props(): MessageActionsRootHTMLProps {
		return {
			'data-message-actions-root': '',
			'data-role': this.role,
			'data-message-index': this.messageIndex,
			'data-has-siblings': boolToEmptyStrOrUndef(this.hasSiblings),
		};
	}

	get snippetProps(): MessageActionsRootSnippetProps {
		return {
			role: this.role,
			messageIndex: this.messageIndex,
			pending: this.pending,
			hasSiblings: this.hasSiblings,
			position: this.position,
		};
	}
}

// ============================================
// EditButton State
// ============================================

interface MessageActionsEditButtonStateOpts {
	ariaLabel: ReadableBox<string>;
	onSuccess: ReadableBox<(() => void) | undefined>;
	onError: ReadableBox<((err: unknown) => void) | undefined>;
}

export class MessageActionsEditButtonState {
	readonly #root: MessageActionsRootState;
	readonly #opts: MessageActionsEditButtonStateOpts;

	#status = $state<MessageActionStatus>('idle');

	constructor(root: MessageActionsRootState, opts: MessageActionsEditButtonStateOpts) {
		this.#root = root;
		this.#opts = opts;
	}

	static create(opts: MessageActionsEditButtonStateOpts): MessageActionsEditButtonState {
		const root = MessageActionsRootContext.get();
		return new MessageActionsEditButtonState(root, opts);
	}

	// Only user messages can be edited
	readonly disabled = $derived.by(
		() => this.#root.role !== 'user' || this.#root.pending
	);

	readonly edit = async (newContent: string): Promise<void> => {
		if (this.disabled) return;
		this.#status = 'pending';
		this.#root._incrementPending();
		try {
			await this.#root.workspace.editMessage(this.#root.messageIndex, newContent);
			this.#status = 'idle';
			this.#opts.onSuccess.current?.();
		} catch (err) {
			this.#status = 'error';
			this.#opts.onError.current?.(err);
		} finally {
			this.#root._decrementPending();
		}
	};

	get props(): MessageActionsEditButtonHTMLProps {
		return {
			'data-message-actions-edit': '',
			'data-status': this.#status,
			'data-disabled': boolToEmptyStrOrUndef(this.disabled),
			type: 'button',
			disabled: this.disabled,
			'aria-label': this.#opts.ariaLabel.current,
		};
	}

	get snippetProps(): MessageActionsEditButtonSnippetProps {
		return {
			status: this.#status,
			disabled: this.disabled,
			edit: this.edit,
		};
	}
}

// ============================================
// RetryButton State
// ============================================

interface MessageActionsRetryButtonStateOpts {
	ariaLabel: ReadableBox<string>;
	onSuccess: ReadableBox<(() => void) | undefined>;
	onError: ReadableBox<((err: unknown) => void) | undefined>;
}

export class MessageActionsRetryButtonState {
	readonly #root: MessageActionsRootState;
	readonly #opts: MessageActionsRetryButtonStateOpts;

	#status = $state<MessageActionStatus>('idle');

	constructor(root: MessageActionsRootState, opts: MessageActionsRetryButtonStateOpts) {
		this.#root = root;
		this.#opts = opts;
	}

	static create(opts: MessageActionsRetryButtonStateOpts): MessageActionsRetryButtonState {
		const root = MessageActionsRootContext.get();
		return new MessageActionsRetryButtonState(root, opts);
	}

	// Retry is available for user messages (re-send same content)
	// and assistant messages (retry the preceding user message)
	// System messages cannot be retried
	readonly disabled = $derived.by(
		() => this.#root.role === 'system' || this.#root.pending
	);

	readonly retry = async (): Promise<void> => {
		if (this.disabled) return;

		const { workspace, messageIndex, role } = this.#root;
		const state = workspace.state;
		if (!state) return;

		// For a user message: re-send it (fork at same index, same content)
		// For an assistant message: find the preceding user message and re-send that
		let targetIndex = messageIndex;
		if (role === 'assistant') {
			const messages = state.messages;
			let found = -1;
			for (let i = messageIndex - 1; i >= 0; i--) {
				if (messages[i].role === 'user') {
					found = i;
					break;
				}
			}
			if (found === -1) return;
			targetIndex = found;
		}

		const messages = state.messages;
		const targetMessage = messages[targetIndex];
		if (!targetMessage) return;

		this.#status = 'pending';
		this.#root._incrementPending();
		try {
			await workspace.editMessage(targetIndex, targetMessage.content);
			this.#status = 'idle';
			this.#opts.onSuccess.current?.();
		} catch (err) {
			this.#status = 'error';
			this.#opts.onError.current?.(err);
		} finally {
			this.#root._decrementPending();
		}
	};

	get props(): MessageActionsRetryButtonHTMLProps {
		return {
			'data-message-actions-retry': '',
			'data-status': this.#status,
			'data-disabled': boolToEmptyStrOrUndef(this.disabled),
			type: 'button',
			disabled: this.disabled,
			'aria-label': this.#opts.ariaLabel.current,
		};
	}

	get snippetProps(): MessageActionsRetryButtonSnippetProps {
		return {
			status: this.#status,
			disabled: this.disabled,
			retry: this.retry,
		};
	}
}

// ============================================
// CopyButton State
// ============================================

interface MessageActionsCopyButtonStateOpts {
	content: ReadableBox<string>;
	resetDelay: ReadableBox<number>;
	ariaLabel: ReadableBox<string>;
	onSuccess: ReadableBox<(() => void) | undefined>;
	onError: ReadableBox<((err: unknown) => void) | undefined>;
}

export class MessageActionsCopyButtonState {
	readonly #opts: MessageActionsCopyButtonStateOpts;

	#copied = $state(false);
	#resetTimer: ReturnType<typeof setTimeout> | null = null;

	constructor(opts: MessageActionsCopyButtonStateOpts) {
		this.#opts = opts;
	}

	static create(opts: MessageActionsCopyButtonStateOpts): MessageActionsCopyButtonState {
		return new MessageActionsCopyButtonState(opts);
	}

	readonly copy = async (): Promise<void> => {
		try {
			await navigator.clipboard.writeText(this.#opts.content.current);
			if (this.#resetTimer !== null) clearTimeout(this.#resetTimer);
			this.#copied = true;
			this.#resetTimer = setTimeout(() => {
				this.#copied = false;
				this.#resetTimer = null;
			}, this.#opts.resetDelay.current);
			this.#opts.onSuccess.current?.();
		} catch (err) {
			this.#opts.onError.current?.(err);
		}
	};

	get props(): MessageActionsCopyButtonHTMLProps {
		return {
			'data-message-actions-copy': '',
			'data-copied': boolToEmptyStrOrUndef(this.#copied),
			type: 'button',
			'aria-label': this.#opts.ariaLabel.current,
		};
	}

	get snippetProps(): MessageActionsCopyButtonSnippetProps {
		return {
			copied: this.#copied,
			copy: this.copy,
		};
	}
}

// ============================================
// Prev State
// ============================================

export class MessageActionsPrevState {
	readonly #root: MessageActionsRootState;
	readonly #ariaLabel: ReadableBox<string>;

	constructor(root: MessageActionsRootState, ariaLabel: ReadableBox<string>) {
		this.#root = root;
		this.#ariaLabel = ariaLabel;
	}

	static create(ariaLabel: ReadableBox<string>): MessageActionsPrevState {
		const root = MessageActionsRootContext.get();
		return new MessageActionsPrevState(root, ariaLabel);
	}

	get props(): MessageActionsPrevHTMLProps {
		return {
			'data-message-actions-prev': '',
			'data-disabled': boolToEmptyStrOrUndef(!this.#root.canGoPrevious),
			type: 'button',
			disabled: !this.#root.canGoPrevious,
			'aria-label': this.#ariaLabel.current,
			onclick: this.#root.goPrevious,
		};
	}

	get snippetProps() {
		return { goPrevious: this.#root.goPrevious };
	}
}

// ============================================
// Next State
// ============================================

export class MessageActionsNextState {
	readonly #root: MessageActionsRootState;
	readonly #ariaLabel: ReadableBox<string>;

	constructor(root: MessageActionsRootState, ariaLabel: ReadableBox<string>) {
		this.#root = root;
		this.#ariaLabel = ariaLabel;
	}

	static create(ariaLabel: ReadableBox<string>): MessageActionsNextState {
		const root = MessageActionsRootContext.get();
		return new MessageActionsNextState(root, ariaLabel);
	}

	get props(): MessageActionsNextHTMLProps {
		return {
			'data-message-actions-next': '',
			'data-disabled': boolToEmptyStrOrUndef(!this.#root.canGoNext),
			type: 'button',
			onclick: this.#root.goNext,
			disabled: !this.#root.canGoNext,
			'aria-label': this.#ariaLabel.current,
		};
	}

	get snippetProps() {
		return { goNext: this.#root.goNext };
	}
}

// ============================================
// Position State
// ============================================

export class MessageActionsPositionState {
	readonly #root: MessageActionsRootState;

	constructor(root: MessageActionsRootState) {
		this.#root = root;
	}

	static create(): MessageActionsPositionState {
		const root = MessageActionsRootContext.get();
		return new MessageActionsPositionState(root);
	}

	get props(): MessageActionsPositionHTMLProps {
		return {
			'data-message-actions-position': '',
			'aria-live': 'polite',
			'aria-atomic': 'true',
		};
	}

	get snippetProps(): MessageActionsPositionSnippetProps {
		return {
			position: this.#root.position,
			label: this.#root.positionLabel,
		};
	}
}
