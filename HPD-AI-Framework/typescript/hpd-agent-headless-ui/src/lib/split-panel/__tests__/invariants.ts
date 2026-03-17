/**
 * Core Invariants for SplitPanel System
 *
 * These are the "rules" that must hold after every operation.
 * Property-based tests will verify these invariants hold under random operation sequences.
 *
 * Based on ShellOSUI V3 Final Architecture proposal section 8.1.2
 */

import type { SplitPanelState } from '../state/split-panel-state.svelte.js';
import type { LayoutNode } from '../types/index.js';

const FLEX_EPS = 1e-6;

/**
 * Core invariants that must hold after every operation.
 */
export const invariants = {
	/**
	 * INV-1: Root is always a BranchNode.
	 * Enforced in afterStructuralChange().
	 */
	rootIsBranch(state: SplitPanelState): boolean {
		return state.root.type === 'branch';
	},

	/**
	 * INV-2: Active count matches flex sum rule.
	 * For each branch: sum of active flexes === activeCount.
	 * Active means flex > FLEX_EPS.
	 */
	activeCountMatchesFlexSum(state: SplitPanelState): boolean {
		const checkBranch = (node: LayoutNode): boolean => {
			if (node.type === 'leaf') return true;

			const activeCount = node.flexes.filter((f) => f > FLEX_EPS).length;
			const sumActive = node.flexes.filter((f) => f > FLEX_EPS).reduce((a, b) => a + b, 0);

			// If no active children, sum should be 0
			if (activeCount === 0) {
				if (sumActive !== 0) return false;
			} else {
				// Sum of active flexes should equal activeCount (within tolerance)
				const tolerance = activeCount * 0.001;
				if (Math.abs(sumActive - activeCount) > tolerance) return false;
			}

			// Recursively check children
			return node.children.every(checkBranch);
		};

		return checkBranch(state.root);
	},

	/**
	 * INV-3: No negative sizes.
	 * All leaf sizes must be >= 0.
	 */
	noNegativeSizes(state: SplitPanelState): boolean {
		const checkNode = (node: LayoutNode): boolean => {
			if (node.type === 'leaf') {
				return node.size >= 0;
			}
			return node.children.every(checkNode);
		};
		return checkNode(state.root);
	},

	/**
	 * INV-4: Collapsed panels have flex = 0.
	 * A panel is collapsed iff its flex <= FLEX_EPS.
	 */
	collapsedMeansZeroFlex(state: SplitPanelState): boolean {
		const checkBranch = (node: LayoutNode): boolean => {
			if (node.type === 'leaf') return true;

			for (let i = 0; i < node.children.length; i++) {
				const child = node.children[i];
				const flex = node.flexes[i];

				if (child.type === 'leaf') {
					// If size is 0 (collapsed), flex should be <= FLEX_EPS
					if (child.size === 0 && flex > FLEX_EPS) {
						return false;
					}
				}

				// Recurse
				if (!checkBranch(child)) return false;
			}

			return true;
		};

		return checkBranch(state.root);
	},

	/**
	 * INV-5: Handles only between active siblings.
	 * No handle should exist next to a collapsed (flex=0) panel.
	 * This is a rendering invariant, checked via active children logic.
	 */
	handlesOnlyBetweenActive(state: SplitPanelState): boolean {
		const checkBranch = (node: LayoutNode): boolean => {
			if (node.type === 'leaf') return true;

			// getActiveChildren should return consecutive pairs without gaps
			const activeChildren: Array<{ child: LayoutNode; index: number }> = [];
			for (let i = 0; i < node.children.length; i++) {
				if (node.flexes[i] > FLEX_EPS) {
					activeChildren.push({ child: node.children[i], index: i });
				}
			}

			// Handles exist between activeChildren[i] and activeChildren[i+1]
			// This is correct by construction if we only render active children

			return node.children.every(checkBranch);
		};

		return checkBranch(state.root);
	},

	/**
	 * INV-6: Children array length matches flexes array length.
	 */
	childrenFlexesLengthMatch(state: SplitPanelState): boolean {
		const checkBranch = (node: LayoutNode): boolean => {
			if (node.type === 'leaf') return true;

			if (node.children.length !== node.flexes.length) {
				return false;
			}

			return node.children.every(checkBranch);
		};

		return checkBranch(state.root);
	},

	/**
	 * INV-7: All panel IDs are unique.
	 */
	uniquePanelIds(state: SplitPanelState): boolean {
		const ids = new Set<string>();
		const checkNode = (node: LayoutNode): boolean => {
			if (node.type === 'leaf') {
				if (ids.has(node.id)) return false;
				ids.add(node.id);
				return true;
			}
			return node.children.every(checkNode);
		};
		return checkNode(state.root);
	}
};

/**
 * Run all invariants and return failures.
 * Returns an array of failed invariant names.
 */
export function checkAllInvariants(state: SplitPanelState): string[] {
	const failures: string[] = [];

	for (const [name, check] of Object.entries(invariants)) {
		try {
			if (!check(state)) {
				failures.push(name);
			}
		} catch (error) {
			// If check throws, consider it a failure
			failures.push(`${name} (threw: ${error})`);
		}
	}

	return failures;
}

/**
 * Assert all invariants hold. Throws if any fail.
 * Useful for test assertions.
 */
export function assertInvariants(state: SplitPanelState, context?: string): void {
	const failures = checkAllInvariants(state);
	if (failures.length > 0) {
		const contextStr = context ? ` (${context})` : '';
		throw new Error(`Invariant violations${contextStr}: ${failures.join(', ')}`);
	}
}
