/**
 * SplitPanelHandleState - Bits UI Style Component Wrapper for Resize Handles
 *
 * Provides component-level state management for resize handles between panes.
 * Follows Bits UI patterns with Context access, BoxedValues, and reactive props.
 *
 * Features:
 * - Context-based access to root state
 * - Pointer-based drag handling with RAF batching
 * - Keyboard resize support (Arrow keys, Shift modifier)
 * - Accessible ARIA attributes
 * - Hit area expansion for better UX
 * - Visual feedback during drag
 */

import { watch } from 'runed';
import type { ReadableBoxedValues } from '../../internal/index.js';
import { SplitPanelRootContext } from './split-panel-context.js';
import { splitPanelAttrs } from './split-panel-attrs.js';
import type { SplitPanelRootState } from './split-panel-root-state.svelte.js';
import type { SplitPanelSplitState } from './split-panel-split-state.svelte.js';

/**
 * Configuration options for SplitPanelHandleState.
 */
export interface SplitPanelHandleStateOpts
	extends
		ReadableBoxedValues<{
			/** Whether handle is disabled */
			disabled?: boolean;

			/** Hit area size in pixels (expands clickable area) */
			hitAreaSize?: number;

			/** Keyboard step size in pixels (for arrow keys) */
			keyboardStep?: number;

			/** Large keyboard step size (for Shift+arrow) */
			keyboardStepLarge?: number;

			/** Parent path for divider operations */
			parentPath?: number[];

			/** Divider index within parent */
			dividerIndex?: number;

			/** Resize axis ('row' = horizontal, 'column' = vertical) */
			axis?: 'row' | 'column';

			/**
			 * Double-click to reset adjacent panes to equal sizes.
			 * Common pattern for quick layout reset.
			 * @default true
			 */
			resetOnDoubleClick?: boolean;

			/**
			 * Single click (not drag) to toggle collapse on nearest collapsible pane.
			 * Useful for quick collapse without dragging.
			 * @default false
			 */
			toggleCollapseOnClick?: boolean;
		}> {
	/** Callback when drag starts */
	onDragStart?: () => void;

	/** Callback when drag ends */
	onDragEnd?: () => void;

	/** Callback before double-click reset. Return false to prevent. */
	onDoubleClick?: () => boolean | void;

	/** Ref to the handle element */
	ref: import('svelte-toolbelt').WritableBox<HTMLElement | null>;
}

/**
 * SplitPanelHandleState - Component state wrapper for resize handles.
 *
 * Manages pointer events, keyboard interactions, and coordinates with root state
 * for resize operations.
 */
export class SplitPanelHandleState {
	/**
	 * Create a new SplitPanelHandleState instance.
	 * Retrieves root state from Context.
	 */
	static create(opts: SplitPanelHandleStateOpts, parentSplit: SplitPanelSplitState | null): SplitPanelHandleState {
		const root = SplitPanelRootContext.get();
		return new SplitPanelHandleState(opts, root, parentSplit);
	}

	/** Configuration options */
	readonly opts: SplitPanelHandleStateOpts;

	/** Root state from context */
	readonly root: SplitPanelRootState;

	/** Parent split state for axis derivation */
	readonly parentSplit: SplitPanelSplitState | null;

	/** Set the ref to the DOM element */
	readonly setRef = (v: HTMLElement | null) => {
		this.opts.ref.current = v;
	};

	/** Auto-computed divider index from parent split registration */
	readonly #autoComputedDividerIndex: number;

	/** Whether handle is currently being dragged */
	#isDragging = $state(false);

	/** Initial pointer position at drag start */
	#dragStartPos: { x: number; y: number } | null = null;

	/** Whether pointer has moved significantly (determines click vs drag) */
	#hasMoved = false;

	/** Timestamp of last click for double-click detection */
	#lastClickTime = 0;

	/** Threshold for considering movement a drag (in pixels) */
	static readonly DRAG_THRESHOLD = 5;

	constructor(opts: SplitPanelHandleStateOpts, root: SplitPanelRootState, parentSplit: SplitPanelSplitState | null) {
		this.opts = opts;
		this.root = root;
		this.parentSplit = parentSplit;

		// Auto-register with parent split to get divider index
		this.#autoComputedDividerIndex = parentSplit?._registerHandle() ?? 0;
	}

	/**
	 * Get the parent path for resize operations.
	 * Uses explicit value if provided, otherwise derives from parent split.
	 * 
	 * NOTE: Access _path directly (not through getter) to ensure Svelte 5 
	 * properly tracks the $state dependency for reactive updates.
	 */
	readonly parentPath = $derived.by(() => {
		if (this.opts.parentPath?.current) {
			return this.opts.parentPath.current;
		}
		// Access _path directly to ensure reactivity tracking
		return this.parentSplit?._path ?? [];
	});

	/**
	 * Get the divider index for resize operations.
	 * Uses explicit value if provided, otherwise uses auto-computed value.
	 */
	readonly dividerIndex = $derived.by(() => {
		if (this.opts.dividerIndex?.current !== undefined) {
			return this.opts.dividerIndex.current;
		}
		return this.#autoComputedDividerIndex;
	});

	/**
	 * Check if handle is disabled.
	 */
	readonly isDisabled = $derived.by(() => {
		return this.opts.disabled?.current ?? false;
	});

	/**
	 * Get resize axis ('row' = horizontal divider, 'column' = vertical).
	 * Derives from parent split's internal axis if not explicitly provided.
	 */
	readonly axis = $derived.by(() => {
		// Explicit axis takes precedence
		if (this.opts.axis?.current) {
			return this.opts.axis.current;
		}
		// Derive from parent split context
		if (this.parentSplit) {
			return this.parentSplit.internalAxis;
		}
		// Fallback (shouldn't happen in normal usage)
		return 'column';
	});

	/**
	 * Get cursor style based on axis and drag state.
	 * - 'row' axis: children horizontal, handle vertical, drag left/right → col-resize
	 * - 'column' axis: children vertical, handle horizontal, drag up/down → row-resize
	 */
	readonly cursor = $derived.by(() => {
		if (this.isDisabled) return 'not-allowed';
		// row axis = vertical handle = col-resize (left/right)
		// column axis = horizontal handle = row-resize (up/down)
		return this.axis === 'row' ? 'col-resize' : 'row-resize';
	});

	/**
	 * Reactive props for component rendering.
	 */
	/**
	 * Get orientation for CSS styling.
	 * CSS expects 'horizontal' or 'vertical', not 'row'/'column'.
	 * - row axis = horizontal handle (divides left/right panes)
	 * - column axis = vertical handle (divides top/bottom panes)
	 */
	readonly orientation = $derived.by(() => {
		return this.axis === 'row' ? 'horizontal' : 'vertical';
	});

	readonly props = $derived.by(() => ({
		role: 'separator',
		'aria-label': `Resize ${this.axis === 'row' ? 'horizontally' : 'vertically'}`,
		'aria-orientation': this.orientation,
		'aria-valuenow': 50, // TODO: Calculate actual position percentage
		'aria-disabled': this.isDisabled ? 'true' : undefined,
		tabindex: this.isDisabled ? -1 : 0,
		[splitPanelAttrs.handle]: '',
		'data-orientation': this.orientation,
		'data-state': this.#isDragging ? 'dragging' : 'idle',
		'data-disabled': this.isDisabled ? '' : undefined,
		...(this.root.opts.debug ? {
			'data-debug-parent-path': this.parentPath.join(','),
			'data-debug-divider-index': this.dividerIndex,
			'data-debug-axis': this.axis,
		} : {}),
		style: `cursor: ${this.cursor};`,
		onpointerdown: this.onpointerdown,
		onkeydown: this.onkeydown
	}));

	/**
	 * Snippet props exposed to consumers.
	 */
	readonly snippetProps = $derived.by(
		() =>
			({
				isDragging: this.#isDragging,
				isDisabled: this.isDisabled,
				axis: this.axis
			}) as const
	);

	// ===== Event Handlers =====

	/**
	 * Handle pointer down - start drag operation or detect click/double-click.
	 */
	private readonly onpointerdown = (e: PointerEvent) => {
		if (this.isDisabled) return;

		// Only handle left click
		if (e.button !== 0) return;

		e.preventDefault();
		e.stopPropagation();

		// Capture pointer for smooth drag
		const target = e.currentTarget as HTMLElement;
		target.setPointerCapture(e.pointerId);

		// Store initial position
		const startPos = { x: e.clientX, y: e.clientY };
		this.#dragStartPos = startPos;
		this.#hasMoved = false;

		// Trigger drag start callback
		this.opts.onDragStart?.();

		// Attach move and up handlers
		const onpointermove = (e: PointerEvent) => {
			if (!this.#dragStartPos) return;

			// Calculate distance moved
			const dx = Math.abs(e.clientX - startPos.x);
			const dy = Math.abs(e.clientY - startPos.y);
			const distance = Math.sqrt(dx * dx + dy * dy);

			// Check if moved beyond drag threshold
			if (distance > SplitPanelHandleState.DRAG_THRESHOLD) {
				if (!this.#hasMoved) {
					this.#hasMoved = true;
					this.#isDragging = true;
					// Notify root that dragging started (for user-select: none)
					this.root._startDragging();
				}

				// Calculate delta based on axis
				// - 'row' axis: children arranged horizontally, handle is vertical bar, drag left/right (X)
				// - 'column' axis: children arranged vertically, handle is horizontal bar, drag up/down (Y)
				const delta =
					this.axis === 'row'
						? e.clientX - this.#dragStartPos.x
						: e.clientY - this.#dragStartPos.y;

				// Update drag start position for continuous delta calculation
				this.#dragStartPos = { x: e.clientX, y: e.clientY };

				// Perform resize via root state using derived values
				this.root.layoutState.resizeDivider(this.parentPath, this.dividerIndex, delta);
			}
		};

		const onpointerup = (e: PointerEvent) => {
			// Release pointer capture
			target.releasePointerCapture(e.pointerId);

			// Check if this was a click (not a drag)
			if (!this.#hasMoved) {
				this.#handleClick(e);
			}

			// Trigger drag end callback
			if (this.#isDragging) {
				this.opts.onDragEnd?.();
				// Notify root that dragging stopped
				this.root._stopDragging();
			}

			// Cleanup
			this.#isDragging = false;
			this.#dragStartPos = null;
			this.#hasMoved = false;

			// Remove listeners
			window.removeEventListener('pointermove', onpointermove);
			window.removeEventListener('pointerup', onpointerup);
		};

		// Attach global listeners for smooth drag
		window.addEventListener('pointermove', onpointermove);
		window.addEventListener('pointerup', onpointerup);
	};

	/**
	 * Handle click (not drag) on handle.
	 * Detects double-click for reset or single-click for collapse toggle.
	 */
	#handleClick = (_e: PointerEvent) => {
		const now = Date.now();
		const timeSinceLastClick = now - this.#lastClickTime;
		const isDoubleClick = timeSinceLastClick < 300; // 300ms double-click window

		if (isDoubleClick) {
			// Double-click: reset adjacent panes to equal sizes
			if (this.opts.resetOnDoubleClick?.current ?? true) {
				// Allow user to prevent default behavior
				const shouldReset = this.opts.onDoubleClick?.();
				if (shouldReset !== false) {
					this.#resetAdjacentPanes();
				}
			}
			// Reset click time to prevent triple-click
			this.#lastClickTime = 0;
		} else {
			// Single click: toggle collapse if enabled
			if (this.opts.toggleCollapseOnClick?.current) {
				this.#toggleNearestCollapsiblePane();
			}
			this.#lastClickTime = now;
		}
	};

	/**
	 * Reset adjacent panes to equal sizes (50/50 split).
	 * Gets the parent node and sets equal flex values for children adjacent to this divider.
	 */
	#resetAdjacentPanes = () => {
		const parentPath = this.parentPath;
		const dividerIndex = this.dividerIndex;

		// Access the internal layoutState to get the tree structure
		const layoutState = this.root.layoutState as any;

		try {
			// Get parent node
			const parent = layoutState.getNodeAt(parentPath);

			if (parent.type !== 'branch') {
				console.warn('Cannot reset: parent is not a branch node');
				return;
			}

			// Get the two children adjacent to this divider
			const leftIndex = dividerIndex;
			const rightIndex = dividerIndex + 1;

			if (leftIndex >= parent.children.length || rightIndex >= parent.children.length) {
				console.warn('Cannot reset: divider index out of bounds');
				return;
			}

			// Set both children to equal flex (0.5 each, normalized)
			parent.flexes[leftIndex] = 0.5;
			parent.flexes[rightIndex] = 0.5;

			// Trigger layout recompute
			this.root.layoutState.updateContainerSize(
				this.root.opts.containerWidth(),
				this.root.opts.containerHeight()
			);
		} catch (error) {
			console.error('Failed to reset adjacent panes:', error);
		}
	};

	/**
	 * Toggle collapse on nearest collapsible pane.
	 * Finds the first leaf node adjacent to this divider and toggles its collapsed state.
	 */
	#toggleNearestCollapsiblePane = () => {
		const parentPath = this.parentPath;
		const dividerIndex = this.dividerIndex;

		// Access the internal layoutState
		const layoutState = this.root.layoutState as any;

		try {
			// Get parent node
			const parent = layoutState.getNodeAt(parentPath);

			if (parent.type !== 'branch') {
				console.warn('Cannot toggle: parent is not a branch node');
				return;
			}

			// Get the two children adjacent to this divider
			const leftIndex = dividerIndex;
			const rightIndex = dividerIndex + 1;

			if (leftIndex >= parent.children.length || rightIndex >= parent.children.length) {
				console.warn('Cannot toggle: divider index out of bounds');
				return;
			}

			// Helper to find first leaf node in a subtree
			const findFirstLeaf = (node: any): string | null => {
				if (node.type === 'leaf') {
					return node.id;
				}
				if (node.type === 'branch' && node.children.length > 0) {
					return findFirstLeaf(node.children[0]);
				}
				return null;
			};

			// Try right pane first (common pattern), then left
			let paneId = findFirstLeaf(parent.children[rightIndex]);
			if (!paneId) {
				paneId = findFirstLeaf(parent.children[leftIndex]);
			}

			if (paneId) {
				// Toggle the pane using the public API
				this.root.togglePane(paneId);
			} else {
				console.warn('No collapsible pane found adjacent to divider');
			}
		} catch (error) {
			console.error('Failed to toggle nearest pane:', error);
		}
	};

	/**
	 * Handle keyboard resize (arrow keys).
	 */
	private readonly onkeydown = (e: KeyboardEvent) => {
		if (this.isDisabled) return;

		const step = e.shiftKey
			? (this.opts.keyboardStepLarge?.current ?? 50)
			: (this.opts.keyboardStep?.current ?? 10);

		let delta = 0;

		// Determine delta based on axis and key
		// 'row' axis = children horizontal = handle is vertical bar = drag left/right
		// 'column' axis = children vertical = handle is horizontal bar = drag up/down
		if (this.axis === 'row') {
			if (e.key === 'ArrowLeft') delta = -step;
			else if (e.key === 'ArrowRight') delta = step;
		} else {
			if (e.key === 'ArrowUp') delta = -step;
			else if (e.key === 'ArrowDown') delta = step;
		}

		if (delta !== 0) {
			e.preventDefault();
			e.stopPropagation();

			this.root.layoutState.resizeDivider(this.parentPath, this.dividerIndex, delta);
		}
	};
}
