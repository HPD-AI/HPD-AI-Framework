/**
 * SplitPanel Type System
 *
 * Core types for the split panel layout system including:
 * - Layout tree nodes (LeafNode, BranchNode)
 * - Error handling (LayoutError, Result)
 * - Serialization (SerializedNode, PersistedLayout)
 * - Panel descriptors for state management
 */

export type { LayoutNode, LeafNode, BranchNode } from './types.js';
export type { LayoutError, Result } from './errors.js';
export { Ok, Err } from './errors.js';
export type {
	SerializedNode,
	SerializedLeafNode,
	SerializedBranchNode,
	SerializedLayout,
	LayoutSnapshot,
	PersistedLayout,
	PanelDescriptor
} from './serialization.js';
export { isLayoutSnapshot } from './serialization.js';
