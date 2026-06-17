<script lang="ts">
	import { createMockAgent } from '$lib/testing/mock-agent.svelte.js';
	import * as PermissionDialog from '../index.js';
	import type { PermissionRequest } from '$lib/agent/types.js';

	// Expose agent for testing
	let { agentForTest } = $props<{ agentForTest?: ReturnType<typeof createMockAgent> }>();

	// Create default agent if not provided
	const agent = $derived(agentForTest ?? createMockAgent());

	// Function to trigger a permission request
	function triggerPermissionRequest() {
		const request: PermissionRequest = {
			permissionId: 'test-permission-1',
			sourceName: 'test-tools',
			functionName: 'dangerousAction',
			description: 'This action requires your approval',
			callId: 'call-123',
			arguments: {
				action: 'delete',
				target: 'important-file.txt'
			}
		};

		// Simulate the permission request event
		agent.state.onPermissionRequest(request);
	}
</script>

<PermissionDialog.Root {agent}>
	<PermissionDialog.Overlay data-testid="overlay" />
	<PermissionDialog.Content data-testid="content">
		<PermissionDialog.Header data-testid="header">
			Permission Required
		</PermissionDialog.Header>

		<PermissionDialog.Description data-testid="description">
			{#snippet children({ functionName, description, arguments: args })}
				<div class="description-content">
					<p data-testid="function-name">Allow <strong>{functionName}</strong>?</p>
					{#if description}
						<p data-testid="tool-description">{description}</p>
					{/if}
					{#if args}
						<pre data-testid="tool-args">{JSON.stringify(args, null, 2)}</pre>
					{/if}
				</div>
			{/snippet}
		</PermissionDialog.Description>

		<PermissionDialog.Actions data-testid="actions">
			<PermissionDialog.Approve choice="allow_always" data-testid="approve-always">
				Always Allow
			</PermissionDialog.Approve>
			<PermissionDialog.Approve choice="ask" data-testid="approve-once">
				Ask Each Time
			</PermissionDialog.Approve>
			<PermissionDialog.Deny data-testid="deny">Deny</PermissionDialog.Deny>
		</PermissionDialog.Actions>
	</PermissionDialog.Content>
</PermissionDialog.Root>

<!-- Trigger for testing -->
<button data-testid="trigger" onclick={triggerPermissionRequest}>
	Request Permission
</button>
