/**
 * SplitPanelRootState - Component API Wrapper for SplitPanelState
 *
 * Provides a higher-level, component-friendly API wrapping the core SplitPanelState.
 * Follows Bits UI patterns for context management, reactive props, and registration.
 *
 * Features:
 * - Preset configurations (default, IDE, dashboard, presentation)
 * - Storage integration with configurable backends
 * - Undo/redo management
 * - Pane/split registration for programmatic access
 * - Focus tracking and management
 * - Event callbacks for layout changes, pane close/focus
 * - Derived snippet props for component consumers
 */

import { untrack } from 'svelte';
import { SplitPanelState, type LayoutChangeDetail } from '../state/split-panel-state.svelte.js';
import { LayoutHistory } from '../state/layout-history.svelte.js';
import { LayoutPersistence, type StorageState } from '../state/layout-persistence.svelte.js';
import type { LayoutNode, LeafNode, ThreadNode } from '../types/index.js';
import type { SplitPanelSplitState } from './split-panel-split-state.svelte.js';

/**
 * Preset configurations for common layout patterns.
 */
export type LayoutPreset = 'default' | 'ide' | 'dashboard' | 'presentation';

/**
 * Storage backend types for persistence.
 */
export type StorageBackend = 'localStorage' | 'sessionStorage' | 'indexedDB' | 'memory';

/**
 * Pane state information for inspection.
 */
export interface PaneStateInfo {
	id: string;
	size: number;
	isCollapsed: boolean;
	isFocused: boolean;
}

/**
 * Layout state API exposed to components via snippet props.
 */
export interface SplitPanelLayoutState {
	undo: () => void;
	redo: () => void;
	canUndo: boolean;
	canRedo: boolean;
	focusPane: (paneId: string) => void;
	collapsePane: (paneId: string) => void;
	expandPane: (paneId: string) => void;
	togglePane: (paneId: string) => void;
	setPaneSize: (paneId: string, size: number, unit?: 'pixels' | 'percent') => void;
	removePane: (paneId: string) => void;
	resetLayout: () => void;
	getPaneState: (paneId: string) => PaneStateInfo | null;
	getFocusedPaneId: () => string | null;
}

/**
 * Configuration options for SplitPanelRootState.
 */
export interface SplitPanelRootStateOpts {
	/** Unique identifier for this layout instance */
	id?: string;

	/** Storage key for persistence (if undefined, persistence is disabled) */
	storageKey?: string;

	/** Storage backend to use */
	storageBackend?: StorageBackend;

	/** Preset configuration */
	preset?: LayoutPreset;

	/** Enable undo/redo functionality */
	undoable?: boolean;

	/** Enable debug logging */
	debug?: boolean;

	/** Callback when layout changes */
	onLayoutChange?: (layout: LayoutNode) => void;

	/** Callback when pane is closed */
	onPaneClose?: (paneId: string) => void;

	/** Callback when pane receives focus */
	onPaneFocus?: (paneId: string) => void;

	/** Callback when one or more pane sizes change (drag, programmatic, container resize) */
	onPaneResize?: (updates: Array<{ panelId: string; newSize: number }>) => void;

	/** Container width accessor (required for persistence and resize) */
	containerWidth: () => number;

	/** Container height accessor (required for persistence and resize) */
	containerHeight: () => number;

	/** Optional storage instance (if not provided, persistence is disabled) */
	storage?: StorageState;
}

/**
 * SplitPanelRootState - Component API wrapper for SplitPanelState.
 *
 * Provides higher-level features:
 * - Preset configurations
 * - Storage integration
 * - Undo/redo management
 * - Pane/split registration
 * - Focus tracking
 */
export class SplitPanelRootState {
	/** Configuration options */
	readonly opts: Required<
		Omit<
			SplitPanelRootStateOpts,
			'storage' | 'storageKey' | 'onLayoutChange' | 'onPaneClose' | 'onPaneFocus' | 'onPaneResize'
		>
	> & {
		storage?: StorageState;
		storageKey?: string;
		onLayoutChange?: (layout: LayoutNode) => void;
		onPaneClose?: (paneId: string) => void;
		onPaneFocus?: (paneId: string) => void;
		onPaneResize?: (updates: Array<{ panelId: string; newSize: number }>) => void;
	};

	/** Core layout state engine */
	readonly layoutState: SplitPanelState;

	/** Undo/redo manager (if undoable) */
	readonly #history: LayoutHistory | null = null;

	/** Persistence manager (if storage provided) */
	readonly #persistence: LayoutPersistence | null = null;

	/** Track focused pane */
	#focusedPaneId = $state<string | null>(null);


	/** Split registry for programmatic access */
	#splitRegistry = $state(new Map<string, SplitPanelSplitState>());

	/**
	 * Metadata for panes awaiting tree sync.
	 * Maps paneId -> { path, config, split }
	 */
	#pendingPanes = $state(
		new Map<
			string,
			{
				path: number[];
				config: Partial<LeafNode>;
				split: SplitPanelSplitState;
			}
		>()
	);

	/**
	 * Tree synchronization flag.
	 * Set to true when components mount/unmount to trigger tree rebuild.
	 */
	#needsTreeSync = $state(false);

	/**
	 * Track last container size to prevent unnecessary updates.
	 */
	#lastContainerWidth = $state(0);
	#lastContainerHeight = $state(0);

	/**
	 * Track if a drag operation is in progress.
	 * Used to apply user-select: none and prevent text selection during resize.
	 */
	#isDragging = $state(false);

	/** Cleanup functions */
	#cleanupFns: Array<() => void> = [];

	constructor(opts: SplitPanelRootStateOpts) {
		// Apply defaults
		this.opts = {
			id: opts.id ?? 'split-panel-root',
			storageBackend: opts.storageBackend ?? 'localStorage',
			preset: opts.preset ?? 'default',
			undoable: opts.undoable ?? true,
			debug: opts.debug ?? false,
			containerWidth: opts.containerWidth,
			containerHeight: opts.containerHeight,
			storage: opts.storage,
			storageKey: opts.storageKey,
			onLayoutChange: opts.onLayoutChange,
			onPaneClose: opts.onPaneClose,
			onPaneFocus: opts.onPaneFocus,
			onPaneResize: opts.onPaneResize
		};

		// Initialize core layout state
		const presetConfig = this.#getPresetConfig(this.opts.preset);
		this.layoutState = new SplitPanelState();

		// Apply preset configuration to root node if needed
		// TODO: Set default minSize, autoCollapseThreshold from preset

		// Initialize undo/redo if enabled
		if (this.opts.undoable) {
			this.#history = new LayoutHistory(this.layoutState);
			this.#history.attachEffect();
			this.#cleanupFns.push(() => this.#history?.destroy());
		}

		// Initialize persistence if storage provided
		if (this.opts.storage && this.opts.storageKey) {
			this.#persistence = new LayoutPersistence(
				this.layoutState,
				this.opts.storage,
				this.opts.containerWidth,
				this.opts.containerHeight,
				this.opts.storageKey
			);

			// Try to load saved layout
			this.#persistence.load();

			// Enable auto-save
			const cleanupAutoSave = this.#persistence.enableAutoSave();
			this.#cleanupFns.push(cleanupAutoSave);
		}

		// Wire up layout change callback if provided
		if (this.opts.onLayoutChange) {
			$effect(() => {
				const layout = this.layoutState.root;
				this.opts.onLayoutChange?.(layout);
			});
		}

		// Wire up onPaneResize callback — fires on every resize-batch-applied event.
		// This is the push alternative to polling getPaneState(id) on layoutVersion.
		if (this.opts.onPaneResize) {
			const listener = (event: CustomEvent<LayoutChangeDetail>) => {
				if (event.detail?.type === 'resize-batch-applied') {
					this.opts.onPaneResize?.(event.detail.updates);
				}
			};
			const eventName = 'layoutchange';
			window.addEventListener(eventName, listener as EventListener);
			this.#cleanupFns.push(() => window.removeEventListener(eventName, listener as EventListener));
		}

		// Tree synchronization effect
		// Watches #needsTreeSync flag and rebuilds tree when components mount/unmount
		$effect(() => {
			if (this.#needsTreeSync) {
				// Untrack the sync to prevent infinite loops
				untrack(() => {
					this.#syncTreeFromComponents();
					this.#needsTreeSync = false;
				});
			}
		});
	}

	/**
	 * Get preset configuration for common layout patterns.
	 */
	#getPresetConfig(preset: LayoutPreset): { minSize: number; autoCollapseThreshold: number } {
		const presets = {
			default: { minSize: 100, autoCollapseThreshold: 50 },
			ide: { minSize: 150, autoCollapseThreshold: 80 },
			dashboard: { minSize: 200, autoCollapseThreshold: 100 },
			presentation: { minSize: 300, autoCollapseThreshold: 150 }
		};
		return presets[preset];
	}

	// =========================================================================
	// Tree Synchronization (Automatic Tree Building from Components)
	// =========================================================================

	/**
	 * Rebuild the layout tree from component registration state.
	 * Called automatically when components mount/unmount via $effect.
	 *
	 * This bridges the declarative component structure to the imperative layout engine.
	 */
	#syncTreeFromComponents(): void {
		// Find the root split for this root instance.
		// A split is the local root if it has no parent, OR if its parent belongs
		// to an outer root (i.e. parent is not registered in this root's registry).
		// This handles nested SplitPanel.Root instances correctly.
		let rootSplit: SplitPanelSplitState | null = null;
		for (const split of this.#splitRegistry.values()) {
			if (!split.parent || !this.#splitRegistry.has(split.parent.splitId)) {
				rootSplit = split;
				break;
			}
		}

		if (this.opts.debug) {
			console.log('[SyncTree] Root split:', rootSplit?.splitId, 'Registry size:', this.#splitRegistry.size);
			console.log('[SyncTree] Pending panes:', this.#pendingPanes.size);
		}

		if (!rootSplit) {
			// No root split yet, keep empty thread
			if (this.opts.debug) console.log('[SyncTree] No root split found, keeping empty thread');
			return;
		}

		// Set root split path
		rootSplit._setPath([]);

		// Build tree structure recursively from root split using DOM ordering
		const newTree = this.#buildThreadFromSplit(rootSplit, []);
		if (this.opts.debug) console.log('[SyncTree] Built tree:', JSON.stringify(newTree, null, 2));

		// Assign to layoutState
		this.layoutState.root = newTree;

		// Trigger recomputation with current container size
		const width = this.opts.containerWidth();
		const height = this.opts.containerHeight();
		if (this.opts.debug) console.log('[SyncTree] Container size:', width, 'x', height);
		this.updateContainerSize(width, height);
	}

	/**
	 * Build a thread node from a split at the given path.
	 * Uses DOM ordering to ensure children are in correct visual order.
	 */
	#buildThreadFromSplit(split: SplitPanelSplitState, path: number[]): ThreadNode {
		const children: LayoutNode[] = [];
		const flexes: number[] = [];

		if (this.opts.debug) {
			console.log('[BuildThread] Building for split at path:', path, 'axis:', split.internalAxis);
		}

		// Get children in DOM order (panes and nested splits sorted by document position)
		const orderedChildren = split.getChildrenInDOMOrder();

		if (this.opts.debug) {
			console.log('[BuildThread] Children in DOM order:', orderedChildren.map(c => ({ id: c.id, type: c.type })));
		}

		// First pass: collect all leaf nodes and their configs to calculate flexes
		interface ChildInfo {
			type: 'pane' | 'split';
			isCollapsed: boolean;
			initialSize?: number;
			initialSizeUnit?: 'percent' | 'pixels';
			config?: Partial<LeafNode>;
			splitState?: SplitPanelSplitState;
			id: string;
		}
		const childInfos: ChildInfo[] = [];

		for (let i = 0; i < orderedChildren.length; i++) {
			const child = orderedChildren[i];

			if (child.type === 'pane') {
				const paneInfo = this.#pendingPanes.get(child.id);
				const config = paneInfo?.config ?? {};
				const isCollapsed = this.isPaneCollapsed(child.id);

				childInfos.push({
					type: 'pane',
					id: child.id,
					isCollapsed,
					initialSize: config.initialSize,
					initialSizeUnit: config.initialSizeUnit,
					config
				});
			} else if (child.type === 'split' && child.splitState) {
				childInfos.push({
					type: 'split',
					id: child.id,
					isCollapsed: false,
					splitState: child.splitState
				});
			}
		}

		// Calculate flexes based on initialSize percentages
		const activeIndices = childInfos
			.map((info, idx) => ({ info, idx }))
			.filter(({ info }) => !info.isCollapsed)
			.map(({ idx }) => idx);

		// Check if any active pane has percentage-based initialSize
		let hasPercentageSizes = false;
		let totalPercent = 0;
		for (const idx of activeIndices) {
			const info = childInfos[idx];
			if (info.type === 'pane' && info.initialSize !== undefined && info.initialSizeUnit === 'percent') {
				hasPercentageSizes = true;
				totalPercent += info.initialSize;
			}
		}

		// Calculate flex values
		const computedFlexes: number[] = new Array(childInfos.length).fill(0);

		if (hasPercentageSizes && activeIndices.length > 0) {
			// Distribute based on percentage initialSize values
			const panesWithoutPercent: number[] = [];
			let allocatedPercent = 0;

			for (const idx of activeIndices) {
				const info = childInfos[idx];
				if (info.type === 'pane' && info.initialSize !== undefined && info.initialSizeUnit === 'percent') {
					allocatedPercent += info.initialSize;
				} else {
					panesWithoutPercent.push(idx);
				}
			}

			// Remaining percentage for panes without explicit size
			const remainingPercent = Math.max(0, 100 - allocatedPercent);
			const perPanePercent = panesWithoutPercent.length > 0 
				? remainingPercent / panesWithoutPercent.length 
				: 0;

			// Calculate total for normalization
			const totalForNormalization = allocatedPercent + (perPanePercent * panesWithoutPercent.length);

			for (const idx of activeIndices) {
				const info = childInfos[idx];
				let percent: number;

				if (info.type === 'pane' && info.initialSize !== undefined && info.initialSizeUnit === 'percent') {
					percent = info.initialSize;
				} else if (panesWithoutPercent.includes(idx)) {
					percent = perPanePercent;
				} else {
					percent = 100 / activeIndices.length; // Fallback for threads
				}

				// Convert percentage to flex (scaled so sum = activeIndices.length)
				computedFlexes[idx] = totalForNormalization > 0 
					? (percent / totalForNormalization) * activeIndices.length 
					: 1.0;
			}

			if (this.opts.debug) {
				console.log('[BuildThread] Using percentage-based flexes:', computedFlexes);
			}
		} else {
			// No percentage sizes, use equal distribution for active children
			for (const idx of activeIndices) {
				computedFlexes[idx] = 1.0;
			}
		}

		// Second pass: build children and collect flexes
		for (let i = 0; i < childInfos.length; i++) {
			const info = childInfos[i];

			if (info.type === 'pane') {
				const config = info.config ?? {};

				// Preserve runtime state (user-resized sizes, collapse stash) across tree rebuilds.
				// Without this, every mount/unmount cycle (e.g. {#if expanded}) resets all sizes
				// back to initialSize, discarding any resizing the user has done.
				const existingLeaf = this.#findLeafNode(this.layoutState.root, info.id);

				// Prefer runtime size over config default, but only if pane is not being collapsed
				const runtimeSize = existingLeaf?.size;
				const configDefault = config.size ?? 300;
				const size = info.isCollapsed
					? 0
					: (runtimeSize !== undefined && runtimeSize > 0 ? runtimeSize : configDefault);

				// Preserve collapse stash so expand restores the correct pre-collapse size/flex
				const cachedSize = info.isCollapsed
					? (existingLeaf?.cachedSize ?? runtimeSize ?? configDefault)
					: existingLeaf?.cachedSize;
				const cachedFlex = info.isCollapsed
					? (existingLeaf?.cachedFlex ?? computedFlexes[i])
					: existingLeaf?.cachedFlex;

				const leafNode: LeafNode = {
					type: 'leaf',
					id: info.id,
					size,
					cachedSize,
					cachedFlex,
					maximized: false,
					priority: config.priority ?? 'normal',
					minSize: config.minSize,
					maxSize: config.maxSize,
					snapPoints: config.snapPoints,
					snapThreshold: config.snapThreshold,
					autoCollapseThreshold: config.autoCollapseThreshold,
					panelType: config.panelType,
					initialSize: config.initialSize,
					initialSizeUnit: config.initialSizeUnit
				};

				children.push(leafNode);

				// Prefer existing flex (preserves user-resized ratios) over freshly computed flex
				// (which is only meaningful on first mount). Collapsed panes always get 0.
				const existingFlex = existingLeaf
					? this.#getExistingFlex(this.layoutState.root, info.id)
					: undefined;
				const flex = info.isCollapsed
					? 0
					: (existingFlex !== undefined && existingFlex > 0 ? existingFlex : computedFlexes[i]);
				flexes.push(flex);

				if (this.opts.debug) {
					console.log('[BuildThread]   Pane:', info.id, 'at index:', i, 'collapsed:', info.isCollapsed, 'flex:', flex);
				}
			} else if (info.type === 'split' && info.splitState) {
				// Recursively build thread for nested split
				const childPath = [...path, i];
				// Update the split's path for tree sync
				info.splitState._setPath(childPath);

				const threadNode = this.#buildThreadFromSplit(info.splitState, childPath);
				children.push(threadNode);
				flexes.push(computedFlexes[i]);

				if (this.opts.debug) {
					console.log('[BuildThread]   Nested split at index:', i, 'path:', childPath, 'flex:', computedFlexes[i]);
				}
			}
		}

		return {
			type: 'thread',
			axis: split.internalAxis,
			children,
			flexes
		};
	}

	// =========================================================================
	// Public API - Undo/Redo
	// =========================================================================

	/**
	 * Get the history instance (if enabled).
	 */
	get history(): LayoutHistory | null {
		return this.#history;
	}

	/**
	 * Check if undo is available.
	 */
	get canUndo(): boolean {
		return this.#history?.state.canUndo ?? false;
	}

	/**
	 * Check if redo is available.
	 */
	get canRedo(): boolean {
		return this.#history?.state.canRedo ?? false;
	}

	/**
	 * Check if a drag operation is in progress.
	 */
	get isDragging(): boolean {
		return this.#isDragging;
	}

	/**
	 * Start a drag operation. Called by handle when dragging begins.
	 * @internal
	 */
	_startDragging(): void {
		this.#isDragging = true;
	}

	/**
	 * End a drag operation. Called by handle when dragging ends.
	 * @internal
	 */
	_stopDragging(): void {
		this.#isDragging = false;
	}

	/**
	 * Undo the last layout change.
	 */
	undo(): void {
		this.#history?.undo();
	}

	/**
	 * Redo the last undone layout change.
	 */
	redo(): void {
		this.#history?.redo();
	}

	// =========================================================================
	// Public API - Layout Control
	// =========================================================================

	/**
	 * Reset layout to default state.
	 */
	resetLayout(): void {
		// TODO: Implement reset to initial/default layout
		// For now, just clear the root
		this.layoutState.root = {
			type: 'thread',
			axis: 'column',
			children: [],
			flexes: []
		};
	}

	// =========================================================================
	// Public API - Pane Control
	// =========================================================================

	/**
	 * Focus a pane by ID.
	 */
	focusPane(paneId: string): void {
		this.#focusedPaneId = paneId;
		this.opts.onPaneFocus?.(paneId);
		// Note: Actual DOM focus is handled by component via registerPanel action
	}

	/**
	 * Check if a pane is collapsed by examining the layout tree.
	 */
	isPaneCollapsed(paneId: string): boolean {
		const leafNode = this.#findLeafNode(this.layoutState.root, paneId);
		return leafNode ? leafNode.size === 0 : false;
	}

	/**
	 * Collapse a pane by ID.
	 */
	collapsePane(paneId: string): void {
		if (this.opts.debug) console.log('[CollapsePane]', paneId, 'current collapsed:', this.isPaneCollapsed(paneId));
		if (!this.isPaneCollapsed(paneId)) {
			this.layoutState.togglePanel(paneId);
			if (this.opts.debug) console.log('[CollapsePane]', paneId, 'collapsed');
		}
	}

	/**
	 * Expand a pane by ID.
	 */
	expandPane(paneId: string): void {
		if (this.opts.debug) console.log('[ExpandPane]', paneId, 'current collapsed:', this.isPaneCollapsed(paneId));
		if (this.isPaneCollapsed(paneId)) {
			this.layoutState.togglePanel(paneId);
			if (this.opts.debug) console.log('[ExpandPane]', paneId, 'expanded');
		}
	}

	/**
	 * Toggle a pane's collapsed state by ID.
	 */
	togglePane(paneId: string): void {
		this.layoutState.togglePanel(paneId);
	}

	/**
	 * Remove a pane from the layout.
	 */
	removePane(paneId: string): void {
		const result = this.layoutState.removePanel(paneId);
		if (result.ok) {
			this.opts.onPaneClose?.(paneId);
		}
	}

	/**
	 * Set a pane to a specific size.
	 *
	 * @param paneId - The ID of the pane to resize
	 * @param size - The desired size (in pixels or percent depending on unit)
	 * @param unit - 'pixels' (default) or 'percent' of container size along the pane's axis
	 *
	 * @example
	 * // Set to 600 pixels
	 * layoutState.setPaneSize('panel', 600);
	 * layoutState.setPaneSize('panel', 600, 'pixels');
	 *
	 * // Set to 70% of available space
	 * layoutState.setPaneSize('panel', 70, 'percent');
	 */
	setPaneSize(paneId: string, size: number, unit: 'pixels' | 'percent' = 'pixels'): void {
		// If pane is collapsed, expand it first
		if (this.isPaneCollapsed(paneId)) {
			this.expandPane(paneId);
		}

		// Find pane and its parent thread
		const result = this.#findPaneWithParent(this.layoutState.root, paneId, null, -1);
		if (!result) {
			console.warn(`[setPaneSize] Pane not found: ${paneId}`);
			return;
		}

		const { parent, index } = result;
		if (!parent || parent.type !== 'thread') {
			console.warn(`[setPaneSize] Pane has no parent thread: ${paneId}`);
			return;
		}

		// Get container size along the split axis
		const containerSize = parent.axis === 'row'
			? this.opts.containerWidth()
			: this.opts.containerHeight();

		// Convert percent to target flex ratio
		// If unit is percent, size is the desired percentage of the container
		// We need to calculate the flex value that achieves this
		let targetFlex: number;
		if (unit === 'percent') {
			// Target percentage of the total container
			const targetPercent = size / 100;
			// Calculate total flex of all active (non-collapsed) children
			let totalActiveFlex = 0;
			for (let i = 0; i < parent.children.length; i++) {
				const child = parent.children[i];
				const isCollapsed = child.type === 'leaf' && child.size === 0;
				if (!isCollapsed) {
					totalActiveFlex += parent.flexes[i];
				}
			}
			// The target pane's current flex
			const currentFlex = parent.flexes[index];
			// Other active flexes (excluding target pane)
			const otherActiveFlex = totalActiveFlex - currentFlex;

			// If target is 70%, then targetFlex / (targetFlex + otherActiveFlex) = 0.7
			// Solving: targetFlex = 0.7 * (targetFlex + otherActiveFlex)
			// targetFlex = 0.7 * targetFlex + 0.7 * otherActiveFlex
			// targetFlex - 0.7 * targetFlex = 0.7 * otherActiveFlex
			// 0.3 * targetFlex = 0.7 * otherActiveFlex
			// targetFlex = (0.7 / 0.3) * otherActiveFlex = (targetPercent / (1 - targetPercent)) * otherActiveFlex
			if (targetPercent >= 1) {
				console.warn(`[setPaneSize] Cannot set pane to 100% or more`);
				return;
			}
			targetFlex = (targetPercent / (1 - targetPercent)) * otherActiveFlex;

			if (this.opts.debug) {
				console.log(`[setPaneSize] percent mode: targetPercent=${targetPercent}, otherActiveFlex=${otherActiveFlex}, targetFlex=${targetFlex}`);
			}
		} else {
			// Pixel mode: calculate what flex value gives us the target pixel size
			// First, calculate total available space (container minus handle widths)
			const handleWidth = 4; // Approximate handle width
			const numHandles = parent.children.filter((c) => {
				const isCollapsed = c.type === 'leaf' && c.size === 0;
				return !isCollapsed;
			}).length - 1;
			const availableSpace = containerSize - (numHandles * handleWidth);

			// Calculate total flex of all active children
			let totalActiveFlex = 0;
			for (let i = 0; i < parent.children.length; i++) {
				const child = parent.children[i];
				const isCollapsed = child.type === 'leaf' && child.size === 0;
				if (!isCollapsed) {
					totalActiveFlex += parent.flexes[i];
				}
			}

			const currentFlex = parent.flexes[index];
			const otherActiveFlex = totalActiveFlex - currentFlex;

			// Target size as ratio of available space
			const targetRatio = size / availableSpace;
			// targetFlex / (targetFlex + otherActiveFlex) = targetRatio
			// targetFlex = targetRatio * (targetFlex + otherActiveFlex)
			// targetFlex * (1 - targetRatio) = targetRatio * otherActiveFlex
			// targetFlex = (targetRatio / (1 - targetRatio)) * otherActiveFlex
			if (targetRatio >= 1) {
				console.warn(`[setPaneSize] Target size exceeds available space`);
				return;
			}
			targetFlex = (targetRatio / (1 - targetRatio)) * otherActiveFlex;

			if (this.opts.debug) {
				console.log(`[setPaneSize] pixel mode: size=${size}, availableSpace=${availableSpace}, targetRatio=${targetRatio}, targetFlex=${targetFlex}`);
			}
		}

		// Update the flex value — mutate in place (number[] is tracked by Svelte's proxy)
		parent.flexes[index] = targetFlex;

		if (this.opts.debug) {
			console.log(`[setPaneSize] Updated flex for ${paneId} to ${targetFlex}, flexes:`, parent.flexes);
		}

		// Trigger recomputation
		this.layoutState.updateContainerSize(
			this.opts.containerWidth(),
			this.opts.containerHeight()
		);
	}

	/**
	 * Find a pane and its parent thread in the tree.
	 */
	#findPaneWithParent(
		node: LayoutNode,
		paneId: string,
		parent: ThreadNode | null,
		indexInParent: number
	): { leaf: LeafNode; parent: ThreadNode | null; index: number } | null {
		if (node.type === 'leaf') {
			if (node.id === paneId) {
				return { leaf: node, parent, index: indexInParent };
			}
			return null;
		}

		// Thread: search children
		for (let i = 0; i < node.children.length; i++) {
			const result = this.#findPaneWithParent(node.children[i], paneId, node, i);
			if (result) return result;
		}

		return null;
	}

	// =========================================================================
	// Public API - Container Size Management
	// =========================================================================

	/**
	 * Update container size and trigger layout recomputation.
	 * This method wraps the layoutState's updateContainerSize and triggers reactive updates.
	 */
	updateContainerSize(width: number, height: number): void {
		// Only update if size actually changed
		if (width === this.#lastContainerWidth && height === this.#lastContainerHeight) {
			if (this.opts.debug) console.log('[RootState] Skipping update - size unchanged:', width, 'x', height);
			return;
		}

		// Skip zero-size updates (element not yet mounted)
		if (width === 0 && height === 0) {
			if (this.opts.debug) console.log('[RootState] Skipping zero-size update');
			return;
		}

		this.#lastContainerWidth = width;
		this.#lastContainerHeight = height;

		this.layoutState.updateContainerSize(width, height);
		if (this.opts.debug) console.log('[RootState] Container size updated:', width, 'x', height);

	}

	// =========================================================================
	// Public API - State Inspection
	// =========================================================================

	/**
	 * Find the flex value of a leaf in its parent thread.
	 * Returns undefined if the pane is not in the tree.
	 */
	#getExistingFlex(node: LayoutNode, paneId: string): number | undefined {
		if (node.type === 'leaf') return undefined;
		for (let i = 0; i < node.children.length; i++) {
			const child = node.children[i];
			if (child.type === 'leaf' && child.id === paneId) {
				return node.flexes[i];
			}
			const found = this.#getExistingFlex(child, paneId);
			if (found !== undefined) return found;
		}
		return undefined;
	}

	/**
	 * Find a leaf node in the layout tree by pane ID.
	 */
	#findLeafNode(node: LayoutNode, paneId: string): LeafNode | null {
		if (node.type === 'leaf') {
			return node.id === paneId ? node : null;
		}

		// Thread: search children recursively
		for (const child of node.children) {
			const found = this.#findLeafNode(child, paneId);
			if (found) return found;
		}

		return null;
	}

	/**
	 * Get state information for a specific pane.
	 * Reads directly from the layout tree — reactive via Svelte's $state proxy on the tree.
	 */
	getPaneState(paneId: string): PaneStateInfo | null {
		// Always read from tree directly - the tree is the source of truth
		// and this ensures we get the latest values after toggle/resize operations
		const leafNode = this.#findLeafNode(this.layoutState.root, paneId);
		if (!leafNode) return null;
		return {
			id: paneId,
			size: leafNode.size,
			isCollapsed: leafNode.size === 0,
			isFocused: this.isPaneFocused(paneId)
		};
	}

	/**
	 * Get the currently focused pane ID.
	 */
	getFocusedPaneId(): string | null {
		return this.#focusedPaneId;
	}

	/**
	 * Check if a pane is currently focused.
	 */
	isPaneFocused(paneId: string): boolean {
		return this.#focusedPaneId === paneId;
	}

	// =========================================================================
	// Internal - Registration (called by Pane/Split components)
	// =========================================================================

	/**
	 * Register a pane for programmatic access.
	 * Returns cleanup function.
	 * 
	 * Note: The pane also registers with its parent split for DOM ordering.
	 * This registration is for metadata and programmatic access.
	 */
	_registerPane(
		paneId: string,
		split: SplitPanelSplitState,
		config: Partial<LeafNode>
	): () => void {
		if (this.opts.debug) {
			console.log('[RegisterPane]', paneId, 'registered with split:', split.splitId);
		}

		// Store pane metadata for tree building
		// Note: path is no longer stored here - DOM ordering determines position
		this.#pendingPanes.set(paneId, {
			path: [], // Path will be computed during tree sync based on DOM order
			config,
			split
		});

		// Mark tree as needing sync — must be outside untrack() so the $effect re-triggers
		this.#needsTreeSync = true;

		return () => {
			this.#pendingPanes.delete(paneId);
			this.#needsTreeSync = true;
		};
	}

	/**
	 * Register a split for programmatic access.
	 * Returns cleanup function.
	 */
	_registerSplit(splitId: string, splitState: SplitPanelSplitState): () => void {
		this.#splitRegistry.set(splitId, splitState);

		if (this.opts.debug) {
			console.log('[RegisterSplit]', splitId, 'registered');
		}

		// Mark tree as needing sync — must be outside untrack() so the $effect re-triggers
		this.#needsTreeSync = true;

		return () => {
			this.#splitRegistry.delete(splitId);
			this.#needsTreeSync = true;
		};
	}

	/**
	 * Trigger a tree sync.
	 * Called by pane/split components when their elements are mounted.
	 * @internal
	 */
	_triggerTreeSync(): void {
		this.#needsTreeSync = true;
	}

	// =========================================================================
	// Component Props (for Bits UI pattern)
	// =========================================================================

	/**
	 * Reactive props for Root component rendering.
	 * Provides data attributes and ARIA attributes for accessibility.
	 */
	get props(): Record<string, any> {
		return {
			id: this.opts.id,
			role: 'group',
			'aria-label': 'Split panel layout',
			'data-split-panel-root': '',
			'data-preset': this.opts.preset,
			'data-dragging': this.isDragging ? '' : undefined,
			style: {
				display: 'flex',
				'flex-direction': 'column',
				width: '100%',
				height: '100%',
				overflow: 'hidden',
				'min-width': 0,
				'min-height': 0
			}
		};
	}

	// =========================================================================
	// Exposed Layout State (for snippet props)
	// =========================================================================

	/**
	 * Layout state API exposed to components.
	 * This is the interface passed to snippet props for consumer control.
	 */
	get snippetProps(): {
		layoutState: SplitPanelLayoutState;
		isDragging: boolean;
		canUndo: boolean;
		canRedo: boolean;
	} {
		return {
			layoutState: {
				undo: () => this.undo(),
				redo: () => this.redo(),
				canUndo: this.canUndo,
				canRedo: this.canRedo,
				focusPane: (id) => this.focusPane(id),
				collapsePane: (id) => this.collapsePane(id),
				expandPane: (id) => this.expandPane(id),
				togglePane: (id) => this.togglePane(id),
				setPaneSize: (id, size, unit) => this.setPaneSize(id, size, unit),
				removePane: (id) => this.removePane(id),
				resetLayout: () => this.resetLayout(),
				getPaneState: (id) => this.getPaneState(id),
				getFocusedPaneId: () => this.getFocusedPaneId()
			},
			isDragging: this.isDragging,
			canUndo: this.canUndo,
			canRedo: this.canRedo
		};
	}

	// =========================================================================
	// Lifecycle
	// =========================================================================

	/**
	 * Cleanup resources when component is unmounted.
	 */
	destroy(): void {
		// Run all cleanup functions
		for (const cleanup of this.#cleanupFns) {
			cleanup();
		}

		// Clear cleanup array
		this.#cleanupFns = [];
	}
}
