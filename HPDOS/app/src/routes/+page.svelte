<script lang="ts">
	import { onMount } from 'svelte';
	import { AgentClient } from '@hpd-research/hpd-agent-client';
	import type { RunConfig, Session, Thread } from '@hpd-research/hpd-agent-client';
	import {
		getThreadBranchChoiceControlsForTimeline,
		type Message as ThreadMessage,
		type RuntimeRequest,
		type ThreadBranchChoiceControl,
		type ThreadTimelineItem
	} from '@hpd-research/hpd-agent-headless-ui';
	import {
		createThreadBranchNavigationState,
		createFileAttachmentState,
		createThreadRevisionState,
		createThreadState,
		FileAttachment,
		Message,
		RuntimeRequest as RuntimeRequestView,
		ThreadBranchSwitcher,
		ThreadComposer,
		ThreadStatus,
		ThreadWorkGroup,
		type StoreUnsubscriber,
		type FileAttachmentState,
		type ThreadBranchNavigationState,
		type ThreadBranchNavigationStateSnapshot,
		type ThreadRevisionState,
		type ThreadState,
		type ThreadStateSnapshot
	} from '@hpd-research/hpd-agent-headless-ui-svelte';

	const agentId = 'hpdos-agent';
	const runConfig: RunConfig = {
		providerKey: 'openrouter',
		modelId: 'qwen/qwen3.7-plus'
	};

	let client = $state<AgentClient | null>(null);
	let sessions = $state<Session[]>([]);
	let threads = $state<Thread[]>([]);
	let activeSessionId = $state('');
	let activeThreadId = $state('main');
	let thread = $state<ThreadState | null>(null);
	let branchNavigation = $state<ThreadBranchNavigationState | null>(null);
	let revisions = $state<ThreadRevisionState | null>(null);
	let attachments = $state<FileAttachmentState | null>(null);
	let snapshot = $state<ThreadStateSnapshot | null>(null);
	let branchSnapshot = $state<ThreadBranchNavigationStateSnapshot | null>(null);
	let statusMessage = $state('Starting Branch Lab.');
	let loadError = $state<string | null>(null);
	let editingMessageId = $state<string | null>(null);
	let editingDraft = $state('');

	let unsubscribeThread: StoreUnsubscriber | null = null;
	let unsubscribeBranches: StoreUnsubscriber | null = null;

	const timeline = $derived(snapshot?.timeline ?? []);
	const branchControls = $derived.by(() => {
		if (!branchSnapshot || !snapshot) return [];
		return getThreadBranchChoiceControlsForTimeline(branchSnapshot.navigation, snapshot.timeline);
	});
	const branchControlsByTimelineItem = $derived.by(() => {
		const controls = new Map<string, ThreadBranchChoiceControl[]>();
		for (const control of branchControls) {
			const existing = controls.get(control.renderTimelineItemId) ?? [];
			existing.push(control);
			controls.set(control.renderTimelineItemId, existing);
		}
		return controls;
	});

	onMount(() => {
		void boot();
		return () => {
			unsubscribeThread?.();
			unsubscribeBranches?.();
			void thread?.dispose();
			void client?.stop();
		};
	});

	function resolveBackendUrl(): string {
		const origin = globalThis.location?.origin ?? '';
		if (origin.includes('localhost') || origin.includes('127.0.0.1')) return origin;
		return 'http://127.0.0.1:4317';
	}

	function createClient(): AgentClient {
		return new AgentClient({
			baseUrl: `${resolveBackendUrl()}/api/hpd-agent`,
			transport: 'sse'
		});
	}

	async function boot(): Promise<void> {
		try {
			client = createClient();
			await refreshSessions();
			if (sessions.length === 0) {
				await createNewSession();
				return;
			}
			const startup = readStartupContext();
			const sessionId = startup.sessionId || sessions[0].id;
			await openSession(sessionId, startup.threadId || 'main');
		} catch (error) {
			loadError = normalizeError(error).message;
		}
	}

	function readStartupContext(): { sessionId: string; threadId: string } {
		const params = new URLSearchParams(globalThis.location?.search ?? '');
		return {
			sessionId: params.get('session') ?? '',
			threadId: params.get('thread') ?? 'main'
		};
	}

	async function refreshSessions(): Promise<void> {
		if (!client) return;
		sessions = await client.listSessions({
			sortBy: 'lastActivity',
			sortDirection: 'desc',
			limit: 50
		});
	}

	async function refreshThreads(): Promise<void> {
		if (!client || !activeSessionId) return;
		threads = await client.listThreads(activeSessionId);
	}

	async function createNewSession(): Promise<void> {
		if (!client) return;
		const session = await client.createSession({
			metadata: {
				app: 'branch-lab'
			}
		});
		await refreshSessions();
		await openSession(session.id, 'main');
	}

	async function openSession(sessionId: string, threadId = 'main'): Promise<void> {
		activeSessionId = sessionId;
		await refreshThreads();
		await openThread(threadId);
	}

	async function openThread(threadId: string): Promise<void> {
		if (!client || !activeSessionId) return;

		unsubscribeThread?.();
		unsubscribeBranches?.();
		await thread?.dispose();

		activeThreadId = threadId;
		editingMessageId = null;
		editingDraft = '';
		loadError = null;
		attachments = createFileAttachmentState({
			client,
			sessionId: activeSessionId,
			threadId
		});

		const nextThread = createThreadState({
			client,
			agentId,
			sessionId: activeSessionId,
			threadId
		});
		thread = nextThread;
		snapshot = nextThread.getSnapshot();
		unsubscribeThread = nextThread.subscribe((nextSnapshot) => {
			snapshot = nextSnapshot;
		});

		const nextBranches = createThreadBranchNavigationState({
			client,
			sessionId: activeSessionId,
			threadId,
			onSelected: async ({ threadId: selectedThreadId }) => {
				await refreshThreads();
				await openThread(selectedThreadId);
			}
		});
		branchNavigation = nextBranches;
		branchSnapshot = nextBranches.getSnapshot();
		unsubscribeBranches = nextBranches.subscribe((nextSnapshot) => {
			branchSnapshot = nextSnapshot;
		});

		revisions = createThreadRevisionState({
			client,
			agentId,
			sessionId: activeSessionId,
			threadId,
			onRevisionCreated: async (result) => {
				statusMessage = `Created fork ${result.threadId}.`;
				await refreshSessions();
				await refreshThreads();
				await openThread(result.threadId);
			},
			onError: (error) => {
				loadError = error.message;
			}
		});

		try {
			await nextThread.start();
			await nextBranches.load(threadId);
			await refreshThreads();
			statusMessage = 'Ready.';
		} catch (error) {
			loadError = normalizeError(error).message;
		}
	}

	async function refreshActive(): Promise<void> {
		if (!thread || !branchNavigation) return;
		await thread.rehydrate();
		await branchNavigation.refresh();
		await refreshSessions();
		await refreshThreads();
		statusMessage = 'Refreshed.';
	}

	function startEdit(message: ThreadMessage): void {
		editingMessageId = message.id;
		editingDraft = message.content;
	}

	function cancelEdit(): void {
		editingMessageId = null;
		editingDraft = '';
	}

	async function saveEdit(message: ThreadMessage): Promise<void> {
		if (!revisions) return;
		const replacement = editingDraft.trim();
		if (!replacement) return;
		await revisions.forkAndEditMessage(message.id, replacement, {
			runConfig,
			fork: ({ sentText }) => ({ name: `Edit ${shortText(sentText)}` })
		});
		cancelEdit();
	}

	async function retryMessage(message: ThreadMessage): Promise<void> {
		if (!revisions) return;
		await revisions.forkAndRetryMessage(message.id, {
			runConfig,
			fork: ({ sentText }) => ({ name: `Retry ${shortText(sentText)}` })
		});
	}

	async function selectBranch(control: ThreadBranchChoiceControl, threadId: string): Promise<void> {
		await branchNavigation?.selectForkGroupMember(control.groupId, threadId);
	}

	function controlsFor(item: ThreadTimelineItem): ThreadBranchChoiceControl[] {
		return branchControlsByTimelineItem.get(item.id) ?? [];
	}

	function normalizeError(error: unknown): Error {
		return error instanceof Error ? error : new Error(String(error));
	}

	function shortText(value: string): string {
		const compact = value.replace(/\s+/g, ' ').trim();
		return compact.length <= 16 ? compact : compact.slice(0, 16);
	}

	function threadLabel(item: Thread): string {
		return item.name || item.id;
	}

	function sessionLabel(item: Session): string {
		return String(item.metadata?.name ?? item.id);
	}

	function formatDate(value?: string): string {
		if (!value) return '';
		return new Date(value).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
	}

	function renderText(value: string): string {
		return escapeHtml(value)
			.replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>')
			.replace(/\n/g, '<br>');
	}

	function escapeHtml(value: string): string {
		return value
			.replace(/&/g, '&amp;')
			.replace(/</g, '&lt;')
			.replace(/>/g, '&gt;')
			.replace(/"/g, '&quot;')
			.replace(/'/g, '&#039;');
	}

	function formatUnknown(value: unknown): string {
		if (value === undefined || value === null) return '';
		if (typeof value === 'string') return value;
		try {
			return JSON.stringify(value, null, 2);
		} catch {
			return String(value);
		}
	}

	function runtimeRequestTitle(item: RuntimeRequest): string {
		if (item.kind === 'permission') return item.request.functionName;
		if (item.kind === 'clarification') return item.request.question;
		if (item.kind === 'client-tool') return item.request.toolName;
		return item.requestEventType;
	}
</script>

<svelte:head>
	<title>HPD-OS Branch Lab</title>
</svelte:head>

<div class="shell">
	<aside class="sidebar">
		<div class="brand">
			<span>HPD-OS</span>
			<h1>Branch Lab</h1>
			<p>Current headless thread primitives, no archive branch actions.</p>
		</div>

		<div class="stack">
			<button type="button" class="wide" onclick={createNewSession}>New session</button>
			<button type="button" class="wide" onclick={refreshActive}>Refresh</button>
		</div>

		<section class="panel">
			<h2>Sessions</h2>
			{#if sessions.length === 0}
				<p class="muted">No sessions yet.</p>
			{:else}
				<div class="list">
					{#each sessions as session (session.id)}
						<button
							type="button"
							class:active={session.id === activeSessionId}
							onclick={() => openSession(session.id, 'main')}
						>
							<strong>{sessionLabel(session)}</strong>
							<small>{session.id}</small>
						</button>
					{/each}
				</div>
			{/if}
		</section>

		<section class="panel">
			<h2>Threads</h2>
			{#if threads.length === 0}
				<p class="muted">No threads loaded.</p>
			{:else}
				<div class="list">
					{#each threads as item (item.id)}
						<button
							type="button"
							class:active={item.id === activeThreadId}
							onclick={() => openThread(item.id)}
						>
							<strong>{threadLabel(item)}</strong>
							<small>{item.id}</small>
						</button>
					{/each}
				</div>
			{/if}
		</section>
	</aside>

	<main class="main">
		<header class="topbar">
			<div>
				<h2>{activeThreadId || 'No thread'}</h2>
				<p>{activeSessionId || 'No session'}</p>
			</div>
			{#if thread}
				<ThreadStatus {thread} class="status-pill" />
			{/if}
		</header>

		<div class="notice" data-error={loadError ? 'true' : undefined}>
			{loadError ?? statusMessage}
		</div>

		<section class="timeline">
			{#if !thread || !snapshot}
				<div class="empty">Loading thread.</div>
			{:else if timeline.length === 0}
				<div class="empty">Send a message to start this thread.</div>
			{:else}
				{#each timeline as item (item.id)}
					{#if item.type === 'message'}
						{@const message = item.message}
						{#if editingMessageId === message.id}
							<article class="message-card editing" data-role={message.role}>
								<header>
									<strong>{message.role}</strong>
									<small>{formatDate(message.timestamp.toISOString())}</small>
								</header>
								<textarea
									class="edit-input"
									value={editingDraft}
									oninput={(event) => {
										editingDraft = event.currentTarget.value;
									}}
								></textarea>
								<div class="message-actions">
									<button type="button" onclick={cancelEdit}>Cancel</button>
									<button type="button" class="primary" onclick={() => saveEdit(message)}>Fork edit</button>
								</div>
							</article>
						{:else}
							<Message
								{message}
								showActions
								class="message-card"
								onEditRequest={() => startEdit(message)}
								onRetryRequest={() => retryMessage(message)}
							>
								{#snippet children({ message: renderedMessage })}
									<header>
										<strong>{renderedMessage.role}</strong>
										<small>{formatDate(renderedMessage.timestamp.toISOString())}</small>
									</header>
									{#if renderedMessage.reasoning}
										<details class="reasoning">
											<summary>Reasoning</summary>
											<p>{renderedMessage.reasoning}</p>
										</details>
									{/if}
									<div class="message-content">
										{@html renderText(renderedMessage.content)}
										{#if renderedMessage.streaming}<span class="cursor">|</span>{/if}
									</div>
									{#if renderedMessage.toolCalls.length > 0}
										<div class="tools">
											{#each renderedMessage.toolCalls as tool (tool.callId)}
												<details class="tool">
													<summary>{tool.name} <span>{tool.status}</span></summary>
													{#if tool.args}<pre>{formatUnknown(tool.args)}</pre>{/if}
													{#if tool.resultText}<pre>{tool.resultText}</pre>{/if}
													{#if tool.error}<pre>{tool.error}</pre>{/if}
												</details>
											{/each}
										</div>
									{/if}
								{/snippet}
								{#snippet actions({ message: renderedMessage, actions })}
									<div class="message-actions">
										<button type="button" onclick={actions.copy}>Copy</button>
										{#if renderedMessage.role === 'user'}
											<button type="button" onclick={() => startEdit(renderedMessage)}>Edit</button>
										{/if}
										<button type="button" onclick={() => retryMessage(renderedMessage)}>Retry</button>
									</div>
								{/snippet}
							</Message>
						{/if}
					{:else if item.type === 'work'}
						<ThreadWorkGroup work={item.work} class="work-card" />
					{:else if item.type === 'runtime-request'}
						<RuntimeRequestView item={item.request} {thread} class="request-card" />
					{:else if item.type === 'warning'}
						<div class="warning-card">{item.message}</div>
					{:else}
						<div class="progress-card">{item.label}</div>
					{/if}

					{#each controlsFor(item) as control (control.groupId)}
						<ThreadBranchSwitcher
							{control}
							class="branch-switcher"
							onSelect={({ threadId }) => selectBranch(control, threadId)}
						/>
					{/each}
				{/each}
			{/if}
		</section>

		{#if thread}
			<footer class="composer">
				{#if attachments}
					{@const activeAttachments = attachments}
					<FileAttachment
						state={activeAttachments}
						accept="image/*,audio/*,video/*,.pdf,.txt,.md,.json,.csv"
						class="file-attachment"
					>
						{#snippet children({ actions, attachments, canSubmit, isUploading, props })}
							<div class="attachment-row">
								<button {...props.trigger} onclick={actions.open}>
									{isUploading ? 'Uploading...' : 'Attach file'}
								</button>
								{#if attachments.length > 0}
									<div class="attachment-list">
										{#each attachments as item (item.id)}
											<span data-status={item.status}>
												{item.file.name}
												<small>{item.status}</small>
												{#if item.status === 'error'}
													<button type="button" onclick={() => actions.retry(item.id)}>Retry</button>
												{/if}
												<button type="button" onclick={() => actions.remove(item.id)}>Remove</button>
											</span>
										{/each}
									</div>
								{/if}
								{#if !canSubmit && attachments.length > 0}
									<small>Wait for uploads to finish before sending.</small>
								{/if}
							</div>
						{/snippet}
					</FileAttachment>
				{/if}
				<ThreadComposer
					{thread}
					{runConfig}
					attachments={attachments ?? undefined}
					placeholder="Message HPD-OS..."
				/>
			</footer>
		{/if}
	</main>

	<aside class="inspector">
		<section class="panel">
			<h2>Fork Groups</h2>
			{#if !branchSnapshot || branchSnapshot.forkGroups.length === 0}
				<p class="muted">No forks yet.</p>
			{:else}
				<div class="fork-groups">
					{#each branchSnapshot.forkGroups as group (group.id)}
						<div class="fork-group">
							<small>{group.id}</small>
							{#each group.members as member (member.threadId)}
								<button
									type="button"
									class:active={member.threadId === activeThreadId}
									onclick={() => branchNavigation?.selectForkGroupMember(group.id, member.threadId)}
								>
									{member.index + 1}/{group.members.length} {member.name}
								</button>
							{/each}
						</div>
					{/each}
				</div>
			{/if}
		</section>

		<section class="panel snapshot">
			<h2>Snapshot</h2>
			<dl>
				<div><dt>Messages</dt><dd>{snapshot?.transcriptMessages.length ?? 0}</dd></div>
				<div><dt>Timeline</dt><dd>{snapshot?.timeline.length ?? 0}</dd></div>
				<div><dt>Fork groups</dt><dd>{branchSnapshot?.forkGroups.length ?? 0}</dd></div>
				<div><dt>Controls</dt><dd>{branchControls.length}</dd></div>
			</dl>
		</section>
	</aside>
</div>

<style>
	:global(body) {
		margin: 0;
		background: #08090b;
		color: #f6f4ef;
		font-family:
			Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
	}

	button,
	textarea {
		font: inherit;
	}

	button {
		border: 1px solid #30323b;
		background: #17181f;
		color: #f6f4ef;
		border-radius: 8px;
		padding: 0.58rem 0.8rem;
		cursor: pointer;
	}

	button:hover,
	button.active {
		border-color: #f2b566;
		color: #ffdba8;
	}

	button.primary {
		background: #f2b566;
		color: #1d1308;
		border-color: #f2b566;
	}

	.shell {
		display: grid;
		grid-template-columns: 21rem minmax(0, 1fr) 25rem;
		min-height: 100vh;
	}

	.sidebar,
	.inspector {
		border-right: 1px solid #292b34;
		padding: 1.4rem;
		overflow: auto;
	}

	.inspector {
		border-right: 0;
		border-left: 1px solid #292b34;
	}

	.brand span {
		color: #f2b566;
		font-weight: 700;
	}

	.brand h1,
	.topbar h2 {
		margin: 0.2rem 0 0;
	}

	.brand p,
	.topbar p,
	.muted,
	small {
		color: #a5a9b7;
	}

	.stack,
	.list,
	.fork-groups {
		display: grid;
		gap: 0.7rem;
	}

	.wide,
	.list button,
	.fork-group button {
		width: 100%;
		text-align: left;
	}

	.list button,
	.fork-group button {
		display: grid;
		gap: 0.2rem;
	}

	.panel {
		margin-top: 1.5rem;
		border-top: 1px solid #292b34;
		padding-top: 1.2rem;
	}

	.panel h2 {
		font-size: 0.82rem;
		text-transform: uppercase;
		color: #a5a9b7;
		letter-spacing: 0;
	}

	.main {
		display: grid;
		grid-template-rows: auto auto minmax(0, 1fr) auto;
		min-width: 0;
	}

	.topbar {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 1.2rem 1.6rem;
		border-bottom: 1px solid #292b34;
	}

	:global(.status-pill) {
		border: 1px solid #30323b;
		border-radius: 999px;
		padding: 0.45rem 0.85rem;
		background: #14151b;
	}

	.notice {
		padding: 0.75rem 1.6rem;
		border-bottom: 1px solid #292b34;
		color: #26e0a1;
	}

	.notice[data-error='true'] {
		color: #ff8f8f;
	}

	.timeline {
		overflow: auto;
		padding: 2rem min(6vw, 5rem);
	}

	.empty {
		color: #a5a9b7;
		border: 1px dashed #30323b;
		border-radius: 8px;
		padding: 2rem;
		text-align: center;
	}

	.message-card,
	:global(.work-card),
	:global(.request-card),
	.warning-card,
	.progress-card {
		max-width: 58rem;
		margin: 0 auto 1rem;
		border: 1px solid #30323b;
		border-radius: 8px;
		background: #121318;
		padding: 1rem;
	}

	.message-card header {
		display: flex;
		justify-content: space-between;
		margin-bottom: 0.75rem;
		color: #a5a9b7;
		text-transform: capitalize;
	}

	.message-content {
		line-height: 1.6;
	}

	.reasoning,
	.tool {
		margin-bottom: 0.75rem;
		color: #d6d8df;
	}

	.tools {
		display: grid;
		gap: 0.6rem;
		margin-top: 1rem;
	}

	pre {
		white-space: pre-wrap;
		background: #090a0d;
		border-radius: 6px;
		padding: 0.75rem;
		overflow: auto;
	}

	.message-actions {
		display: flex;
		gap: 0.55rem;
		flex-wrap: wrap;
		margin-top: 1rem;
	}

	.edit-input {
		box-sizing: border-box;
		width: 100%;
		min-height: 7rem;
		border: 1px solid #30323b;
		border-radius: 8px;
		background: #090a0d;
		color: #f6f4ef;
		padding: 0.8rem;
		resize: vertical;
	}

	:global(.branch-switcher) {
		max-width: 58rem;
		margin: -0.35rem auto 1.2rem;
		display: flex;
		align-items: center;
		gap: 0.55rem;
		color: #ffdba8;
	}

	:global(.branch-switcher span) {
		font-weight: 700;
	}

	.composer {
		border-top: 1px solid #292b34;
		padding: 1rem 1.6rem;
	}

	.attachment-row {
		display: grid;
		gap: 0.65rem;
		max-width: 58rem;
		margin: 0 auto 0.75rem;
	}

	.attachment-row > button {
		justify-self: start;
	}

	.attachment-list {
		display: flex;
		flex-wrap: wrap;
		gap: 0.5rem;
	}

	.attachment-list span {
		display: inline-flex;
		align-items: center;
		gap: 0.45rem;
		border: 1px solid #30323b;
		border-radius: 8px;
		background: #121318;
		color: #f6f4ef;
		padding: 0.45rem 0.55rem;
	}

	.attachment-list span[data-status='error'] {
		border-color: #884646;
		color: #ffb4b4;
	}

	.attachment-list small,
	.attachment-row > small {
		color: #a5a9b7;
	}

	.composer :global(form) {
		display: grid;
		grid-template-columns: minmax(0, 1fr) auto;
		gap: 0.75rem;
		max-width: 58rem;
		margin: 0 auto;
	}

	.composer :global(textarea) {
		min-height: 3rem;
		max-height: 12rem;
		border: 1px solid #30323b;
		border-radius: 8px;
		background: #121318;
		color: #f6f4ef;
		padding: 0.8rem;
		resize: vertical;
	}

	.fork-group {
		display: grid;
		gap: 0.5rem;
		border: 1px solid #292b34;
		border-radius: 8px;
		padding: 0.8rem;
	}

	.snapshot dl {
		display: grid;
		gap: 0.65rem;
	}

	.snapshot div {
		display: flex;
		justify-content: space-between;
	}

	.snapshot dt {
		color: #a5a9b7;
	}

	.snapshot dd {
		margin: 0;
		font-weight: 800;
	}

	.cursor {
		color: #f2b566;
	}
</style>
