<!--
	SplitPanel.Split Component

	Split container that arranges children along an axis (horizontal/vertical).
	Represents a BranchNode in the layout tree. Enables declarative nested layouts.

	Usage:
		<SplitPanel.Split axis="horizontal">
			<SplitPanel.Pane id="left" />
			<SplitPanel.Handle />
			<SplitPanel.Pane id="right" />
		</SplitPanel.Split>
-->
<script lang="ts">
	import type { Snippet } from 'svelte';
	import type { HTMLAttributes } from 'svelte/elements';
	import { boxWith } from 'svelte-toolbelt';
	import { mergeProps } from 'svelte-toolbelt';
	import {
		SplitPanelSplitState,
		type SplitPanelSplitStateOpts
	} from './split-panel-split-state.svelte.js';

	interface Props extends Omit<HTMLAttributes<HTMLDivElement>, 'children'> {
		/**
		 * Layout axis for children.
		 * - 'horizontal': children arranged left-to-right (row in CSS Grid)
		 * - 'vertical': children arranged top-to-bottom (column in CSS Grid)
		 */
		axis: 'horizontal' | 'vertical';

		/**
		 * Initial flex values for children (optional).
		 * Array length must match number of children.
		 * Values are normalized automatically.
		 * @default equal distribution
		 */
		initialFlexes?: number[];

		/**
		 * Custom snippet for complete control over rendering.
		 * Receives props and snippet state.
		 */
		child?: Snippet<
			[
				{
					props: Record<string, any>;
					isRoot: boolean;
					axis: 'horizontal' | 'vertical';
					childCount: number;
					path: number[];
				}
			]
		>;

		/**
		 * Default children snippet.
		 * Receives snippet props for conditional rendering.
		 */
		children?: Snippet<
			[
				{
					isRoot: boolean;
					axis: 'horizontal' | 'vertical';
					childCount: number;
					path: number[];
				}
			]
		>;

		/**
		 * Bindable ref to DOM element.
		 */
		ref?: HTMLDivElement | null;
	}

	let {
		axis,
		initialFlexes,
		class: className,
		child,
		children,
		ref = $bindable(null),
		...restProps
	}: Props = $props();

	// Create split state with boxed values for reactivity
	const splitState = SplitPanelSplitState.create({
		ref: boxWith(
			() => ref,
			(v) => (ref = v)
		),
		axis: boxWith(() => axis),
		initialFlexes: boxWith(() => initialFlexes)
	});

	// Track previous ref to detect first mount
	let prevRef: HTMLDivElement | null = null;

	// Update split state with element ref when mounted (for DOM ordering)
	$effect(() => {
		// Skip if ref hasn't changed
		if (ref === prevRef) return;
		
		const wasNull = prevRef === null;
		prevRef = ref;
		
		splitState._setElement(ref);
		
		// Also notify parent split if this is a nested split
		if (splitState.parent) {
			splitState.parent._updateChildSplitElement(splitState.splitId, ref);
		}
		
		// Only trigger tree sync on first mount (null -> element)
		if (wasNull && ref) {
			splitState.root._triggerTreeSync();
		}
	});

	// Merge props with state-generated attributes
	const mergedProps = $derived(mergeProps(restProps, splitState.props, className ? { class: className } : {}) as Record<string, unknown>);
</script>

{#if child}
	{@render child({ props: mergedProps, ...splitState.snippetProps })}
{:else}
	<div bind:this={ref} {...mergedProps}>
		{@render children?.(splitState.snippetProps)}
	</div>
{/if}
