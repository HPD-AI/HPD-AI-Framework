<script lang="ts">
	/**
	 * SessionList Test Component
	 *
	 * Test harness for the SessionList compound component.
	 * Renders Root + Item + Empty + CreateButton with data-testid attributes.
	 */
	import * as SessionList from '../exports.js';
	import type { Session } from '@hpd/hpd-agent-client';

	interface Props {
		sessions?: Session[];
		activeSessionId?: string | null;
		onSelect?: (id: string) => void;
		onDelete?: (id: string) => void;
		onCreate?: () => void;
	}

	let {
		sessions = [],
		activeSessionId = $bindable(null),
		onSelect,
		onDelete,
		onCreate
	}: Props = $props();

	// Mirror activeSessionId for binding display
	let activeDisplay = $derived(activeSessionId ?? 'none');

	function handleSelect(id: string) {
		activeSessionId = id;
		onSelect?.(id);
	}
</script>

<SessionList.Root
	{sessions}
	bind:activeSessionId
	onSelect={handleSelect}
	{onDelete}
	{onCreate}
	data-testid="root"
>
	{#snippet children({ isEmpty, count })}
		<div data-testid="count">{count}</div>

		{#if onCreate}
			<SessionList.CreateButton data-testid="create-btn">
				New Session
			</SessionList.CreateButton>
		{/if}

		{#if isEmpty}
			<SessionList.Empty data-testid="empty">
				No sessions yet
			</SessionList.Empty>
		{:else}
			{#each sessions as session (session.id)}
				<SessionList.Item {session} data-testid="item-{session.id}">
					{#snippet children({ isActive, lastActivity })}
						<span data-testid="label-{session.id}">{session.id}</span>
						<span data-testid="activity-{session.id}">{lastActivity}</span>
						{#if isActive}
							<span data-testid="active-indicator-{session.id}">active</span>
						{/if}
						{#if onDelete}
							<button
								data-testid="delete-{session.id}"
								onclick={(e) => { e.stopPropagation(); onDelete?.(session.id); }}
							>
								×
							</button>
						{/if}
					{/snippet}
				</SessionList.Item>
			{/each}
		{/if}
	{/snippet}
</SessionList.Root>

<!-- Binding display for assertions -->
<div data-testid="active-binding">{activeDisplay}</div>
