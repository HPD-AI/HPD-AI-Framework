<script lang="ts">
	import { boxWith, mergeProps } from 'svelte-toolbelt';
	import { ChatInputRootState } from '../chat-input.svelte.js';
	import type { ChatInputRootComponentProps } from '../types.js';

	let {
		value = $bindable(),
		defaultValue,
		disabled = false,
		onSubmit,
		onChange,
		class: className,
		child,
		children,
		ref = $bindable(null),
		...restProps
	}: ChatInputRootComponentProps = $props();

	// Determine if controlled mode at creation
	const isControlled = value !== undefined;

	// Create root state with boxed values
	const rootState = ChatInputRootState.create({
		value: boxWith(() => value),
		defaultValue: boxWith(() => defaultValue),
		disabled: boxWith(() => disabled),
		onSubmit: boxWith(() => onSubmit),
		onChange: boxWith(() => onChange)
	});

	// Sync bindable value with state (only in controlled mode)
	$effect(() => {
		if (isControlled && value !== rootState.value) {
			value = rootState.value;
		}
	});

	// Props for the root container
	const mergedProps = $derived(
		mergeProps(restProps as Record<string, unknown>, {
			[rootState.getHPDAttr('root')]: '',
			...rootState.sharedProps
		}, className ? { class: className } : {}) as Record<string, unknown>
	);
</script>

{#if child}
	{@render child({ props: mergedProps })}
{:else}
	<div bind:this={ref} {...mergedProps}>
		{@render children?.()}
	</div>
{/if}
