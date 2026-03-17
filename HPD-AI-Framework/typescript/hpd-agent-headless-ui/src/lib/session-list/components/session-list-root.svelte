<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { SessionListRootProps, SessionListRootHTMLProps } from '../types.js';
	import { SessionListRootState } from '../session-list.svelte.js';

	let {
		sessions,
		activeSessionId = $bindable(null),
		loading = false,
		orientation = 'vertical',
		loop = true,
		'aria-label': ariaLabel = 'Sessions',
		class: className,
		onSelect,
		onDelete,
		onCreate,
		child,
		children,
		...restProps
	}: SessionListRootProps & { class?: string } = $props();

	$effect(() => {
		if (import.meta.env.DEV && !child && !children) {
			console.warn('[SessionList.Root] No children or child snippet provided — nothing will render.');
		}
	});

	let rootNode: HTMLElement | null = $state(null);

	// Rename to avoid conflict with $state rune
	const rootState = SessionListRootState.create({
		sessions: boxWith(() => sessions),
		activeSessionId: boxWith(() => activeSessionId),
		loading: boxWith(() => loading),
		orientation: boxWith(() => orientation),
		loop: boxWith(() => loop),
		rootNode: boxWith(() => rootNode),
		// Wrap callbacks in closures so latest prop value is always called
		onSelect: (id) => onSelect?.(id),
		onDelete: (id) => onDelete?.(id),
		onCreate: () => onCreate?.(),
	});

	const mergedProps = $derived(mergeProps(
		restProps,
		rootState.props,
		{ 'aria-label': ariaLabel },
		className ? { class: className } : {}
	) as SessionListRootHTMLProps);
</script>

{#if child}
	{@render child({ props: mergedProps, ...rootState.snippetProps })}
{:else}
	<div bind:this={rootNode} {...mergedProps}>
		{@render children?.(rootState.snippetProps)}
	</div>
{/if}
