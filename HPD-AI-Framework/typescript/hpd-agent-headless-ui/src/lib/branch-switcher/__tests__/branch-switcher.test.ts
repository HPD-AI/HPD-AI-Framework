/**
 * Unit tests for BranchSwitcherRootState
 */

import { describe, it, expect } from 'vitest';
import { box } from 'svelte-toolbelt';
import { BranchSwitcherRootState } from '../branch-switcher.svelte.js';
import type { Branch } from '@hpd/hpd-agent-client';

// ============================================
// Helpers
// ============================================

const createMockBranch = (overrides: Partial<Branch> = {}): Branch => ({
	id: 'main',
	sessionId: 'session-1',
	name: 'Main Branch',
	createdAt: '2024-01-01T00:00:00Z',
	lastActivity: '2024-01-01T00:10:00Z',
	messageCount: 5,
	siblingIndex: 0,
	totalSiblings: 1,
	isOriginal: true,
	childBranches: [],
	totalForks: 0,
	...overrides,
});

function createRootState(branch: Branch | null) {
	return new BranchSwitcherRootState({ branch: box<Branch | null>(branch) });
}

// ============================================
// branch
// ============================================

describe('BranchSwitcherRootState — branch', () => {
	it('returns null when no branch', () => {
		const state = createRootState(null);
		expect(state.branch).toBeNull();
	});

	it('returns the branch when set', () => {
		const branch = createMockBranch();
		const state = createRootState(branch);
		expect(state.branch).toBe(branch);
	});
});

// ============================================
// canGoPrevious
// ============================================

describe('BranchSwitcherRootState — canGoPrevious', () => {
	it('returns false when branch is null', () => {
		expect(createRootState(null).canGoPrevious).toBe(false);
	});

	it('returns false when no previousSiblingId', () => {
		expect(createRootState(createMockBranch({ previousSiblingId: undefined })).canGoPrevious).toBe(false);
	});

	it('returns true when previousSiblingId is set', () => {
		const branch = createMockBranch({ siblingIndex: 1, totalSiblings: 2, previousSiblingId: 'main', isOriginal: false });
		expect(createRootState(branch).canGoPrevious).toBe(true);
	});
});

// ============================================
// canGoNext
// ============================================

describe('BranchSwitcherRootState — canGoNext', () => {
	it('returns false when branch is null', () => {
		expect(createRootState(null).canGoNext).toBe(false);
	});

	it('returns false when no nextSiblingId', () => {
		expect(createRootState(createMockBranch({ nextSiblingId: undefined })).canGoNext).toBe(false);
	});

	it('returns true when nextSiblingId is set', () => {
		const branch = createMockBranch({ siblingIndex: 0, totalSiblings: 2, nextSiblingId: 'fork-1' });
		expect(createRootState(branch).canGoNext).toBe(true);
	});
});

// ============================================
// hasSiblings
// ============================================

describe('BranchSwitcherRootState — hasSiblings', () => {
	it('returns false when branch is null', () => {
		expect(createRootState(null).hasSiblings).toBe(false);
	});

	it('returns false when totalSiblings is 1', () => {
		expect(createRootState(createMockBranch({ totalSiblings: 1 })).hasSiblings).toBe(false);
	});

	it('returns true when totalSiblings > 1', () => {
		expect(createRootState(createMockBranch({ totalSiblings: 3 })).hasSiblings).toBe(true);
	});
});

// ============================================
// isOriginal
// ============================================

describe('BranchSwitcherRootState — isOriginal', () => {
	it('returns false when branch is null', () => {
		expect(createRootState(null).isOriginal).toBe(false);
	});

	it('returns true for original branch', () => {
		expect(createRootState(createMockBranch({ isOriginal: true })).isOriginal).toBe(true);
	});

	it('returns false for forked branch', () => {
		expect(createRootState(createMockBranch({ isOriginal: false, forkedFrom: 'main' })).isOriginal).toBe(false);
	});
});

// ============================================
// position
// ============================================

describe('BranchSwitcherRootState — position', () => {
	it('returns empty string when branch is null', () => {
		expect(createRootState(null).position).toBe('');
	});

	it('returns "1 / 1" for a lone branch', () => {
		expect(createRootState(createMockBranch({ siblingIndex: 0, totalSiblings: 1 })).position).toBe('1 / 1');
	});

	it('returns "1 / 3" for first of three', () => {
		expect(createRootState(createMockBranch({ siblingIndex: 0, totalSiblings: 3 })).position).toBe('1 / 3');
	});

	it('returns "2 / 4" for second of four', () => {
		expect(createRootState(createMockBranch({ siblingIndex: 1, totalSiblings: 4 })).position).toBe('2 / 4');
	});

	it('returns "4 / 4" for last of four', () => {
		expect(createRootState(createMockBranch({ siblingIndex: 3, totalSiblings: 4 })).position).toBe('4 / 4');
	});
});

// ============================================
// label
// ============================================

describe('BranchSwitcherRootState — label', () => {
	it('returns empty string when branch is null', () => {
		expect(createRootState(null).label).toBe('');
	});

	it('returns empty string when totalSiblings is 1', () => {
		expect(createRootState(createMockBranch({ totalSiblings: 1 })).label).toBe('');
	});

	it('returns "Original (1 / 3)" for original with siblings', () => {
		const branch = createMockBranch({ isOriginal: true, siblingIndex: 0, totalSiblings: 3 });
		expect(createRootState(branch).label).toBe('Original (1 / 3)');
	});

	it('returns "Fork 2 / 4" for second fork of four', () => {
		const branch = createMockBranch({ isOriginal: false, siblingIndex: 1, totalSiblings: 4, forkedFrom: 'main' });
		expect(createRootState(branch).label).toBe('Fork 2 / 4');
	});

	it('returns "Fork 4 / 4" for last fork', () => {
		const branch = createMockBranch({ isOriginal: false, siblingIndex: 3, totalSiblings: 4, forkedFrom: 'main' });
		expect(createRootState(branch).label).toBe('Fork 4 / 4');
	});
});

// ============================================
// props
// ============================================

describe('BranchSwitcherRootState — props', () => {
	it('has data-branch-switcher-root', () => {
		expect(createRootState(null).props['data-branch-switcher-root']).toBe('');
	});

	it('does not have data-has-siblings when no siblings', () => {
		expect(createRootState(createMockBranch({ totalSiblings: 1 })).props['data-has-siblings']).toBeUndefined();
	});

	it('has data-has-siblings="" when siblings exist', () => {
		expect(createRootState(createMockBranch({ totalSiblings: 3 })).props['data-has-siblings']).toBe('');
	});

	it('does not have data-has-siblings when branch is null', () => {
		expect(createRootState(null).props['data-has-siblings']).toBeUndefined();
	});
});

// ============================================
// snippetProps
// ============================================

describe('BranchSwitcherRootState — snippetProps', () => {
	it('exposes all expected fields when branch is null', () => {
		const sp = createRootState(null).snippetProps;
		expect(sp.branch).toBeNull();
		expect(sp.hasSiblings).toBe(false);
		expect(sp.canGoPrevious).toBe(false);
		expect(sp.canGoNext).toBe(false);
		expect(sp.position).toBe('');
		expect(sp.label).toBe('');
		expect(sp.isOriginal).toBe(false);
	});

	it('exposes correct values for a mid-sibling fork', () => {
		const branch = createMockBranch({
			siblingIndex: 1,
			totalSiblings: 3,
			isOriginal: false,
			previousSiblingId: 'main',
			nextSiblingId: 'fork-2',
			forkedFrom: 'main',
		});
		const sp = createRootState(branch).snippetProps;
		expect(sp.branch).toBe(branch);
		expect(sp.hasSiblings).toBe(true);
		expect(sp.canGoPrevious).toBe(true);
		expect(sp.canGoNext).toBe(true);
		expect(sp.position).toBe('2 / 3');
		expect(sp.label).toBe('Fork 2 / 3');
		expect(sp.isOriginal).toBe(false);
	});

	it('exposes correct values for first-and-only sibling', () => {
		const branch = createMockBranch({ siblingIndex: 0, totalSiblings: 1, isOriginal: true });
		const sp = createRootState(branch).snippetProps;
		expect(sp.hasSiblings).toBe(false);
		expect(sp.canGoPrevious).toBe(false);
		expect(sp.canGoNext).toBe(false);
		expect(sp.position).toBe('1 / 1');
		expect(sp.label).toBe('');
		expect(sp.isOriginal).toBe(true);
	});
});
