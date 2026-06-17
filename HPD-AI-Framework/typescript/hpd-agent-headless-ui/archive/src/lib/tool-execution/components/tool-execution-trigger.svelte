<script lang="ts">
	/**
	 * ToolExecution.Trigger - Clickable header for expand/collapse
	 *
	 * This component renders the clickable trigger button that
	 * expands/collapses the tool execution details.
	 *
	 */

	import { mergeProps } from 'svelte-toolbelt';
	import type { ToolExecutionTriggerProps, ToolExecutionTriggerHTMLProps } from '../types.js';
	import { ToolExecutionTriggerState } from '../tool-execution.svelte.js';

	let { class: className, child, children, ...restProps }: ToolExecutionTriggerProps = $props();

	// Get state from context
	const rootState = ToolExecutionTriggerState.create();

	// Merge props
	const mergedProps = $derived(mergeProps(restProps, rootState.props, className ? { class: className } : {}) as ToolExecutionTriggerHTMLProps);
</script>

{#if child}
	{@render child({ props: mergedProps })}
{:else}
	<button {...mergedProps}>
		{@render children?.(rootState.snippetProps)}
	</button>
{/if}
