/**
 * SplitPanel Serialization Types
 *
 * Plain JSON structures for persistence without proxies or circular references.
 * Supports both basic layout persistence and full snapshot with panel content states.
 */

/**
 * Serialized node format for persistence.
 * Plain JSON structure without proxies or circular references.
 */
export type SerializedNode = SerializedLeafNode | SerializedBranchNode;

export interface SerializedLeafNode {
	type: 'leaf';
	id: string;
	size: number;
	maximized: boolean;
	cachedSize?: number;
	priority: 'high' | 'normal' | 'low';
	minSize?: number;
	maxSize?: number;
	snapPoints?: number[];
	snapThreshold?: number;
	autoCollapseThreshold?: number;
	panelType?: string;
}

export interface SerializedBranchNode {
	type: 'branch';
	axis: 'row' | 'column';
	children: SerializedNode[];
	flexes: number[]; // Converted from Float32Array for JSON
}

/**
 * Complete serialized layout with metadata.
 * Used for basic persistence without panel content state.
 */
export interface SerializedLayout {
	version: 3;
	root: SerializedNode;
	containerWidth: number; // Container size at serialization time
	containerHeight: number;
	timestamp: number;
}

/**
 * Extended snapshot including panel content states.
 * Used by undo/redo and full persistence with content restoration.
 */
export interface LayoutSnapshot extends SerializedLayout {
	panelStates: Record<string, unknown>; // Content state keyed by panel ID
}

/**
 * Union type for deserialization input.
 * deserialize() accepts either basic layout or full snapshot with panel states.
 *
 * Type guard: Check for 'panelStates' property to determine variant.
 */
export type PersistedLayout = SerializedLayout | LayoutSnapshot;

/**
 * Type guard to check if persisted layout includes panel states.
 */
export function isLayoutSnapshot(data: PersistedLayout): data is LayoutSnapshot {
	return 'panelStates' in data && data.panelStates !== undefined;
}

/**
 * Panel state serializer/deserializer for undo/redo and persistence.
 * Applications register descriptors by panel type to define how to serialize/restore content state.
 */
export interface PanelDescriptor<T = unknown> {
	/** Serialize panel content state to JSON-serializable value */
	serialize: (panelElement: HTMLElement | undefined) => T;

	/** Restore panel content state from serialized value */
	deserialize: (state: T, panelElement: HTMLElement) => void;
}
