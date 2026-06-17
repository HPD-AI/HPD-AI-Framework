/**
 * SplitPanelState Helper Methods
 *
 * Utility functions for tree traversal, path validation, and node operations.
 * Separated for clarity and testability.
 */

import type { LayoutNode, ThreadNode, LeafNode } from '../types/index.js';

/**
 * Encode a resize key for pending deltas map.
 * Format: "parentPath:dividerIndex"
 */
export function encodeResizeKey(parentPath: number[], dividerIndex: number): string {
	return `${parentPath.join(',')}:${dividerIndex}`;
}

/**
 * Decode a resize key back to parentPath and dividerIndex.
 */
export function decodeResizeKey(key: string): { parentPath: number[]; dividerIndex: number } {
	const [pathStr, indexStr] = key.split(':');
	return {
		parentPath: pathStr ? pathStr.split(',').map(Number) : [],
		dividerIndex: parseInt(indexStr, 10)
	};
}

/**
 * Get the node at a specific path in the tree.
 * @throws Error if path is invalid
 */
export function getNodeAt(root: LayoutNode, path: number[]): LayoutNode {
	let current = root;
	for (const index of path) {
		if (current.type !== 'thread') {
			throw new Error(`Invalid path: cannot traverse into leaf node`);
		}
		if (index < 0 || index >= current.children.length) {
			throw new Error(`Invalid path: index ${index} out of bounds`);
		}
		current = current.children[index];
	}
	return current;
}

/**
 * Find the path to a panel by ID.
 * Returns undefined if not found.
 */
export function findPanelPath(root: LayoutNode, panelId: string): number[] | undefined {
	function search(node: LayoutNode, currentPath: number[]): number[] | undefined {
		if (node.type === 'leaf') {
			return node.id === panelId ? currentPath : undefined;
		}
		for (let i = 0; i < node.children.length; i++) {
			const result = search(node.children[i], [...currentPath, i]);
			if (result) return result;
		}
		return undefined;
	}
	return search(root, []);
}

/**
 * Check if a path is valid in the tree.
 */
export function isValidPath(root: LayoutNode, path: number[]): boolean {
	try {
		getNodeAt(root, path);
		return true;
	} catch {
		return false;
	}
}

/**
 * Get indices of active (non-collapsed) children in a thread.
 * Active children have flex > FLEX_EPS.
 */
export function getActiveChildIndices(thread: ThreadNode, flexEps: number): number[] {
	const result: number[] = [];
	for (let i = 0; i < thread.children.length; i++) {
		if (thread.flexes[i] > flexEps) {
			result.push(i);
		}
	}
	return result;
}

/**
 * Remove a panel recursively from the tree.
 * Returns the new tree with the panel removed, or null if tree becomes empty.
 */
export function removePanelRecursive(
	node: LayoutNode,
	panelId: string
): { node: LayoutNode | null; found: boolean } {
	if (node.type === 'leaf') {
		// If this is the target leaf, signal removal
		return node.id === panelId ? { node: null, found: true } : { node, found: false };
	}

	// Thread node: recursively try to remove from children
	let found = false;
	const newChildren: LayoutNode[] = [];
	const newFlexes: number[] = [];

	for (let i = 0; i < node.children.length; i++) {
		const result = removePanelRecursive(node.children[i], panelId);
		if (result.found) {
			found = true;
			// Skip this child (it's been removed)
			if (result.node) {
				newChildren.push(result.node);
				newFlexes.push(node.flexes[i]);
			}
		} else {
			newChildren.push(result.node!);
			newFlexes.push(node.flexes[i]);
		}
	}

	if (!found) {
		return { node, found: false };
	}

	// Panel was found and removed. Sanitize tree:
	if (newChildren.length === 0) {
		// Thread is now empty
		return { node: null, found: true };
	}

	if (newChildren.length === 1) {
		// Thread has only one child, unwrap it
		return { node: newChildren[0], found: true };
	}

	// Thread still has multiple children, update it
	return {
		node: {
			type: 'thread',
			axis: node.axis,
			children: newChildren,
			flexes: newFlexes
		},
		found: true
	};
}

/**
 * Traverse tree and rebuild path cache.
 */
export function rebuildPathCache(root: LayoutNode): Map<string, number[]> {
	const cache = new Map<string, number[]>();

	function traverse(node: LayoutNode, path: number[]): void {
		if (node.type === 'leaf') {
			cache.set(node.id, path);
		} else {
			for (let i = 0; i < node.children.length; i++) {
				traverse(node.children[i], [...path, i]);
			}
		}
	}

	traverse(root, []);
	return cache;
}
