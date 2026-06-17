/**
 * SplitPanelState - Core Layout State Manager
 *
 * Root state manager for the split panel layout system.
 * Uses Svelte 5 runes for automatic reactivity and HPD utilities for events.
 *
 * Key features:
 * - RAF-batched resize operations for 60fps performance
 * - O(1) panel lookups via path caching
 * - O(1) DOM access via element registry
 * - Spring animations for smooth transitions
 * - State machine guards to prevent reentrancy
 * - Dimension caching for BEAR 3 constraint validation
 */

import { createId, CustomEventDispatcher } from '../../internal/index.js';
import { Spring } from 'svelte/motion';
import type {
	LayoutNode,
	LeafNode,
	ThreadNode,
	Result,
	SerializedNode,
	SerializedLayout,
	LayoutSnapshot,
	PersistedLayout
} from '../types/index.js';
import { Ok, Err, isLayoutSnapshot } from '../types/index.js';
import { getPanelDescriptor } from '../utils/index.js';

/**
 * Layout change event detail types
 */
export type LayoutChangeDetail =
	| { type: 'panel-added'; panelId: string; path: number[] }
	| { type: 'panel-removed'; panelId: string }
	| { type: 'panel-toggled'; panelId: string; visible: boolean }
	| { type: 'resize-batch-applied'; updates: Array<{ panelId: string; newSize: number }> };

/**
 * Root state manager for split panel layout system.
 */
export class SplitPanelState {
	/**
	 * Resize handle thickness in pixels.
	 * Used consistently throughout layout calculations, grid templates, and CSS.
	 * Centralized to simplify maintenance: changing here updates all calculations.
	 */
	static readonly HANDLE_SIZE_PX = 4;

	/**
	 * Epsilon threshold for flex "active" detection.
	 * Values <= this are treated as zero (collapsed) to prevent zombie panels from float rounding.
	 * After many normalize operations, flex can accumulate to 1e-9 and incorrectly appear "active".
	 * This threshold ensures only meaningfully nonzero flexes participate in layout.
	 */
	static readonly FLEX_EPS = 1e-6;

	// ===== Event Dispatchers =====

	/**
	 * Layout change event dispatcher for user callbacks.
	 * Uses HPD CustomEventDispatcher for type-safe events.
	 */
	readonly layoutChangeEvent = new CustomEventDispatcher<LayoutChangeDetail>('layoutchange');

	// ===== Reactive State =====

	/**
	 * Root layout tree.
	 * Svelte 5 $state creates deep reactive proxies automatically.
	 */
	root = $state<LayoutNode>({
		type: 'thread',
		axis: 'row', // Default to horizontal layout (matches most UI patterns)
		children: [],
		flexes: []
	});

	/**
	 * State machine guard to prevent reentrancy.
	 * Protects against concurrent layout modifications.
	 */
	#state = $state<'idle' | 'busy'>('idle');

	/**
	 * Accumulated resize deltas per divider, coalesced from high-frequency input events.
	 * Multiple pointer events per frame (gaming mouse at 1000Hz) are accumulated into one delta.
	 * Key format: "${parentPath.join(',')}:${dividerIndex}" (encoded by encodeResizeKey)
	 * Processed once per RAF frame via #rafHandle loop.
	 */
	#pendingDeltas = new Map<string, number>();

	/**
	 * RAF loop handle for batching pending resize operations.
	 * Ensures all accumulated deltas are applied in a single frame,
	 * preventing 16+ resize calculations per frame on high-poll mice.
	 */
	#rafHandle: number | null = null;

	/**
	 * Starvation escape hatch: timestamp when RAF rescheduling started.
	 * Tracks how long we've been waiting for busy state to clear.
	 */
	#rafStarvationStart: number | null = null;

	/**
	 * Flag indicating deltas should be flushed immediately when busy state clears.
	 * Set when starvation timeout is reached (>100ms waiting for busy to clear).
	 * This ensures we flush as soon as safe rather than during a critical section.
	 *
	 * SAFETY: Never force flush mid-busy. Invariants may be partially updated.
	 * Instead, set this flag and flush when the busy operation naturally completes.
	 */
	#forceFlushAfterBusy = false;

	/**
	 * Path cache: panelId → number[] (path in tree)
	 * Updated on structural changes, used for O(1) lookups during drag.
	 */
	#panelPathCache = new Map<string, number[]>();

	/**
	 * Element registry: panelId → HTMLElement
	 * Populated by registerPanel action when panels mount, cleared on unmount.
	 *
	 * PERFORMANCE: Avoids O(n) document.querySelector calls during snapshot
	 * operations (undo/redo, persistence). Direct Map lookup is O(1).
	 *
	 * RELIABILITY: Immune to forceMount/unmount timing issues that could
	 * cause querySelector to fail finding panels in unexpected DOM states.
	 */
	#panelElementRegistry = new Map<string, HTMLElement>();

	/**
	 * Spring animations for smooth panel size transitions.
	 * Each panel gets a Spring instance with physics-based damping/stiffness.
	 * Enables smooth resize animations and momentum/fling gestures.
	 */
	#panelSprings = new Map<string, Spring<number>>();

	/**
	 * Container dimensions used for flex-to-size recomputation.
	 * Tracks the last known container size for layout calculations.
	 */
	#containerSize = $state<{ width: number; height: number }>({ width: 0, height: 0 });

	/**
	 * Cache of all node dimensions (both leaf and thread) from the last recompute/render cycle.
	 * Key: stable object reference (leaf or thread node)
	 * Value: { width, height } in pixels (absolute coordinates, not relative)
	 *
	 * Populated during recomputeFromFlexes for every node in the tree.
	 * Eliminates axis-ambiguity when measuring nested subtrees:
	 * - sizeAlongAxis(node, 'row') returns cached width
	 * - sizeAlongAxis(node, 'column') returns cached height
	 *
	 * This enables correct measurement for arbitrary nesting depth + mixed axes
	 * without recursive traversal (faster, more correct).
	 *
	 *  Solves: Axis-ambiguous leaf.size with arbitrary nesting
	 *  BEAR 3: Enables true constraint enforcement without reflow
	 */
	#nodeDimsCache = new WeakMap<LayoutNode, { width: number; height: number }>();

	/**
	 * Cache of thread node dimensions for constraint validation.
	 * Subset of #nodeDimsCache: only threads, with handle-aware main axis.
	 * Kept separate for clarity in BEAR 3 logic (applyResizeDelta).
	 */
	#threadDimsCache = new WeakMap<ThreadNode, { main: number; cross: number }>();

	/**
	 * Panel content states to restore after deserialization.
	 * Set by deserialize() before returning, queried by components on mount.
	 * Components call getPanelStateToRestore(panelId) after mounting to get their saved state.
	 */
	#panelStatesToRestore = new Map<string, unknown>();

	// ===== Public Getters =====

	/**
	 * Get current container width in pixels.
	 */
	get containerWidth(): number {
		return this.#containerSize.width;
	}

	/**
	 * Get current container height in pixels.
	 */
	get containerHeight(): number {
		return this.#containerSize.height;
	}

	/**
	 * Get current container size (width and height).
	 * Used by LayoutHistory for snapshot capture.
	 */
	get containerSize(): { width: number; height: number } {
		return this.#containerSize;
	}

	/**
	 * Get current layout state (idle or busy).
	 * Useful for debugging or UI feedback.
	 */
	get state(): 'idle' | 'busy' {
		return this.#state;
	}

	// ===== Derived Values (Auto-Memoized) =====

	/**
	 * Flat list of all leaf panels in the tree.
	 * Automatically recalculates when tree structure changes.
	 */
	readonly flatPanels = $derived.by(() => {
		const result: LeafNode[] = [];
		this.traverseTree(this.root, (node) => {
			if (node.type === 'leaf') {
				result.push(node);
			}
		});
		return result;
	});

	/**
	 * Minimum dimensions required for entire layout tree to fit all children.
	 * Automatically recalculates when tree structure or constraints change.
	 * minWidth: minimum extent along row axis
	 * minHeight: minimum extent along column axis
	 *  BEAR 3: Enables reactive constraint validation in resizePanel()
	 */
	readonly minSizeRequired = $derived.by(() => {
		const root = this.root;
		return {
			minWidth: this.minAlongAxis(root, 'row'),
			minHeight: this.minAlongAxis(root, 'column')
		};
	});

	// ===== Element Registry Methods =====

	/**
	 * Register a panel element for direct O(1) lookup.
	 * Called by registerPanel action when panel mounts.
	 */
	public registerPanelElement(panelId: string, element: HTMLElement): void {
		this.#panelElementRegistry.set(panelId, element);
	}

	/**
	 * Unregister a panel element when it unmounts.
	 * Called by registerPanel action's destroy callback.
	 */
	public unregisterPanelElement(panelId: string): void {
		this.#panelElementRegistry.delete(panelId);
	}

	/**
	 * Get a registered panel element by ID.
	 * Returns undefined if panel is not mounted (collapsed, unmounted, etc.).
	 *
	 * Used by LayoutHistory for O(1) snapshot operations instead of DOM queries.
	 */
	public getPanelElement(panelId: string): HTMLElement | undefined {
		return this.#panelElementRegistry.get(panelId);
	}

	// ===== Container Size Management =====

	/**
	 * Update container dimensions and recompute leaf sizes from flexes.
	 * Called when container is resized (typically from ResizeObserver in Svelte component).
	 *
	 * The recomputeFromFlexes call will cache root dimensions automatically,
	 * accounting for active children and handle space correctly.
	 * No manual root cache needed.
	 */
	updateContainerSize(width: number, height: number): void {
		this.#containerSize = { width, height };
		// Immediately recompute all leaf sizes based on new container dimensions
		// This also caches root dimensions via getThreadDims for BEAR 3 validation
		this.recomputeFromFlexes(this.root, { width, height });
	}

	// ===== Helper Methods (Stubs - to be implemented) =====

	/**
	 * Traverse the layout tree depth-first, calling visitor on each node.
	 */
	private traverseTree(node: LayoutNode, visitor: (node: LayoutNode) => void): void {
		visitor(node);
		if (node.type === 'thread') {
			for (const child of node.children) {
				this.traverseTree(child, visitor);
			}
		}
	}

	// ===== Public CRUD Operations =====

	/**
	 * Initialize Spring animation for a panel.
	 * Called when panel is created to set up smooth resize animations.
	 */
	private initializeSpring(panelId: string, initialSize: number): Spring<number> {
		const spring = new Spring(initialSize, {
			stiffness: 0.2,
			damping: 0.4,
			precision: 0.1
		});
		this.#panelSprings.set(panelId, spring);
		return spring;
	}

	/**
	 * Add a new panel to the layout tree.
	 *
	 * @param panelId - Unique identifier (use createId() if not provided)
	 * @param path - Tree path where to insert
	 * @param config - Panel configuration
	 * @returns Result with success or error details
	 */
	addPanel(panelId: string | undefined, path: number[], config: Partial<LeafNode>): Result<void> {
		// Generate ID using HPD utility if not provided
		const id = panelId ?? createId('split-panel');

		// Guard against reentrancy
		if (this.#state === 'busy') {
			return Err({
				type: 'layout-in-progress',
				message: 'Cannot modify during layout calculation'
			});
		}

		// Validate no duplicate ID
		if (this.findPanelPath(id)) {
			return Err({ type: 'duplicate-id', id });
		}

		this.#state = 'busy';
		try {
			// Validate path and get parent
			if (!this.isValidPath(path)) {
				return Err({ type: 'invalid-path', path });
			}

			const parent = this.getNodeAt(path);
			if (parent.type !== 'thread') {
				return Err({
					type: 'invalid-parent',
					message: 'Cannot add panel to leaf node'
				});
			}

			// Validate constraints
			if (config.minSize && config.maxSize && config.minSize > config.maxSize) {
				return Err({
					type: 'constraint-violation',
					message: 'minSize cannot exceed maxSize'
				});
			}

			// Create new leaf node
			const initialSize = config.size ?? 300;
			const newLeaf: LeafNode = {
				type: 'leaf',
				id,
				size: initialSize,
				maximized: false,
				priority: config.priority ?? 'normal',
				minSize: config.minSize,
				maxSize: config.maxSize,
				snapPoints: config.snapPoints,
				snapThreshold: config.snapThreshold,
				autoCollapseThreshold: config.autoCollapseThreshold,
				initialSize: config.initialSize,
				initialSizeUnit: config.initialSizeUnit
			};

			// Initialize Spring animation for this panel
			this.initializeSpring(id, initialSize);

			// Add to parent (Svelte tracks mutation automatically)
			parent.children = [...parent.children, newLeaf];

			// Extend flexes array to match new children count
			// Spread existing values then append 1.0 for the new child (normalized below)
			parent.flexes = [...parent.flexes, 1.0];

			this.normalizeFlexes(parent);

			// Refresh caches after structural change (children count changed)
			this.afterStructuralChange();

			// Cache the new panel's path
			this.#panelPathCache.set(id, [...path, parent.children.length - 1]);

			// Dispatch event
			this.layoutChangeEvent.dispatch(window, {
				type: 'panel-added',
				panelId: id,
				path
			});

			return Ok(undefined);
		} finally {
			this.#state = 'idle';
		}
	}

	/**
	 * Remove a panel from the layout tree.
	 * Recursively finds and removes the panel, then sanitizes the tree.
	 */
	removePanel(panelId: string): Result<boolean> {
		if (this.#state === 'busy') {
			return Err({
				type: 'layout-in-progress',
				message: 'Cannot modify during layout calculation'
			});
		}

		this.#state = 'busy';
		try {
			const result = this.removePanelRecursive(this.root, panelId);

			// Check if panel was actually found
			if (!result.found) {
				return Err({ type: 'not-found', id: panelId });
			}

			// Panel was found and removed. Set root to new tree (or empty if null).
			this.root = result.node ?? {
				type: 'thread',
				axis: 'column',
				children: [],
				flexes: []
			};

			// Clean up Spring animation for this panel
			this.#panelSprings.delete(panelId);

			// Invalidate path cache (structure changed)
			this.#panelPathCache.delete(panelId);

			// Invalidate dimension caches (root structure changed)
			this.afterStructuralChange();

			// Dispatch event
			this.layoutChangeEvent.dispatch(window, {
				type: 'panel-removed',
				panelId
			});

			return Ok(true);
		} finally {
			this.#state = 'idle';
		}
	}

	/**
	 * Toggle panel collapsed state.
	 */
	togglePanel(panelId: string): Result<void> {
		const path = this.findPanelPath(panelId);
		if (!path) {
			return Err({ type: 'not-found', id: panelId });
		}

		const parent = this.getNodeAt(path.slice(0, -1)) as ThreadNode;
		const index = path[path.length - 1];
		const node = parent.children[index] as LeafNode;

		if (node.size === 0) {
			// Expand: restore cached size and flex
			node.size = node.cachedSize ?? 300;
			node.cachedSize = undefined;

			// Re-enable flex participation: restore cached flex or default to 1.0
			parent.flexes[index] = node.cachedFlex ?? 1.0;
			node.cachedFlex = undefined;
			// Don't normalize - preserve the exact flex ratios
		} else {
			// Collapse: cache current size and flex, then set to 0
			node.cachedSize = node.size;
			node.cachedFlex = parent.flexes[index];
			node.size = 0;

			// Disable flex participation
			parent.flexes[index] = 0;
			// Don't normalize - the 0 flex will naturally be excluded from distribution
		}

		// Recompute layout with new flexes
		this.recomputeFromFlexes(this.root, this.#containerSize);

		// Dispatch event
		this.layoutChangeEvent.dispatch(window, {
			type: 'panel-toggled',
			panelId,
			visible: node.size > 0
		});

		return Ok(undefined);
	}

	// ===== Helper Methods from split-panel-state-helpers.ts =====

	private encodeResizeKey(parentPath: number[], dividerIndex: number): string {
		return `${parentPath.join(',')}:${dividerIndex}`;
	}

	private decodeResizeKey(key: string): { parentPath: number[]; dividerIndex: number } {
		const [pathStr, indexStr] = key.split(':');
		return {
			parentPath: pathStr ? pathStr.split(',').map(Number) : [],
			dividerIndex: parseInt(indexStr, 10)
		};
	}

	private getNodeAt(path: number[]): LayoutNode {
		let current = this.root;
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

	private findPanelPath(panelId: string): number[] | undefined {
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
		return search(this.root, []);
	}

	private isValidPath(path: number[]): boolean {
		try {
			this.getNodeAt(path);
			return true;
		} catch {
			return false;
		}
	}

	private getActiveChildIndices(thread: ThreadNode): number[] {
		const result: number[] = [];
		for (let i = 0; i < thread.children.length; i++) {
			if (thread.flexes[i] > SplitPanelState.FLEX_EPS) {
				result.push(i);
			}
		}
		return result;
	}

	private removePanelRecursive(
		node: LayoutNode,
		panelId: string
	): { node: LayoutNode | null; found: boolean } {
		if (node.type === 'leaf') {
			return node.id === panelId ? { node: null, found: true } : { node, found: false };
		}

		let found = false;
		const newChildren: LayoutNode[] = [];
		const newFlexes: number[] = [];

		for (let i = 0; i < node.children.length; i++) {
			const result = this.removePanelRecursive(node.children[i], panelId);
			if (result.found) {
				found = true;
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

		if (newChildren.length === 0) {
			return { node: null, found: true };
		}

		if (newChildren.length === 1) {
			return { node: newChildren[0], found: true };
		}

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
	 * Invalidate all path and dimension caches (called after undo/redo restores a snapshot).
	 */
	invalidatePathCache(): void {
		this.afterStructuralChange();
	}

	private afterStructuralChange(): void {
		// Rebuild path cache
		this.#panelPathCache.clear();
		const buildCache = (node: LayoutNode, path: number[]): void => {
			if (node.type === 'leaf') {
				this.#panelPathCache.set(node.id, path);
			} else {
				for (let i = 0; i < node.children.length; i++) {
					buildCache(node.children[i], [...path, i]);
				}
			}
		};
		buildCache(this.root, []);

		// Clear dimension caches
		this.#nodeDimsCache = new WeakMap();
		this.#threadDimsCache = new WeakMap();
	}

	/**
	 * Normalize flexes so sum of active flexes equals active count.
	 * Uses last-child remainder strategy to handle float rounding.
	 *
	 * Special case: If ALL flexes are 0 (initial state), treat all children as active
	 * and initialize them based on initialSize values if available, otherwise equal distribution.
	 */
	private normalizeFlexes(thread: ThreadNode): void {
		// Check if ALL flexes are 0 (initial state before first normalization)
		let allZero = true;
		for (let i = 0; i < thread.children.length; i++) {
			if (thread.flexes[i] > SplitPanelState.FLEX_EPS) {
				allZero = false;
				break;
			}
		}

		// If all flexes are 0, initialize them based on initialSize or equal distribution
		if (allZero) {
			// Collect active (non-collapsed) children indices
			const activeIndices: number[] = [];
			for (let i = 0; i < thread.children.length; i++) {
				const child = thread.children[i];
				// A child is active if it's a thread OR a leaf with size > 0
				if (child.type === 'thread' || child.size > 0) {
					activeIndices.push(i);
				}
			}

			if (activeIndices.length === 0) {
				// No active children, set all to 1.0 as fallback
				for (let i = 0; i < thread.children.length; i++) {
					thread.flexes[i] = 1.0;
				}
				return;
			}

			// Check if any active leaf has percentage-based initialSize
			let hasPercentageSizes = false;
			let totalPercent = 0;

			for (const idx of activeIndices) {
				const child = thread.children[idx];
				if (child.type === 'leaf' && child.initialSize !== undefined && child.initialSizeUnit === 'percent') {
					hasPercentageSizes = true;
					totalPercent += child.initialSize;
				}
			}

			if (hasPercentageSizes) {
				// Distribute based on percentage initialSize values
				// Panes without percentage get equal share of remaining space
				const panesWithoutPercent: number[] = [];
				let allocatedPercent = 0;

				for (const idx of activeIndices) {
					const child = thread.children[idx];
					if (child.type === 'leaf' && child.initialSize !== undefined && child.initialSizeUnit === 'percent') {
						allocatedPercent += child.initialSize;
					} else {
						panesWithoutPercent.push(idx);
					}
				}

				// Remaining percentage for panes without explicit size
				const remainingPercent = Math.max(0, 100 - allocatedPercent);
				const perPanePercent = panesWithoutPercent.length > 0 
					? remainingPercent / panesWithoutPercent.length 
					: 0;

				// Set flexes based on percentages (flex is proportional to percentage)
				// Normalize so that total flex equals number of active children
				const totalForNormalization = allocatedPercent + (perPanePercent * panesWithoutPercent.length);
				
				for (const idx of activeIndices) {
					const child = thread.children[idx];
					let percent: number;
					
					if (child.type === 'leaf' && child.initialSize !== undefined && child.initialSizeUnit === 'percent') {
						percent = child.initialSize;
					} else if (panesWithoutPercent.includes(idx)) {
						percent = perPanePercent;
					} else {
						percent = 100 / activeIndices.length; // Fallback for threads
					}
					
					// Convert percentage to flex (scaled so sum = activeIndices.length)
					thread.flexes[idx] = (percent / totalForNormalization) * activeIndices.length;
				}

				// Set collapsed panes to 0
				for (let i = 0; i < thread.children.length; i++) {
					if (!activeIndices.includes(i)) {
						thread.flexes[i] = 0;
					}
				}
			} else {
				// Check if any active leaf has pixel-based initialSize
				let hasPixelSizes = false;
				for (const idx of activeIndices) {
					const child = thread.children[idx];
					if (child.type === 'leaf' && child.initialSize !== undefined && child.initialSizeUnit === 'pixels') {
						hasPixelSizes = true;
						break;
					}
				}

				if (hasPixelSizes) {
					// Pixel-sized panes: defer flex-to-pixel conversion to recomputeFromFlexes
					// (container size is unknown here). Give equal flex as placeholder and mark
					// initialFlexSet=false so recomputeFromFlexes can fix it on first layout pass.
					for (let i = 0; i < thread.children.length; i++) {
						thread.flexes[i] = activeIndices.includes(i) ? 1.0 : 0;
					}
					for (const idx of activeIndices) {
						const child = thread.children[idx];
						if (child.type === 'leaf' && child.initialSize !== undefined && child.initialSizeUnit === 'pixels') {
							child.initialFlexSet = false;
						}
					}
				} else {
					// No percentage or pixel sizes, use equal distribution for active children
					for (let i = 0; i < thread.children.length; i++) {
						thread.flexes[i] = activeIndices.includes(i) ? 1.0 : 0;
					}
				}
			}
			return;
		}

		// Normal case: get active children (flex > FLEX_EPS)
		const active = this.getActiveChildIndices(thread);
		if (active.length === 0) return;

		// Sum current active flexes
		let sum = 0;
		for (const idx of active) {
			sum += thread.flexes[idx];
		}

		if (sum === 0) {
			// All active flexes are 0, distribute evenly
			for (const idx of active) {
				thread.flexes[idx] = 1.0;
			}
			sum = active.length;
		}

		// Normalize: scale so sum equals active count
		const scale = active.length / sum;
		for (let i = 0; i < active.length - 1; i++) {
			thread.flexes[active[i]] *= scale;
		}

		// Last child gets remainder to ensure exact sum
		const sumExceptLast = active.slice(0, -1).reduce((acc, idx) => acc + thread.flexes[idx], 0);
		thread.flexes[active[active.length - 1]] = active.length - sumExceptLast;
	}

	/**
	 * Calculate minimum size required along a specific axis.
	 * Collapsed panes (size=0) contribute 0 to minimum calculations.
	 */
	private minAlongAxis(node: LayoutNode, axis: 'row' | 'column'): number {
		if (node.type === 'leaf') {
			// Collapsed panes (size=0) don't contribute to minimum space requirements
			if (node.size === 0) {
				return 0;
			}
			return node.minSize ?? 0;
		}

		// Thread: sum minimums of children along axis
		if (node.axis === axis) {
			// Same axis: sum minimums + handles
			const active = this.getActiveChildIndices(node);
			let sum = 0;
			for (const idx of active) {
				sum += this.minAlongAxis(node.children[idx], axis);
			}
			// Add handle space between active children
			const handleCount = Math.max(0, active.length - 1);
			sum += handleCount * SplitPanelState.HANDLE_SIZE_PX;
			return sum;
		} else {
			// Cross axis: max of minimums
			let max = 0;
			for (const child of node.children) {
				const childMin = this.minAlongAxis(child, axis);
				max = Math.max(max, childMin);
			}
			return max;
		}
	}

	/**
	 * Get size of a node along a specific axis using dimension cache.
	 *
	 * @param node - Node to measure
	 * @param axis - Axis to measure along ('row' = width, 'column' = height)
	 * @param parentAxis - Parent's axis for correct cache lookup
	 * @returns Size in pixels
	 */
	sizeAlongAxis(
		node: LayoutNode,
		axis: 'row' | 'column',
		parentAxis: 'row' | 'column'
	): number {
		// Leaf: size is the single source of truth - no cache needed
		// leaf.size represents the dimension along the parent's main axis
		if (node.type === 'leaf') {
			return node.size;
		}

		// Thread: use cache if available (computing requires traversing children)
		const cached = this.#nodeDimsCache.get(node);
		if (cached) {
			return axis === 'row' ? cached.width : cached.height;
		}

		// Fallback: compute from children
		if (node.axis === axis) {
			const active = this.getActiveChildIndices(node);
			let sum = 0;
			for (const idx of active) {
				sum += this.sizeAlongAxis(node.children[idx], axis, node.axis);
			}
			const handleCount = Math.max(0, active.length - 1);
			sum += handleCount * SplitPanelState.HANDLE_SIZE_PX;
			return sum;
		} else {
			// Cross axis: max of children
			let max = 0;
			for (const child of node.children) {
				max = Math.max(max, this.sizeAlongAxis(child, axis, node.axis));
			}
			return max;
		}
	}

	/**
	 * Get cached thread dimensions.
	 */
	private getThreadDims(thread: ThreadNode): { main: number; cross: number } | undefined {
		return this.#threadDimsCache.get(thread);
	}

	/**
	 * Recompute all leaf sizes from flexes based on container dimensions.
	 */
	private recomputeFromFlexes(
		node: LayoutNode,
		dims: { width: number; height: number },
		_parentAxis?: 'row' | 'column'
	): void {
		// Cache node dimensions
		this.#nodeDimsCache.set(node, dims);

		if (node.type === 'leaf') {
			// Leaf: size determined by parent's flex allocation
			return;
		}

		// Thread: distribute space to children
		const active = this.getActiveChildIndices(node);
		if (active.length === 0) return;

		const mainAxis = node.axis;
		const mainSize = mainAxis === 'row' ? dims.width : dims.height;
		const crossSize = mainAxis === 'row' ? dims.height : dims.width;

		// Cache thread dims for BEAR 3
		this.#threadDimsCache.set(node, { main: mainSize, cross: crossSize });

		// Calculate available space (subtract handles between active children only)
		const handleCount = Math.max(0, active.length - 1);
		const availableMain = mainSize - handleCount * SplitPanelState.HANDLE_SIZE_PX;

		// First: cache dimensions for COLLAPSED children as 0
		// This ensures constraint calculations don't use stale cached values
		for (let i = 0; i < node.children.length; i++) {
			if (node.flexes[i] <= SplitPanelState.FLEX_EPS) {
				const child = node.children[i];
				// Collapsed child gets 0 size in main axis, but inherits cross axis
				const collapsedDims = mainAxis === 'row'
					? { width: 0, height: crossSize }
					: { width: crossSize, height: 0 };
				this.#nodeDimsCache.set(child, collapsedDims);
				
				// If collapsed child is a leaf, ensure its size is 0
				if (child.type === 'leaf') {
					child.size = 0;
				}
			}
		}

		// Deferred pixel-to-flex conversion: runs on first layout pass when container size
		// is known. Any active leaf with initialFlexSet===false and initialSizeUnit==='pixels'
		// gets its flex recomputed from its pixel initialSize relative to availableMain.
		if (availableMain > 0) {
			let anyDeferred = false;
			for (const idx of active) {
				const child = node.children[idx];
				if (
					child.type === 'leaf' &&
					child.initialFlexSet === false &&
					child.initialSize !== undefined &&
					child.initialSizeUnit === 'pixels'
				) {
					anyDeferred = true;
					break;
				}
			}

			if (anyDeferred) {
				// Compute flex for pixel-pinned panes first
				let pixelFlexSum = 0;
				let pixelCount = 0;
				const pixelIndices = new Set<number>();

				for (const idx of active) {
					const child = node.children[idx];
					if (
						child.type === 'leaf' &&
						child.initialFlexSet === false &&
						child.initialSize !== undefined &&
						child.initialSizeUnit === 'pixels'
					) {
						// flex proportional to pixel value; will renormalize below
						node.flexes[idx] = child.initialSize;
						pixelFlexSum += child.initialSize;
						pixelCount++;
						pixelIndices.add(idx);
						child.initialFlexSet = true;
					}
				}

				// Unsized panes share the remaining space equally.
				// Use average of pixel-sized panes as their weight so the proportions hold.
				const unsizedIndices = active.filter(i => !pixelIndices.has(i));
				const avgPixelFlex = pixelCount > 0 ? pixelFlexSum / pixelCount : availableMain / active.length;
				for (const idx of unsizedIndices) {
					node.flexes[idx] = avgPixelFlex;
				}

				// Renormalize so sum of active flexes === active.length (invariant)
				const rawSum = active.reduce((s, i) => s + node.flexes[i], 0);
				if (rawSum > SplitPanelState.FLEX_EPS) {
					const scale = active.length / rawSum;
					for (const idx of active) {
						node.flexes[idx] *= scale;
					}
				}
			}
		}

		// Calculate sum of active flexes for proportional distribution
		let totalFlex = 0;
		for (const idx of active) {
			totalFlex += node.flexes[idx];
		}

		// Fallback if all flexes are 0 (shouldn't happen but be safe)
		if (totalFlex <= SplitPanelState.FLEX_EPS) {
			totalFlex = active.length;
			for (const idx of active) {
				node.flexes[idx] = 1.0;
			}
		}

		// Distribute space according to flexes (proportionally)
		for (const idx of active) {
			const child = node.children[idx];
			const flex = node.flexes[idx];
			const allocation = (flex / totalFlex) * availableMain;

			if (child.type === 'leaf') {
				// Leaf: size is the single source of truth - no cache needed
				child.size = Math.max(0, allocation);
			} else {
				// Thread: recursively recompute and cache dimensions
				const childDims =
					mainAxis === 'row'
						? { width: allocation, height: crossSize }
						: { width: crossSize, height: allocation };
				this.recomputeFromFlexes(child, childDims, mainAxis);
			}
		}
	}

	/**
	 * Apply snap point logic to a resize operation.
	 */
	private applySnapPoint(leaf: LeafNode, proposedSize: number): number {
		if (!leaf.snapPoints || leaf.snapPoints.length === 0) {
			return proposedSize;
		}

		const threshold = leaf.snapThreshold ?? 20;
		for (const snapPoint of leaf.snapPoints) {
			if (Math.abs(proposedSize - snapPoint) < threshold) {
				return snapPoint;
			}
		}

		return proposedSize;
	}

	/**
	 * Update flexes after a size change.
	 */
	private updateFlexes(thread: ThreadNode): void {
		const active = this.getActiveChildIndices(thread);
		if (active.length === 0) return;

		// Recalculate flexes proportionally based on current sizes
		let totalSize = 0;
		for (const idx of active) {
			const child = thread.children[idx];
			const size =
				child.type === 'leaf' ? child.size : this.sizeAlongAxis(child, thread.axis, thread.axis);
			totalSize += size;
		}

		if (totalSize === 0) return;

		// Update flexes proportionally
		for (const idx of active) {
			const child = thread.children[idx];
			const size =
				child.type === 'leaf' ? child.size : this.sizeAlongAxis(child, thread.axis, thread.axis);
			thread.flexes[idx] = (size / totalSize) * active.length;
		}

		this.normalizeFlexes(thread);
	}

	// ===== Resize Operations =====

	/**
	 * Resize a divider between two adjacent active siblings.
	 * Uses RAF batching to accumulate high-frequency pointer events (1000Hz+).
	 *
	 * BATCHING STRATEGY:
	 * - Accumulate deltas in #pendingDeltas Map keyed by "parentPath:dividerIndex"
	 * - Schedule RAF flush on first delta in a frame
	 * - Apply all accumulated deltas in one critical section during flush
	 *
	 * STARVATION PROTECTION:
	 * - If busy for >100ms, set #forceFlushAfterBusy flag instead of forcing mid-critical-section
	 * - When busy operation completes, flush immediately if flag is set
	 *
	 * @param parentPath - Path to parent thread node
	 * @param dividerIndex - Index of divider between active children (0-based)
	 * @param delta - Pixels to move in positive direction (right/down)
	 */
	resizeDivider(parentPath: number[], dividerIndex: number, delta: number): Result<void> {
		// Encode resize key for batching
		const key = this.encodeResizeKey(parentPath, dividerIndex);

		// Accumulate delta
		const currentDelta = this.#pendingDeltas.get(key) ?? 0;
		this.#pendingDeltas.set(key, currentDelta + delta);

		// Schedule RAF flush if not already scheduled
		if (this.#rafHandle === null) {
			this.#rafHandle = requestAnimationFrame(() => this.#flushPendingDeltas());
		}

		return Ok(undefined);
	}

	/**
	 * Flush all pending resize deltas in one critical section.
	 * If called while busy (state === 'busy'), reschedules for next frame.
	 *
	 * STARVATION SAFETY:
	 * If waiting > 100ms for busy to clear, we set #forceFlushAfterBusy flag instead
	 * of forcing a flush mid-critical-section. This prevents applying deltas against
	 * an inconsistent tree with partially-updated invariants.
	 *
	 * When the current busy operation completes and returns to idle, the finally
	 * block checks #forceFlushAfterBusy and immediately flushes if set.
	 */
	#flushPendingDeltas(): void {
		if (this.#state === 'busy') {
			// Track how long we've been waiting for busy state to clear
			const now = performance.now();
			if (!this.#rafStarvationStart) {
				this.#rafStarvationStart = now;
			}

			const starvationDuration = now - this.#rafStarvationStart;
			if (starvationDuration > 100) {
				// SAFETY: Don't force flush mid-busy - invariants may be inconsistent.
				// Instead, set flag so we flush immediately when busy naturally completes.
				console.warn(
					`RAF flush starvation detected (${starvationDuration.toFixed(0)}ms). ` +
						`Scheduling flush for when busy state clears.`
				);
				this.#forceFlushAfterBusy = true;
				// Don't reschedule - the busy operation's finally block will handle it
				return;
			} else {
				// Still within timeout window; reschedule for next frame
				this.#rafHandle = requestAnimationFrame(() => this.#flushPendingDeltas());
				return;
			}
		}

		// Not busy; clear starvation timer and flag
		this.#rafStarvationStart = null;
		this.#forceFlushAfterBusy = false;

		this.#state = 'busy';
		try {
			// Process all accumulated deltas in one critical section
			// Track affected leaf panels so we can dispatch a single batch event
			const affectedPanels = new Map<string, number>();

			for (const [key, accumulatedDelta] of this.#pendingDeltas) {
				const { parentPath, dividerIndex } = this.decodeResizeKey(key);
				const updates = this.applyDividerResize(parentPath, dividerIndex, accumulatedDelta);
				// Collect updates from resize operation (returns affected panel IDs and new sizes)
				for (const [panelId, newSize] of updates) {
					affectedPanels.set(panelId, newSize);
				}
			}

			this.#pendingDeltas.clear();

			// Dispatch single batch event after all resizes complete
			// This ensures history and persistence capture changes consistently
			if (affectedPanels.size > 0) {
				const updates = Array.from(affectedPanels, ([panelId, newSize]) => ({ panelId, newSize }));
				this.layoutChangeEvent.dispatch(window, {
					type: 'resize-batch-applied',
					updates
				});
			}
		} finally {
			this.#state = 'idle';
			this.#rafHandle = null;

			// STARVATION RECOVERY: If we waited too long for busy to clear,
			// flush immediately now that we're safely idle again.
			if (this.#forceFlushAfterBusy && this.#pendingDeltas.size > 0) {
				this.#forceFlushAfterBusy = false;
				// Use queueMicrotask to flush in same frame but after current stack clears
				queueMicrotask(() => this.#flushPendingDeltas());
			}
		}
	}

	/**
	 * Apply a divider resize operation to two adjacent siblings.
	 * Updates both panels' sizes and flexes while respecting all constraints.
	 *
	 * Returns: Map of affected leaf panel IDs to their new sizes.
	 * Used by #flushPendingDeltas() to dispatch a single batch event.
	 *
	 * Algorithm:
	 * 1. Find left/top and right/bottom siblings
	 * 2. Calculate proposed new sizes (left += delta, right -= delta)
	 * 3. Clamp each with its own min/max
	 * 4. If one clamped, adjust the other (maintain zero-sum)
	 * 5. Validate parent constraints (BEAR 3)
	 * 6. Apply and update flexes
	 * 7. Auto-collapse panels below threshold
	 */
	private applyDividerResize(
		parentPath: number[],
		dividerIndex: number,
		delta: number
	): Map<string, number> {
		const affectedPanels = new Map<string, number>();

		let parent: LayoutNode;
		try {
			parent = this.getNodeAt(parentPath);
		} catch {
			return affectedPanels; // Path invalid, skip
		}

		if (parent.type !== 'thread') {
			return affectedPanels; // Parent must be thread
		}

		// dividerIndex is the PHYSICAL divider index:
		// - dividerIndex 0 is between children[0] and children[1]
		// - dividerIndex 1 is between children[1] and children[2]
		// etc.
		//
		// We need to find the closest ACTIVE children on either side of this physical divider.
		// A divider at physical index N sits between children[N] and children[N+1].
		// We scan backward from N to find the nearest active left child,
		// and forward from N+1 to find the nearest active right child.

		// Validate physical divider index bounds
		if (dividerIndex < 0 || dividerIndex >= parent.children.length - 1) {
			return affectedPanels; // Divider out of bounds
		}

		// Find nearest active child to the left (scan backward from dividerIndex)
		let leftIdx = -1;
		let leftIsCollapsed = false;
		for (let i = dividerIndex; i >= 0; i--) {
			if (parent.flexes[i] > SplitPanelState.FLEX_EPS) {
				leftIdx = i;
				break;
			}
		}

		// Find nearest active child to the right (scan forward from dividerIndex + 1)
		let rightIdx = -1;
		let rightIsCollapsed = false;
		for (let i = dividerIndex + 1; i < parent.children.length; i++) {
			if (parent.flexes[i] > SplitPanelState.FLEX_EPS) {
				rightIdx = i;
				break;
			}
		}

		// Handle collapsed panes - allow handle to expand them
		// If no active child on left side, use the immediate left neighbor (collapsed pane)
		if (leftIdx === -1) {
			leftIdx = dividerIndex; // Use the collapsed pane directly adjacent to the handle
			leftIsCollapsed = true;
		}
		
		// If no active child on right side, use the immediate right neighbor (collapsed pane)
		if (rightIdx === -1) {
			rightIdx = dividerIndex + 1; // Use the collapsed pane directly adjacent to the handle
			rightIsCollapsed = true;
		}

		// If both sides are collapsed, we still can't resize (nothing to take from)
		if (leftIsCollapsed && rightIsCollapsed) {
			return affectedPanels;
		}

		const leftChild = parent.children[leftIdx];
		const rightChild = parent.children[rightIdx];
		const parentAxis = parent.axis;

		// Get current allocated sizes (use fresh measurements, not cache)
		// Pass parentAxis to sizeAlongAxis so leaves use correct cached dimension
		const leftSize = this.sizeAlongAxis(leftChild, parentAxis, parentAxis);
		const rightSize = this.sizeAlongAxis(rightChild, parentAxis, parentAxis);

		// Proposed allocation: zero-sum game
		let newLeftSize = leftSize + delta;
		let newRightSize = rightSize - delta;

		// Apply snap points (allow user-defined resize stops)
		if (leftChild.type === 'leaf') {
			newLeftSize = this.applySnapPoint(leftChild, newLeftSize);
		}
		if (rightChild.type === 'leaf') {
			newRightSize = this.applySnapPoint(rightChild, newRightSize);
		}

		// Get constraints
		const leftMin = this.minAlongAxis(leftChild, parentAxis);
		const leftMax = leftChild.type === 'leaf' ? (leftChild.maxSize ?? Infinity) : Infinity;
		const rightMin = this.minAlongAxis(rightChild, parentAxis);
		const rightMax = rightChild.type === 'leaf' ? (rightChild.maxSize ?? Infinity) : Infinity;

		// Clamp left within its bounds
		if (newLeftSize < leftMin) {
			newLeftSize = leftMin;
		} else if (newLeftSize > leftMax) {
			newLeftSize = leftMax;
		}

		// Clamp right within its bounds
		if (newRightSize < rightMin) {
			newRightSize = rightMin;
		} else if (newRightSize > rightMax) {
			newRightSize = rightMax;
		}

		// If both clamped, maintain the zero-sum property
		// by accepting the tighter constraint
		const actualLeftDelta = newLeftSize - leftSize;
		const actualRightDelta = newRightSize - rightSize;

		if (Math.abs(actualLeftDelta + actualRightDelta) > 0.01) {
			// One or both clamped; adjust right to maintain zero-sum
			// Use epsilon (0.01px) instead of exact equality to handle float rounding
			newRightSize = rightSize - actualLeftDelta;
			if (newRightSize < rightMin) {
				newRightSize = rightMin;
				newLeftSize = leftSize - (rightSize - rightMin);
				if (newLeftSize < leftMin) {
					// Can't satisfy both constraints; keep old sizes
					return affectedPanels;
				}
			} else if (newRightSize > rightMax) {
				newRightSize = rightMax;
				newLeftSize = leftSize - (rightSize - rightMax);
				if (newLeftSize > leftMax) {
					return affectedPanels;
				}
			}
		}

		// Validate that thread-children can fit their nested constraints
		// This prevents creating impossible states where a subtree's minimum exceeds allocation
		if (leftChild.type === 'thread') {
			const leftChildMin = this.minAlongAxis(leftChild, parentAxis);
			if (newLeftSize < leftChildMin) {
				// Left thread's nested minimums don't fit in allocated space; reject
				return affectedPanels;
			}
		}
		if (rightChild.type === 'thread') {
			const rightChildMin = this.minAlongAxis(rightChild, parentAxis);
			if (newRightSize < rightChildMin) {
				// Right thread's nested minimums don't fit in allocated space; reject
				return affectedPanels;
			}
		}

		// BEAR 3 hard constraint: Ensure combined allocation never exceeds parent availableMain
		// This is not a warning—if it happens, we clamp to fit (preferring the non-drag side)
		const parentDims = this.getThreadDims(parent);
		if (parentDims) {
			const allocatedSpace = newLeftSize + newRightSize + SplitPanelState.HANDLE_SIZE_PX;
			const maxAllowed = parentDims.main;

			if (allocatedSpace > maxAllowed + 0.01) {
				// epsilon tolerance for float rounding
				// Overflow detected; apply emergency correction
				// Prefer to adjust the side that was NOT user-dragged (usually right side)
				const excess = allocatedSpace - maxAllowed;

				// Try to clamp right side first (non-dragged side)
				const adjustedRight = newRightSize - excess;
				if (adjustedRight >= rightMin) {
					// Right side can absorb the correction
					newRightSize = adjustedRight;
				} else {
					// Right side at minimum, try left side
					const adjustedLeft = newLeftSize - excess;
					if (adjustedLeft >= leftMin) {
						newLeftSize = adjustedLeft;
					} else {
						// Both sides at minimum; can't fit. Reject entire operation.
						console.warn(
							`BEAR 3: Cannot fit allocation within parent bounds. ` +
								`Needed: ${allocatedSpace}px, Available: ${maxAllowed}px. Rejecting resize.`
						);
						return affectedPanels;
					}
				}

				// Verify the correction worked
				const finalAllocated = newLeftSize + newRightSize + SplitPanelState.HANDLE_SIZE_PX;
				if (finalAllocated > maxAllowed + 0.01) {
					console.error(
						`BEAR 3: Correction failed. Final: ${finalAllocated}px > ${maxAllowed}px. Rejecting.`
					);
					return affectedPanels;
				}
			}
		}

		// Auto-collapse if below threshold
		const leftCollapseThreshold =
			leftChild.type === 'leaf' ? (leftChild.autoCollapseThreshold ?? 50) : 0;
		if (newLeftSize < leftCollapseThreshold && newLeftSize > 0) {
			// Cache size and flex before collapsing
			if (leftChild.type === 'leaf') {
				leftChild.cachedSize = leftSize;
				leftChild.cachedFlex = parent.flexes[leftIdx];
			}
			newLeftSize = 0;
			newRightSize = rightSize + leftSize; // Reclaim space
		}

		const rightCollapseThreshold =
			rightChild.type === 'leaf' ? (rightChild.autoCollapseThreshold ?? 50) : 0;
		if (newRightSize < rightCollapseThreshold && newRightSize > 0) {
			// Cache size and flex before collapsing
			if (rightChild.type === 'leaf') {
				rightChild.cachedSize = rightSize;
				rightChild.cachedFlex = parent.flexes[rightIdx];
			}
			newRightSize = 0;
			newLeftSize = leftSize + rightSize; // Reclaim space
		}

		// Update children allocations (works for both leaf and thread nodes)
		// For leaves: directly set size and track in affectedPanels
		// For threads: update flex value to match desired allocation
		if (leftChild.type === 'leaf') {
			this.setLeafSize(leftChild, parent, leftIdx, newLeftSize);
			affectedPanels.set(leftChild.id, newLeftSize);
		} else {
			this.setChildAllocation(parent, leftIdx, newLeftSize, this.#containerSize);
		}

		if (rightChild.type === 'leaf') {
			this.setLeafSize(rightChild, parent, rightIdx, newRightSize);
			affectedPanels.set(rightChild.id, newRightSize);
		} else {
			this.setChildAllocation(parent, rightIdx, newRightSize, this.#containerSize);
		}

		return affectedPanels;
	}

	/**
	 * Set a leaf's size and enforce collapse invariant (size=0 ⟹ flex=0).
	 * Also triggers Spring animation and updates the dimension cache.
	 */
	private setLeafSize(leaf: LeafNode, parent: ThreadNode, index: number, newSize: number): void {
		// Leaf.size is the single source of truth - no cache needed for leaves
		leaf.size = Math.max(0, newSize); // Never negative

		// Enforce collapse authority: if size becomes 0, flex must be 0
		if (leaf.size === 0) {
			parent.flexes[index] = 0;
			// Don't normalize - preserve other flex ratios
		} else if (parent.flexes[index] === 0 && leaf.size > 0) {
			// Re-activate if size > 0 but flex was 0
			// Restore cached flex or default to 1.0
			parent.flexes[index] = leaf.cachedFlex ?? 1.0;
			leaf.cachedFlex = undefined;
			// Don't normalize - preserve flex ratios
		} else {
			// Update flex proportionally
			this.updateFlexes(parent);
		}

		// Trigger Spring animation
		// Spring Animation Strategy:
		// - Layout updates instantly (node.size) → grid $derived recalculates → grid snaps to follow mouse
		// - Spring animates child panel elements separately for visual smoothing
		// - User perceives responsive drag with smooth visual feedback
		const spring = this.#panelSprings.get(leaf.id);
		if (spring) {
			spring.set(leaf.size);
		}
	}

	/**
	 * Set allocation for a child node (leaf or thread).
	 * For leaves: directly set size and trigger flex update.
	 * For threads: convert desired px allocation to flex value and recompute subtree.
	 * This enables divider resize to work with nested threads (not just leaves).
	 */
	private setChildAllocation(
		parent: ThreadNode,
		index: number,
		newMainSize: number,
		containerDims: { width: number; height: number }
	): void {
		const child = parent.children[index];

		// Validate: child must be active to resize
		if (parent.flexes[index] <= 0) return;

		if (child.type === 'leaf') {
			// Leaf: directly set size
			this.setLeafSize(child, parent, index, newMainSize);
			return;
		}

		// Thread: convert desired allocation (px) to flex value
		// Get current allocations of all active children along parent axis
		const active = this.getActiveChildIndices(parent);
		const activeCount = active.length;
		if (activeCount === 0) return;

		// Sum current px allocations of all active children
		let currentTotal = 0;
		for (const activeIdx of active) {
			currentTotal += this.sizeAlongAxis(parent.children[activeIdx], parent.axis, parent.axis);
		}

		if (currentTotal === 0) return;

		// Find this child's position in active set
		const indexInActive = active.indexOf(index);
		if (indexInActive === -1) return;

		// Calculate new flex proportional to allocation
		// flex[i] = (newMainSize / totalMainSize) * activeCount
		const newFlex = (newMainSize / currentTotal) * activeCount;
		parent.flexes[index] = newFlex;
		this.normalizeFlexes(parent);

		// Recompute the thread subtree with CORRECT dimensions for this child
		// BUG FIX: We must pass the actual dimensions allocated to this child,
		// NOT the root container dimensions!
		// - Main axis size = newMainSize (what we just allocated)
		// - Cross axis size = parent's cross axis (inherited from parent)
		const parentDims = this.getThreadDims(parent);
		const crossSize = parentDims?.cross ?? (parent.axis === 'row' ? containerDims.height : containerDims.width);
		
		const childDims = parent.axis === 'row'
			? { width: newMainSize, height: crossSize }
			: { width: crossSize, height: newMainSize };
		
		// Recompute with correct child dimensions
		this.recomputeFromFlexes(child, childDims, parent.axis);
	}

	/**
	 * Convenience wrapper: resize a panel by finding its adjacent divider.
	 * Useful for API compatibility or UI that targets a panel directly.
	 *
	 * Strategy: Prefer the right/bottom divider (dragging right edge).
	 * Falls back to left/top divider if panel is rightmost/bottommost.
	 *
	 * @param panelId - Panel to resize
	 * @param delta - Pixels to move in positive direction (expand right/down)
	 */
	public resizePanel(panelId: string, delta: number): Result<void> {
		const path = this.#panelPathCache.get(panelId);
		if (!path) {
			const found = this.findPanelPath(panelId);
			if (!found) return Err({ type: 'not-found', id: panelId });
			this.#panelPathCache.set(panelId, found);
		}

		const finalPath = this.#panelPathCache.get(panelId)!;
		const parentPath = finalPath.slice(0, -1);
		const childIndex = finalPath[finalPath.length - 1];

		const parent = this.getNodeAt(parentPath) as ThreadNode;
		const active = this.getActiveChildIndices(parent);
		const activeIdx = active.indexOf(childIndex);

		if (activeIdx === -1) {
			return Err({ type: 'invalid-path', path: parentPath });
		}

		// Try to resize right/bottom divider
		if (activeIdx < active.length - 1) {
			return this.resizeDivider(parentPath, activeIdx, delta);
		}

		// At rightmost/bottommost, try left/top divider (inverted delta)
		if (activeIdx > 0) {
			return this.resizeDivider(parentPath, activeIdx - 1, -delta);
		}

		// Only child, nowhere to resize
		return Ok(undefined);
	}

	// ===== Serialization & Panel State Management =====

	/**
	 * Get panel content state to restore after deserialization.
	 * Called by components in onMount to query and restore their saved state.
	 *
	 * @param panelId - Panel ID to get state for
	 * @returns Panel state object, or undefined if not found
	 */
	public getPanelStateToRestore(panelId: string): unknown {
		const state = this.#panelStatesToRestore.get(panelId);
		return state;
	}

	/**
	 * Clear panel states after restoration is complete.
	 * Called by restoration coordinator after all panels have mounted and restored state.
	 */
	public clearPanelStatesToRestore(): void {
		this.#panelStatesToRestore.clear();
	}

	/**
	 * Set panel states to restore after undo/redo or deserialization.
	 * Called by LayoutHistory when restoring a snapshot.
	 *
	 * NOTE: This method exists because private fields (#) cannot be accessed
	 * via bracket notation from external classes. LayoutHistory calls this
	 * instead of trying to set this.layoutState['#panelStatesToRestore'].
	 *
	 * @param states - Map of panelId -> panel state to restore
	 */
	public setPanelStatesToRestore(states: Map<string, unknown>): void {
		this.#panelStatesToRestore = states;
	}

	/**
	 * Serialize layout to plain JSON.
	 * Uses $state.snapshot() to remove Svelte proxies.
	 * Records container size for proper restoration during container resize.
	 *
	 * @param containerWidth - Current container width in pixels
	 * @param containerHeight - Current container height in pixels
	 * @returns Serialized layout with metadata
	 */
	serialize(containerWidth: number, containerHeight: number): SerializedLayout {
		const snapshot = $state.snapshot(this.root);

		return {
			version: 3,
			root: this.serializeNode(snapshot),
			containerWidth,
			containerHeight,
			timestamp: Date.now()
		};
	}

	/**
	 * Serialize a single node recursively.
	 * Converts Float32Array to regular array for JSON compatibility.
	 */
	private serializeNode(node: LayoutNode): SerializedNode {
		if (node.type === 'leaf') {
			return {
				type: 'leaf',
				id: node.id,
				size: node.size,
				maximized: node.maximized,
				cachedSize: node.cachedSize,
				priority: node.priority,
				minSize: node.minSize,
				maxSize: node.maxSize,
				snapPoints: node.snapPoints,
				snapThreshold: node.snapThreshold,
				autoCollapseThreshold: node.autoCollapseThreshold,
				panelType: node.panelType
			};
		}

		return {
			type: 'thread',
			axis: node.axis,
			children: node.children.map((c) => this.serializeNode(c)),
			flexes: Array.from(node.flexes)
		};
	}

	/**
	 * Serialize layout with panel content states for undo/redo or full persistence.
	 * Collects panel states using registered PanelDescriptors.
	 *
	 * @param containerWidth - Current container width in pixels
	 * @param containerHeight - Current container height in pixels
	 * @returns Layout snapshot with panel content states
	 */
	serializeWithPanelStates(containerWidth: number, containerHeight: number): LayoutSnapshot {
		const panelStates: Record<string, unknown> = {};

		// Collect panel states using O(1) element registry lookup
		const collectPanelStates = (node: LayoutNode): void => {
			if (node.type === 'leaf') {
				const panelEl = this.getPanelElement(node.id);
				const descriptor = getPanelDescriptor(node.panelType ?? 'default');
				if (descriptor && panelEl) {
					panelStates[node.id] = descriptor.serialize(panelEl);
				}
			} else {
				node.children.forEach(collectPanelStates);
			}
		};

		collectPanelStates(this.root);

		const layout = this.serialize(containerWidth, containerHeight);

		return {
			...layout,
			panelStates
		};
	}

	/**
	 * Deserialize layout from JSON.
	 * Accepts either SerializedLayout (basic) or LayoutSnapshot (with panel states).
	 *
	 * DRIFT DETECTION:
	 * - Validates flexes on deserialize to detect and fix accumulated drift
	 * - Under active-set semantics: sum(active flexes) should equal activeCount
	 * - Tolerance: 0.1% (acceptable IEEE 754 rounding error)
	 * - If drift detected, resets active flexes to uniform (1.0 per active child)
	 *
	 * COLLAPSE SEMANTICS:
	 * - Children with flex=0 are collapsed and intentional
	 * - activeCount = count of flexes where flexes[i] > FLEX_EPS
	 * - expectedSum = activeCount (not total children)
	 *
	 * @param data - Either SerializedLayout or LayoutSnapshot (with panelStates)
	 * @returns New SplitPanelState instance with restored layout
	 */
	static deserialize(data: PersistedLayout): SplitPanelState {
		const instance = new SplitPanelState();

		// Recursively validate flex integrity before deserialization
		const validateFlexes = (node: SerializedNode): void => {
			if (node.type === 'thread' && node.flexes.length > 0) {
				// Apply epsilon floor: any flex <= FLEX_EPS becomes 0
				// Prevents zombie panels from float rounding in serialized data
				for (let i = 0; i < node.flexes.length; i++) {
					if (node.flexes[i] > 0 && node.flexes[i] <= SplitPanelState.FLEX_EPS) {
						node.flexes[i] = 0;
					}
				}

				// Count active children (flex > FLEX_EPS)
				let activeCount = 0;
				let sumActive = 0;
				for (let i = 0; i < node.flexes.length; i++) {
					if (node.flexes[i] > SplitPanelState.FLEX_EPS) {
						activeCount++;
						sumActive += node.flexes[i];
					}
				}

				// If nothing is active, keep all zeros (intentional collapse)
				if (activeCount === 0) {
					// All zeros is valid state; don't repair
					// Recursively validate children
					node.children.forEach(validateFlexes);
					return;
				}

				// For active children, expected sum = activeCount
				const expected = activeCount;
				const tolerance = expected * 0.001; // 0.1% drift tolerance

				if (Math.abs(sumActive - expected) > tolerance) {
					console.warn(
						`Flex drift detected in serialized data: sum=${sumActive.toFixed(4)}, expected=${expected} ` +
							`(activeCount=${activeCount}). Resetting active flexes to uniform distribution.`
					);
					// Reset active flexes to uniform (1.0 each), keep inactive at 0
					for (let i = 0; i < node.flexes.length; i++) {
						node.flexes[i] = node.flexes[i] > SplitPanelState.FLEX_EPS ? 1.0 : 0;
					}
				}

				// Recursively validate children
				node.children.forEach(validateFlexes);
			}
		};

		validateFlexes(data.root);

		instance.root = instance.deserializeNode(data.root);
		instance.#containerSize = { width: data.containerWidth, height: data.containerHeight };

		// Restore panel content states if present in snapshot
		// Uses isLayoutSnapshot type guard for type-safe threading
		if (isLayoutSnapshot(data)) {
			instance.#panelStatesToRestore = new Map(Object.entries(data.panelStates));
		}

		return instance;
	}

	/**
	 * Deserialize a single node recursively.
	 * Public API for undo/redo and testing purposes.
	 *
	 * @param data - Serialized node to deserialize
	 * @returns Deserialized LayoutNode
	 */
	deserializeNode(data: SerializedNode): LayoutNode {
		if (data.type === 'leaf') {
			return {
				type: 'leaf',
				id: data.id,
				size: data.size,
				maximized: data.maximized,
				cachedSize: data.cachedSize,
				priority: data.priority,
				minSize: data.minSize,
				maxSize: data.maxSize,
				snapPoints: data.snapPoints,
				snapThreshold: data.snapThreshold,
				autoCollapseThreshold: data.autoCollapseThreshold,
				panelType: data.panelType
			};
		}

		return {
			type: 'thread',
			axis: data.axis,
			children: data.children.map((c: SerializedNode) => this.deserializeNode(c)),
			flexes: [...data.flexes]
		};
	}
}
