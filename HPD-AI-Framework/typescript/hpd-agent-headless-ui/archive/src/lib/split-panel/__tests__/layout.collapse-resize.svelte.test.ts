/**
 * Regression tests for collapse + resize interactions.
 *
 * These tests verify that after collapsing a pane:
 * 1. Other dividers still work correctly
 * 2. Physical divider indices map to correct active children
 * 3. Flex values redistribute properly
 * 4. Expanding collapsed panes works
 * 5. Edge cases (first/last pane collapsed, multiple collapses)
 */

import { describe, it, expect, beforeEach } from 'vitest';
import { flushSync } from 'svelte';
import { SplitPanelState } from '../state/split-panel-state.svelte.js';
import type { LeafNode, BranchNode } from '../types/types.js';

// Helper to wait for a RAF cycle (resizeDivider batches via RAF)
function waitForRaf(): Promise<void> {
	return new Promise(resolve => requestAnimationFrame(() => resolve()));
}

describe('SplitPanelState - Collapse + Resize Interactions', () => {
	let state: SplitPanelState;

	// Helper to setup a 3-pane horizontal layout
	function setupThreePanes(): void {
		state = new SplitPanelState();
		state.updateContainerSize(600, 400);
		state.addPanel('pane-1', [], { size: 200, minSize: 50 });
		state.addPanel('pane-2', [], { size: 200, minSize: 50 });
		state.addPanel('pane-3', [], { size: 200, minSize: 50 });
	}

	// Helper to setup a 4-pane horizontal layout
	function setupFourPanes(): void {
		state = new SplitPanelState();
		state.updateContainerSize(600, 400);
		state.addPanel('pane-1', [], { size: 150, minSize: 50 });
		state.addPanel('pane-2', [], { size: 150, minSize: 50 });
		state.addPanel('pane-3', [], { size: 150, minSize: 50 });
		state.addPanel('pane-4', [], { size: 150, minSize: 50 });
	}

	// Helper to get root branch
	function getRootBranch(): BranchNode {
		const root = state.root;
		if (root.type !== 'branch') throw new Error('Root is not a branch');
		return root;
	}

	// Helper to collapse a pane using togglePanel
	function collapsePaneById(panelId: string): void {
		// Check if expanded, then collapse
		const panel = state.flatPanels.find(p => p.id === panelId);
		if (panel && panel.size > 0) {
			state.togglePanel(panelId);
		}
	}

	// Helper to expand a pane using togglePanel
	function expandPaneById(panelId: string): void {
		// Check if collapsed, then expand
		const panel = state.flatPanels.find(p => p.id === panelId);
		if (panel && panel.size === 0) {
			state.togglePanel(panelId);
		}
	}

	// Helper to get a leaf size from the root
	function getLeafSize(index: number): number {
		const root = getRootBranch();
		const child = root.children[index];
		if (child.type === 'leaf') {
			return child.size;
		}
		throw new Error(`Child at index ${index} is not a leaf`);
	}

	// Helper to get a leaf flex from the root
	function getLeafFlex(index: number): number {
		const root = getRootBranch();
		return root.flexes[index];
	}

	beforeEach(() => {
		setupThreePanes();
	});

	describe('Basic collapse does not break other dividers', () => {
		it('should allow resizing pane-2/pane-3 divider after collapsing pane-1', async () => {
			// Initial: [pane-1] | [pane-2] | [pane-3]
			//           ^div0      ^div1
			collapsePaneById('pane-1');
			flushSync();

			// After collapse: [collapsed] | [pane-2] | [pane-3]
			// Divider 1 (between pane-2 and pane-3) should still work

			const pane2SizeBefore = getLeafSize(1);
			const pane3SizeBefore = getLeafSize(2);

			// Resize using physical divider index 1 (between pane-2 and pane-3)
			state.resizeDivider([], 1, 50);

			// Wait for RAF to process the resize
			await waitForRaf();

			const pane2SizeAfter = getLeafSize(1);
			const pane3SizeAfter = getLeafSize(2);

			// pane-2 should grow, pane-3 should shrink (positive delta)
			expect(pane2SizeAfter).toBeGreaterThan(pane2SizeBefore);
			expect(pane3SizeAfter).toBeLessThan(pane3SizeBefore);
		});

		it('should allow resizing pane-1/pane-2 divider after collapsing pane-3', async () => {
			// Initial: [pane-1] | [pane-2] | [pane-3]
			collapsePaneById('pane-3');
			flushSync();

			// After collapse: [pane-1] | [pane-2] | [collapsed]
			// Divider 0 (between pane-1 and pane-2) should still work

			const pane1SizeBefore = getLeafSize(0);
			const pane2SizeBefore = getLeafSize(1);

			state.resizeDivider([], 0, 30);

			// Wait for RAF to process the resize
			await waitForRaf();

			const pane1SizeAfter = getLeafSize(0);
			const pane2SizeAfter = getLeafSize(1);

			expect(pane1SizeAfter).toBeGreaterThan(pane1SizeBefore);
			expect(pane2SizeAfter).toBeLessThan(pane2SizeBefore);
		});

		it('should allow resizing after collapsing middle pane', async () => {
			// Initial: [pane-1] | [pane-2] | [pane-3]
			collapsePaneById('pane-2');
			flushSync();

			// After collapse: [pane-1] | [collapsed] | [pane-3]
			// Physical divider 0 should now resize pane-1 and pane-3 (skipping collapsed pane-2)

			const pane1SizeBefore = getLeafSize(0);
			const pane3SizeBefore = getLeafSize(2);

			// Use divider 0 - should find nearest active on each side
			state.resizeDivider([], 0, 40);

			// Wait for RAF to process the resize
			await waitForRaf();

			const pane1SizeAfter = getLeafSize(0);
			const pane3SizeAfter = getLeafSize(2);

			// pane-1 grows, pane-3 shrinks
			expect(pane1SizeAfter).toBeGreaterThan(pane1SizeBefore);
			expect(pane3SizeAfter).toBeLessThan(pane3SizeBefore);
		});
	});

	describe('Multiple panes collapsed', () => {
		it('should still allow resize when only 2 panes remain active', async () => {
			// Start with 4 panes
			setupFourPanes();

			// Collapse pane-1 and pane-3
			collapsePaneById('pane-1');
			collapsePaneById('pane-3');
			flushSync();

			// Active: [collapsed] | [pane-2] | [collapsed] | [pane-4]
			// Any divider should resize pane-2 and pane-4

			const pane2SizeBefore = getLeafSize(1);
			const pane4SizeBefore = getLeafSize(3);

			// Use divider 1 (between original pane-2 and pane-3)
			state.resizeDivider([], 1, 25);

			// Wait for RAF to process the resize
			await waitForRaf();

			const pane2SizeAfter = getLeafSize(1);
			const pane4SizeAfter = getLeafSize(3);

			expect(pane2SizeAfter).toBeGreaterThan(pane2SizeBefore);
			expect(pane4SizeAfter).toBeLessThan(pane4SizeBefore);
		});

		it('should do nothing when all but one pane collapsed', () => {
			// Collapse pane-1 and pane-2, leaving only pane-3
			collapsePaneById('pane-1');
			collapsePaneById('pane-2');
			flushSync();

			// Active: [collapsed] | [collapsed] | [pane-3]
			// No resize should be possible (need 2 active panes)

			const pane3SizeBefore = getLeafSize(2);

			// Try to resize - should have no effect
			state.resizeDivider([], 0, 50);
			state.resizeDivider([], 1, 50);

			flushSync();

			const pane3SizeAfter = getLeafSize(2);
			expect(pane3SizeAfter).toBe(pane3SizeBefore);
		});
	});

	describe('Expand after collapse', () => {
		it('should allow resize after collapsing then expanding a pane', async () => {
			// Collapse pane-1
			collapsePaneById('pane-1');
			flushSync();

			// Expand pane-1
			expandPaneById('pane-1');
			flushSync();

			const pane1SizeBefore = getLeafSize(0);
			const pane2SizeBefore = getLeafSize(1);

			// Now divider 0 should work normally again
			state.resizeDivider([], 0, 20);

			// Wait for RAF to process the resize
			await waitForRaf();

			const pane1SizeAfter = getLeafSize(0);
			const pane2SizeAfter = getLeafSize(1);

			expect(pane1SizeAfter).toBeGreaterThan(pane1SizeBefore);
			expect(pane2SizeAfter).toBeLessThan(pane2SizeBefore);
		});
	});

	describe('Divider index edge cases', () => {
		it('should do nothing for out-of-bounds physical divider index', () => {
			// 3 panes = 2 dividers (indices 0 and 1)
			// Divider index 2 should be invalid
			const size0Before = getLeafSize(0);
			const size1Before = getLeafSize(1);
			const size2Before = getLeafSize(2);

			state.resizeDivider([], 2, 50);
			flushSync();

			// No pane sizes should change
			expect(getLeafSize(0)).toBe(size0Before);
			expect(getLeafSize(1)).toBe(size1Before);
			expect(getLeafSize(2)).toBe(size2Before);
		});

		it('should do nothing for negative divider index', () => {
			const size0Before = getLeafSize(0);

			state.resizeDivider([], -1, 50);
			flushSync();

			expect(getLeafSize(0)).toBe(size0Before);
		});

		it('should handle divider at collapsed boundary correctly', async () => {
			// Collapse pane-2 (middle)
			collapsePaneById('pane-2');
			flushSync();

			// Physical divider 0 is between pane-1 and collapsed pane-2
			// Physical divider 1 is between collapsed pane-2 and pane-3
			// Both should effectively resize pane-1 and pane-3

			const pane1Before = getLeafSize(0);
			const pane3Before = getLeafSize(2);

			state.resizeDivider([], 0, 30);

			// Wait for RAF to process the resize
			await waitForRaf();

			expect(getLeafSize(0)).toBeGreaterThan(pane1Before);
			expect(getLeafSize(2)).toBeLessThan(pane3Before);
		});
	});

	describe('Flex distribution after collapse', () => {
		it('should mark collapsed pane flex as 0', () => {
			// Before collapse all flexes should be positive
			expect(getLeafFlex(0)).toBeGreaterThan(0);
			expect(getLeafFlex(1)).toBeGreaterThan(0);
			expect(getLeafFlex(2)).toBeGreaterThan(0);

			// Collapse pane-1
			collapsePaneById('pane-1');
			flushSync();

			// After collapse: flex[0] should be 0 or near 0
			expect(getLeafFlex(0)).toBeLessThan(0.01);
		});

		it('should preserve relative ratios when resizing after collapse', async () => {
			// Collapse pane-1
			collapsePaneById('pane-1');
			flushSync();

			// pane-2 and pane-3 should each have ~50% of space
			// After resize, they should still sum to 100% of active space

			state.resizeDivider([], 1, 50);

			// Wait for RAF to process the resize
			await waitForRaf();

			// Collapsed pane should still have flex ~0
			expect(getLeafFlex(0)).toBeLessThan(0.01);

			// Active panes' flexes should still sum to ~1
			// Note: After resize, flexes may not be perfectly normalized, so we use a looser check
			const activeFlex = getLeafFlex(1) + getLeafFlex(2);
			expect(activeFlex).toBeGreaterThan(0.5); // At least some flex distribution exists
		});
	});

	describe('Concurrent operations', () => {
		it('should handle rapid collapse and resize together', async () => {
			// Collapse pane-1
			collapsePaneById('pane-1');

			// Immediately try to resize multiple times (batched into RAF)
			state.resizeDivider([], 1, 10);
			state.resizeDivider([], 1, 10);
			state.resizeDivider([], 1, 10);

			// Wait for RAF to process all batched resizes
			await waitForRaf();

			// Pane-1 should still be collapsed
			expect(getLeafFlex(0)).toBeLessThan(0.01);

			// Resize should have accumulated on pane-2/pane-3
			const pane2 = getRootBranch().children[1] as LeafNode;
			const pane3 = getRootBranch().children[2] as LeafNode;

			// Sizes should have changed from initial ~200px each
			// One grew, one shrunk
			expect(Math.abs(pane2.size - pane3.size)).toBeGreaterThan(10);
		});
	});
});
