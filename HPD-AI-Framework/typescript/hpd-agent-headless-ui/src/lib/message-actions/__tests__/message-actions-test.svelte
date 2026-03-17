<script lang="ts">
	/**
	 * MessageActions Test Harness
	 *
	 * Renders the full compound component with data-testid attributes
	 * on everything so browser tests can assert against real DOM output.
	 */
	import * as MessageActions from '../exports.js';
	import type { Branch } from '@hpd/hpd-agent-client';
	import type { Workspace } from '../../workspace/types.ts';
	import type { MessageRole } from '../../agent/types.ts';

	interface Props {
		workspace: Workspace;
		messageIndex?: number;
		role?: MessageRole;
		branch?: Branch | null;
		copyContent?: string;
		onPrev?: () => void;
		onNext?: () => void;
		onEditSuccess?: () => void;
		onEditError?: (err: unknown) => void;
		onRetrySuccess?: () => void;
		onRetryError?: (err: unknown) => void;
		onCopySuccess?: () => void;
		onCopyError?: (err: unknown) => void;
		editAriaLabel?: string;
		retryAriaLabel?: string;
		copyAriaLabel?: string;
		prevAriaLabel?: string;
		nextAriaLabel?: string;
		copyResetDelay?: number;
		/** Content to pass to edit() when the edit trigger button is clicked */
		editContent?: string;
	}

	let {
		workspace,
		messageIndex = 0,
		role = 'user',
		branch = null,
		copyContent = 'Hello world',
		onPrev,
		onNext,
		onEditSuccess,
		onEditError,
		onRetrySuccess,
		onRetryError,
		onCopySuccess,
		onCopyError,
		editAriaLabel = 'Edit message',
		retryAriaLabel = 'Retry message',
		copyAriaLabel = 'Copy message',
		prevAriaLabel = 'Previous version',
		nextAriaLabel = 'Next version',
		copyResetDelay = 2000,
		editContent = 'Edited content',
	}: Props = $props();
</script>

<MessageActions.Root
	{workspace}
	{messageIndex}
	{role}
	{branch}
	data-testid="root"
>
	{#snippet children({ hasSiblings, pending, position })}
		<!-- Snippet prop display -->
		<div data-testid="snippet-has-siblings">{hasSiblings}</div>
		<div data-testid="snippet-pending">{pending}</div>
		<div data-testid="snippet-position">{position}</div>

		<!-- EditButton -->
		<MessageActions.EditButton
			aria-label={editAriaLabel}
			onSuccess={onEditSuccess}
			onError={onEditError}
			data-testid="edit-btn"
		>
			{#snippet children({ edit, status, disabled })}
				<div data-testid="edit-status">{status}</div>
				<div data-testid="edit-disabled">{disabled}</div>
				<button data-testid="edit-trigger" onclick={() => edit(editContent)}>
					Edit
				</button>
			{/snippet}
		</MessageActions.EditButton>

		<!-- RetryButton -->
		<MessageActions.RetryButton
			aria-label={retryAriaLabel}
			onSuccess={onRetrySuccess}
			onError={onRetryError}
			data-testid="retry-btn"
		>
			{#snippet children({ retry, status, disabled })}
				<div data-testid="retry-status">{status}</div>
				<div data-testid="retry-disabled">{disabled}</div>
				<button data-testid="retry-trigger" onclick={retry}>Retry</button>
			{/snippet}
		</MessageActions.RetryButton>

		<!-- CopyButton -->
		<MessageActions.CopyButton
			content={copyContent}
			resetDelay={copyResetDelay}
			aria-label={copyAriaLabel}
			onSuccess={onCopySuccess}
			onError={onCopyError}
			data-testid="copy-btn"
		>
			{#snippet children({ copy, copied })}
				<div data-testid="copy-copied">{copied}</div>
				<button data-testid="copy-trigger" onclick={copy}>
					{copied ? 'Copied!' : 'Copy'}
				</button>
			{/snippet}
		</MessageActions.CopyButton>

		<!-- Branch nav — always rendered so tests can assert disabled state -->
		<MessageActions.Prev
			aria-label={prevAriaLabel}
			onclick={onPrev}
			data-testid="prev-btn"
		/>
		<MessageActions.Position data-testid="position-el" />
		<MessageActions.Next
			aria-label={nextAriaLabel}
			onclick={onNext}
			data-testid="next-btn"
		/>
	{/snippet}
</MessageActions.Root>
