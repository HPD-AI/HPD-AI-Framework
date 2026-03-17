<!--
	SplitPanel.Root Component

	Root container for the split panel layout system.
	Provides context to child components and manages global layout state.
-->
<script lang="ts">
	import type { Snippet } from 'svelte';
	import type { HTMLAttributes } from 'svelte/elements';
	import { mergeProps } from 'svelte-toolbelt';
	import { SplitPanelRootState } from './split-panel-root-state.svelte.js';
	import { SplitPanelRootContext } from './split-panel-context.js';
	import type { LayoutPreset, StorageBackend } from './split-panel-root-state.svelte.js';
	import type { LayoutNode } from '../types/index.js';

	interface Props extends Omit<HTMLAttributes<HTMLDivElement>, 'children'> {
		/** Unique ID for this layout instance */
		id: string;

		/** Storage key for persisting layout state */
		storageKey?: string;

		/** Storage backend for persistence */
		storageBackend?: StorageBackend;

		/** Preset layout configuration */
		preset?: LayoutPreset;

		/** Enable undo/redo */
		undoable?: boolean;

		/** Enable debug logging */
		debug?: boolean;

		/** Callback when layout changes */
		onLayoutChange?: (layout: LayoutNode) => void;

		/** Callback when pane closes */
		onPaneClose?: (paneId: string) => void;

		/** Callback when pane receives focus */
		onPaneFocus?: (paneId: string) => void;

		/** Callback when pane(s) are resized */
		onPaneResize?: (updates: Array<{ panelId: string; newSize: number }>) => void;

		/** Custom snippet for complete control */
		child?: Snippet<[{ props: Record<string, any>; layoutState: SplitPanelRootState }]>;

		/** Default children */
		children?: Snippet<[{ layoutState: SplitPanelRootState; canUndo: boolean; canRedo: boolean }]>;

		/** Bindable ref to DOM element */
		ref?: HTMLDivElement | null;

		/** Bindable layout state for external access */
		layout?: SplitPanelRootState | null;
	}

	let {
		id,
		storageKey,
		storageBackend,
		preset,
		undoable = false,
		debug = false,
		onLayoutChange,
		onPaneClose,
		onPaneFocus,
		onPaneResize,
		class: className,
		child,
		children,
		ref = $bindable(null),
		layout = $bindable(null),
		...restProps
	}: Props = $props();

	// Container size accessors (required for persistence)
	let containerElement = $state<HTMLDivElement | null>(null);
	const getContainerWidth = () => containerElement?.clientWidth ?? 0;
	const getContainerHeight = () => containerElement?.clientHeight ?? 0;

	// Track last size to prevent unnecessary updates
	let lastWidth = $state(0);
	let lastHeight = $state(0);

	// Create root state with container size accessors
	// svelte-ignore state_referenced_locally
	const rootState = new SplitPanelRootState({
		id,
		storageKey,
		storageBackend,
		preset,
		undoable,
		debug,
		containerWidth: getContainerWidth,
		containerHeight: getContainerHeight,
		onLayoutChange,
		onPaneClose,
		onPaneFocus,
		onPaneResize
	});

	// Expose state for external binding
	$effect(() => {
		layout = rootState;
	});

	// Sync ref with internal element for size tracking and observe resizes
	$effect(() => {
		containerElement = ref;

		if (!ref) return;

		// Trigger initial size computation when element is mounted
		const element = ref; // Capture ref for closure
		const updateSize = () => {
			const width = element.clientWidth;
			const height = element.clientHeight;

			// Only update if size actually changed
			if (width === lastWidth && height === lastHeight) {
				return;
			}

			lastWidth = width;
			lastHeight = height;

			if (debug) console.log('[Root] Container size update:', width, 'x', height);
			rootState.updateContainerSize(width, height);
		};

		// Initial size update
		updateSize();

		// Observe size changes
		// Note: ResizeObserver fires for any layout changes in descendants too,
		// not just when the root element size changes. We rely on updateSize()
		// to check if the actual container size changed before triggering updates.
		let rafId = 0;
		const resizeObserver = new ResizeObserver(() => {
			cancelAnimationFrame(rafId);
			rafId = requestAnimationFrame(updateSize);
		});

		resizeObserver.observe(ref);

		return () => {
			cancelAnimationFrame(rafId);
			resizeObserver.disconnect();
		};
	});

	// Set context for child components
	SplitPanelRootContext.set(rootState);

	// Merge props
	const mergedProps = $derived(mergeProps(restProps, rootState.props, className ? { class: className } : {}) as Record<string, unknown>);

	// Snippet props
	const snippetProps = $derived({
		layoutState: rootState,
		canUndo: rootState.canUndo,
		canRedo: rootState.canRedo
	});
</script>

{#if child}
	{@render child({ props: mergedProps, layoutState: rootState })}
{:else}
	<div bind:this={ref} {...mergedProps}>
		{@render children?.(snippetProps)}
	</div>
{/if}
