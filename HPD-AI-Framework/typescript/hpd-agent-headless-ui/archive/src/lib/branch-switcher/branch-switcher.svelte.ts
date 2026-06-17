/**
 * ThreadSwitcher State Management
 *
 * Compound headless component for navigating sibling threads.
 * Displays current position (e.g., "2 / 4") and wires prev/next buttons.
 *
 * Parts: Root, Prev, Next, Position
 *
 * @example
 * ```svelte
 * <ThreadSwitcher.Root thread={threadManager.activeThread}>
 *   {#snippet children({ hasSiblings })}
 *     {#if hasSiblings}
 *       <ThreadSwitcher.Prev onclick={() => threadManager.goToPreviousSibling()} />
 *       <ThreadSwitcher.Position />
 *       <ThreadSwitcher.Next onclick={() => threadManager.goToNextSibling()} />
 *     {/if}
 *   {/snippet}
 * </ThreadSwitcher.Root>
 * ```
 */

import { Context } from 'runed';
import { type ReadableBox } from 'svelte-toolbelt';
import { createHPDAttrs, boolToEmptyStrOrUndef } from '$lib/internal/attrs.js';
import type { Thread } from '@hpd-research/hpd-agent-client';
import type {
	ThreadSwitcherRootHTMLProps,
	ThreadSwitcherRootSnippetProps,
	ThreadSwitcherPrevHTMLProps,
	ThreadSwitcherNextHTMLProps,
	ThreadSwitcherPositionHTMLProps,
	ThreadSwitcherPositionSnippetProps,
} from './types.js';

// ============================================
// Data Attributes
// ============================================

export const threadSwitcherAttrs = createHPDAttrs({
	component: 'thread-switcher',
	parts: ['root', 'prev', 'next', 'position'] as const,
});

// ============================================
// Root Context
// ============================================

const ThreadSwitcherRootContext = new Context<ThreadSwitcherRootState>('ThreadSwitcher.Root');

// ============================================
// Root State
// ============================================

interface ThreadSwitcherRootStateOpts {
	thread: ReadableBox<Thread | null>;
}

export class ThreadSwitcherRootState {
	readonly #opts: ThreadSwitcherRootStateOpts;

	constructor(opts: ThreadSwitcherRootStateOpts) {
		this.#opts = opts;
	}

	static create(opts: ThreadSwitcherRootStateOpts): ThreadSwitcherRootState {
		return ThreadSwitcherRootContext.set(new ThreadSwitcherRootState(opts));
	}

	// ============================================
	// Derived State
	// ============================================

	readonly thread = $derived.by(() => this.#opts.thread.current);
	readonly canGoPrevious = $derived.by(() => this.thread?.previousSiblingId != null);
	readonly canGoNext = $derived.by(() => this.thread?.nextSiblingId != null);
	readonly hasSiblings = $derived.by(() => this.thread != null && this.thread.totalSiblings > 1);
	readonly isOriginal = $derived.by(() => this.thread?.isOriginal ?? false);

	readonly position = $derived.by(() => {
		const thread = this.thread;
		if (!thread) return '';
		return `${thread.siblingIndex + 1} / ${thread.totalSiblings}`;
	});

	readonly label = $derived.by(() => {
		const thread = this.thread;
		if (!thread || thread.totalSiblings <= 1) return '';
		if (thread.isOriginal) return `Original (1 / ${thread.totalSiblings})`;
		return `Fork ${thread.siblingIndex + 1} / ${thread.totalSiblings}`;
	});

	// ============================================
	// Props
	// ============================================

	get props(): ThreadSwitcherRootHTMLProps {
		return {
			'data-thread-switcher-root': '',
			'data-has-siblings': boolToEmptyStrOrUndef(this.hasSiblings),
		};
	}

	get snippetProps(): ThreadSwitcherRootSnippetProps {
		return {
			thread: this.thread,
			hasSiblings: this.hasSiblings,
			canGoPrevious: this.canGoPrevious,
			canGoNext: this.canGoNext,
			position: this.position,
			label: this.label,
			isOriginal: this.isOriginal,
		};
	}
}

// ============================================
// Prev State
// ============================================

export class ThreadSwitcherPrevState {
	readonly #root: ThreadSwitcherRootState;
	readonly #ariaLabel: ReadableBox<string>;

	constructor(root: ThreadSwitcherRootState, ariaLabel: ReadableBox<string>) {
		this.#root = root;
		this.#ariaLabel = ariaLabel;
	}

	static create(ariaLabel: ReadableBox<string>): ThreadSwitcherPrevState {
		const root = ThreadSwitcherRootContext.get();
		return new ThreadSwitcherPrevState(root, ariaLabel);
	}

	get props(): ThreadSwitcherPrevHTMLProps {
		return {
			'data-thread-switcher-prev': '',
			'data-disabled': boolToEmptyStrOrUndef(!this.#root.canGoPrevious),
			type: 'button',
			disabled: !this.#root.canGoPrevious,
			'aria-label': this.#ariaLabel.current,
		};
	}
}

// ============================================
// Next State
// ============================================

export class ThreadSwitcherNextState {
	readonly #root: ThreadSwitcherRootState;
	readonly #ariaLabel: ReadableBox<string>;

	constructor(root: ThreadSwitcherRootState, ariaLabel: ReadableBox<string>) {
		this.#root = root;
		this.#ariaLabel = ariaLabel;
	}

	static create(ariaLabel: ReadableBox<string>): ThreadSwitcherNextState {
		const root = ThreadSwitcherRootContext.get();
		return new ThreadSwitcherNextState(root, ariaLabel);
	}

	get props(): ThreadSwitcherNextHTMLProps {
		return {
			'data-thread-switcher-next': '',
			'data-disabled': boolToEmptyStrOrUndef(!this.#root.canGoNext),
			type: 'button',
			disabled: !this.#root.canGoNext,
			'aria-label': this.#ariaLabel.current,
		};
	}
}

// ============================================
// Position State
// ============================================

export class ThreadSwitcherPositionState {
	readonly #root: ThreadSwitcherRootState;

	constructor(root: ThreadSwitcherRootState) {
		this.#root = root;
	}

	static create(): ThreadSwitcherPositionState {
		const root = ThreadSwitcherRootContext.get();
		return new ThreadSwitcherPositionState(root);
	}

	get props(): ThreadSwitcherPositionHTMLProps {
		return {
			'data-thread-switcher-position': '',
			'aria-live': 'polite',
			'aria-atomic': 'true',
		};
	}

	get snippetProps(): ThreadSwitcherPositionSnippetProps {
		return {
			position: this.#root.position,
			label: this.#root.label,
		};
	}
}
