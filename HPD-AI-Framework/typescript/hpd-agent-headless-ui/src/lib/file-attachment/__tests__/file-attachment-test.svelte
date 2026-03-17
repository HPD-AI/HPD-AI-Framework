<script lang="ts">
	/**
	 * FileAttachment Test Harness
	 *
	 * Renders the FileAttachment component in two modes:
	 *   1. External state (pre-constructed FileAttachmentState passed via `state` prop)
	 *   2. Internal state (client + sessionId props, state built inside component)
	 *
	 * The harness exposes DOM elements with data-testid for every relevant piece
	 * of state so the browser tests can make pure DOM assertions.
	 */
	import * as FileAttachment from '../exports.js';
	import { FileAttachmentState } from '../file-attachment.svelte.js';
	import { boxWith } from 'svelte-toolbelt';
	import type { AssetReference } from '@hpd/hpd-agent-client';
	import type { AgentClientLike } from '$lib/workspace/types.js';

	interface Props {
		// Controls which mode the harness uses
		mode?: 'external' | 'internal';
		sessionId?: string | null;
		disabled?: boolean;
		// Mock upload function — called by the state
		uploadFn?: (sessionId: string, file: File) => Promise<AssetReference>;
		// Mock client for internal mode
		client?: AgentClientLike;
	}

	const DEFAULT_ASSET: AssetReference = {
		assetId: 'test-asset-1',
		contentType: 'image/png',
		name: 'test.png',
		sizeBytes: 512,
	};

	let {
		mode = 'external',
		sessionId = 'sess-test',
		disabled = false,
		uploadFn = async (_sid: string, _file: File) => DEFAULT_ASSET,
		client = undefined,
	}: Props = $props();

	// External state — constructed here, shared with FileAttachment component
	const externalState = new FileAttachmentState({
		uploadFn: boxWith(() => uploadFn),
		sessionId: boxWith(() => sessionId),
		disabled: boxWith(() => disabled),
	});

	// Programmatic add helper — simulates add() from outside the component
	async function triggerAdd(fileName: string, contentType: string) {
		const file = new File(['content'], fileName, { type: contentType });
		await externalState.add([file]);
	}
</script>

{#if mode === 'external'}
	<!-- External state mode: state prop bypasses internal construction -->
	<FileAttachment.Root state={externalState} data-testid="root">
		{#snippet children(s)}
			<div data-testid="attachment-list">
				{#each s.attachments as att (att.localId)}
					<div data-testid="attachment-{att.localId}" data-status={att.status}>
						<span data-testid="att-name-{att.localId}">{att.file.name}</span>
						<span data-testid="att-status-{att.localId}">{att.status}</span>
						{#if att.error}
							<span data-testid="att-error-{att.localId}">{att.error}</span>
						{/if}
						<button data-testid="remove-{att.localId}" onclick={() => s.remove(att.localId)}>
							Remove
						</button>
						{#if att.status === 'error'}
							<button data-testid="retry-{att.localId}" onclick={() => s.retry(att.localId)}>
								Retry
							</button>
						{/if}
					</div>
				{/each}
			</div>
			<span data-testid="has-attachments">{s.hasAttachments}</span>
			<span data-testid="is-uploading">{s.isUploading}</span>
			<span data-testid="can-submit">{s.canSubmit}</span>
			<button data-testid="clear-btn" onclick={() => s.clear()}>Clear</button>
		{/snippet}
	</FileAttachment.Root>

	<!-- Trigger buttons outside the component to test programmatic add -->
	<button
		data-testid="add-png-btn"
		onclick={async () => { await triggerAdd('photo.png', 'image/png'); }}
	>Add PNG</button>

	<button
		data-testid="add-txt-btn"
		onclick={async () => { await triggerAdd('doc.txt', 'text/plain'); }}
	>Add TXT</button>

{:else}
	<!-- Internal state mode: client + sessionId props -->
	<FileAttachment.Root {client} {sessionId} {disabled} data-testid="root">
		{#snippet children(s)}
			<span data-testid="has-attachments">{s.hasAttachments}</span>
			<span data-testid="can-submit">{s.canSubmit}</span>
		{/snippet}
	</FileAttachment.Root>
{/if}

<!-- Resolved assets as JSON — readable by tests after upload completes -->
<div data-testid="resolved-assets">{JSON.stringify(externalState.resolvedAssets)}</div>
<div data-testid="attachment-count">{externalState.attachments.length}</div>
