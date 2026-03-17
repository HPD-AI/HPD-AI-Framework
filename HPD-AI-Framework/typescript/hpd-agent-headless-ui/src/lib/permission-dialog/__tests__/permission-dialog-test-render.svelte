<script lang="ts">
	import { createMockAgent } from '$lib/testing/mock-agent.svelte.js';
	import * as PermissionDialog from '../index.js';

	// Expose agent for testing
	let { agentForTest } = $props<{ agentForTest?: ReturnType<typeof createMockAgent> }>();

	// Create default agent if not provided
	const agent = $derived(agentForTest ?? createMockAgent());
</script>

<PermissionDialog.Root {agent}>
	{#snippet render({ request, status, approve, deny })}
		<div data-testid="custom-permission-ui">
			<p data-testid="status-value">{status}</p>
			{#if request}
				<h2>Custom UI: {request.functionName}</h2>
				<p>{request.description ?? ''}</p>
				<button data-testid="custom-approve" onclick={() => approve('ask')}>
					Approve
				</button>
				<button data-testid="custom-deny" onclick={() => deny()}>
					Deny
				</button>
			{:else}
				<p>No permission request</p>
			{/if}
		</div>
	{/snippet}
</PermissionDialog.Root>
