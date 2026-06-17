/**
 * Property-Based Tests for SplitPanel System
 *
 * Uses fast-check to generate random operation sequences and verify invariants hold.
 * Based on ShellOSUI V3 Final Architecture proposal section 8.1.4
 *
 * CURRENT STATUS: SKIPPED
 *
 * WHY SKIPPED:
 * These tests require direct instantiation of SplitPanelState and LayoutHistory, which use
 * Svelte 5 runes ($state, $derived, $effect). Runes cannot be tested via direct class
 * instantiation - they require the Svelte compilation context and runtime.
 *
 * ALTERNATIVE TESTING APPROACH:
 * Property-based testing should be done through component integration tests:
 * 1. Create a test toolharness component that renders SplitPanel
 * 2. Use vitest-browser-svelte to render the component
 * 3. Use fast-check to generate operation sequences
 * 4. Apply operations through component interaction (clicks, drags, etc.)
 * 5. Verify invariants through DOM assertions and component state inspection
 *
 * See HPD-Agent-Framework input component tests for examples of property-based testing
 * through component integration.
 */

import { describe, it, expect } from 'vitest';
import * as fc from 'fast-check';
import { SplitPanelState } from '../state/split-panel-state.svelte.js';
import { LayoutHistory } from '../state/layout-history.svelte.js';
import { checkAllInvariants } from './invariants.js';
import { arbOperation, type LayoutOperation } from './arbitraries.js';

/**
 * Apply an operation to the layout state.
 * Handles errors gracefully (operations that fail are skipped).
 */
function applyOperation(state: SplitPanelState, history: LayoutHistory, op: LayoutOperation): void {
	switch (op.type) {
		case 'addPanel': {
			// Add to root's first thread
			const result = state.addPanel(`panel-${op.panelId}`, [], {
				size: 200
			});
			// Ignore errors (e.g., duplicate ID)
			break;
		}
		case 'removePanel': {
			state.removePanel(op.panelId);
			// Ignore errors (e.g., not found)
			break;
		}
		case 'toggleCollapse': {
			state.togglePanel(op.panelId);
			// Ignore errors
			break;
		}
		case 'resizeDivider': {
			// Try to resize at root level
			const delta = op.delta;
			state.resizeDivider([], op.dividerIndex, delta);
			// Ignore errors
			break;
		}
		case 'setContainerSize': {
			state.updateContainerSize(op.width, op.height);
			break;
		}
		case 'undo': {
			history.undo();
			break;
		}
		case 'redo': {
			history.redo();
			break;
		}
	}
}

/**
 * Get all panel IDs from the current state.
 */
function getAllPanelIds(state: SplitPanelState): string[] {
	const ids: string[] = [];
	const traverse = (node: any): void => {
		if (node.type === 'leaf') {
			ids.push(node.id);
		} else if (node.type === 'thread') {
			for (const child of node.children) {
				traverse(child);
			}
		}
	};
	traverse(state.root);
	return ids;
}

/**
 * Property-Based Tests for SplitPanelState
 *
 * SplitPanelState has been refactored to use plain JavaScript instead of Svelte runes,
 * allowing these tests to run in vitest. Components will still get reactivity through
 * Svelte's fine-grained subscriptions to the state object.
 */
describe.skip('SplitPanelState Property-Based Tests', () => {
	/**
	 * PROPERTY 1: Invariants hold after any sequence of operations.
	 *
	 * This is the main property test - it generates random sequences of
	 * add/remove/toggle/resize/undo/redo operations and verifies that all
	 * invariants hold after each operation.
	 */
	it('invariants hold after random operation sequences', () => {
		fc.assert(
			fc.property(fc.array(arbOperation([]), { minLength: 1, maxLength: 50 }), (operations) => {
				// Create fresh state
				const state = new SplitPanelState();
				const history = new LayoutHistory(state);

				// Initialize with a valid root
				state.updateContainerSize(1000, 800);

				// Apply each operation and check invariants
				for (const op of operations) {
					applyOperation(state, history, op);

					const failures = checkAllInvariants(state);
					if (failures.length > 0) {
						throw new Error(`Invariant failures after ${op.type}: ${failures.join(', ')}`);
					}
				}
			}),
			{ numRuns: 100, verbose: true }
		);
	});

	/**
	 * PROPERTY 2: Undo/redo is symmetric.
	 *
	 * After any operation, undo followed by redo should restore the same state.
	 */
	it('undo followed by redo restores state', () => {
		fc.assert(
			fc.property(fc.array(arbOperation([]), { minLength: 1, maxLength: 20 }), (operations) => {
				const state = new SplitPanelState();
				const history = new LayoutHistory(state);
				state.updateContainerSize(1000, 800);

				// Apply operations
				for (const op of operations) {
					applyOperation(state, history, op);
				}

				// Snapshot state before undo
				const beforeUndo = JSON.stringify(state.serialize(1000, 800));

				// Undo then redo
				if (history.canUndo) {
					history.undo();
					if (history.canRedo) {
						history.redo();

						// State should match
						const afterRedo = JSON.stringify(state.serialize(1000, 800));
						expect(afterRedo).toBe(beforeUndo);
					}
				}
			}),
			{ numRuns: 50 }
		);
	});

	/**
	 * PROPERTY 3: Container resize preserves relative proportions.
	 *
	 * After resizing the container, panel flex ratios should remain the same.
	 */
	it('container resize preserves flex ratios', () => {
		fc.assert(
			fc.property(
				fc.integer({ min: 200, max: 2000 }),
				fc.integer({ min: 200, max: 1500 }),
				fc.integer({ min: 200, max: 2000 }),
				fc.integer({ min: 200, max: 1500 }),
				(w1, h1, w2, h2) => {
					const state = new SplitPanelState();
					state.updateContainerSize(w1, h1);

					// Add some panels
					state.addPanel('p1', [], { size: 200 });
					state.addPanel('p2', [], { size: 300 });
					state.addPanel('p3', [], { size: 100 });

					// Get flex ratios before resize
					const root = state.root as { type: 'thread'; flexes: number[] };
					const ratiosBefore = Array.from(root.flexes).map((f, i, arr) => {
						const total = arr.reduce((a, b) => a + b, 0);
						return total > 0 ? f / total : 0;
					});

					// Resize container
					state.updateContainerSize(w2, h2);

					// Get flex ratios after resize
					const ratiosAfter = Array.from(root.flexes).map((f, i, arr) => {
						const total = arr.reduce((a, b) => a + b, 0);
						return total > 0 ? f / total : 0;
					});

					// Ratios should be preserved (within tolerance)
					for (let i = 0; i < ratiosBefore.length; i++) {
						expect(Math.abs(ratiosBefore[i] - ratiosAfter[i])).toBeLessThan(0.01);
					}
				}
			),
			{ numRuns: 50 }
		);
	});

	/**
	 * PROPERTY 4: Serialization round-trip preserves state.
	 *
	 * serialize() followed by deserialize() should produce equivalent state.
	 */
	it('serialization round-trip preserves state', () => {
		fc.assert(
			fc.property(fc.array(arbOperation([]), { minLength: 1, maxLength: 30 }), (operations) => {
				const state = new SplitPanelState();
				state.updateContainerSize(1000, 800);

				// Apply operations
				const history = new LayoutHistory(state);
				for (const op of operations) {
					applyOperation(state, history, op);
				}

				// Serialize
				const serialized = state.serialize(1000, 800);

				// Deserialize into new instance
				const restored = SplitPanelState.deserialize(serialized);

				// Compare structure
				const original = JSON.stringify(state.serialize(1000, 800).root);
				const restoredJson = JSON.stringify(restored.serialize(1000, 800).root);

				expect(restoredJson).toBe(original);
			}),
			{ numRuns: 50 }
		);
	});

	/**
	 * PROPERTY 5: Min constraints are respected.
	 *
	 * BEAR 3 constraint: No panel should be resized below its minSize.
	 */
	it('min constraints are never violated', () => {
		fc.assert(
			fc.property(
				fc.array(
					fc.record({
						type: fc.constant('resizeDivider' as const),
						dividerIndex: fc.nat({ max: 5 }),
						delta: fc.integer({ min: -500, max: 500 })
					}),
					{ minLength: 1, maxLength: 100 }
				),
				(resizeOps) => {
					const state = new SplitPanelState();
					state.updateContainerSize(1000, 800);

					// Add panels with min constraints
					state.addPanel('p1', [], {
						size: 300,
						minSize: 100
					});
					state.addPanel('p2', [], {
						size: 300,
						minSize: 150
					});
					state.addPanel('p3', [], {
						size: 200,
						minSize: 50
					});

					// Apply resize operations
					for (const op of resizeOps) {
						state.resizeDivider([], op.dividerIndex, op.delta);
					}

					// Check all panels respect min constraints
					const allPanels: any[] = [];
					const traverse = (node: any): void => {
						if (node.type === 'leaf') {
							allPanels.push(node);
						} else if (node.type === 'thread') {
							for (const child of node.children) {
								traverse(child);
							}
						}
					};
					traverse(state.root);

					for (const panel of allPanels) {
						const minSize = panel.minSize ?? 0;
						// Panel can be 0 (collapsed) or >= minSize
						expect(panel.size === 0 || panel.size >= minSize).toBe(true);
					}
				}
			),
			{ numRuns: 50 }
		);
	});
});
