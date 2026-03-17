<script lang="ts">
	/**
	 * ToolExecution.Args - Tool arguments display
	 *
	 * Displays the arguments passed to the tool in JSON format
	 */

	import { mergeProps } from 'svelte-toolbelt';
	import type { ToolExecutionArgsProps, ToolExecutionArgsHTMLProps } from '../types.js';
	import { ToolExecutionArgsState } from '../tool-execution.svelte.js';

	let { class: className, child, children, ...restProps }: ToolExecutionArgsProps = $props();

	const rootState = ToolExecutionArgsState.create();
	const mergedProps = $derived(mergeProps(restProps, rootState.props, className ? { class: className } : {}) as ToolExecutionArgsHTMLProps);
</script>

{#if child}
	{@render child({ props: mergedProps })}
{:else}
	<div {...mergedProps}>
		{@render children?.(rootState.snippetProps)}
	</div>
{/if}
