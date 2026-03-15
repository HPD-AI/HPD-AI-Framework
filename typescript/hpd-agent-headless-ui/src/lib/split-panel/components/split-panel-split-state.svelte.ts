/**
 * SplitPanelSplitState - Component Wrapper for Split (BranchNode)
 *
 * Provides component-level state management for split containers that arrange
 * children along an axis (horizontal/vertical). Represents BranchNode in the layout tree.
 *
 * Features:
 * - Context-based access to root state and parent split
 * - DOM-order-based child ordering (not registration order)
 * - Axis-based layout (row = horizontal, column = vertical)
 * - Auto-registration with root state
 * - Data attribute generation for styling
 * - Snippet props for nested rendering
 *
 * ARCHITECTURE NOTE:
 * Child ordering uses DOM element position, not registration order.
 * This is critical because Svelte initializes nested components depth-first,
 * which would cause incorrect ordering with counter-based registration.
 * The tree sync happens after mount, querying actual DOM order.
 */

import { Context } from 'runed';
import type { ReadableBoxedValues } from '../../internal/index.js';
import { SplitPanelRootContext } from './split-panel-context.js';
import { splitPanelAttrs } from './split-panel-attrs.js';
import type { SplitPanelRootState } from './split-panel-root-state.svelte.js';

/**
 * Context for parent Split state.
 * Enables nested splits to compute their path in the tree.
 */
export const SplitPanelSplitContext = new Context<SplitPanelSplitState>('SplitPanel.Split');

/**
 * Configuration options for SplitPanelSplitState.
 * Uses BoxedValues for reactive prop tracking.
 */
export interface SplitPanelSplitStateOpts
	extends
		ReadableBoxedValues<{
			/** Layout axis: 'horizontal' (row) or 'vertical' (column) */
			axis: 'horizontal' | 'vertical';

			/** Initial flex values for children (optional, defaults to equal distribution) */
			initialFlexes?: number[];
		}> {
	/** Ref to the split element */
	ref: import('svelte-toolbelt').WritableBox<HTMLElement | null>;
}

/**
 * Registered child info for DOM order resolution.
 */
export interface RegisteredChild {
	/** Unique ID (pane ID or generated split ID) */
	id: string;
	/** Type of child */
	type: 'pane' | 'split';
	/** DOM element reference (set after mount) */
	element: HTMLElement | null;
	/** Nested split state (only for type='split') */
	splitState?: SplitPanelSplitState;
}

/**
 * SplitPanelSplitState - Component state wrapper for split containers (BranchNode).
 *
 * Manages split-specific state and coordinates with root state for layout operations.
 * Provides reactive props for component rendering and child path computation.
 */
export class SplitPanelSplitState {
	/** Counter for generating unique split IDs */
	static #splitIdCounter = 0;

	/**
	 * Create a new SplitPanelSplitState instance.
	 * Retrieves root state from Context and optionally parent split.
	 */
	static create(opts: SplitPanelSplitStateOpts): SplitPanelSplitState {
		const root = SplitPanelRootContext.get();
		const parent = SplitPanelSplitContext.getOr(null);
		const instance = new SplitPanelSplitState(opts, root, parent);

		// Set this split as the context for children
		SplitPanelSplitContext.set(instance);

		return instance;
	}

	/** Configuration options */
	readonly opts: SplitPanelSplitStateOpts;

	/** Root state from context */
	readonly root: SplitPanelRootState;

	/** Parent split state (null if this is the root split) */
	readonly parent: SplitPanelSplitState | null;

	/** Set the ref to the DOM element */
	readonly setRef = (v: HTMLElement | null) => {
		this.opts.ref.current = v;
	};

	/** Unique ID for this split (used for DOM ordering) */
	readonly splitId: string;

	/** 
	 * Path in the layout tree.
	 * Computed lazily during tree sync using DOM order, not at construction time.
	 * Initially empty for non-root splits until tree sync resolves DOM order.
	 * Must be reactive ($state) so handles can react to path changes.
	 * 
	 * Exposed as a public $state so handles can reactively track path changes.
	 */
	_path = $state<number[]>([]);

	/** Registered children (panes and nested splits) */
	#registeredChildren = $state<RegisteredChild[]>([]);

	/** Handle count for divider index assignment */
	#handleCount = $state(0);

	/** Cleanup function from registration */
	#cleanupFn: (() => void) | null = null;

	/** DOM element reference for this split container */
	#element: HTMLElement | null = null;

	constructor(
		opts: SplitPanelSplitStateOpts,
		root: SplitPanelRootState,
		parent: SplitPanelSplitState | null
	) {
		this.opts = opts;
		this.root = root;
		this.parent = parent;

		// Generate unique split ID
		this.splitId = `__split_${SplitPanelSplitState.#splitIdCounter++}`;

		// For root split (no parent), path is always []
		// For nested splits, path is computed during tree sync based on DOM order
		if (parent) {
			// Register this split as a child of parent (for DOM order resolution)
			parent._registerChildSplit(this);
			// Path will be computed during tree sync
			this._path = [];
		} else {
			this._path = [];
		}

		// Register this split with root state
		// Use splitId as key since path isn't known yet for nested splits
		this.#cleanupFn = this.root._registerSplit(this.splitId, this);
	}

	/**
	 * Get the path (computed during tree sync).
	 */
	get path(): number[] {
		return this._path;
	}

	/**
	 * Set the path (called during tree sync).
	 * @internal
	 */
	_setPath(path: number[]): void {
		this._path = path;
	}

	/**
	 * Set the DOM element reference.
	 * Called when the component mounts.
	 * @internal
	 */
	_setElement(element: HTMLElement | null): void {
		this.#element = element;
	}

	/**
	 * Get the DOM element reference.
	 */
	get element(): HTMLElement | null {
		return this.#element;
	}

	/**
	 * Check if this is the root split (no parent).
	 */
	readonly isRoot = $derived.by(() => {
		return this.parent === null;
	});

	/**
	 * Get the layout axis as 'row' or 'column' (internal format).
	 * Converts from user-friendly 'horizontal'/'vertical'.
	 */
	readonly internalAxis = $derived.by(() => {
		return this.opts.axis.current === 'horizontal' ? 'row' : 'column';
	});

	/**
	 * Get registered children (for tree building).
	 */
	get registeredChildren(): readonly RegisteredChild[] {
		return this.#registeredChildren;
	}

	/**
	 * Register a pane as a child of this split.
	 * Called by pane components during initialization.
	 * Does NOT return an index - index is computed from DOM order during tree sync.
	 */
	_registerChildPane(paneId: string, element: HTMLElement | null): () => void {
		const child: RegisteredChild = {
			id: paneId,
			type: 'pane',
			element
		};
		this.#registeredChildren = [...this.#registeredChildren, child];

		// Return cleanup function
		return () => {
			this.#registeredChildren = this.#registeredChildren.filter(c => c.id !== paneId);
		};
	}

	/**
	 * Update a pane's element reference (called when DOM element mounts).
	 * Note: Does NOT trigger reactivity - element refs are only used during tree sync.
	 */
	_updateChildPaneElement(paneId: string, element: HTMLElement | null): void {
		const child = this.#registeredChildren.find(c => c.id === paneId);
		if (child && child.element !== element) {
			// Mutate in place - no need to trigger reactivity for element refs
			// The tree sync will read these values when it runs
			child.element = element;
		}
	}

	/**
	 * Register a nested split as a child of this split.
	 * Called by nested split components during initialization.
	 */
	_registerChildSplit(splitState: SplitPanelSplitState): () => void {
		const child: RegisteredChild = {
			id: splitState.splitId,
			type: 'split',
			element: null,
			splitState
		};
		this.#registeredChildren = [...this.#registeredChildren, child];

		// Return cleanup function
		return () => {
			this.#registeredChildren = this.#registeredChildren.filter(c => c.id !== splitState.splitId);
		};
	}

	/**
	 * Update a nested split's element reference (called when DOM element mounts).
	 * Note: Does NOT trigger reactivity - element refs are only used during tree sync.
	 */
	_updateChildSplitElement(splitId: string, element: HTMLElement | null): void {
		const child = this.#registeredChildren.find(c => c.id === splitId);
		if (child && child.element !== element) {
			// Mutate in place - no need to trigger reactivity for element refs
			child.element = element;
		}
	}

	/**
	 * Get children sorted by DOM order.
	 * Uses compareDocumentPosition to determine correct order.
	 * Falls back to registration order if elements aren't mounted yet.
	 */
	getChildrenInDOMOrder(): RegisteredChild[] {
		const children = [...this.#registeredChildren];
		
		// Check if all children have element references
		const allMounted = children.every(c => c.element !== null);

		if (!allMounted) {
			// Can't sort by DOM order yet, return as-is
			return children;
		}

		// Sort by DOM position
		children.sort((a, b) => {
			if (!a.element || !b.element) return 0;
			const position = a.element.compareDocumentPosition(b.element);
			if (position & Node.DOCUMENT_POSITION_FOLLOWING) {
				return -1; // a comes before b
			} else if (position & Node.DOCUMENT_POSITION_PRECEDING) {
				return 1; // b comes before a
			}
			return 0;
		});

		return children;
	}

	/**
	 * Register a handle with this split.
	 * Returns the divider index (0-based, between children).
	 * Called by handle components during initialization.
	 */
	_registerHandle(): number {
		const index = this.#handleCount;
		this.#handleCount++;
		return index;
	}

	/**
	 * Reactive props for component rendering.
	 * Spreads into component element.
	 */
	readonly props = $derived.by(
		() =>
			({
				role: 'group',
				'aria-label': `Split container ${this.isRoot ? '(root)' : ''}`,
				'data-orientation': this.opts.axis.current,
				'data-root': this.isRoot ? '' : undefined,
				'data-split-id': this.splitId,
				[splitPanelAttrs.split]: '',
				style: {
					display: 'flex',
					'flex-direction': this.opts.axis.current === 'horizontal' ? 'row' : 'column',
					flex: '1 1 0%',
					overflow: 'hidden',
					'min-width': 0,
					'min-height': 0
				}
			}) as const
	);

	/**
	 * Snippet props exposed to consumers.
	 * Provides split-specific state for custom rendering.
	 */
	readonly snippetProps = $derived.by(
		() =>
			({
				isRoot: this.isRoot,
				axis: this.opts.axis.current,
				childCount: this.#registeredChildren.length,
				path: this._path
			}) as const
	);

	/**
	 * Cleanup method called when component is destroyed.
	 */
	destroy(): void {
		if (this.#cleanupFn) {
			this.#cleanupFn();
			this.#cleanupFn = null;
		}
	}
}
