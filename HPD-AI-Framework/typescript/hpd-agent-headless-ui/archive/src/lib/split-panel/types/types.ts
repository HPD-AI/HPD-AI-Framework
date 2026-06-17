/**
 * SplitPanel Core Type System
 *
 * Discriminated union types for layout tree nodes with type-safe traversal
 * and exhaustive pattern matching support.
 */

/**
 * Discriminated union for layout tree nodes.
 * Enables type-safe tree traversal with exhaustive pattern matching.
 * Both LeafNode and ThreadNode are treated uniformly in rendering/traversal
 * to support arbitrary nesting depth without type-specific logic.
 */
export type LayoutNode = LeafNode | ThreadNode;

/**
 * Leaf node representing a single panel in the layout.
 */
export interface LeafNode {
	type: 'leaf';

	/** Unique identifier for this panel (generated via HPD createId) */
	id: string;

	/** Current size in pixels (0 when collapsed) */
	size: number;

	/** Whether panel is maximized (fills entire container) */
	maximized: boolean;

	/** Size to restore when expanding from collapsed state */
	cachedSize?: number;

	/** Flex value to restore when expanding from collapsed state */
	cachedFlex?: number;

	/**
	 * Layout priority for space distribution.
	 * - high: Main content areas (never collapse)
	 * - normal: Sidebars and panels (shrink but don't collapse)
	 * - low: Terminals, breadcrumbs (collapse first)
	 */
	priority: 'high' | 'normal' | 'low';

	/** Minimum size in pixels (enforced when visible) */
	minSize?: number;

	/** Maximum size in pixels */
	maxSize?: number;

	/**
	 * Snap points for magnetic resize (in pixels).
	 * When dragging within snapThreshold of a snap point, panel snaps to that size.
	 * @example [60, 250, 400] - Mini, Normal, Wide modes
	 */
	snapPoints?: number[];

	/** Distance from snap point to trigger snapping (default: 20px) */
	snapThreshold?: number;

	/** Size below which panel auto-collapses (default: 50px) */
	autoCollapseThreshold?: number;

	/** Panel type for content state serialization (registered with PanelDescriptor) */
	panelType?: string;

	/** Initial size of pane for first layout computation */
	initialSize?: number;

	/** Unit for initial size: 'percent' or 'pixels' */
	initialSizeUnit?: 'percent' | 'pixels';

	/**
	 * False until the user has dragged a handle for the first time.
	 * When false and initialSizeUnit === 'pixels', recomputeFromFlexes will
	 * convert the pixel initialSize to a proper flex ratio on first layout pass
	 * (deferred because container size is unknown at normalizeFlexes time).
	 */
	initialFlexSet?: boolean;
}

/**
 * Thread node representing a container that splits space among children.
 * Children can be either leaf nodes or other thread nodes (arbitrary nesting).
 */
export interface ThreadNode {
	type: 'thread';

	/** Layout axis: 'row' for horizontal split, 'column' for vertical split */
	axis: 'row' | 'column';

	/** Child nodes (can be leaves or other threads) */
	children: LayoutNode[];

	/**
	 * Normalized flex values for space distribution.
	 * Invariant: sum(flexes where flexes[i] > 0) === count of active (non-collapsed) children
	 * Using Float32Array for performance and memory efficiency.
	 *
	 * Collapse semantics:
	 * - flex > 0 means child is active (receives allocation during recompute)
	 * - flex = 0 means child is collapsed (no allocation, no handle space)
	 * - handles counted only between active siblings
	 *
	 * These represent persistent proportions used for:
	 * - Resizing operations (dragging a divider)
	 * - Container resize recomputation
	 * - Serialization/persistence
	 */
	/**
	 * NOTE: plain number[] (not Float32Array) so Svelte 5's proxy tracks index writes
	 * reactively. Float32Array index mutations are invisible to Svelte proxies,
	 * requiring a manual layoutVersion counter. number[] eliminates that workaround.
	 */
	flexes: number[];
}

/**
 * Design Note: Thread Identity
 *
 * Threads do NOT have stable IDs (only leaves do). This is an intentional simplification:
 *
 *  Why: Threads are ephemeral—created/destroyed during tree restructuring (add, remove, split).
 *    Giving them IDs would complicate serialization and identity tracking with no clear benefit.
 *
 *  Trade-off: Simpler mental model—only leaves are addressable units. Threads are
 *    organizational structure, not user-facing panels.
 *
 * ⚠️ Future consideration: If you later need thread-level operations (targeted logging,
 *    thread-scoped persistence, thread-level instrumentation), add `id?: string` to
 *    ThreadNode and update serialization. This is a non-breaking change.
 */
