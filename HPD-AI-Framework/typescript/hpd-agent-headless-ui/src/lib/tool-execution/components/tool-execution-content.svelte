<script lang="ts">
	/**
	 * ToolExecution.Content - Collapsible content area
	 *
	 * This component renders the collapsible content area that shows
	 * tool arguments and results when expanded.
	 *
	 */

	import { mergeProps } from 'svelte-toolbelt';
	import type { ToolExecutionContentProps, ToolExecutionContentHTMLProps } from '../types.js';
	import { ToolExecutionContentState } from '../tool-execution.svelte.js';

	let { class: className, child, children, ...restProps }: ToolExecutionContentProps = $props();

	const rootState = ToolExecutionContentState.create();
	const mergedProps = $derived(mergeProps(restProps, rootState.props, className ? { class: className } : {}) as ToolExecutionContentHTMLProps);
</script>

{#if rootState.shouldRender}
	{#if child}
		{@render child({ props: mergedProps })}
	{:else}
		<div {...mergedProps}>
			{@render children?.(rootState.snippetProps)}
		</div>
	{/if}
{/if}
