<script lang="ts">
	import { mergeProps } from 'svelte-toolbelt';
	import type { SessionListEmptyProps, SessionListEmptyHTMLProps } from '../types.js';
	import { SessionListEmptyState } from '../session-list.svelte.js';

	let { class: className, child, children, ...restProps }: SessionListEmptyProps & { class?: string } = $props();

	const emptyState = SessionListEmptyState.create();

	const mergedProps = $derived(mergeProps(restProps, emptyState.props, className ? { class: className } : {}) as SessionListEmptyHTMLProps);
</script>

{#if child}
	{@render child({ props: mergedProps })}
{:else}
	<div {...mergedProps}>
		{@render children?.()}
	</div>
{/if}
