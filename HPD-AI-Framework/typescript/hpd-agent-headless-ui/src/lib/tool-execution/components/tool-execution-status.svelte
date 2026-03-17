<script lang="ts">
	/**
	 * ToolExecution.Status - Status badge component
	 *
	 * Displays the current status of the tool execution
	 * (pending, executing, complete, error)
	 */

	import { mergeProps } from 'svelte-toolbelt';
	import type { ToolExecutionStatusProps, ToolExecutionStatusHTMLProps } from '../types.js';
	import { ToolExecutionStatusState } from '../tool-execution.svelte.js';

	let { class: className, child, children, ...restProps }: ToolExecutionStatusProps = $props();

	const rootState = ToolExecutionStatusState.create();
	const mergedProps = $derived(mergeProps(restProps, rootState.props, className ? { class: className } : {}) as ToolExecutionStatusHTMLProps);
</script>

{#if child}
	{@render child({ props: mergedProps })}
{:else}
	<span {...mergedProps}>
		{@render children?.(rootState.snippetProps)}
	</span>
{/if}
