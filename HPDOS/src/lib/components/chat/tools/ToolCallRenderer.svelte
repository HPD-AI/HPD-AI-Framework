<script lang="ts">
	import type { ToolCall } from '@hpd/hpd-agent-headless-ui';
	import { toolRegistry } from './toolRegistry.js';
	import DefaultToolCall from './DefaultToolCall.svelte';

	interface Props {
		toolCall: ToolCall;
	}

	let { toolCall }: Props = $props();

	const specialized = $derived(toolRegistry.resolve(toolCall.name));
</script>

{#if specialized}
	<svelte:component this={specialized} {toolCall} />
{:else}
	<DefaultToolCall {toolCall} />
{/if}
