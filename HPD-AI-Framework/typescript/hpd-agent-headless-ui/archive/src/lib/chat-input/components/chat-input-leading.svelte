<script lang="ts">
	import { mergeProps } from 'svelte-toolbelt';
	import { ChatInputRootState } from '../chat-input.svelte.js';
	import type { ChatInputAccessoryComponentProps } from '../types.js';

	let {
		ref = $bindable(null),
		child,
		children,
		class: className,
		...restProps
	}: ChatInputAccessoryComponentProps = $props();

	// Get shared state from context
	const rootState = ChatInputRootState.get();

	// Props for snippet
	const snippetProps = $derived({
		value: rootState.value,
		focused: rootState.focused,
		disabled: rootState.disabled,
		isEmpty: rootState.isEmpty,
		characterCount: rootState.characterCount,
		canSubmit: rootState.canSubmit,
		submit: () => rootState.submit(),
		clear: () => rootState.clear()
	});

	// Merge props
	const mergedProps = $derived(
		mergeProps(restProps as Record<string, unknown>, {
			[rootState.getHPDAttr('leading')]: '',
			...rootState.sharedProps,
		
		}, className ? { class: className } : {}) as Record<string, unknown>
	);

	let defaultEl = $state<HTMLDivElement | null>(null);
	$effect(() => { ref = defaultEl; });
</script>

{#if child}
	{@render child({ ...snippetProps, props: mergedProps })}
{:else if children}
	<div bind:this={defaultEl} {...mergedProps}>
		{@render children(snippetProps)}
	</div>
{:else}
	<div bind:this={defaultEl} {...mergedProps}></div>
{/if}
