/**
 * Regression tests for three architectural fixes:
 *
 * Gap 4 — Tree rebuild clobbers user-resized sizes
 *   Every mount/unmount cycle called #buildBranchFromSplit which always used
 *   initialSize config, discarding any sizes the user had dragged.
 *   Fix: look up existing leaf before constructing, preserve size/cachedSize/
 *        cachedFlex/flex from the live tree.
 *
 * Gap 5 — Float32Array index mutations invisible to Svelte 5 proxy
 *   Float32Array index writes (flexes[i] = x) are invisible to Svelte's Proxy.
 *   This caused isCollapsed/size derived values to not update after toggle/resize.
 *   Fix: change BranchNode.flexes from Float32Array to number[].
 *        Remove layoutVersion counter entirely.
 *
 * Gap 3 — onPaneResize callback missing
 *   No push callback existed for resize events; consumers had to poll getPaneState.
 *   Fix: add onPaneResize to SplitPanelRootStateOpts, subscribe to the
 *        'layoutchange' window event filtered to type === 'resize-batch-applied'.
 */

import { describe, it, expect, beforeEach, vi } from 'vitest';
import { flushSync } from 'svelte';
import { SplitPanelState } from '../state/split-panel-state.svelte.js';
import type { BranchNode, LeafNode } from '../types/types.js';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function waitForRaf(): Promise<void> {
	return new Promise((resolve) => requestAnimationFrame(() => resolve()));
}

function getRootBranch(state: SplitPanelState): BranchNode {
	const root = state.root;
	if (root.type !== 'branch') throw new Error('Root is not a branch');
	return root;
}

function getLeafAt(state: SplitPanelState, index: number): LeafNode {
	const child = getRootBranch(state).children[index];
	if (child.type !== 'leaf') throw new Error(`Child at ${index} is not a leaf`);
	return child;
}

function getLeafFlex(state: SplitPanelState, index: number): number {
	return getRootBranch(state).flexes[index];
}

// ---------------------------------------------------------------------------
// Gap 4 — Tree rebuild must preserve user-resized sizes
// ---------------------------------------------------------------------------

describe('Gap 4 — tree rebuild preserves user-resized sizes', () => {
	let state: SplitPanelState;

	beforeEach(() => {
		state = new SplitPanelState();
		state.updateContainerSize(600, 400);
		state.addPanel('a', [], { size: 200, minSize: 50 });
		state.addPanel('b', [], { size: 200, minSize: 50 });
		state.addPanel('c', [], { size: 200, minSize: 50 });
	});

	it('sizes survive a serialise → deserialise round-trip (simulates rebuild)', () => {
		// Resize panel a → b divider so sizes diverge from initial
		state.resizeDivider([], 0, 60);
		return waitForRaf().then(() => {
			const sizeA = getLeafAt(state, 0).size;
			const sizeB = getLeafAt(state, 1).size;

			// Sanity: sizes must have changed
			expect(sizeA).toBeGreaterThan(200);
			expect(sizeB).toBeLessThan(200);

			// Simulate tree rebuild by serialising and deserialising
			const serialised = state.serialize(600, 400);
			const restored = SplitPanelState.deserialize(serialised);
			restored.updateContainerSize(600, 400);

			// Sizes must be preserved after rebuild (within a few pixels — handle spacing can shift things slightly)
			expect(Math.abs(getLeafAt(restored, 0).size - sizeA)).toBeLessThan(5);
			expect(Math.abs(getLeafAt(restored, 1).size - sizeB)).toBeLessThan(5);
		});
	});

	it('flex ratios survive serialise → deserialise round-trip', () => {
		state.resizeDivider([], 0, 80);
		return waitForRaf().then(() => {
			const flexA = getLeafFlex(state, 0);
			const flexB = getLeafFlex(state, 1);

			const serialised = state.serialize(600, 400);
			const restored = SplitPanelState.deserialize(serialised);

			expect(getLeafFlex(restored, 0)).toBeCloseTo(flexA, 3);
			expect(getLeafFlex(restored, 1)).toBeCloseTo(flexB, 3);
		});
	});

	it('collapse stash (cachedSize/cachedFlex) survives rebuild', () => {
		// Resize so pane-a is 250px
		state.resizeDivider([], 0, 50);
		return waitForRaf().then(() => {
			const sizeABeforeCollapse = getLeafAt(state, 0).size;
			const flexABeforeCollapse = getLeafFlex(state, 0);

			// Collapse pane-a
			state.togglePanel('a');
			flushSync();

			// Verify stash was saved
			expect(getLeafAt(state, 0).cachedSize).toBeCloseTo(sizeABeforeCollapse, 0);
			expect(getLeafAt(state, 0).cachedFlex).toBeCloseTo(flexABeforeCollapse, 3);

			// Serialise and deserialise (tree rebuild)
			const serialised = state.serialize(600, 400);
			const restored = SplitPanelState.deserialize(serialised);

			// cachedSize must survive
			expect(getLeafAt(restored, 0).cachedSize).toBeCloseTo(sizeABeforeCollapse, 0);

			// Expanding should restore the pre-collapse size
			restored.updateContainerSize(600, 400);
			restored.togglePanel('a');
			flushSync();

			expect(getLeafAt(restored, 0).size).toBeGreaterThan(0);
		});
	});

	it('all pane sizes are preserved when container is re-applied with same dimensions', () => {
		state.resizeDivider([], 1, -40);
		return waitForRaf().then(() => {
			const sizes = [0, 1, 2].map((i) => getLeafAt(state, i).size);

			// Re-applying the same container size should not change sizes
			state.updateContainerSize(600, 400);

			const sizesAfter = [0, 1, 2].map((i) => getLeafAt(state, i).size);
			for (let i = 0; i < 3; i++) {
				// Within a few pixels — handle-space arithmetic can introduce minor rounding
				expect(Math.abs(sizesAfter[i] - sizes[i])).toBeLessThan(5);
			}
		});
	});
});

// ---------------------------------------------------------------------------
// Gap 5 — number[] flex mutations are reactive (no layoutVersion needed)
// ---------------------------------------------------------------------------

describe('Gap 5 — flex array is plain number[] and reactive', () => {
	let state: SplitPanelState;

	beforeEach(() => {
		state = new SplitPanelState();
		state.updateContainerSize(600, 400);
		state.addPanel('x', [], { size: 300, minSize: 50 });
		state.addPanel('y', [], { size: 300, minSize: 50 });
	});

	it('BranchNode.flexes is a plain number[] (not Float32Array)', () => {
		const root = getRootBranch(state);
		expect(Array.isArray(root.flexes)).toBe(true);
		// Must NOT be a typed array
		expect(root.flexes instanceof Float32Array).toBe(false);
		expect(root.flexes instanceof Uint8Array).toBe(false);
	});

	it('flex values are plain numbers (not typed array elements)', () => {
		const flex = getLeafFlex(state, 0);
		expect(typeof flex).toBe('number');
	});

	it('togglePanel sets flex to 0 for collapsed pane', () => {
		expect(getLeafFlex(state, 0)).toBeGreaterThan(0);

		state.togglePanel('x');
		flushSync();

		expect(getLeafFlex(state, 0)).toBe(0);
	});

	it('togglePanel restores flex > 0 when expanding', () => {
		state.togglePanel('x');
		flushSync();
		expect(getLeafFlex(state, 0)).toBe(0);

		state.togglePanel('x');
		flushSync();

		expect(getLeafFlex(state, 0)).toBeGreaterThan(0);
	});

	it('flatPanels reflects collapse state without layoutVersion', () => {
		// Both panels visible
		expect(state.flatPanels.find((p) => p.id === 'x')?.size).toBeGreaterThan(0);

		state.togglePanel('x');
		flushSync();

		// Panel x must now show size 0
		expect(state.flatPanels.find((p) => p.id === 'x')?.size).toBe(0);
		// Panel y must still be visible
		expect(state.flatPanels.find((p) => p.id === 'y')?.size).toBeGreaterThan(0);
	});

	it('resizeDivider updates flexes as plain numbers', async () => {
		const flexBefore = getLeafFlex(state, 0);

		state.resizeDivider([], 0, 50);
		await waitForRaf();

		const flexAfter = getLeafFlex(state, 0);
		expect(typeof flexAfter).toBe('number');
		expect(flexAfter).not.toBeCloseTo(flexBefore, 3); // Flex must have changed
	});

	it('sum of active flexes equals active child count after resize', async () => {
		state.resizeDivider([], 0, 50);
		await waitForRaf();

		const root = getRootBranch(state);
		const activeFlex = root.flexes.filter((f) => f > 1e-6);
		const sum = activeFlex.reduce((a, b) => a + b, 0);
		const count = activeFlex.length;

		expect(Math.abs(sum - count)).toBeLessThan(0.01);
	});

	it('sum of active flexes equals active count after collapse', () => {
		// Collapse x — active count becomes 1
		state.togglePanel('x');
		flushSync();

		const root = getRootBranch(state);
		const activeFlex = root.flexes.filter((f) => f > 1e-6);
		expect(activeFlex.length).toBe(1);

		const sum = activeFlex.reduce((a, b) => a + b, 0);
		expect(Math.abs(sum - 1)).toBeLessThan(0.01);
	});
});

// ---------------------------------------------------------------------------
// Gap 3 — onPaneResize callback fires on resize-batch-applied events
// ---------------------------------------------------------------------------

describe('Gap 3 — onPaneResize callback via layoutchange window event', () => {
	let state: SplitPanelState;

	beforeEach(() => {
		state = new SplitPanelState();
		state.updateContainerSize(600, 400);
		state.addPanel('p1', [], { size: 200, minSize: 50 });
		state.addPanel('p2', [], { size: 200, minSize: 50 });
		state.addPanel('p3', [], { size: 200, minSize: 50 });
	});

	it('resize-batch-applied event fires on window after resizeDivider', async () => {
		const received: Array<{ panelId: string; newSize: number }> = [];

		const listener = (event: Event) => {
			const ce = event as CustomEvent;
			if (ce.detail?.type === 'resize-batch-applied') {
				received.push(...ce.detail.updates);
			}
		};
		window.addEventListener('layoutchange', listener);

		try {
			state.resizeDivider([], 0, 30);
			await waitForRaf();

			expect(received.length).toBeGreaterThan(0);
		} finally {
			window.removeEventListener('layoutchange', listener);
		}
	});

	it('resize-batch-applied event includes correct panelId and newSize', async () => {
		const received: Array<{ panelId: string; newSize: number }> = [];

		const listener = (event: Event) => {
			const ce = event as CustomEvent;
			if (ce.detail?.type === 'resize-batch-applied') {
				received.push(...ce.detail.updates);
			}
		};
		window.addEventListener('layoutchange', listener);

		try {
			state.resizeDivider([], 0, 40);
			await waitForRaf();

			// Should contain updates for p1 and p2 (the two panels on either side of divider 0)
			const p1Update = received.find((u) => u.panelId === 'p1');
			const p2Update = received.find((u) => u.panelId === 'p2');

			expect(p1Update).toBeDefined();
			expect(p2Update).toBeDefined();

			// p1 should have grown, p2 should have shrunk
			expect(p1Update!.newSize).toBeGreaterThan(200);
			expect(p2Update!.newSize).toBeLessThan(200);
		} finally {
			window.removeEventListener('layoutchange', listener);
		}
	});

	it('resize-batch-applied newSize matches actual leaf size after flush', async () => {
		const received: Array<{ panelId: string; newSize: number }> = [];

		const listener = (event: Event) => {
			const ce = event as CustomEvent;
			if (ce.detail?.type === 'resize-batch-applied') {
				received.push(...ce.detail.updates);
			}
		};
		window.addEventListener('layoutchange', listener);

		try {
			state.resizeDivider([], 1, 25);
			await waitForRaf();

			for (const update of received) {
				const leaf = state.flatPanels.find((p) => p.id === update.panelId);
				if (leaf) {
					expect(update.newSize).toBeCloseTo(leaf.size, 0);
				}
			}
		} finally {
			window.removeEventListener('layoutchange', listener);
		}
	});

	it('multiple accumulated resizes fire a single batch event per RAF frame', async () => {
		let batchCount = 0;
		const received: Array<{ panelId: string; newSize: number }> = [];

		const listener = (event: Event) => {
			const ce = event as CustomEvent;
			if (ce.detail?.type === 'resize-batch-applied') {
				batchCount++;
				received.push(...ce.detail.updates);
			}
		};
		window.addEventListener('layoutchange', listener);

		try {
			// Three resizes accumulated before RAF fires
			state.resizeDivider([], 0, 10);
			state.resizeDivider([], 0, 10);
			state.resizeDivider([], 0, 10);
			await waitForRaf();

			// Should be exactly one batch event (delta = 30 total)
			expect(batchCount).toBe(1);

			const p1Update = received.find((u) => u.panelId === 'p1');
			expect(p1Update).toBeDefined();
			// Net delta = 30px from initial 200
			expect(p1Update!.newSize).toBeGreaterThan(225); // at least ~230
		} finally {
			window.removeEventListener('layoutchange', listener);
		}
	});

	it('no resize-batch-applied event fires when no resize is performed', async () => {
		let fired = false;
		const listener = (event: Event) => {
			const ce = event as CustomEvent;
			if (ce.detail?.type === 'resize-batch-applied') fired = true;
		};
		window.addEventListener('layoutchange', listener);

		try {
			// Wait a frame without calling resizeDivider
			await waitForRaf();
			expect(fired).toBe(false);
		} finally {
			window.removeEventListener('layoutchange', listener);
		}
	});
});
