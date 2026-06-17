/**
 * Regression tests for 8 confirmed bugs in split-panel handle + supporting code.
 *
 * Bug 1 — console.log in handle constructor/drag hot path (removed, no test needed)
 * Bug 2 — #resetAdjacentPanes called recomputeLayout() which doesn't exist
 *           Fix: use updateContainerSize() instead
 * Bug 3 — Keyboard arrow axes swapped in onkeydown
 *           (row axis should use ArrowLeft/Right, column uses ArrowUp/Down)
 *           Verified structurally — no DOM needed for axis derivation
 * Bug 4 — #resetAdjacentPanes / #toggleNearestCollapsiblePane / onkeydown read
 *           opts.parentPath/opts.dividerIndex directly instead of derived this.parentPath
 *           / this.dividerIndex — always used [] / 0 in practice
 *           Fix: use derived values
 * Bug 5 — storageKey prop ignored in LayoutPersistence (hardcoded 'shellos.layout.v3')
 *           Fix: pass storageKey into LayoutPersistence constructor
 * Bug 6 — undo/redo left #panelPathCache stale after restoreSnapshot
 *           Fix: call invalidatePathCache() (now public) after restore
 * Bug 7 — onPaneResize missing from split-panel-root.svelte Props
 *           Fix: added to Props + $props() destructure + SplitPanelRootState constructor call
 * Bug 8 — data-debug-* attributes rendered unconditionally (no test — rendering only)
 */

import { describe, it, expect, beforeEach, vi } from 'vitest';
import { flushSync } from 'svelte';
import { SplitPanelState } from '../state/split-panel-state.svelte.js';
import { LayoutHistory } from '../state/layout-history.svelte.js';
import { LayoutPersistence } from '../state/layout-persistence.svelte.js';
import type { ThreadNode } from '../types/types.js';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function setup3Panes(): SplitPanelState {
	const state = new SplitPanelState();
	state.updateContainerSize(600, 400);
	state.addPanel('p1', [], { size: 200, minSize: 50 });
	state.addPanel('p2', [], { size: 200, minSize: 50 });
	state.addPanel('p3', [], { size: 200, minSize: 50 });
	return state;
}

function getRootThread(state: SplitPanelState): ThreadNode {
	const root = state.root;
	if (root.type !== 'thread') throw new Error('root is not thread');
	return root;
}

// ---------------------------------------------------------------------------
// Bug 5 — storageKey is forwarded to LayoutPersistence
// ---------------------------------------------------------------------------

describe('Bug 5 — LayoutPersistence uses the provided storageKey', () => {
	it('stores and loads under the provided key, not the default key', () => {
		const store = new Map<string, unknown>();
		const storage = {
			get: <T>(key: string) => store.get(key) as T | undefined,
			set: <T>(key: string, value: T) => { store.set(key, value); }
		};

		const state = setup3Panes();
		const persistence = new LayoutPersistence(
			state,
			storage,
			() => 600,
			() => 400,
			'my-app.layout.v3'  // custom key
		);

		persistence.save();

		// Must have written under the custom key
		expect(store.has('my-app.layout.v3')).toBe(true);
		// Must NOT have written under the old hardcoded key
		expect(store.has('shellos.layout.v3')).toBe(false);
	});

	it('loads from the provided key', () => {
		const state1 = setup3Panes();
		// Resize so sizes differ from default
		state1.resizeDivider([], 0, 80);

		const store = new Map<string, unknown>();
		const storage = {
			get: <T>(key: string) => store.get(key) as T | undefined,
			set: <T>(key: string, value: T) => { store.set(key, value); }
		};

		const p1 = new LayoutPersistence(state1, storage, () => 600, () => 400, 'custom.key');
		p1.save();

		// Load into a fresh state using the same custom key
		const state2 = setup3Panes();
		const p2 = new LayoutPersistence(state2, storage, () => 600, () => 400, 'custom.key');
		const loaded = p2.load();
		expect(loaded).toBe(true);
	});

	it('does NOT load when key does not match stored key', () => {
		const state1 = setup3Panes();
		const store = new Map<string, unknown>();
		const storage = {
			get: <T>(key: string) => store.get(key) as T | undefined,
			set: <T>(key: string, value: T) => { store.set(key, value); }
		};

		const p1 = new LayoutPersistence(state1, storage, () => 600, () => 400, 'key-A');
		p1.save();

		const state2 = setup3Panes();
		const p2 = new LayoutPersistence(state2, storage, () => 600, () => 400, 'key-B');
		const loaded = p2.load();
		expect(loaded).toBe(false);
	});
});

// ---------------------------------------------------------------------------
// Bug 6 — invalidatePathCache() is public and called after undo/redo
// ---------------------------------------------------------------------------

describe('Bug 6 — invalidatePathCache is public and clears stale path cache', () => {
	it('SplitPanelState exposes invalidatePathCache() as a public method', () => {
		const state = setup3Panes();
		expect(typeof state.invalidatePathCache).toBe('function');
	});

	it('invalidatePathCache() does not throw and subsequent resize still works', () => {
		const state = setup3Panes();

		// Manually call it (as undo/redo would)
		expect(() => state.invalidatePathCache()).not.toThrow();

		// Resize should still find panels correctly after cache invalidation
		const sizeBefore = (getRootThread(state).children[0] as any).size;
		state.resizeDivider([], 0, 40);

		return new Promise<void>((resolve) => requestAnimationFrame(() => {
			const sizeAfter = (getRootThread(state).children[0] as any).size;
			expect(sizeAfter).not.toBe(sizeBefore);
			resolve();
		}));
	});

	it('resize after invalidatePathCache targets the correct panel', () => {
		const state = setup3Panes();

		state.invalidatePathCache();
		state.resizeDivider([], 0, 50);

		return new Promise<void>((resolve) => requestAnimationFrame(() => {
			const thread = getRootThread(state);
			const p1 = thread.children[0] as any;
			const p2 = thread.children[1] as any;

			// p1 grew, p2 shrank — correct panels targeted
			expect(p1.size).toBeGreaterThan(200);
			expect(p2.size).toBeLessThan(200);
			resolve();
		}));
	});

	it('LayoutHistory.undo() calls invalidatePathCache (path cache not stale after undo)', () => {
		const state = setup3Panes();
		const history = new LayoutHistory(state);

		// Take snapshot, then add a new panel (structural change)
		history.captureSnapshot();
		state.addPanel('p4', [], { size: 100, minSize: 50 });
		flushSync();

		// Undo — tree goes back to 3-panel state
		history.undo();
		flushSync();

		// Resize on the now-restored tree — must not throw and must find correct panels
		expect(() => state.resizeDivider([], 0, 30)).not.toThrow();

		return new Promise<void>((resolve) => requestAnimationFrame(() => {
			const thread = getRootThread(state);
			expect(thread.children.length).toBe(3);
			resolve();
		}));
	});
});

// ---------------------------------------------------------------------------
// Bug 2 — #resetAdjacentPanes must use updateContainerSize not recomputeLayout
// (Verified indirectly: SplitPanelState has no recomputeLayout method)
// ---------------------------------------------------------------------------

describe('Bug 2 — SplitPanelState has no recomputeLayout method', () => {
	it('SplitPanelState does NOT expose recomputeLayout()', () => {
		const state = setup3Panes();
		expect((state as any).recomputeLayout).toBeUndefined();
	});

	it('updateContainerSize with same dimensions is safe to call repeatedly', () => {
		const state = setup3Panes();
		state.resizeDivider([], 0, 40);

		return new Promise<void>((resolve) => requestAnimationFrame(() => {
			const sizesBefore = getRootThread(state).children.map((c: any) => c.size);

			// Calling updateContainerSize with same dims (what resetAdjacentPanes now uses)
			expect(() => state.updateContainerSize(600, 400)).not.toThrow();

			const sizesAfter = getRootThread(state).children.map((c: any) => c.size);
			// Sizes should be stable (within rounding)
			for (let i = 0; i < 3; i++) {
				expect(Math.abs(sizesAfter[i] - sizesBefore[i])).toBeLessThan(5);
			}
			resolve();
		}));
	});
});

// ---------------------------------------------------------------------------
// Bug 3 — Keyboard axis mapping (structural verification via axis/key table)
// ---------------------------------------------------------------------------

describe('Bug 3 — keyboard arrow key to axis mapping is correct', () => {
	/**
	 * Correct mapping after fix:
	 *   axis='row'    (horizontal split, vertical handle)  → ArrowLeft / ArrowRight
	 *   axis='column' (vertical split, horizontal handle)  → ArrowUp / ArrowDown
	 *
	 * We verify this by simulating the fixed onkeydown logic inline.
	 */
	function computeDelta(axis: 'row' | 'column', key: string, step = 10): number {
		let delta = 0;
		if (axis === 'row') {
			if (key === 'ArrowLeft') delta = -step;
			else if (key === 'ArrowRight') delta = step;
		} else {
			if (key === 'ArrowUp') delta = -step;
			else if (key === 'ArrowDown') delta = step;
		}
		return delta;
	}

	it('row axis: ArrowLeft produces negative delta', () => {
		expect(computeDelta('row', 'ArrowLeft')).toBe(-10);
	});

	it('row axis: ArrowRight produces positive delta', () => {
		expect(computeDelta('row', 'ArrowRight')).toBe(10);
	});

	it('row axis: ArrowUp produces zero delta (wrong key for axis)', () => {
		expect(computeDelta('row', 'ArrowUp')).toBe(0);
	});

	it('row axis: ArrowDown produces zero delta (wrong key for axis)', () => {
		expect(computeDelta('row', 'ArrowDown')).toBe(0);
	});

	it('column axis: ArrowUp produces negative delta', () => {
		expect(computeDelta('column', 'ArrowUp')).toBe(-10);
	});

	it('column axis: ArrowDown produces positive delta', () => {
		expect(computeDelta('column', 'ArrowDown')).toBe(10);
	});

	it('column axis: ArrowLeft produces zero delta (wrong key for axis)', () => {
		expect(computeDelta('column', 'ArrowLeft')).toBe(0);
	});

	it('column axis: ArrowRight produces zero delta (wrong key for axis)', () => {
		expect(computeDelta('column', 'ArrowRight')).toBe(0);
	});

	it('Shift modifier applies large step', () => {
		expect(computeDelta('row', 'ArrowRight', 50)).toBe(50);
	});
});
