/**
 * SplitPanel Type System
 *
 * Core types for the split panel layout system including:
 * - Layout tree nodes (LeafNode, ThreadNode)
 * - Error handling (LayoutError, Result)
 * - Serialization (SerializedNode, PersistedLayout)
 * - Panel descriptors for state management
 */

export type { LayoutNode, LeafNode, ThreadNode } from './types.js';
export type { LayoutError, Result } from './errors.js';
export { Ok, Err } from './errors.js';
export type {
	SerializedNode,
	SerializedLeafNode,
	SerializedThreadNode,
	SerializedLayout,
	LayoutSnapshot,
	PersistedLayout,
	PanelDescriptor
} from './serialization.js';
export { isLayoutSnapshot } from './serialization.js';
