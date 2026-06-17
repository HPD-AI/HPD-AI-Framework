<script lang="ts">
	/**
	 * ToolExecution.Result - Tool result/error display
	 *
	 * Displays the result or error from tool execution
	 */

	import { mergeProps } from 'svelte-toolbelt';
	import type { ToolExecutionResultProps, ToolExecutionResultHTMLProps } from '../types.js';
	import { ToolExecutionResultState } from '../tool-execution.svelte.js';

	let { class: className, child, children, ...restProps }: ToolExecutionResultProps = $props();

	const rootState = ToolExecutionResultState.create();
	const mergedProps = $derived(mergeProps(restProps, rootState.props, className ? { class: className } : {}) as ToolExecutionResultHTMLProps);
</script>

{#if child}
	{@render child({ props: mergedProps })}
{:else}
	<div {...mergedProps}>
		{@render children?.(rootState.snippetProps)}
	</div>
{/if}
