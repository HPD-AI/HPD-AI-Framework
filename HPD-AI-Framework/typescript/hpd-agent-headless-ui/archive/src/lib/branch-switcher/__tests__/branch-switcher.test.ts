/**
 * Unit tests for ThreadSwitcherRootState
 */

import { describe, it, expect } from 'vitest';
import { box } from 'svelte-toolbelt';
import { ThreadSwitcherRootState } from '../thread-switcher.svelte.js';
import type { Thread } from '@hpd-research/hpd-agent-client';

// ============================================
// Helpers
// ============================================

const createMockThread = (overrides: Partial<Thread> = {}): Thread => ({
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

function createRootState(thread: Thread | null) {
	return new ThreadSwitcherRootState({ thread: box<Thread | null>(thread) });
}

// ============================================
// thread
// ============================================

describe('ThreadSwitcherRootState — thread', () => {
	it('returns null when no thread', () => {
		const state = createRootState(null);
		expect(state.thread).toBeNull();
	});

	it('returns the thread when set', () => {
		const thread = createMockThread();
		const state = createRootState(thread);
		expect(state.thread).toBe(thread);
	});
});

// ============================================
// canGoPrevious
// ============================================

describe('ThreadSwitcherRootState — canGoPrevious', () => {
	it('returns false when thread is null', () => {
		expect(createRootState(null).canGoPrevious).toBe(false);
	});

	it('returns false when no previousSiblingId', () => {
		expect(createRootState(createMockThread({ previousSiblingId: undefined })).canGoPrevious).toBe(false);
	});

	it('returns true when previousSiblingId is set', () => {
		const thread = createMockThread({ siblingIndex: 1, totalSiblings: 2, previousSiblingId: 'main', isOriginal: false });
		expect(createRootState(thread).canGoPrevious).toBe(true);
	});
});

// ============================================
// canGoNext
// ============================================

describe('ThreadSwitcherRootState — canGoNext', () => {
	it('returns false when thread is null', () => {
		expect(createRootState(null).canGoNext).toBe(false);
	});

	it('returns false when no nextSiblingId', () => {
		expect(createRootState(createMockThread({ nextSiblingId: undefined })).canGoNext).toBe(false);
	});

	it('returns true when nextSiblingId is set', () => {
		const thread = createMockThread({ siblingIndex: 0, totalSiblings: 2, nextSiblingId: 'fork-1' });
		expect(createRootState(thread).canGoNext).toBe(true);
	});
});

// ============================================
// hasSiblings
// ============================================

describe('ThreadSwitcherRootState — hasSiblings', () => {
	it('returns false when thread is null', () => {
		expect(createRootState(null).hasSiblings).toBe(false);
	});

	it('returns false when totalSiblings is 1', () => {
		expect(createRootState(createMockThread({ totalSiblings: 1 })).hasSiblings).toBe(false);
	});

	it('returns true when totalSiblings > 1', () => {
		expect(createRootState(createMockThread({ totalSiblings: 3 })).hasSiblings).toBe(true);
	});
});

// ============================================
// isOriginal
// ============================================

describe('ThreadSwitcherRootState — isOriginal', () => {
	it('returns false when thread is null', () => {
		expect(createRootState(null).isOriginal).toBe(false);
	});

	it('returns true for original thread', () => {
		expect(createRootState(createMockThread({ isOriginal: true })).isOriginal).toBe(true);
	});

	it('returns false for forked thread', () => {
		expect(createRootState(createMockThread({ isOriginal: false, forkedFrom: 'main' })).isOriginal).toBe(false);
	});
});

// ============================================
// position
// ============================================

describe('ThreadSwitcherRootState — position', () => {
	it('returns empty string when thread is null', () => {
		expect(createRootState(null).position).toBe('');
	});

	it('returns "1 / 1" for a lone thread', () => {
		expect(createRootState(createMockThread({ siblingIndex: 0, totalSiblings: 1 })).position).toBe('1 / 1');
	});

	it('returns "1 / 3" for first of three', () => {
		expect(createRootState(createMockThread({ siblingIndex: 0, totalSiblings: 3 })).position).toBe('1 / 3');
	});

	it('returns "2 / 4" for second of four', () => {
		expect(createRootState(createMockThread({ siblingIndex: 1, totalSiblings: 4 })).position).toBe('2 / 4');
	});

	it('returns "4 / 4" for last of four', () => {
		expect(createRootState(createMockThread({ siblingIndex: 3, totalSiblings: 4 })).position).toBe('4 / 4');
	});
});

// ============================================
// label
// ============================================

describe('ThreadSwitcherRootState — label', () => {
	it('returns empty string when thread is null', () => {
		expect(createRootState(null).label).toBe('');
	});

	it('returns empty string when totalSiblings is 1', () => {
		expect(createRootState(createMockThread({ totalSiblings: 1 })).label).toBe('');
	});

	it('returns "Original (1 / 3)" for original with siblings', () => {
		const thread = createMockThread({ isOriginal: true, siblingIndex: 0, totalSiblings: 3 });
		expect(createRootState(thread).label).toBe('Original (1 / 3)');
	});

	it('returns "Fork 2 / 4" for second fork of four', () => {
		const thread = createMockThread({ isOriginal: false, siblingIndex: 1, totalSiblings: 4, forkedFrom: 'main' });
		expect(createRootState(thread).label).toBe('Fork 2 / 4');
	});

	it('returns "Fork 4 / 4" for last fork', () => {
		const thread = createMockThread({ isOriginal: false, siblingIndex: 3, totalSiblings: 4, forkedFrom: 'main' });
		expect(createRootState(thread).label).toBe('Fork 4 / 4');
	});
});

// ============================================
// props
// ============================================

describe('ThreadSwitcherRootState — props', () => {
	it('has data-thread-switcher-root', () => {
		expect(createRootState(null).props['data-thread-switcher-root']).toBe('');
	});

	it('does not have data-has-siblings when no siblings', () => {
		expect(createRootState(createMockThread({ totalSiblings: 1 })).props['data-has-siblings']).toBeUndefined();
	});

	it('has data-has-siblings="" when siblings exist', () => {
		expect(createRootState(createMockThread({ totalSiblings: 3 })).props['data-has-siblings']).toBe('');
	});

	it('does not have data-has-siblings when thread is null', () => {
		expect(createRootState(null).props['data-has-siblings']).toBeUndefined();
	});
});

// ============================================
// snippetProps
// ============================================

describe('ThreadSwitcherRootState — snippetProps', () => {
	it('exposes all expected fields when thread is null', () => {
		const sp = createRootState(null).snippetProps;
		expect(sp.thread).toBeNull();
		expect(sp.hasSiblings).toBe(false);
		expect(sp.canGoPrevious).toBe(false);
		expect(sp.canGoNext).toBe(false);
		expect(sp.position).toBe('');
		expect(sp.label).toBe('');
		expect(sp.isOriginal).toBe(false);
	});

	it('exposes correct values for a mid-sibling fork', () => {
		const thread = createMockThread({
			siblingIndex: 1,
			totalSiblings: 3,
			isOriginal: false,
			previousSiblingId: 'main',
			nextSiblingId: 'fork-2',
			forkedFrom: 'main',
		});
		const sp = createRootState(thread).snippetProps;
		expect(sp.thread).toBe(thread);
		expect(sp.hasSiblings).toBe(true);
		expect(sp.canGoPrevious).toBe(true);
		expect(sp.canGoNext).toBe(true);
		expect(sp.position).toBe('2 / 3');
		expect(sp.label).toBe('Fork 2 / 3');
		expect(sp.isOriginal).toBe(false);
	});

	it('exposes correct values for first-and-only sibling', () => {
		const thread = createMockThread({ siblingIndex: 0, totalSiblings: 1, isOriginal: true });
		const sp = createRootState(thread).snippetProps;
		expect(sp.hasSiblings).toBe(false);
		expect(sp.canGoPrevious).toBe(false);
		expect(sp.canGoNext).toBe(false);
		expect(sp.position).toBe('1 / 1');
		expect(sp.label).toBe('');
		expect(sp.isOriginal).toBe(true);
	});
});
