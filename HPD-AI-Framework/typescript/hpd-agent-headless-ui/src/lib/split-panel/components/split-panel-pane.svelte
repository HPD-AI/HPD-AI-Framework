<!--
	SplitPanel.Pane Component

	Individual resizable pane within a split panel layout.
	Supports collapse, focus management, and size constraints.
-->
<script lang="ts">
	import type { Snippet } from 'svelte';
	import type { HTMLAttributes } from 'svelte/elements';
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import { SplitPanelPaneState } from './split-panel-pane-state.svelte.js';

	interface Props extends Omit<HTMLAttributes<HTMLDivElement>, 'children'> {
		/** Unique pane ID */
		id: string;

		/** Minimum size in pixels */
		minSize?: number;

		/** Maximum size in pixels */
		maxSize?: number;

		/** Resize priority */
		priority?: 'high' | 'normal' | 'low';

		/** Auto-collapse threshold in pixels */
		autoCollapseThreshold?: number;

		/** Snap points for resize */
		snapPoints?: number[];

		/** Snap threshold in pixels */
		snapThreshold?: number;

		/** Panel type for content state serialization */
		panelType?: string;
	/** Initial size of pane (percentage or pixels based on initialSizeUnit) */
	initialSize?: number;

	/** Unit for initial size: 'percent' or 'pixels' */
	initialSizeUnit?: 'percent' | 'pixels';
		/** Collapsed state (bindable) */
		collapsed?: boolean;

		/**
		 * Strategy for animating collapse/expand transitions.
		 * @default 'unmount'
		 */
		collapseStrategy?: 'unmount' | 'force-mount' | 'view-transition';

		/**
		 * Custom view-transition-name for this pane.
		 * Only used when collapseStrategy is 'view-transition'.
		 */
		viewTransitionName?: string;

		/**
		 * Legacy: Force mount content regardless of collapsed state.
		 * @deprecated Use collapseStrategy="force-mount" instead
		 */
		forceMount?: boolean;

		/** Custom snippet for complete control */
		child?: Snippet<
			[
				{
					props: Record<string, any>;
					isFocused: boolean;
					isCollapsed: boolean;
					size: number;
				}
			]
		>;

		/** Default children */
		children?: Snippet<
			[
				{
					isFocused: boolean;
					isCollapsed: boolean;
					size: number;
					toggle: () => void;
					focus: () => void;
					collapse: () => void;
					expand: () => void;
				}
			]
		>;

		/** Bindable ref to DOM element */
		ref?: HTMLDivElement | null;
	}

	let {
		id,
		minSize,
		maxSize,
		priority,
		autoCollapseThreshold,
		snapPoints,
		snapThreshold,
		panelType,
		initialSize,
		initialSizeUnit,
		collapsed = $bindable(false),
		collapseStrategy,
		viewTransitionName,
		forceMount,
		class: className,
		child,
		children,
		ref = $bindable(null),
		...restProps
	}: Props = $props();

	// Create pane state with boxed values
	const paneState = SplitPanelPaneState.create({
		id: boxWith(() => id),
		minSize: boxWith(() => minSize),
		maxSize: boxWith(() => maxSize),
		priority: boxWith(() => priority),
		autoCollapseThreshold: boxWith(() => autoCollapseThreshold),
		snapPoints: boxWith(() => snapPoints),
		snapThreshold: boxWith(() => snapThreshold),
		panelType: boxWith(() => panelType),
		initialSize: boxWith(() => initialSize),
		initialSizeUnit: boxWith(() => initialSizeUnit),
		collapseStrategy: boxWith(() => collapseStrategy),
		viewTransitionName: boxWith(() => viewTransitionName),
		forceMount: boxWith(() => forceMount),
		collapsed: boxWith(
			() => collapsed,
			(v) => (collapsed = v)
		),
		ref: boxWith(
			() => ref,
			(v) => (ref = v)
		)
	});

	let defaultEl = $state<HTMLDivElement | null>(null);

	// Update pane state with element ref when mounted (for DOM ordering)
	$effect(() => {
		paneState._setElement(defaultEl);
		paneState.setRef(defaultEl);
	});

	// Merge props
	const mergedProps = $derived(mergeProps(restProps, paneState.props, className ? { class: className } : {}) as Record<string, unknown>);
</script>

{#if child}
	{@render child({
		props: mergedProps,
		isFocused: paneState.isFocused,
		isCollapsed: paneState.isCollapsed,
		size: paneState.size
	})}
{:else}
	<div bind:this={defaultEl} {...mergedProps}>
		{@render children?.(paneState.snippetProps)}
	</div>
{/if}
