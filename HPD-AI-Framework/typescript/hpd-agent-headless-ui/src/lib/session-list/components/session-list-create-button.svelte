<script lang="ts">
	import { mergeProps } from 'svelte-toolbelt';
	import type { SessionListCreateButtonProps, SessionListCreateButtonHTMLProps } from '../types.js';
	import { SessionListCreateButtonState } from '../session-list.svelte.js';

	let { class: className, child, children, ...restProps }: SessionListCreateButtonProps & { class?: string } = $props();

	const createBtnState = SessionListCreateButtonState.create();

	const mergedProps = $derived(mergeProps(restProps, createBtnState.props, className ? { class: className } : {}) as SessionListCreateButtonHTMLProps);
</script>

{#if child}
	{@render child({ props: mergedProps })}
{:else}
	<button {...mergedProps}>
		{@render children?.()}
	</button>
{/if}
