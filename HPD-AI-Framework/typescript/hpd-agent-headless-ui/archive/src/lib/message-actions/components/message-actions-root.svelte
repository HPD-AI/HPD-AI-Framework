<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { MessageActionsRootProps, MessageActionsRootHTMLProps } from '../types.js';
	import { MessageActionsRootState } from '../message-actions.svelte.js';

	let {
		workspace,
		messageIndex,
		role,
		branch = null,
		class: className,
		child,
		children,
		...restProps
	}: MessageActionsRootProps = $props();

	const rootState = MessageActionsRootState.create({
		workspace: boxWith(() => workspace),
		messageIndex: boxWith(() => messageIndex),
		role: boxWith(() => role),
		branch: boxWith(() => branch),
	});

	const mergedProps = $derived(mergeProps(restProps, rootState.props, className ? { class: className } : {}) as MessageActionsRootHTMLProps);
</script>

{#if child}
	{@render child({ props: mergedProps, ...rootState.snippetProps })}
{:else}
	<div {...mergedProps}>
		{@render children?.(rootState.snippetProps)}
	</div>
{/if}
