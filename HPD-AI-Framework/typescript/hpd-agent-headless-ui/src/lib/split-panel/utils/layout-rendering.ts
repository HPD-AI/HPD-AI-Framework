/**
 * Layout Rendering Utilities
 *
 * Helper functions for rendering the layout tree structure.
 * Supports polymorphic rendering of Branch and Leaf nodes.
 */

import type { LayoutNode, BranchNode } from '../types/index.js';
import type { SplitPanelState } from '../state/split-panel-state.svelte.js';

/**
 * Flex epsilon for determining active children (matches core state).
 */
const FLEX_EPS = 1e-6;

/**
 * Handle size in pixels (for grid template calculations).
 */
const HANDLE_SIZE_PX = 4;

/**
 * Active child information for rendering.
 */
export interface ActiveChild {
	/** The child node */
	child: LayoutNode;
	/** Index among active children (0, 1, 2...) */
	activeIndex: number;
	/** Total number of active children */
	totalActive: number;
	/** Absolute index in children array */
	childIndex: number;
}

/**
 * Get active children (flex > FLEX_EPS) with their indices.
 * Used for rendering only visible children and computing handle positions.
 *
 * @param node - Branch node to get active children from
 * @returns Array of active child information
 */
export function getActiveChildren(node: BranchNode): ActiveChild[] {
	const active: number[] = [];

	// Collect indices of active children
	for (let i = 0; i < node.children.length; i++) {
		if (node.flexes[i] > FLEX_EPS) {
			active.push(i);
		}
	}

	// Map to ActiveChild objects
	return active.map((childIndex, activeIndex) => ({
		child: node.children[childIndex],
		childIndex,
		activeIndex,
		totalActive: active.length
	}));
}

/**
 * Compute CSS grid template string from active child sizes.
 * Includes handle sizes between active siblings.
 *
 * @param node - Branch node to compute template for
 * @param axis - Layout axis ('row' = horizontal, 'column' = vertical)
 * @param layoutState - Layout state for size computation
 * @returns CSS grid template string (e.g., "200px 4px 1fr")
 */
export function computeGridTemplate(
	node: BranchNode,
	axis: 'row' | 'column',
	layoutState: SplitPanelState
): string {
	console.log(`[computeGridTemplate] Called with axis=${axis}, node.axis=${node.axis}, node.children.length=${node.children.length}`);

	const active = getActiveChildren(node);
	console.log(`[computeGridTemplate] Active children count: ${active.length}`);

	// No active children → default to 1fr
	if (active.length === 0) {
		console.log('[computeGridTemplate] No active children, returning 1fr');
		return '1fr';
	}

	const parts: string[] = [];

	for (let i = 0; i < active.length; i++) {
		const child = active[i].child;
		// Pass node.axis as parentAxis for correct cache lookup
		const size = layoutState.sizeAlongAxis(child, axis, node.axis);
		parts.push(`${size}px`);

		// Insert handle between active siblings (not after last)
		if (i < active.length - 1) {
			parts.push(`${HANDLE_SIZE_PX}px`);
		}
	}

	const result = parts.join(' ');
	console.log(`[computeGridTemplate] axis=${axis}, node.axis=${node.axis}, result="${result}"`);
	return result;
}

/**
 * Encode parent path and divider index for resize handle data attributes.
 * Format: "parentPath:dividerIndex" (e.g., "0,1:2" or ":0" for root)
 *
 * @param parentPath - Path to parent branch node
 * @param dividerIndex - Index of divider in parent
 * @returns Encoded string
 */
export function encodeResizeHandle(parentPath: number[], dividerIndex: number): string {
	return `${parentPath.join(',')}:${dividerIndex}`;
}

/**
 * Decode resize handle data attribute back to structured data.
 *
 * @param encoded - Encoded resize handle string
 * @returns Decoded parent path and divider index
 */
export function decodeResizeHandle(encoded: string): {
	parentPath: number[];
	dividerIndex: number;
} {
	const parts = encoded.split(':');
	const dividerIndex = parseInt(parts[parts.length - 1], 10);
	const pathStr = parts.slice(0, -1).join(':');
	const parentPath = pathStr ? pathStr.split(',').map(Number) : [];

	return { parentPath, dividerIndex };
}
