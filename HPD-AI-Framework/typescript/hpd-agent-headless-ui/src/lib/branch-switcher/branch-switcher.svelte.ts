/**
 * BranchSwitcher State Management
 *
 * Compound headless component for navigating sibling branches.
 * Displays current position (e.g., "2 / 4") and wires prev/next buttons.
 *
 * Parts: Root, Prev, Next, Position
 *
 * @example
 * ```svelte
 * <BranchSwitcher.Root branch={branchManager.activeBranch}>
 *   {#snippet children({ hasSiblings })}
 *     {#if hasSiblings}
 *       <BranchSwitcher.Prev onclick={() => branchManager.goToPreviousSibling()} />
 *       <BranchSwitcher.Position />
 *       <BranchSwitcher.Next onclick={() => branchManager.goToNextSibling()} />
 *     {/if}
 *   {/snippet}
 * </BranchSwitcher.Root>
 * ```
 */

import { Context } from 'runed';
import { type ReadableBox } from 'svelte-toolbelt';
import { createHPDAttrs, boolToEmptyStrOrUndef } from '$lib/internal/attrs.js';
import type { Branch } from '@hpd/hpd-agent-client';
import type {
	BranchSwitcherRootHTMLProps,
	BranchSwitcherRootSnippetProps,
	BranchSwitcherPrevHTMLProps,
	BranchSwitcherNextHTMLProps,
	BranchSwitcherPositionHTMLProps,
	BranchSwitcherPositionSnippetProps,
} from './types.js';

// ============================================
// Data Attributes
// ============================================

export const branchSwitcherAttrs = createHPDAttrs({
	component: 'branch-switcher',
	parts: ['root', 'prev', 'next', 'position'] as const,
});

// ============================================
// Root Context
// ============================================

const BranchSwitcherRootContext = new Context<BranchSwitcherRootState>('BranchSwitcher.Root');

// ============================================
// Root State
// ============================================

interface BranchSwitcherRootStateOpts {
	branch: ReadableBox<Branch | null>;
}

export class BranchSwitcherRootState {
	readonly #opts: BranchSwitcherRootStateOpts;

	constructor(opts: BranchSwitcherRootStateOpts) {
		this.#opts = opts;
	}

	static create(opts: BranchSwitcherRootStateOpts): BranchSwitcherRootState {
		return BranchSwitcherRootContext.set(new BranchSwitcherRootState(opts));
	}

	// ============================================
	// Derived State
	// ============================================

	readonly branch = $derived.by(() => this.#opts.branch.current);
	readonly canGoPrevious = $derived.by(() => this.branch?.previousSiblingId != null);
	readonly canGoNext = $derived.by(() => this.branch?.nextSiblingId != null);
	readonly hasSiblings = $derived.by(() => this.branch != null && this.branch.totalSiblings > 1);
	readonly isOriginal = $derived.by(() => this.branch?.isOriginal ?? false);

	readonly position = $derived.by(() => {
		const branch = this.branch;
		if (!branch) return '';
		return `${branch.siblingIndex + 1} / ${branch.totalSiblings}`;
	});

	readonly label = $derived.by(() => {
		const branch = this.branch;
		if (!branch || branch.totalSiblings <= 1) return '';
		if (branch.isOriginal) return `Original (1 / ${branch.totalSiblings})`;
		return `Fork ${branch.siblingIndex + 1} / ${branch.totalSiblings}`;
	});

	// ============================================
	// Props
	// ============================================

	get props(): BranchSwitcherRootHTMLProps {
		return {
			'data-branch-switcher-root': '',
			'data-has-siblings': boolToEmptyStrOrUndef(this.hasSiblings),
		};
	}

	get snippetProps(): BranchSwitcherRootSnippetProps {
		return {
			branch: this.branch,
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

export class BranchSwitcherPrevState {
	readonly #root: BranchSwitcherRootState;
	readonly #ariaLabel: ReadableBox<string>;

	constructor(root: BranchSwitcherRootState, ariaLabel: ReadableBox<string>) {
		this.#root = root;
		this.#ariaLabel = ariaLabel;
	}

	static create(ariaLabel: ReadableBox<string>): BranchSwitcherPrevState {
		const root = BranchSwitcherRootContext.get();
		return new BranchSwitcherPrevState(root, ariaLabel);
	}

	get props(): BranchSwitcherPrevHTMLProps {
		return {
			'data-branch-switcher-prev': '',
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

export class BranchSwitcherNextState {
	readonly #root: BranchSwitcherRootState;
	readonly #ariaLabel: ReadableBox<string>;

	constructor(root: BranchSwitcherRootState, ariaLabel: ReadableBox<string>) {
		this.#root = root;
		this.#ariaLabel = ariaLabel;
	}

	static create(ariaLabel: ReadableBox<string>): BranchSwitcherNextState {
		const root = BranchSwitcherRootContext.get();
		return new BranchSwitcherNextState(root, ariaLabel);
	}

	get props(): BranchSwitcherNextHTMLProps {
		return {
			'data-branch-switcher-next': '',
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

export class BranchSwitcherPositionState {
	readonly #root: BranchSwitcherRootState;

	constructor(root: BranchSwitcherRootState) {
		this.#root = root;
	}

	static create(): BranchSwitcherPositionState {
		const root = BranchSwitcherRootContext.get();
		return new BranchSwitcherPositionState(root);
	}

	get props(): BranchSwitcherPositionHTMLProps {
		return {
			'data-branch-switcher-position': '',
			'aria-live': 'polite',
			'aria-atomic': 'true',
		};
	}

	get snippetProps(): BranchSwitcherPositionSnippetProps {
		return {
			position: this.#root.position,
			label: this.#root.label,
		};
	}
}
