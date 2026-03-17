<script lang="ts">
	/**
	 * MessageEdit Test Harness
	 *
	 * Renders the full compound component with data-testid attributes
	 * on everything so browser tests can assert against real DOM output.
	 *
	 * The harness can operate in both uncontrolled and controlled modes.
	 * In controlled mode, pass `editing` + `onStartEdit`/`onSave`/`onCancel`.
	 */
	import * as MessageEdit from '../exports.js';
	import type { Workspace } from '../../workspace/types.ts';

	interface Props {
		workspace: Workspace;
		messageIndex?: number;
		initialValue?: string;
		/** Controlled mode: pass this to drive editing externally */
		editing?: boolean;
		onStartEdit?: () => void;
		onSave?: () => void;
		onCancel?: () => void;
		onError?: (err: unknown) => void;
	}

	let {
		workspace,
		messageIndex = 0,
		initialValue = 'Original message content',
		editing,
		onStartEdit,
		onSave,
		onCancel,
		onError,
	}: Props = $props();
</script>

<MessageEdit.Root
	{workspace}
	{messageIndex}
	{initialValue}
	{editing}
	{onStartEdit}
	{onSave}
	{onCancel}
	{onError}
	data-testid="root"
>
	{#snippet children({ editing: isEditing, startEdit, draft, pending, canSave, save, cancel })}
		<!-- Snippet prop display for assertions -->
		<div data-testid="snippet-editing">{isEditing}</div>
		<div data-testid="snippet-draft">{draft}</div>
		<div data-testid="snippet-pending">{pending}</div>
		<div data-testid="snippet-can-save">{canSave}</div>

		<!-- Start-edit trigger (view mode) -->
		<button data-testid="start-edit-trigger" onclick={startEdit}>Edit</button>

		<!-- Edit UI (shown when editing) -->
		{#if isEditing}
			<MessageEdit.Textarea
				placeholder="Edit message…"
				aria-label="Edit message"
				data-testid="textarea-root"
			/>

			<MessageEdit.SaveButton
				aria-label="Save edit"
				data-testid="save-btn"
			>
				{#snippet children({ pending: savePending, disabled })}
					<div data-testid="save-pending">{savePending}</div>
					<div data-testid="save-disabled">{disabled}</div>
				{/snippet}
			</MessageEdit.SaveButton>

			<MessageEdit.CancelButton
				aria-label="Cancel edit"
				data-testid="cancel-btn"
			>
				{#snippet children({ pending: cancelPending })}
					<div data-testid="cancel-pending">{cancelPending}</div>
				{/snippet}
			</MessageEdit.CancelButton>
		{/if}
	{/snippet}
</MessageEdit.Root>
