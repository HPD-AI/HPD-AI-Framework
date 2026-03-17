<script lang="ts">
	import { mergeProps, boxWith } from 'svelte-toolbelt';
	import type { FileAttachmentProps, FileAttachmentHTMLProps } from '../types.js';
	import { FileAttachmentState } from '../file-attachment.svelte.js';

	let {
		state: externalState,
		client,
		sessionId = null,
		disabled = false,
		class: className,
		child,
		children,
		...restProps
	}: FileAttachmentProps = $props();

	// Use pre-constructed state when provided; otherwise build internally.
	// $derived ensures externalState prop changes are tracked correctly.
	const internalState = new FileAttachmentState({
		uploadFn: boxWith(() => {
			if (!client) throw new Error('FileAttachment: provide either state or client');
			return (sid: string, file: File) => client.uploadAsset(sid, file);
		}),
		sessionId: boxWith(() => sessionId ?? null),
		disabled: boxWith(() => disabled),
	});
	const state = $derived(externalState ?? internalState);

	const mergedProps = $derived(mergeProps(restProps, state.props, className ? { class: className } : {}) as FileAttachmentHTMLProps);
</script>

{#if child}
	{@render child({ props: mergedProps, ...state.snippetProps })}
{:else}
	<div {...mergedProps}>
		{@render children?.(state.snippetProps)}
	</div>
{/if}
