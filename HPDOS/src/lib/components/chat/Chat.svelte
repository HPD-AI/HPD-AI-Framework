<script lang="ts">
	import { ChatInput, MessageList, Message, FileAttachment, FileAttachmentState } from '@hpd/hpd-agent-headless-ui';
	import { RunConfigState } from '@hpd/hpd-agent-headless-ui';
	import type { Workspace, MessageListSnippetProps, MessageHTMLProps } from '@hpd/hpd-agent-headless-ui';
	import { boxFrom } from 'svelte-toolbelt';
	import Markdown from './Markdown.svelte';
	import ModelPicker from './ModelPicker.svelte';
	import ToolCallRenderer from './tools/ToolCallRenderer.svelte';
	import MessageActionsBar from './MessageActionsBar.svelte';
	import { providers } from '../../providers.svelte.js';
	import { toolRegistry } from './tools/toolRegistry.js';
	import ArtifactToolCall from './tools/ArtifactToolCall.svelte';
	import { buildAppSystemInstructions } from '../../apps/appHarness.js';

	// Register specialized tool renderers (idempotent — safe to call multiple times)
	toolRegistry.register('upsert_artifact', ArtifactToolCall);


	interface Props {
		workspace: Workspace;
		onOpenSettings?: () => void;
	}

	let { workspace, onOpenSettings }: Props = $props();

	// Per-session run config — holds model override for this session
	const runConfig = new RunConfigState();
	runConfig.setAdditionalSystemInstructions(buildAppSystemInstructions());

	// Local cache of per-session model selection (stays in sync with server).
	// Plain Map intentionally — not reactive, used only as a lookup cache.
	const sessionModels = new Map<string, { providerKey: string; modelId: string }>();

	// Load providers on mount so model picker has data
	$effect(() => { providers.load(); });

	// Seed runConfig from local cache or session metadata when active session changes
	$effect(() => {
		const sid = workspace.activeSessionId;
		if (!sid) return;

		// Local cache takes priority (avoids stale workspace.sessions metadata)
		const cached = sessionModels.get(sid);
		if (cached) {
			runConfig.setModel(cached.providerKey, cached.modelId);
			return;
		}

		// Fall back to server metadata (first load of this session)
		const session = workspace.sessions.find(s => s.id === sid);
		if (!session) return;
		const pk = session.metadata?.providerKey as string | undefined;
		const mid = session.metadata?.modelId as string | undefined;
		if (pk && mid) {
			sessionModels.set(sid, { providerKey: pk, modelId: mid });
			runConfig.setModel(pk, mid);
		} else if (providers.defaults?.providerKey && providers.defaults?.modelId) {
			runConfig.setModel(providers.defaults.providerKey, providers.defaults.modelId);
		} else {
			runConfig.setModel(undefined, undefined);
		}
	});

	// Persist model selection to local cache + server on change
	$effect(() => {
		const pk = runConfig.providerKey;
		const mid = runConfig.modelId;
		const sid = workspace.activeSessionId;
		if (!sid || !pk || !mid) return;
		sessionModels.set(sid, { providerKey: pk, modelId: mid });
		workspace.client.updateSession(sid, { metadata: { providerKey: pk, modelId: mid } });
		providers.setDefaults(pk, mid);
	});

	const agentState = $derived(workspace.state);
	const messages = $derived(agentState?.messages ?? []);
	const isEmpty = $derived(messages.length === 0 && !agentState?.streaming);

	// File attachment state — stub upload fn until real endpoint exists.
	// Held externally so we can call clear() after submit.
	const fileState = new FileAttachmentState({
		uploadFn: boxFrom(() => async (_sessionId: string, file: File) => ({
			assetId: crypto.randomUUID(),
			contentType: file.type,
			name: file.name,
			sizeBytes: file.size,
		})) as any,
		sessionId: boxFrom(() => workspace.activeSessionId ?? null) as any,
		disabled: boxFrom(() => agentState?.streaming ?? false) as any,
	});

	let inputValue = $state('');
	let fileInput = $state<HTMLInputElement | null>(null);

	function openFilePicker() {
		fileInput?.click();
	}

	function onFileInputChange(e: Event) {
		const files = (e.target as HTMLInputElement).files;
		if (files?.length) fileState.add(files);
		(e.target as HTMLInputElement).value = '';
	}

	function wireScrollRef(el: HTMLDivElement, setRef: (el: HTMLDivElement | null) => void) {
		setRef(el);
		return { destroy() { setRef(null); } };
	}

	async function handleSubmit({ value }: { value: string }) {
		if (!workspace.activeSessionId) {
			await workspace.createSession();
		}
		workspace.send(value, {
			runConfig: runConfig.value,
		});
		fileState.clear();
		inputValue = '';
	}

</script>

<!-- Hidden file input -->
<input
	bind:this={fileInput}
	type="file"
	multiple
	accept="*/*"
	style="display:none"
	onchange={onFileInputChange}
/>

<div class="chat-root" data-empty={isEmpty}>

	<!-- ===== MAIN AREA (chat + optional artifact panel) ===== -->
	<div class="chat-body">

	{#if isEmpty}
		<!-- ===== EMPTY / NEW CONVERSATION STATE ===== -->
		<div class="chat-welcome">
			<div class="chat-inner">
				<h1 class="chat-welcome-title">What can I help with?</h1>
				{@render chatInput(false)}
			</div>
		</div>
	{:else}
		<div class="chat-left">
		<!-- ===== MESSAGE LIST ===== -->
		<MessageList.Root {messages} scrollBehavior="sent-message">
			{#snippet child({ messages: msgs, setRef }: MessageListSnippetProps & { props: Record<string, unknown> })}
				<div
					class="chat-message-list"
					role="log"
					aria-label="Message history"
					aria-live="polite"
					aria-atomic="false"
					tabindex="0"
					use:wireScrollRef={setRef}
				>
					<div class="chat-inner">
						{#each msgs as msg, i (msg.id)}
							<Message message={msg}>
								{#snippet child({ props }: { props: MessageHTMLProps })}
									<div {...props} class="chat-message-row">
										{#if msg.thinking}
											<span class="chat-thinking">Thinking…</span>
										{/if}
										{#if msg.toolCalls?.length}
											<div class="chat-tool-calls">
												{#each msg.toolCalls as tc (tc.callId)}
													<ToolCallRenderer toolCall={tc} />
												{/each}
											</div>
											{#if msg.content || msg.streaming}
												<div class="chat-content">
													<Markdown content={msg.content} streaming={msg.streaming ?? false} />
												</div>
											{/if}
										{:else if msg.content || msg.streaming}
											<span class="chat-content">{msg.content}</span>
										{/if}
										{#if !(msg.toolCalls?.length && !msg.content)}
					<MessageActionsBar
											{workspace}
											messageIndex={i}
											role={msg.role}
											content={msg.content}
										/>
				{/if}
									</div>
								{/snippet}
							</Message>
						{/each}
					</div>
				</div>
			{/snippet}
		</MessageList.Root>

		<!-- ===== INPUT (bottom, conversation active) ===== -->
		<div class="chat-input-row">
			<div class="chat-inner">
				{@render chatInput(agentState?.streaming ?? false)}
			</div>
		</div>
		</div><!-- .chat-left -->

	{/if}

	</div><!-- .chat-body -->
</div>

<!-- ===== SHARED CHAT INPUT SNIPPET ===== -->
{#snippet chatInput(streaming: boolean)}
	<ChatInput.Root onSubmit={handleSubmit} disabled={streaming} class="chat-input-root" value={inputValue} onChange={(v) => { inputValue = v; }}>

		<!-- Textarea -->
		<ChatInput.Input placeholder="Message…" maxRows={6} />

		<!-- Attachment chips — shown between textarea and toolbar when files attached -->
		{#if fileState.hasAttachments}
			<ChatInput.Top class="chat-input-chips">
				<FileAttachment.Root state={fileState}>
					{#snippet children({ attachments, remove })}
						{#each attachments as att (att.localId)}
							<span class="chat-attachment-chip" data-status={att.status}>
								<span class="chat-attachment-name">{att.file.name}</span>
								{#if att.status === 'uploading'}
									<span class="chat-attachment-spinner" aria-label="Uploading">…</span>
								{:else if att.status === 'error'}
									<span class="chat-attachment-error" title={att.error}>!</span>
								{/if}
								<button
									class="chat-attachment-remove"
									onclick={() => remove(att.localId)}
									aria-label="Remove {att.file.name}"
								>✕</button>
							</span>
						{/each}
					{/snippet}
				</FileAttachment.Root>
			</ChatInput.Top>
		{/if}

		<!-- Bottom toolbar: plus (attach) on left, model pill center-left, send/stop on right -->
		<ChatInput.Bottom class="chat-input-toolbar">
			{#snippet child({ canSubmit, submit, props }: { canSubmit: boolean; submit: () => void; props: Record<string, unknown> })}
				<div {...props} class="chat-toolbar-inner">
					<!-- Attach button — disabled until upload API is implemented -->
					<button
						class="chat-icon-btn"
						onclick={openFilePicker}
						disabled
						aria-label="Attach file"
						title="Attach file (coming soon)"
					>
						<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
							<path d="M12 5v14M5 12h14"/>
						</svg>
					</button>

					<!-- Model picker -->
					<ModelPicker {runConfig} onOpenSettings={() => onOpenSettings?.()} />

					<div class="chat-toolbar-spacer"></div>

					{#if streaming}
						<button class="chat-send-btn chat-abort-btn" onclick={() => workspace.abort()} aria-label="Stop">
							<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><rect x="4" y="4" width="16" height="16" rx="2"/></svg>
						</button>
					{:else}
						<button class="chat-send-btn" onclick={submit} disabled={!canSubmit} aria-label="Send">
							<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
								<path d="M12 19V5M5 12l7-7 7 7"/>
							</svg>
						</button>
					{/if}
				</div>
			{/snippet}
		</ChatInput.Bottom>

	</ChatInput.Root>
{/snippet}

<style>
	.chat-root {
		display: flex;
		flex-direction: column;
		flex: 1;
		min-height: 0;
		overflow: hidden;
	}

	/* ===== Body: chat + optional artifact panel side-by-side ===== */
	.chat-body {
		display: flex;
		flex: 1;
		min-height: 0;
		overflow: hidden;
	}

	.chat-left {
		display: flex;
		flex-direction: column;
		flex: 1;
		min-width: 0;
		min-height: 0;
		overflow: hidden;
	}

/* ===== Centered content column ===== */
	.chat-inner {
		max-width: 48rem;
		margin: 0 auto;
		width: 100%;
		display: flex;
		flex-direction: column;
		gap: 0.75rem;
	}

	/* ===== Welcome / empty state ===== */
	.chat-welcome {
		flex: 1;
		display: flex;
		flex-direction: column;
		align-items: stretch;
		justify-content: center;
		padding: 2rem 1.5rem 4rem;
	}

	.chat-welcome-title {
		font-size: 1.75rem;
		font-weight: 600;
		color: rgb(var(--color-text-primary));
		margin: 0 0 1.25rem;
		text-align: center;
		letter-spacing: -0.02em;
	}

	/* ===== Message list ===== */
	:global(.chat-message-list) {
		flex: 1;
		min-height: 0;
		overflow-y: auto;
		padding: 1.5rem 1.5rem 1rem;
		scroll-behavior: smooth;
	}

	/* ===== Message rows ===== */
	:global(.chat-message-row) {
		display: flex;
		flex-direction: column;
	}
	:global(.chat-message-row[data-role="user"]) { align-items: flex-end; }
	:global(.chat-message-row[data-role="assistant"]),
	:global(.chat-message-row[data-role="system"]) { align-items: flex-start; }

	/* Tool call wrapper stretches to full column width */
	:global(.chat-tool-calls) {
		align-self: stretch;
		display: flex;
		flex-direction: column;
	}

	:global(.chat-message-row[data-role="user"] > .chat-content) {
		display: block;
		max-width: 75%;
		padding: 0.625rem 0.875rem;
		border-radius: 12px;
		border-bottom-right-radius: 4px;
		background: rgb(var(--color-accent-primary) / 0.18);
		color: rgb(var(--color-text-primary));
		font-size: 0.875rem;
		line-height: 1.5;
		white-space: pre-wrap;
		word-break: break-word;
	}
	:global(.chat-message-row[data-role="assistant"] > .chat-content),
	:global(.chat-message-row[data-role="system"] > .chat-content) {
		display: block;
		max-width: 75%;
		padding: 0.625rem 0.875rem;
		border-radius: 12px;
		border-bottom-left-radius: 4px;
		background: rgb(255 255 255 / 0.05);
		color: rgb(var(--color-text-primary));
		font-size: 0.875rem;
		line-height: 1.5;
		word-break: break-word;
	}

	:global(.chat-thinking) {
		display: block;
		font-size: 0.75rem;
		color: rgb(var(--color-text-tertiary));
		font-style: italic;
		margin-bottom: 0.25rem;
	}

	/* ===== Bottom input row ===== */
	.chat-input-row {
		flex: none;
		padding: 0.75rem 1.5rem 1rem;
	}

	/* ===== Chat input shell ===== */
	:global(.chat-input-root) {
		display: flex;
		flex-direction: column;
		background: rgb(255 255 255 / 0.05);
		border: 1px solid rgb(255 255 255 / 0.08);
		border-radius: 14px;
		transition: border-color 0.15s;
		padding: 0.625rem 0.625rem 0.375rem;
	}
	:global(.chat-input-root:focus-within) {
		border-color: rgb(var(--color-accent-primary) / 0.5);
	}

	/* Textarea fills the top */
	:global([data-chat-input-input]) {
		width: 100%;
	}
	:global(.chat-input-root textarea) {
		width: 100%;
		background: transparent;
		border: none;
		outline: none;
		font-size: 0.875rem;
		color: rgb(var(--color-text-primary));
		font-family: inherit;
		line-height: 1.5;
		resize: none;
		padding: 0;
		min-height: 1.5rem;
	}
	:global(.chat-input-root textarea::placeholder) {
		color: rgb(var(--color-text-quaternary, var(--color-text-tertiary)));
	}
	:global(.chat-input-root textarea:disabled) { opacity: 0.5; }

	/* Attachment chips row (between textarea and toolbar) */
	:global(.chat-input-chips) {
		display: flex;
		flex-wrap: wrap;
		gap: 0.375rem;
		padding: 0.375rem 0 0;
	}

	/* Bottom toolbar */
	:global(.chat-input-toolbar) {
		display: flex;
		align-items: center;
		padding-top: 0.375rem;
	}
	:global(.chat-toolbar-inner) {
		display: flex;
		align-items: center;
		width: 100%;
		gap: 0.375rem;
	}
	:global(.chat-toolbar-spacer) {
		flex: 1;
	}

	/* Plus / attach icon button */
	:global(.chat-icon-btn) {
		width: 30px;
		height: 30px;
		display: flex;
		align-items: center;
		justify-content: center;
		border-radius: 50%;
		border: 1px solid rgb(255 255 255 / 0.12);
		background: transparent;
		color: rgb(var(--color-text-secondary));
		cursor: pointer;
		transition: background 0.15s, color 0.15s, border-color 0.15s;
	}
	:global(.chat-icon-btn:hover:not(:disabled)) {
		background: rgb(255 255 255 / 0.07);
		color: rgb(var(--color-text-primary));
		border-color: rgb(255 255 255 / 0.2);
	}
	:global(.chat-icon-btn:disabled) { opacity: 0.35; cursor: default; }

	/* Send / abort button */
	:global(.chat-send-btn) {
		width: 34px;
		height: 34px;
		flex: none;
		display: flex;
		align-items: center;
		justify-content: center;
		border-radius: 10px;
		border: none;
		cursor: pointer;
		transition: all 0.15s;
		background: rgb(var(--color-accent-primary));
		color: #fff;
	}
	:global(.chat-send-btn:disabled) { opacity: 0.4; cursor: default; }
	:global(.chat-send-btn:not(:disabled):hover) {
		background: rgb(var(--color-accent-light, var(--color-accent-primary)));
		transform: translateY(-1px);
	}
	:global(.chat-abort-btn) { background: rgb(var(--color-error, 239 68 68) / 0.8); }
	:global(.chat-abort-btn:not(:disabled):hover) { background: rgb(var(--color-error, 239 68 68)); }

	/* ===== Attachment chips ===== */
	:global(.chat-attachment-chip) {
		display: inline-flex;
		align-items: center;
		gap: 0.25rem;
		padding: 0.2rem 0.5rem;
		border-radius: 6px;
		background: rgb(255 255 255 / 0.08);
		border: 1px solid rgb(255 255 255 / 0.1);
		font-size: 0.75rem;
		color: rgb(var(--color-text-secondary));
		max-width: 12rem;
	}
	:global(.chat-attachment-chip[data-status="error"]) {
		border-color: rgb(var(--color-error, 239 68 68) / 0.5);
		background: rgb(var(--color-error, 239 68 68) / 0.08);
	}
	:global(.chat-attachment-name) {
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
		max-width: 8rem;
	}
	:global(.chat-attachment-spinner) { opacity: 0.6; }
	:global(.chat-attachment-error) {
		color: rgb(var(--color-error, 239 68 68));
		font-weight: 700;
	}
	:global(.chat-attachment-remove) {
		display: flex;
		align-items: center;
		justify-content: center;
		width: 14px;
		height: 14px;
		border: none;
		background: transparent;
		color: rgb(var(--color-text-tertiary));
		cursor: pointer;
		padding: 0;
		font-size: 0.65rem;
		border-radius: 3px;
		flex: none;
	}
	:global(.chat-attachment-remove:hover) { color: rgb(var(--color-text-primary)); }
</style>
