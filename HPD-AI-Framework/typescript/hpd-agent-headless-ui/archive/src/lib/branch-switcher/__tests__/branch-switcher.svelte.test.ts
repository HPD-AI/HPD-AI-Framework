/**
 * ThreadSwitcher Component Tests
 *
 * Browser-based tests for the ThreadSwitcher compound component.
 * Tests: data attributes, ARIA, disabled state, callbacks, position display.
 */

import { describe, it, expect, vi } from 'vitest';
import { render } from 'vitest-browser-svelte';
import { page } from 'vitest/browser';
import type { Thread } from '@hpd-research/hpd-agent-client';
import ThreadSwitcherTest from './thread-switcher-test.svelte';

// ============================================
// Helpers
// ============================================

const createThread = (overrides: Partial<Thread> = {}): Thread => ({
	id: 'main',
	sessionId: 'session-1',
	name: 'Main Thread',
	createdAt: '2024-01-01T00:00:00Z',
	lastActivity: '2024-01-01T00:10:00Z',
	messageCount: 5,
	siblingIndex: 0,
	totalSiblings: 1,
	isOriginal: true,
	childThreads: [],
	totalForks: 0,
	...overrides,
});

function setup(props: {
	thread?: Thread | null;
	onPrev?: () => void;
	onNext?: () => void;
	prevLabel?: string;
	nextLabel?: string;
} = {}) {
	render(ThreadSwitcherTest, { props } as any);

	return {
		root: page.getByTestId('root'),
		prev: page.getByTestId('prev'),
		next: page.getByTestId('next'),
		positionEl: page.getByTestId('position-el'),
		hasSiblings: page.getByTestId('has-siblings'),
		canGoPrevious: page.getByTestId('can-go-previous'),
		canGoNext: page.getByTestId('can-go-next'),
		position: page.getByTestId('position'),
		label: page.getByTestId('label'),
		isOriginal: page.getByTestId('is-original'),
	};
}

// ============================================
// Data Attributes — Root
// ============================================

describe('ThreadSwitcher — Root Data Attributes', () => {
	it('root has data-thread-switcher-root', async () => {
		const t = setup();
		await expect.element(t.root).toHaveAttribute('data-thread-switcher-root');
	});

	it('root does not have data-has-siblings when thread is null', async () => {
		const t = setup({ thread: null });
		await expect.element(t.root).not.toHaveAttribute('data-has-siblings');
	});

	it('root does not have data-has-siblings when only one sibling', async () => {
		const t = setup({ thread: createThread({ totalSiblings: 1 }) });
		await expect.element(t.root).not.toHaveAttribute('data-has-siblings');
	});

	it('root has data-has-siblings when multiple siblings', async () => {
		const t = setup({ thread: createThread({ totalSiblings: 3 }) });
		await expect.element(t.root).toHaveAttribute('data-has-siblings');
	});
});

// ============================================
// Data Attributes — Prev
// ============================================

describe('ThreadSwitcher — Prev Data Attributes', () => {
	it('prev has data-thread-switcher-prev', async () => {
		const t = setup();
		await expect.element(t.prev).toHaveAttribute('data-thread-switcher-prev');
	});

	it('prev has data-disabled when canGoPrevious is false', async () => {
		const t = setup({ thread: createThread({ previousSiblingId: undefined }) });
		await expect.element(t.prev).toHaveAttribute('data-disabled');
	});

	it('prev does not have data-disabled when canGoPrevious is true', async () => {
		const thread = createThread({ siblingIndex: 1, totalSiblings: 2, previousSiblingId: 'main', isOriginal: false });
		const t = setup({ thread });
		await expect.element(t.prev).not.toHaveAttribute('data-disabled');
	});
});

// ============================================
// Data Attributes — Next
// ============================================

describe('ThreadSwitcher — Next Data Attributes', () => {
	it('next has data-thread-switcher-next', async () => {
		const t = setup();
		await expect.element(t.next).toHaveAttribute('data-thread-switcher-next');
	});

	it('next has data-disabled when canGoNext is false', async () => {
		const t = setup({ thread: createThread({ nextSiblingId: undefined }) });
		await expect.element(t.next).toHaveAttribute('data-disabled');
	});

	it('next does not have data-disabled when canGoNext is true', async () => {
		const thread = createThread({ siblingIndex: 0, totalSiblings: 2, nextSiblingId: 'fork-1' });
		const t = setup({ thread });
		await expect.element(t.next).not.toHaveAttribute('data-disabled');
	});
});

// ============================================
// Data Attributes — Position
// ============================================

describe('ThreadSwitcher — Position Data Attributes', () => {
	it('position has data-thread-switcher-position', async () => {
		const t = setup();
		await expect.element(t.positionEl).toHaveAttribute('data-thread-switcher-position');
	});

	it('position has aria-live="polite"', async () => {
		const t = setup();
		await expect.element(t.positionEl).toHaveAttribute('aria-live', 'polite');
	});

	it('position has aria-atomic="true"', async () => {
		const t = setup();
		await expect.element(t.positionEl).toHaveAttribute('aria-atomic', 'true');
	});
});

// ============================================
// ARIA — Prev / Next
// ============================================

describe('ThreadSwitcher — ARIA', () => {
	it('prev has type="button"', async () => {
		const t = setup();
		await expect.element(t.prev).toHaveAttribute('type', 'button');
	});

	it('next has type="button"', async () => {
		const t = setup();
		await expect.element(t.next).toHaveAttribute('type', 'button');
	});

	it('prev uses default aria-label', async () => {
		const t = setup();
		await expect.element(t.prev).toHaveAttribute('aria-label', 'Previous thread');
	});

	it('next uses default aria-label', async () => {
		const t = setup();
		await expect.element(t.next).toHaveAttribute('aria-label', 'Next thread');
	});

	it('prev uses custom aria-label', async () => {
		const t = setup({ prevLabel: 'Go back' });
		await expect.element(t.prev).toHaveAttribute('aria-label', 'Go back');
	});

	it('next uses custom aria-label', async () => {
		const t = setup({ nextLabel: 'Go forward' });
		await expect.element(t.next).toHaveAttribute('aria-label', 'Go forward');
	});
});

// ============================================
// Disabled State
// ============================================

describe('ThreadSwitcher — Disabled State', () => {
	it('prev is disabled when no previousSiblingId', async () => {
		const t = setup({ thread: createThread({ previousSiblingId: undefined }) });
		await expect.element(t.prev).toBeDisabled();
	});

	it('prev is enabled when previousSiblingId exists', async () => {
		const thread = createThread({ siblingIndex: 1, totalSiblings: 2, previousSiblingId: 'main', isOriginal: false });
		const t = setup({ thread });
		await expect.element(t.prev).not.toBeDisabled();
	});

	it('next is disabled when no nextSiblingId', async () => {
		const t = setup({ thread: createThread({ nextSiblingId: undefined }) });
		await expect.element(t.next).toBeDisabled();
	});

	it('next is enabled when nextSiblingId exists', async () => {
		const thread = createThread({ siblingIndex: 0, totalSiblings: 2, nextSiblingId: 'fork-1' });
		const t = setup({ thread });
		await expect.element(t.next).not.toBeDisabled();
	});

	it('both buttons disabled when thread is null', async () => {
		const t = setup({ thread: null });
		await expect.element(t.prev).toBeDisabled();
		await expect.element(t.next).toBeDisabled();
	});

	it('both buttons disabled when only one sibling', async () => {
		const t = setup({ thread: createThread({ totalSiblings: 1 }) });
		await expect.element(t.prev).toBeDisabled();
		await expect.element(t.next).toBeDisabled();
	});
});

// ============================================
// Snippet Props
// ============================================

describe('ThreadSwitcher — Snippet Props', () => {
	it('hasSiblings is false when thread is null', async () => {
		const t = setup({ thread: null });
		await expect.element(t.hasSiblings).toHaveTextContent('false');
	});

	it('hasSiblings is false when totalSiblings is 1', async () => {
		const t = setup({ thread: createThread({ totalSiblings: 1 }) });
		await expect.element(t.hasSiblings).toHaveTextContent('false');
	});

	it('hasSiblings is true when totalSiblings > 1', async () => {
		const t = setup({ thread: createThread({ totalSiblings: 3 }) });
		await expect.element(t.hasSiblings).toHaveTextContent('true');
	});

	it('canGoPrevious is false when no previousSiblingId', async () => {
		const t = setup({ thread: createThread({ previousSiblingId: undefined }) });
		await expect.element(t.canGoPrevious).toHaveTextContent('false');
	});

	it('canGoPrevious is true when previousSiblingId exists', async () => {
		const thread = createThread({ siblingIndex: 1, totalSiblings: 2, previousSiblingId: 'main', isOriginal: false });
		const t = setup({ thread });
		await expect.element(t.canGoPrevious).toHaveTextContent('true');
	});

	it('canGoNext is false when no nextSiblingId', async () => {
		const t = setup({ thread: createThread({ nextSiblingId: undefined }) });
		await expect.element(t.canGoNext).toHaveTextContent('false');
	});

	it('canGoNext is true when nextSiblingId exists', async () => {
		const thread = createThread({ siblingIndex: 0, totalSiblings: 2, nextSiblingId: 'fork-1' });
		const t = setup({ thread });
		await expect.element(t.canGoNext).toHaveTextContent('true');
	});

	it('position is empty string when thread is null', async () => {
		const t = setup({ thread: null });
		await expect.element(t.position).toHaveTextContent('');
	});

	it('position shows "2 / 4" for second of four', async () => {
		const thread = createThread({ siblingIndex: 1, totalSiblings: 4 });
		const t = setup({ thread });
		await expect.element(t.position).toHaveTextContent('2 / 4');
	});

	it('label is empty when totalSiblings is 1', async () => {
		const t = setup({ thread: createThread({ totalSiblings: 1 }) });
		await expect.element(t.label).toHaveTextContent('');
	});

	it('label shows "Original (1 / 3)" for original with siblings', async () => {
		const thread = createThread({ isOriginal: true, siblingIndex: 0, totalSiblings: 3 });
		const t = setup({ thread });
		await expect.element(t.label).toHaveTextContent('Original (1 / 3)');
	});

	it('label shows "Fork 2 / 4" for second fork', async () => {
		const thread = createThread({ isOriginal: false, siblingIndex: 1, totalSiblings: 4, forkedFrom: 'main' });
		const t = setup({ thread });
		await expect.element(t.label).toHaveTextContent('Fork 2 / 4');
	});

	it('isOriginal is true for original thread', async () => {
		const t = setup({ thread: createThread({ isOriginal: true }) });
		await expect.element(t.isOriginal).toHaveTextContent('true');
	});

	it('isOriginal is false for forked thread', async () => {
		const thread = createThread({ isOriginal: false, forkedFrom: 'main' });
		const t = setup({ thread });
		await expect.element(t.isOriginal).toHaveTextContent('false');
	});
});

// ============================================
// Position Element Default Text
// ============================================

describe('ThreadSwitcher — Position Element Content', () => {
	it('shows label text by default when siblings exist', async () => {
		const thread = createThread({ isOriginal: true, siblingIndex: 0, totalSiblings: 3 });
		const t = setup({ thread });
		await expect.element(t.positionEl).toHaveTextContent('Original (1 / 3)');
	});

	it('shows position text when no label (single sibling)', async () => {
		const thread = createThread({ siblingIndex: 0, totalSiblings: 1 });
		const t = setup({ thread });
		await expect.element(t.positionEl).toHaveTextContent('1 / 1');
	});

	it('is empty when thread is null', async () => {
		const t = setup({ thread: null });
		await expect.element(t.positionEl).toHaveTextContent('');
	});
});

// ============================================
// Callbacks
// ============================================

describe('ThreadSwitcher — Callbacks', () => {
	it('calls onPrev when prev button is clicked', async () => {
		const onPrev = vi.fn();
		const thread = createThread({ siblingIndex: 1, totalSiblings: 2, previousSiblingId: 'main', isOriginal: false });
		const t = setup({ thread, onPrev });
		await t.prev.click();
		expect(onPrev).toHaveBeenCalledOnce();
	});

	it('calls onNext when next button is clicked', async () => {
		const onNext = vi.fn();
		const thread = createThread({ siblingIndex: 0, totalSiblings: 2, nextSiblingId: 'fork-1' });
		const t = setup({ thread, onNext });
		await t.next.click();
		expect(onNext).toHaveBeenCalledOnce();
	});

	it('does not call onPrev when button is disabled', async () => {
		const onPrev = vi.fn();
		const thread = createThread({ previousSiblingId: undefined });
		const t = setup({ thread, onPrev });
		await t.prev.click({ force: true });
		expect(onPrev).not.toHaveBeenCalled();
	});

	it('does not call onNext when button is disabled', async () => {
		const onNext = vi.fn();
		const thread = createThread({ nextSiblingId: undefined });
		const t = setup({ thread, onNext });
		await t.next.click({ force: true });
		expect(onNext).not.toHaveBeenCalled();
	});
});
