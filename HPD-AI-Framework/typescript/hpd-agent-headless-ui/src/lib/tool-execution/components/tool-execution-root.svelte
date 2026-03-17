<script lang="ts">
	/**
	 * ToolExecution.Root - Container for tool execution visualization
	 *
	 * This is the root component that manages the collapsible state
	 * and provides context to child components.
	 *
	 */

	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { ToolExecutionRootProps, ToolExecutionRootHTMLProps } from '../types.js';
	import { ToolExecutionRootState } from '../tool-execution.svelte.js';

	let {
		toolCall,
		expanded = $bindable(false),
		onExpandChange,
		class: className,
		child,
		children,
		...restProps
	}: ToolExecutionRootProps = $props();

	// Create state with boxed props
	// Note: toolCall is passed directly at creation, then synced via $effect
	// svelte-ignore state_referenced_locally
	const state = ToolExecutionRootState.create({
		toolCall: toolCall, // Initial value captured intentionally
		expanded: boxWith(
			() => expanded,
			(v) => {
				expanded = v;
			}
		),
		onExpandChange: boxWith(() => onExpandChange)
	});

	// Merge props for pass-through
	const mergedProps = $derived(mergeProps(restProps, state.props, className ? { class: className } : {}) as ToolExecutionRootHTMLProps);

	// Update state when toolCall changes
	$effect(() => {
		state.update(toolCall);
	});
</script>

<!--
  Snippet pattern:
  - child: Full control over the element (receives both props and state)
  - children: Partial control with snippet props
-->
{#if child}
	{@render child({ props: mergedProps, ...state.snippetProps })}
{:else}
	<div {...mergedProps}>
		{@render children?.(state.snippetProps)}
	</div>
{/if}
