<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { RunConfigPermissionOverridesPanelProps, RunConfigPermissionOverridesPanelHTMLProps } from '../types.js';
	import { RunConfigPermissionOverridesPanelState } from '../run-config.svelte.js';

	let {
		runConfig,
		permissions = [],
		disabled = false,
		class: className,
		child,
		children,
		...restProps
	}: RunConfigPermissionOverridesPanelProps = $props();

	const state = RunConfigPermissionOverridesPanelState.create({
		runConfig: boxWith(() => runConfig),
		permissions: boxWith(() => permissions),
		disabled: boxWith(() => disabled),
	});

	const mergedProps = $derived(mergeProps(restProps, state.props, className ? { class: className } : {}) as RunConfigPermissionOverridesPanelHTMLProps);
</script>

{#if child}
	{@render child({ props: mergedProps, ...state.snippetProps })}
{:else}
	<div {...mergedProps}>
		{@render children?.(state.snippetProps)}
	</div>
{/if}
