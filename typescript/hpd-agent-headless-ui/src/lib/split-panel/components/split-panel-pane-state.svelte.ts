/**
 * SplitPanelPaneState
 *
 * Provides component-level state management for individual panes in the split panel layout.
 *
 * Features:
 * - Context-based access to root state
 * - Reactive size and collapse state tracking
 * - Focus management with keyboard navigation
 * - Auto-registration with root state
 * - Spring-animated resize transitions
 * - Data attribute generation for styling
 */

import { watch } from 'runed';
import type {
	WithRefOpts,
	ReadableBoxedValues,
	WritableBoxedValues
} from '../../internal/index.js';
import { SplitPanelRootContext } from './split-panel-context.js';
import { SplitPanelSplitContext, type SplitPanelSplitState } from './split-panel-split-state.svelte.js';
import { splitPanelAttrs } from './split-panel-attrs.js';
import type { SplitPanelRootState } from './split-panel-root-state.svelte.js';
import type { LeafNode } from '../types/index.js';

/**
 * Configuration options for SplitPanelPaneState.
 * Uses BoxedValues for reactive prop tracking.
 */
export interface SplitPanelPaneStateOpts
	extends
		WithRefOpts<
			ReadableBoxedValues<{
				/** Minimum size in pixels */
				minSize?: number;

				/** Maximum size in pixels */
				maxSize?: number;

				/** Resize priority (high = keeps size better during resizes) */
				priority?: 'high' | 'normal' | 'low';

				/** Auto-collapse threshold in pixels */
				autoCollapseThreshold?: number;

				/** Snap points for resize operations */
				snapPoints?: number[];

				/** Snap threshold in pixels */
				snapThreshold?: number;

				/** Panel type for content state serialization */
				panelType?: string;
				/** Initial size of pane (percentage or pixels based on initialSizeUnit) */
				initialSize?: number;

				/** Unit for initial size: 'percent' or 'pixels' */
				initialSizeUnit?: 'percent' | 'pixels';
				/**
				 * Strategy for animating collapse/expand transitions.
				 *
				 * - 'unmount': Content is unmounted when collapsed (default, best performance)
				 * - 'force-mount': Content stays in DOM, user controls animation via snippet
				 * - 'view-transition': Uses CSS View Transitions API for smooth animations
				 *
				 * @default 'unmount'
				 */
				collapseStrategy?: 'unmount' | 'force-mount' | 'view-transition';

				/**
				 * Custom view-transition-name for this pane.
				 * Only used when collapseStrategy is 'view-transition'.
				 *
				 * @default `split-panel-${id}`
				 */
				viewTransitionName?: string;

				/**
				 * Legacy: Force mount content regardless of collapsed state.
				 * Equivalent to collapseStrategy="force-mount".
				 *
				 * @deprecated Use collapseStrategy="force-mount" instead
				 * @default false
				 */
				forceMount?: boolean;
			}> &
				WritableBoxedValues<{
					/** Whether pane is collapsed */
					collapsed?: boolean;
				}>
		> {}

/**
 * SplitPanelPaneState - Component state wrapper for individual panes.
 *
 * Manages pane-specific state and coordinates with root state for layout operations.
 * Provides reactive props for component rendering.
 */
export class SplitPanelPaneState {
	/**
	 * Create a new SplitPanelPaneState instance.
	 * Retrieves root state from Context.
	 */
	static create(opts: SplitPanelPaneStateOpts): SplitPanelPaneState {
		const root = SplitPanelRootContext.get();
		return new SplitPanelPaneState(opts, root);
	}

	/** Configuration options */
	readonly opts: SplitPanelPaneStateOpts;

	/** Root state from context */
	readonly root: SplitPanelRootState;

	/** Parent split state */
	readonly split: SplitPanelSplitState;

	/** Set the ref to the DOM element */
	readonly setRef = (v: HTMLElement | null) => {
		this.opts.ref.current = v;
	};

	/** Internal collapsed state - initialized from opts if provided */
	#collapsed = $state(false);

	/** Cleanup function from split registration */
	#splitCleanupFn: (() => void) | null = null;

	/** Cleanup function from root registration */
	#rootCleanupFn: (() => void) | null = null;

	/** DOM element reference */
	#element: HTMLElement | null = null;

	constructor(opts: SplitPanelPaneStateOpts, root: SplitPanelRootState) {
		this.opts = opts;
		this.root = root;

		// Initialize collapsed state from opts if provided
		if (opts.collapsed?.current !== undefined) {
			this.#collapsed = opts.collapsed.current;
		}

		// Get parent split from context
		this.split = SplitPanelSplitContext.get();

		// Register this pane with parent split for DOM ordering
		// Element is null initially, will be updated when mounted
		const initialId = opts.id.current ?? '';
		this.#splitCleanupFn = this.split._registerChildPane(initialId, null);

		// Watch ID changes and register with root state
		watch([() => this.opts.id.current], ([id]) => {
			// Skip if id is null/undefined
			if (!id) return;

			// Unregister previous pane if ID changed
			if (this.#rootCleanupFn) {
				this.#rootCleanupFn();
			}

			// Build config from opts for tree building
			// Note: TypeScript doesn't see these properties due to ReadableBoxedValues structure,
			// but they exist at runtime via boxWith() wrapping
			const opts = this.opts as any;

			// Get initial size config
			const initialSizeValue = opts.initialSize?.current;
			const initialSizeUnit = opts.initialSizeUnit?.current ?? 'pixels';

			const config: Partial<LeafNode> = {
				size: initialSizeValue ?? 300,
				minSize: opts.minSize?.current,
				maxSize: opts.maxSize?.current,
				priority: opts.priority?.current ?? 'normal',
				autoCollapseThreshold: opts.autoCollapseThreshold?.current,
				snapPoints: opts.snapPoints?.current,
				snapThreshold: opts.snapThreshold?.current,
				panelType: opts.panelType?.current,
				// Pass initial size info for flex computation
				initialSize: initialSizeValue,
				initialSizeUnit: initialSizeValue !== undefined ? initialSizeUnit : undefined
			};

			// Register this pane with root state
			this.#rootCleanupFn = this.root._registerPane(
				id,
				this.split,
				config
			);

			return () => {
				if (this.#rootCleanupFn) {
					this.#rootCleanupFn();
					this.#rootCleanupFn = null;
				}
			};
		});

		// Sync collapsed state to writable box if provided
		if (opts.collapsed) {
			watch([() => this.#collapsed], ([collapsed]) => {
				if (opts.collapsed) {
					opts.collapsed.current = collapsed;
				}
			});
		}
	}

	/**
	 * Set the DOM element reference and notify parent split.
	 * Called when the component mounts.
	 * @internal
	 */
	_setElement(element: HTMLElement | null): void {
		// Skip if element hasn't changed
		if (this.#element === element) return;

		const id = this.opts.id.current;
		if (!id) return;

		const wasNull = this.#element === null;
		this.#element = element;

		// Update parent split with element reference for DOM ordering
		this.split._updateChildPaneElement(id, element);
		
		// Only trigger tree sync on first mount (null -> element)
		if (wasNull && element) {
			this.root._triggerTreeSync();
		}
	}

	/**
	 * Get the DOM element reference.
	 */
	get element(): HTMLElement | null {
		return this.#element;
	}

	/**
	 * Check if this pane is currently focused.
	 */
	readonly isFocused = $derived.by(() => {
		const id = this.opts.id.current;
		return id ? this.root.isPaneFocused(id) : false;
	});

	/**
	 * Check if this pane is currently collapsed.
	 */
	readonly isCollapsed = $derived.by(() => {
		const id = this.opts.id.current;
		const state = id ? this.root.getPaneState(id) : null;
		return state?.isCollapsed ?? false;
	});

	/**
	 * Get current pane size in pixels.
	 */
	readonly size = $derived.by(() => {
		const opts = this.opts as any;
		const paneId = opts.id?.current ?? '';
		const state = this.root.getPaneState(paneId);
		const size = state?.size ?? 0;
		if (this.root.opts.debug) {
			console.log('[Pane]', paneId, 'computed size:', size, 'state:', state);
		}
		return size;
	});

	/**
	 * Reactive props for component rendering.
	 * Spreads into component element.
	 */
	readonly props = $derived.by(() => {
		const baseProps = {
			id: this.opts.id.current,
			role: 'region' as const,
			tabindex: this.isFocused ? 0 : -1,
			'aria-label': `Pane ${this.opts.id.current}`,
			[splitPanelAttrs.pane]: '',
			'data-pane-id': this.opts.id.current,
			'data-state': this.isCollapsed ? 'collapsed' : 'expanded',
			'data-focused': this.isFocused ? '' : undefined,
			'data-collapse-strategy': this.effectiveCollapseStrategy
		};

		// Get parent split to determine axis
		const split = SplitPanelSplitContext.get();
		const axis = split.internalAxis; // 'row' or 'column'

		const size = this.size;
		const isCollapsed = this.isCollapsed;
		
		// Check if layout has been computed: container has received at least one non-zero size
		const cs = this.root.layoutState.containerSize;
		const layoutComputed = cs.width > 0 || cs.height > 0;

		let sizeStyle: Record<string, string | number>;
		
		if (isCollapsed) {
			// Collapsed: zero size, no flex participation
			sizeStyle = axis === 'row'
				? { width: '0px', height: '100%', flex: 'none', overflow: 'hidden' }
				: { width: '100%', height: '0px', flex: 'none', overflow: 'hidden' };
		} else if (size > 0) {
			// Normal: use computed pixel size
			sizeStyle = axis === 'row'
				? { width: `${size}px`, height: '100%', flex: 'none', overflow: 'hidden' }
				: { width: '100%', height: `${size}px`, flex: 'none', overflow: 'hidden' };
		} else if (!layoutComputed) {
			// Layout not yet computed: use flex fallback to ensure visibility during initial render
			sizeStyle = { flex: '1 1 0%', minWidth: 0, minHeight: 0, overflow: 'hidden' };
		} else {
			// Layout computed but size is 0 and not collapsed - shouldn't happen, but handle gracefully
			sizeStyle = axis === 'row'
				? { width: '0px', height: '100%', flex: 'none', overflow: 'hidden' }
				: { width: '100%', height: '0px', flex: 'none', overflow: 'hidden' };
		}

		// Merge with view-transition-name if needed
		const style = this.effectiveCollapseStrategy === 'view-transition'
			? { ...sizeStyle, 'view-transition-name': this.viewTransitionName }
			: sizeStyle;

		return {
			...baseProps,
			style
		};
	});

	/**
	 * Snippet props exposed to consumers.
	 * Provides pane-specific state and control methods.
	 */
	readonly snippetProps = $derived.by(
		() =>
			({
				isFocused: this.isFocused,
				isCollapsed: this.isCollapsed,
				size: this.size,
				collapseStrategy: this.effectiveCollapseStrategy,
				shouldRenderContent: this.shouldRenderContent,
				supportsViewTransitions: this.supportsViewTransitions,
				toggle: () => this.toggle(),
				focus: () => this.focus(),
				collapse: () => this.collapse(),
				expand: () => this.expand(),
				toggleWithTransition: () => this.toggleWithTransition(),
				collapseWithTransition: () => this.collapseWithTransition(),
				expandWithTransition: () => this.expandWithTransition()
			}) as const
	);

	// ===== Public API Methods =====

	/**
	 * Toggle pane collapsed state.
	 */
	toggle(): void {
		const id = this.opts.id.current;
		if (!id) return;
		this.root.togglePane(id);
		// Sync local collapsed state from layout (will be updated reactively)
	}

	/**
	 * Collapse this pane.
	 */
	collapse(): void {
		const id = this.opts.id.current;
		if (!id) return;
		this.root.collapsePane(id);
	}

	/**
	 * Expand this pane.
	 */
	expand(): void {
		const id = this.opts.id.current;
		if (!id) return;
		this.root.expandPane(id);
	}

	/**
	 * Focus this pane.
	 */
	focus(): void {
		const id = this.opts.id.current;
		if (!id) return;

		this.root.focusPane(id);
		// Also focus the DOM element if available
		if (this.opts.ref.current) {
			this.opts.ref.current.focus();
		}
	}

	// ===== Collapse Strategy Methods =====

	/**
	 * Get the effective collapse strategy.
	 * Handles legacy forceMount prop by converting to collapseStrategy.
	 */
	readonly effectiveCollapseStrategy = $derived.by((): 'unmount' | 'force-mount' | 'view-transition' => {
		// Legacy forceMount prop takes precedence if true
		if (this.opts.forceMount?.current) {
			return 'force-mount';
		}
		return this.opts.collapseStrategy?.current ?? 'unmount';
	});

	/**
	 * Check if View Transitions API is supported in the current browser.
	 */
	get supportsViewTransitions(): boolean {
		return typeof document !== 'undefined' && 'startViewTransition' in document;
	}

	/**
	 * Get the view-transition-name for this pane.
	 * Used when collapseStrategy is 'view-transition'.
	 */
	readonly viewTransitionName = $derived.by(() => {
		return this.opts.viewTransitionName?.current ?? `split-panel-${this.opts.id.current}`;
	});

	/**
	 * Check if content should be rendered (not unmounted).
	 * For collapse strategies:
	 * - 'unmount': render only when expanded
	 * - 'force-mount': always render
	 * - 'view-transition': render only when expanded (transition handles animation)
	 */
	readonly shouldRenderContent = $derived.by(() => {
		const strategy = this.effectiveCollapseStrategy;

		if (strategy === 'force-mount') {
			return true;
		}

		return !this.isCollapsed;
	});

	/**
	 * Collapse with view transition animation.
	 * Falls back to immediate collapse if View Transitions API unavailable.
	 */
	async collapseWithTransition(): Promise<void> {
		if (this.effectiveCollapseStrategy !== 'view-transition' || !this.supportsViewTransitions) {
			this.collapse();
			return;
		}

		// Use View Transitions API
		const transition = (document as any).startViewTransition(() => {
			this.collapse();
		});

		try {
			await transition.finished;
		} catch (error) {
			// Transition was skipped or cancelled, that's okay
			console.debug('View transition cancelled:', error);
		}
	}

	/**
	 * Expand with view transition animation.
	 * Falls back to immediate expand if View Transitions API unavailable.
	 */
	async expandWithTransition(): Promise<void> {
		if (this.effectiveCollapseStrategy !== 'view-transition' || !this.supportsViewTransitions) {
			this.expand();
			return;
		}

		// Use View Transitions API
		const transition = (document as any).startViewTransition(() => {
			this.expand();
		});

		try {
			await transition.finished;
		} catch (error) {
			// Transition was skipped or cancelled, that's okay
			console.debug('View transition cancelled:', error);
		}
	}

	/**
	 * Toggle with view transition animation.
	 * Falls back to immediate toggle if View Transitions API unavailable.
	 */
	async toggleWithTransition(): Promise<void> {
		if (this.isCollapsed) {
			await this.expandWithTransition();
		} else {
			await this.collapseWithTransition();
		}
	}
}
