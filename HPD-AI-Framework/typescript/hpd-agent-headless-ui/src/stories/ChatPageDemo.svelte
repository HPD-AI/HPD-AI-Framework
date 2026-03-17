<script lang="ts">
	import { boxWith } from 'svelte-toolbelt';
	import {
		createMockWorkspace,
		RunConfig,
		RunConfigState,
		FileAttachment,
		FileAttachmentState,
		Message,
		MessageList,
		MessageActions,
		MessageEdit,
		ToolExecution,
		Artifact,
		PermissionDialog,
		SessionList,
		BranchSwitcher,
		ChatInput,
	} from '$lib/index.js';
	import type { AssetReference } from '$lib/index.js';

	// ── Props ────────────────────────────────────────────────────────────────
	let {
		enableReasoning     = false,
		typingDelay         = 25,
		showSidebar         = true,
		showRunConfig       = false,
		initialSessionCount = 2,
	} = $props();

	// ── Workspace ────────────────────────────────────────────────────────────
	// svelte-ignore state_referenced_locally
	const workspace = createMockWorkspace({
		typingDelay,
		enableReasoning,
		initialSessionCount,
		responses: [
			'Paris is the capital of France. It sits on the Seine river and has been the cultural heart of Europe for centuries.',
			'That\'s a great question. Let me think through it carefully.\n\nThe answer depends on context, but generally speaking the most pragmatic approach wins.',
			'Here is a simple TypeScript example:\n\n```ts\nfunction greet(name: string): string {\n  return `Hello, ${name}!`;\n}\n```\n\nYou can call it with `greet("world")`.',
			'I understand. Would you like me to elaborate on any specific aspect?',
			'Absolutely — the key insight is that headless components separate *behaviour* from *appearance*, giving you full styling control without sacrificing functionality.',
		],
	});

	// ── RunConfig ────────────────────────────────────────────────────────────
	const runConfig = new RunConfigState();
	const providers = [
		{
			key: 'anthropic',
			label: 'Anthropic',
			models: [
				{ id: 'claude-opus-4-6',           label: 'Claude Opus 4.6'   },
				{ id: 'claude-sonnet-4-6',          label: 'Claude Sonnet 4.6' },
				{ id: 'claude-haiku-4-5-20251001',  label: 'Claude Haiku 4.5'  },
			],
		},
		{
			key: 'openai',
			label: 'OpenAI',
			models: [
				{ id: 'gpt-4o',      label: 'GPT-4o'      },
				{ id: 'gpt-4o-mini', label: 'GPT-4o Mini' },
			],
		},
	];
	const permissions = ['read_file', 'write_file', 'execute_command'];

	// ── FileAttachment ───────────────────────────────────────────────────────
	const activeSessionId = $derived(workspace.activeSessionId);
	const isStreaming     = $derived(workspace.state?.streaming ?? false);

	const attachments = new FileAttachmentState({
		uploadFn:  boxWith(() => mockUpload),
		sessionId: boxWith(() => activeSessionId),
		disabled:  boxWith(() => isStreaming),
	});

	async function mockUpload(_sid: string, file: File): Promise<AssetReference> {
		await new Promise(r => setTimeout(r, 600));
		return { assetId: `asset-${Date.now()}`, contentType: file.type || 'application/octet-stream', name: file.name, sizeBytes: file.size };
	}

	let fileInput: HTMLInputElement | undefined = $state();

	// ── UI state ─────────────────────────────────────────────────────────────
	// svelte-ignore state_referenced_locally
	let configOpen   = $state(showRunConfig);
	let editingIndex = $state<number | null>(null);

	// ── Derived ──────────────────────────────────────────────────────────────
	const messages   = $derived(workspace.state?.messages ?? []);
	const canSend    = $derived(!isStreaming && attachments.canSubmit);
	const activeBranch = $derived(workspace.activeBranch);

	// ── Send ─────────────────────────────────────────────────────────────────
	function handleSend(value: string) {
		if (!value.trim() || !canSend) return;
		workspace.send(value, { runConfig: runConfig.value ?? undefined });
		attachments.clear();
	}
</script>

<!-- Hidden file input -->
<input
	bind:this={fileInput}
	type="file"
	multiple
	style="display:none"
	onchange={(e) => {
		const files = e.currentTarget.files;
		if (files?.length) attachments.add(files);
		e.currentTarget.value = '';
	}}
/>

<!-- ═══════════════════════════════════════════════════════════════════════ -->
<!-- Root: Artifact.Provider wraps everything so panels can teleport        -->
<!-- ═══════════════════════════════════════════════════════════════════════ -->
<Artifact.Provider>
	<div class="shell">

		<!-- ── Top bar ──────────────────────────────────────────────────── -->
		<header class="topbar">
			<span class="topbar-logo">HPD Agent</span>
			<button
				class="icon-btn"
				class:active={configOpen}
				onclick={() => (configOpen = !configOpen)}
				aria-label="Toggle run config"
				title="Run Config"
			>
				⚙
			</button>
		</header>

		<!-- ── Body ─────────────────────────────────────────────────────── -->
		<div class="body">

			<!-- Sidebar: SessionList -->
			{#if showSidebar}
			<aside class="sidebar">
				<SessionList.Root
					sessions={workspace.sessions}
					activeSessionId={workspace.activeSessionId}
					onSelect={(id) => workspace.selectSession(id)}
				>
					{#snippet children(s)}
						{#if s.isEmpty}
							<SessionList.Empty>
								{#snippet children()}
									<p class="sidebar-empty">No sessions</p>
								{/snippet}
							</SessionList.Empty>
						{:else}
							{#each workspace.sessions as session (session.id)}
								<SessionList.Item {session}>
									{#snippet children(item)}
										<button
											class="session-item"
											class:active={item.isActive}
											onclick={() => workspace.selectSession(session.id)}
										>
											<span class="session-dot" class:active={item.isActive}></span>
											<span class="session-label">
												{session.id.length > 18 ? session.id.slice(0, 18) + '…' : session.id}
											</span>
										</button>
									{/snippet}
								</SessionList.Item>
							{/each}
						{/if}

						<SessionList.CreateButton>
							{#snippet children()}
								<button class="new-session-btn" onclick={() => workspace.createSession()}>
									+ New session
								</button>
							{/snippet}
						</SessionList.CreateButton>
					{/snippet}
				</SessionList.Root>
			</aside>
			{/if}

			<!-- Center: messages + input -->
			<main class="center">

				<!-- Message list -->
				<div class="messages-wrap">
					<MessageList.Root {messages}>
						{#snippet children()}
							{#each messages as message, i (message.id)}

								<Message {message}>
									{#snippet children(ms)}
										<div class="msg" data-role={message.role}>

											<!-- Header -->
											<div class="msg-header">
												<span class="msg-role">
													{message.role === 'user' ? 'You' : 'Assistant'}
												</span>
												<span class="msg-time">
													{message.timestamp.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
												</span>
											</div>

											<!-- Thinking -->
											{#if ms.thinking}
												<div class="msg-thinking">
													<span class="thinking-dot"></span>
													<span class="thinking-dot"></span>
													<span class="thinking-dot"></span>
												</div>
											{/if}

											<!-- Reasoning -->
											{#if ms.hasReasoning}
												<details class="msg-reasoning">
													<summary>Reasoning</summary>
													<p>{ms.reasoning}</p>
												</details>
											{/if}

											<!-- Tool calls -->
											{#if ms.toolCalls.length}
												<div class="tool-list">
													{#each ms.toolCalls as tc (tc.callId)}
														<ToolExecution.Root toolCall={tc}>
															{#snippet children(te)}
																<div class="tool-item" data-tool-status={te.status}>
																	<ToolExecution.Trigger>
																		{#snippet children(trig)}
																			<button class="tool-trigger" onclick={() => {}}>
																				<span class="tool-status-dot" data-status={te.status}></span>
																				<span class="tool-name">{te.name}</span>
																				{#if te.duration}
																					<span class="tool-dur">{te.duration.toFixed(1)}s</span>
																				{/if}
																				<span class="tool-chevron">{trig.expanded ? '▾' : '▸'}</span>
																			</button>
																		{/snippet}
																	</ToolExecution.Trigger>
																	{#if te.expanded}
																		<ToolExecution.Content>
																			{#snippet children(tc2)}
																				<div class="tool-body">
																					{#if tc2.hasArgs}
																						<ToolExecution.Args>
																							{#snippet children(a)}
																								<pre class="tool-code">{a.argsJson}</pre>
																							{/snippet}
																						</ToolExecution.Args>
																					{/if}
																					{#if tc2.hasResult || tc2.hasError}
																						<ToolExecution.Result>
																							{#snippet children(r)}
																								<pre class="tool-code" class:error={r.hasError}>{r.result ?? r.error}</pre>
																							{/snippet}
																						</ToolExecution.Result>
																					{/if}
																				</div>
																			{/snippet}
																		</ToolExecution.Content>
																	{/if}
																</div>
															{/snippet}
														</ToolExecution.Root>
													{/each}
												</div>
											{/if}

											<!-- Content -->
											{#if ms.content || !ms.thinking}
												<p class="msg-content">
													{ms.content}{#if ms.streaming}<span class="cursor">▊</span>{/if}
												</p>
											{/if}

											<!-- Artifact triggers (mock — shown for assistant) -->
											{#if message.role === 'assistant' && i === 2 && messages.length > 3}
												<Artifact.Root id="artifact-{i}">
													{#snippet children(art)}
														<div class="artifact-chip">
															<span class="artifact-icon">📄</span>
															<span class="artifact-name">Button.tsx</span>
															<Artifact.Trigger>
																{#snippet children(trig)}
																	<button class="artifact-open-btn" class:open={trig.open}>
																		{trig.open ? 'Close' : 'Open →'}
																	</button>
																{/snippet}
															</Artifact.Trigger>
														</div>
														<Artifact.Slot>
															{#snippet title()}<span>Button.tsx</span>{/snippet}
															{#snippet content()}
																<pre class="artifact-code">{'function Button({ children, onClick }) {\n  return (\n    <button onClick={onClick}>\n      {children}\n    </button>\n  );\n}'}</pre>
															{/snippet}
														</Artifact.Slot>
													{/snippet}
												</Artifact.Root>
											{/if}

										</div><!-- /msg -->
									{/snippet}
								</Message>

								<!-- Actions row (below bubble) -->
								<MessageActions.Root
									{workspace}
									messageIndex={i}
									role={message.role}
									branch={activeBranch}
								>
									{#snippet children(act)}
										<div class="msg-actions" data-role={message.role}>
											<!-- Edit (all roles) -->
											{#if editingIndex !== i}
												<MessageActions.EditButton
													onSuccess={() => (editingIndex = null)}
													onError={() => {}}
												>
													{#snippet children(eb)}
														<button
															class="action-btn"
															disabled={eb.disabled}
															onclick={() => (editingIndex = i)}
															title="Edit"
														>✎</button>
													{/snippet}
												</MessageActions.EditButton>

												<!-- Retry (all roles) -->
												<MessageActions.RetryButton>
													{#snippet children(rb)}
														<button
															class="action-btn"
															disabled={rb.disabled}
															onclick={rb.retry}
															title="Retry"
														>↺</button>
													{/snippet}
												</MessageActions.RetryButton>

												<!-- Copy (all roles) -->
												<MessageActions.CopyButton content={message.content}>
													{#snippet children(cb)}
														<button
															class="action-btn"
															class:copied={cb.copied}
															onclick={cb.copy}
															title="Copy"
														>{cb.copied ? '✓' : '⎘'}</button>
													{/snippet}
												</MessageActions.CopyButton>

												<!-- Branch nav — user only, when siblings exist -->
												{#if message.role === 'user' && act.hasSiblings}
													<span class="action-sep"></span>
													<MessageActions.Prev>
														{#snippet children()}
															<button class="action-btn nav" onclick={() => workspace.goToPreviousSibling()} disabled={!workspace.canGoPrevious}>‹</button>
														{/snippet}
													</MessageActions.Prev>
													<MessageActions.Position>
														{#snippet children(pos)}
															<span class="action-pos">{pos.position}</span>
														{/snippet}
													</MessageActions.Position>
													<MessageActions.Next>
														{#snippet children()}
															<button class="action-btn nav" onclick={() => workspace.goToNextSibling()} disabled={!workspace.canGoNext}>›</button>
														{/snippet}
													</MessageActions.Next>
												{/if}
											{/if}
										</div>

										<!-- Inline edit form -->
										{#if editingIndex === i}
											<MessageEdit.Root
												{workspace}
												messageIndex={i}
												initialValue={message.content}
												editing={true}
												onSave={() => (editingIndex = null)}
												onCancel={() => (editingIndex = null)}
											>
												{#snippet children(ed: any)}
													<div class="edit-form">
														{@html `<div data-message-edit-textarea>`}
														<textarea
															class="edit-textarea"
															value={ed.value ?? ''}
															placeholder="Edit message..."
															disabled={false}
															oninput={(e) => (ed.value = e.currentTarget.value)}
														></textarea>
														{@html `</div>`}
														<div class="edit-actions">
															<MessageEdit.CancelButton>
																{#snippet children(cb)}
																	<button class="edit-btn cancel" onclick={cb.cancel}>Cancel</button>
																{/snippet}
															</MessageEdit.CancelButton>
															<MessageEdit.SaveButton>
																{#snippet children(sb)}
																	<button class="edit-btn save" disabled={sb.disabled} onclick={sb.save}>
																		{sb.pending ? 'Saving…' : 'Save & Submit'}
																	</button>
																{/snippet}
															</MessageEdit.SaveButton>
														</div>
													</div>
												{/snippet}
											</MessageEdit.Root>
										{/if}
									{/snippet}
								</MessageActions.Root>

							{/each}

							<!-- Streaming placeholder -->
							{#if isStreaming && messages.length === 0}
								<div class="msg" data-role="assistant">
									<div class="msg-thinking">
										<span class="thinking-dot"></span>
										<span class="thinking-dot"></span>
										<span class="thinking-dot"></span>
									</div>
								</div>
							{/if}
						{/snippet}
					</MessageList.Root>
				</div>

				<!-- ── Input area ──────────────────────────────────────────── -->
				<div class="input-area">

					<!-- Attachment chips -->
					{#if attachments.hasAttachments}
						<FileAttachment.Root state={attachments}>
							{#snippet children(fa)}
								<div class="attachment-chips">
									{#each fa.attachments as att (att.localId)}
										<span class="chip" data-status={att.status}>
											<span class="chip-icon">
												{att.status === 'done' ? '✓' : att.status === 'error' ? '✕' : '⏳'}
											</span>
											<span class="chip-name">{att.file.name}</span>
											{#if att.status === 'error'}
												<button class="chip-action" onclick={() => fa.retry(att.localId)} title="Retry">↺</button>
											{/if}
											<button class="chip-action" onclick={() => fa.remove(att.localId)} title="Remove">×</button>
										</span>
									{/each}
									<button class="chip-clear" onclick={fa.clear}>Clear all</button>
								</div>
							{/snippet}
						</FileAttachment.Root>
					{/if}

					<!-- ChatInput -->
					<ChatInput.Root
						disabled={!canSend}
						onSubmit={(d) => handleSend(d.value)}
					>
						{#snippet children()}
							<!-- Top zone: empty -->

							<!-- Main textarea -->
							<div class="input-row">
								<ChatInput.Leading>
									{#snippet children()}
										<button
											class="icon-btn sm"
											onclick={() => fileInput?.click()}
											disabled={isStreaming}
											title="Attach file"
										>📎</button>
									{/snippet}
								</ChatInput.Leading>

								<ChatInput.Input placeholder="Type a message…" />

								<ChatInput.Trailing>
									{#snippet children(trail)}
										<button
											class="send-btn"
											disabled={!trail.canSubmit}
											onclick={trail.submit}
											title="Send"
										>
											{#if isStreaming}
												<span class="send-spinner"></span>
											{:else}
												↵
											{/if}
										</button>
									{/snippet}
								</ChatInput.Trailing>
							</div>

							<!-- Bottom zone: model + temp hint -->
							<ChatInput.Bottom>
								{#snippet children()}
									<div class="input-bottom">
										<span class="model-hint">
											{runConfig.providerKey && runConfig.modelId
												? `${runConfig.modelId}`
												: 'default model'}
										</span>
										{#if runConfig.temperature !== undefined}
											<span class="temp-hint">🌡 {runConfig.temperature.toFixed(2)}</span>
										{/if}
										{#if isStreaming}
											<button class="abort-btn" onclick={() => workspace.abort()}>Stop</button>
										{/if}
									</div>
								{/snippet}
							</ChatInput.Bottom>
						{/snippet}
					</ChatInput.Root>
				</div>
			</main>

			<!-- ── Run Config drawer ──────────────────────────────────────── -->
			{#if configOpen}
				<aside class="config-drawer">
					<div class="drawer-header">
						<span class="drawer-title">Run Config</span>
						<button class="icon-btn" onclick={() => (configOpen = false)}>×</button>
					</div>

					<div class="drawer-body">
						<div class="drawer-field">
							<!-- svelte-ignore a11y_label_has_associated_control -->
							<label class="drawer-label">Model</label>
							<RunConfig.ModelSelector {runConfig} {providers}>
								{#snippet children(ms)}
									<select
										class="drawer-select"
										onchange={(e) => {
											const [pk, mid] = e.currentTarget.value.split('::');
											ms.setModel(pk || undefined, mid || undefined);
										}}
									>
										<option value="">— default —</option>
										{#each ms.providers as prov}
											<optgroup label={prov.label}>
												{#each prov.models as model}
													<option
														value="{prov.key}::{model.id}"
														selected={ms.providerKey === prov.key && ms.modelId === model.id}
													>{model.label}</option>
												{/each}
											</optgroup>
										{/each}
									</select>
								{/snippet}
							</RunConfig.ModelSelector>
						</div>

						<div class="drawer-field">
							<!-- svelte-ignore a11y_label_has_associated_control -->
							<label class="drawer-label">
								Temperature
								{#if runConfig.temperature !== undefined}
									<span class="drawer-badge">{runConfig.temperature.toFixed(2)}</span>
								{:else}
									<span class="drawer-badge muted">default</span>
								{/if}
							</label>
							<RunConfig.TemperatureSlider {runConfig}>
								{#snippet children(ts)}
									<div class="slider-wrap">
										<input
											type="range" class="drawer-slider"
											min={ts.min} max={ts.max} step={ts.step}
											value={ts.value ?? 0.7}
											oninput={(e) => ts.setValue(+e.currentTarget.value)}
										/>
										{#if ts.value !== undefined}
											<button class="clear-x" onclick={() => ts.setValue(undefined)}>×</button>
										{/if}
									</div>
								{/snippet}
							</RunConfig.TemperatureSlider>
						</div>

						<div class="drawer-field">
							<!-- svelte-ignore a11y_label_has_associated_control -->
							<label class="drawer-label">
								Top-P
								{#if runConfig.topP !== undefined}
									<span class="drawer-badge">{runConfig.topP.toFixed(2)}</span>
								{:else}
									<span class="drawer-badge muted">default</span>
								{/if}
							</label>
							<RunConfig.TopPSlider {runConfig}>
								{#snippet children(ts)}
									<div class="slider-wrap">
										<input
											type="range" class="drawer-slider"
											min={ts.min} max={ts.max} step={ts.step}
											value={ts.value ?? 1}
											oninput={(e) => ts.setValue(+e.currentTarget.value)}
										/>
										{#if ts.value !== undefined}
											<button class="clear-x" onclick={() => ts.setValue(undefined)}>×</button>
										{/if}
									</div>
								{/snippet}
							</RunConfig.TopPSlider>
						</div>

						<div class="drawer-field">
							<!-- svelte-ignore a11y_label_has_associated_control -->
							<label class="drawer-label">Max Tokens</label>
							<RunConfig.MaxTokensInput {runConfig}>
								{#snippet children(mt)}
									<div class="input-wrap">
										<input
											type="number" class="drawer-input"
											min={mt.min}
											value={mt.value ?? ''}
											placeholder="default"
											oninput={(e) => {
												const n = parseInt(e.currentTarget.value, 10);
												mt.setValue(isNaN(n) ? undefined : n);
											}}
										/>
										{#if mt.value !== undefined}
											<button class="clear-x" onclick={() => mt.setValue(undefined)}>×</button>
										{/if}
									</div>
								{/snippet}
							</RunConfig.MaxTokensInput>
						</div>

						<div class="drawer-field">
							<!-- svelte-ignore a11y_label_has_associated_control -->
							<label class="drawer-label">System Instructions</label>
							<RunConfig.SystemInstructionsInput {runConfig}>
								{#snippet children(si)}
									<textarea
										class="drawer-textarea"
										value={si.value ?? ''}
										placeholder="Additional instructions…"
										oninput={(e) => si.setValue(e.currentTarget.value.trim() || undefined)}
									></textarea>
								{/snippet}
							</RunConfig.SystemInstructionsInput>
						</div>

						<div class="drawer-field">
							<!-- svelte-ignore a11y_label_has_associated_control -->
							<label class="drawer-label">Skip Tools</label>
							<RunConfig.SkipToolsToggle {runConfig}>
								{#snippet children(st)}
									<button
										class="toggle-btn"
										class:on={st.value === true}
										onclick={() => st.setValue(st.value === true ? undefined : true)}
									>
										{st.value === true ? 'Enabled' : st.value === false ? 'Disabled' : 'Default'}
									</button>
								{/snippet}
							</RunConfig.SkipToolsToggle>
						</div>

						<div class="drawer-field">
							<!-- svelte-ignore a11y_label_has_associated_control -->
							<label class="drawer-label">Permissions</label>
							<RunConfig.PermissionOverridesPanel {runConfig} {permissions}>
								{#snippet children(po)}
									<div class="perm-list">
										{#each po.items as item}
											<div class="perm-row">
												<code class="perm-key">{item.key}</code>
												<div class="perm-btns">
													<button class="perm-btn" class:active-allow={item.value === true}
														onclick={() => po.setOverride(item.key, true)}>Allow</button>
													<button class="perm-btn" class:active-deny={item.value === false}
														onclick={() => po.setOverride(item.key, false)}>Deny</button>
													<button class="perm-btn" class:active-def={item.value === undefined}
														onclick={() => po.setOverride(item.key, undefined)}>—</button>
												</div>
											</div>
										{/each}
									</div>
								{/snippet}
							</RunConfig.PermissionOverridesPanel>
						</div>

						<div class="drawer-field">
							<!-- svelte-ignore a11y_label_has_associated_control -->
							<label class="drawer-label">Run Timeout</label>
							<RunConfig.RunTimeoutInput {runConfig}>
								{#snippet children(rt)}
									<div class="input-wrap">
										<input
											type="text" class="drawer-input"
											value={rt.value ?? ''}
											placeholder="e.g. PT5M"
											oninput={(e) => rt.setValue(e.currentTarget.value.trim() || undefined)}
										/>
										{#if rt.value !== undefined}
											<button class="clear-x" onclick={() => rt.setValue(undefined)}>×</button>
										{/if}
									</div>
								{/snippet}
							</RunConfig.RunTimeoutInput>
						</div>

						<div class="drawer-reset">
							<button class="reset-btn" onclick={() => runConfig.reset()}>Reset all</button>
						</div>
					</div>
				</aside>
			{/if}

			<!-- ── Artifact panel ─────────────────────────────────────────── -->
			{#if !configOpen}
				<Artifact.Panel>
					{#snippet children(ap)}
						{#if ap.open}
							<aside class="artifact-panel">
								<div class="drawer-header">
									<Artifact.Title />
									<Artifact.Close>
										{#snippet children()}
											<button class="icon-btn" onclick={ap.close}>×</button>
										{/snippet}
									</Artifact.Close>
								</div>
								<div class="artifact-body">
									<Artifact.Content />
								</div>
							</aside>
						{/if}
					{/snippet}
				</Artifact.Panel>
			{/if}

		</div><!-- /body -->
	</div><!-- /shell -->

	<!-- ── Permission dialog — only mount when state is ready ─────────────── -->
	{#if workspace.state}
		<PermissionDialog.Root agent={workspace}>
			<PermissionDialog.Overlay />
			<PermissionDialog.Content>
				{#snippet children(dlg)}
					{#if dlg.isOpen && dlg.request}
						<div class="dialog-backdrop">
							<div class="dialog">
								<PermissionDialog.Header>
									{#snippet children(hdr)}
										<h2 class="dialog-title">🔒 {hdr.functionName ?? 'Permission Request'}</h2>
									{/snippet}
								</PermissionDialog.Header>
								<PermissionDialog.Description>
									{#snippet children(desc)}
										<p class="dialog-source">{desc.functionName}</p>
										{#if desc.arguments}
											<pre class="dialog-args">{JSON.stringify(desc.arguments, null, 2)}</pre>
										{/if}
									{/snippet}
								</PermissionDialog.Description>
								<PermissionDialog.Actions>
									{#snippet children(act)}
										<div class="dialog-actions">
											<PermissionDialog.Deny>
												<button class="dialog-btn deny" onclick={() => act.deny()}>Deny</button>
											</PermissionDialog.Deny>
											<PermissionDialog.Approve choice="ask">
												<button class="dialog-btn allow-once" onclick={() => act.approve('ask')}>Allow once</button>
											</PermissionDialog.Approve>
											<PermissionDialog.Approve choice="allow_always">
												<button class="dialog-btn allow" onclick={() => act.approve('allow_always')}>Allow always</button>
											</PermissionDialog.Approve>
										</div>
									{/snippet}
								</PermissionDialog.Actions>
							</div>
						</div>
					{/if}
				{/snippet}
			</PermissionDialog.Content>
		</PermissionDialog.Root>
	{/if}

</Artifact.Provider>

<style>
	/* ── Tokens ──────────────────────────────────────────────────────── */
	:root {
		--bg:        #09090b;
		--bg-card:   #18181b;
		--bg-hover:  #27272a;
		--bg-input:  #18181b;
		--border:    #27272a;
		--border-hi: #3f3f46;
		--text:      #fafafa;
		--text-muted:#a1a1aa;
		--text-dim:  #71717a;
		--accent:    #fafafa;
		--accent-bg: #18181b;
		--radius:    6px;
		--radius-lg: 12px;
		--font:      -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
		--font-mono: 'JetBrains Mono', 'Fira Code', monospace;
	}

	/* ── Shell ───────────────────────────────────────────────────────── */
	.shell {
		display: flex;
		flex-direction: column;
		height: 100vh;
		background: var(--bg);
		color: var(--text);
		font-family: var(--font);
		font-size: 0.875rem;
		overflow: hidden;
	}

	/* ── Topbar ──────────────────────────────────────────────────────── */
	.topbar {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 0 1rem;
		height: 44px;
		border-bottom: 1px solid var(--border);
		background: var(--bg);
		flex-shrink: 0;
	}
	.topbar-logo {
		font-weight: 600;
		font-size: 0.9rem;
		color: var(--text);
		letter-spacing: -0.01em;
	}

	/* ── Body ────────────────────────────────────────────────────────── */
	.body {
		display: flex;
		flex: 1;
		overflow: hidden;
	}

	/* ── Sidebar ─────────────────────────────────────────────────────── */
	.sidebar {
		width: 220px;
		flex-shrink: 0;
		display: flex;
		flex-direction: column;
		border-right: 1px solid var(--border);
		background: var(--bg);
		padding: 0.5rem 0;
		overflow-y: auto;
	}
	.sidebar-empty {
		color: var(--text-dim);
		font-size: 0.8rem;
		padding: 0.5rem 0.75rem;
	}
	.session-item {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		width: 100%;
		padding: 0.4rem 0.75rem;
		background: var(--bg);
		border: none;
		color: var(--text-muted);
		cursor: pointer;
		font-size: 0.82rem;
		text-align: left;
		transition: background 0.1s, color 0.1s;
		border-radius: 0;
	}
	.session-item:hover { background: var(--bg-hover); color: var(--text); }
	.session-item.active { color: var(--text); background: var(--bg-hover); }
	.session-dot {
		width: 6px; height: 6px;
		border-radius: 50%;
		background: var(--border-hi);
		flex-shrink: 0;
	}
	.session-dot.active { background: var(--accent); }
	.session-label { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
	.new-session-btn {
		margin: 0.5rem 0.75rem 0;
		padding: 0.35rem 0.6rem;
		border: 1px dashed var(--border-hi);
		border-radius: var(--radius);
		background: var(--bg);
		color: var(--text-dim);
		cursor: pointer;
		font-size: 0.8rem;
		width: calc(100% - 1.5rem);
		text-align: left;
		transition: border-color 0.15s, color 0.15s;
	}
	.new-session-btn:hover { border-color: var(--text-muted); color: var(--text-muted); }

	/* ── Center ──────────────────────────────────────────────────────── */
	.center {
		flex: 1;
		display: flex;
		flex-direction: column;
		overflow: hidden;
		min-width: 0;
	}

	/* ── Messages ────────────────────────────────────────────────────── */
	.messages-wrap {
		flex: 1;
		overflow-y: auto;
		padding: 1.5rem 1.5rem 0.5rem;
		display: flex;
		flex-direction: column;
		gap: 0.25rem;
	}
	:global([data-message-list]) {
		display: flex;
		flex-direction: column;
		gap: 0;
	}

	.msg {
		max-width: 72%;
		padding: 0.65rem 0.85rem;
		border-radius: var(--radius-lg);
		line-height: 1.6;
		font-size: 0.875rem;
	}
	.msg[data-role='user'] {
		align-self: flex-end;
		background: var(--bg-hover);
		border: 1px solid var(--border-hi);
		border-bottom-right-radius: 4px;
	}
	.msg[data-role='assistant'] {
		align-self: flex-start;
		background: var(--bg-card);
		border: 1px solid var(--border);
		border-bottom-left-radius: 4px;
	}
	.msg-header {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		margin-bottom: 0.35rem;
	}
	.msg-role { font-weight: 600; font-size: 0.78rem; color: var(--text-muted); }
	.msg-time { font-size: 0.72rem; color: var(--text-dim); margin-left: auto; }
	.msg-content { margin: 0; white-space: pre-wrap; word-break: break-word; }
	.cursor {
		display: inline-block;
		margin-left: 1px;
		animation: blink 1s step-end infinite;
	}
	@keyframes blink { 0%,100%{opacity:1} 50%{opacity:0} }

	/* Thinking dots */
	.msg-thinking {
		display: flex; gap: 4px; align-items: center; padding: 0.25rem 0;
	}
	.thinking-dot {
		width: 6px; height: 6px; border-radius: 50%;
		background: var(--text-dim);
		animation: pulse 1.2s ease-in-out infinite;
	}
	.thinking-dot:nth-child(2) { animation-delay: 0.2s; }
	.thinking-dot:nth-child(3) { animation-delay: 0.4s; }
	@keyframes pulse { 0%,100%{opacity:0.3} 50%{opacity:1} }

	/* Reasoning */
	.msg-reasoning {
		border-left: 2px solid var(--border-hi);
		padding: 0.4rem 0.65rem;
		margin-bottom: 0.5rem;
		font-size: 0.8rem;
		color: var(--text-dim);
	}
	.msg-reasoning summary { cursor: pointer; color: var(--text-muted); font-weight: 500; }

	/* Tool execution */
	.tool-list { display: flex; flex-direction: column; gap: 0.3rem; margin-bottom: 0.5rem; }
	.tool-item {
		border: 1px solid var(--border);
		border-radius: var(--radius);
		overflow: hidden;
		font-size: 0.8rem;
	}
	.tool-trigger {
		display: flex; align-items: center; gap: 0.4rem;
		width: 100%; padding: 0.35rem 0.6rem;
		background: transparent; border: none;
		color: var(--text-muted); cursor: pointer;
		text-align: left; transition: background 0.1s;
	}
	.tool-trigger:hover { background: var(--bg-hover); }
	.tool-status-dot {
		width: 7px; height: 7px; border-radius: 50%; flex-shrink: 0;
		background: var(--text-dim);
	}
	.tool-status-dot[data-status='executing'] { background: #f59e0b; animation: pulse 1s infinite; }
	.tool-status-dot[data-status='complete']  { background: #10b981; }
	.tool-status-dot[data-status='error']     { background: #ef4444; }
	.tool-name { flex: 1; font-family: var(--font-mono); }
	.tool-dur  { color: var(--text-dim); }
	.tool-chevron { color: var(--text-dim); }
	.tool-body { padding: 0.4rem 0.6rem; border-top: 1px solid var(--border); }
	.tool-code {
		margin: 0; font-size: 0.75rem; font-family: var(--font-mono);
		color: var(--text-muted); white-space: pre-wrap; word-break: break-all;
		background: var(--bg); padding: 0.4rem; border-radius: 4px;
	}
	.tool-code.error { color: #f87171; }

	/* Artifact chip */
	.artifact-chip {
		display: inline-flex; align-items: center; gap: 0.4rem;
		padding: 0.3rem 0.55rem;
		border: 1px solid var(--border-hi);
		border-radius: var(--radius);
		margin-top: 0.5rem;
		font-size: 0.8rem;
		background: var(--bg);
	}
	.artifact-icon { font-size: 0.85em; }
	.artifact-name { color: var(--text-muted); }
	.artifact-open-btn {
		border: none; background: transparent;
		color: var(--text-dim); cursor: pointer; font-size: 0.78rem;
		padding: 0 0.2rem;
	}
	.artifact-open-btn:hover { color: var(--text); }

	/* Artifact panel */
	.artifact-panel {
		width: 340px; flex-shrink: 0;
		display: flex; flex-direction: column;
		border-left: 1px solid var(--border);
		background: var(--bg-card);
		overflow: hidden;
	}
	.artifact-body {
		flex: 1; overflow: auto; padding: 0.75rem;
	}
	.artifact-code {
		margin: 0; font-family: var(--font-mono); font-size: 0.78rem;
		color: var(--text-muted); white-space: pre; line-height: 1.6;
	}

	/* ── Message actions ─────────────────────────────────────────────── */
	.msg-actions {
		display: flex; align-items: center; gap: 2px;
		padding: 2px 4px;
		margin-bottom: 0.75rem;
		opacity: 0;
		transition: opacity 0.15s;
	}
	.msg-actions[data-role='user']      { justify-content: flex-end; }
	.msg-actions[data-role='assistant'] { justify-content: flex-start; }
	:global(.msg-actions:has(+ *)) { opacity: 0; }
	/* Always show on hover of the message group */
	:global([data-message-list] > * :hover .msg-actions),
	.msg-actions:focus-within { opacity: 1; }

	.action-btn {
		padding: 0.2rem 0.45rem;
		border: 1px solid transparent;
		border-radius: 5px;
		background: transparent;
		color: var(--text-dim);
		cursor: pointer;
		font-size: 0.8rem;
		transition: background 0.1s, color 0.1s, border-color 0.1s;
	}
	.action-btn:hover:not(:disabled) {
		background: var(--bg-hover);
		border-color: var(--border);
		color: var(--text);
	}
	.action-btn:disabled { opacity: 0.35; cursor: not-allowed; }
	.action-btn.copied   { color: #10b981; }
	.action-btn.nav      { font-size: 1rem; padding: 0.1rem 0.35rem; }
	.action-sep          { width: 1px; height: 14px; background: var(--border); margin: 0 2px; }
	.action-pos          { font-size: 0.75rem; color: var(--text-dim); padding: 0 0.25rem; }

	/* Inline edit */
	.edit-form {
		display: flex; flex-direction: column; gap: 0.5rem;
		padding: 0.5rem 0; margin-bottom: 0.75rem;
		max-width: 72%; align-self: flex-end;
	}
	.msg-actions[data-role='user'] + .edit-form { align-self: flex-end; }
	.edit-textarea {
		width: 100%; min-height: 80px;
		background: var(--bg-card); color: var(--text);
		border: 1px solid var(--border-hi);
		border-radius: var(--radius);
		padding: 0.5rem 0.65rem;
		font-family: var(--font); font-size: 0.875rem;
		resize: vertical; outline: none;
	}
	.edit-textarea:focus { border-color: var(--text-dim); }
	.edit-actions { display: flex; gap: 0.4rem; justify-content: flex-end; }
	.edit-btn {
		padding: 0.3rem 0.75rem;
		border-radius: var(--radius);
		font-size: 0.82rem; cursor: pointer;
		border: 1px solid var(--border);
	}
	.edit-btn.cancel { background: transparent; color: var(--text-muted); }
	.edit-btn.cancel:hover { background: var(--bg-hover); color: var(--text); }
	.edit-btn.save {
		background: var(--accent); color: var(--bg);
		border-color: transparent; font-weight: 600;
	}
	.edit-btn.save:disabled { opacity: 0.4; cursor: not-allowed; }

	/* ── Input area ──────────────────────────────────────────────────── */
	.input-area {
		flex-shrink: 0;
		border-top: 1px solid var(--border);
		padding: 0.75rem 1.5rem 1rem;
		background: var(--bg);
	}

	/* Attachment chips */
	.attachment-chips {
		display: flex; flex-wrap: wrap; gap: 0.4rem;
		margin-bottom: 0.5rem;
	}
	.chip {
		display: inline-flex; align-items: center; gap: 0.3rem;
		padding: 0.2rem 0.5rem;
		border: 1px solid var(--border);
		border-radius: 99px;
		font-size: 0.78rem; color: var(--text-muted);
		background: var(--bg-card);
	}
	.chip[data-status='done']     { border-color: #10b981; color: #10b981; }
	.chip[data-status='error']    { border-color: #ef4444; color: #ef4444; }
	.chip[data-status='uploading']{ border-color: #6366f1; color: #818cf8; }
	.chip-icon { font-size: 0.8em; }
	.chip-name { max-width: 120px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
	.chip-action {
		background: none; border: none; cursor: pointer;
		color: inherit; padding: 0; font-size: 0.85em; line-height: 1;
		opacity: 0.7;
	}
	.chip-action:hover { opacity: 1; }
	.chip-clear {
		background: none; border: none; cursor: pointer;
		color: var(--text-dim); font-size: 0.75rem; padding: 0 0.25rem;
	}
	.chip-clear:hover { color: var(--text-muted); }

	/* ChatInput */
	.input-row {
		display: flex; align-items: flex-end; gap: 0.4rem;
	}
	:global([data-chat-input-root]) {
		background: var(--bg-input);
		border: 1px solid var(--border);
		border-radius: var(--radius-lg);
		padding: 0.5rem 0.6rem;
		display: flex; flex-direction: column; gap: 0.35rem;
	}
	:global([data-chat-input-root]:focus-within) {
		border-color: var(--border-hi);
	}
	:global([data-chat-input-input]) {
		background: transparent;
		border: none; outline: none;
		color: var(--text);
		font-family: var(--font); font-size: 0.875rem;
		resize: none; width: 100%; line-height: 1.5;
	}
	:global([data-chat-input-input]::placeholder) { color: var(--text-dim); }

	.send-btn {
		padding: 0.45rem 0.85rem;
		border: 1px solid var(--border-hi);
		border-radius: var(--radius);
		background: var(--accent); color: var(--bg);
		font-weight: 600; font-size: 0.85rem;
		cursor: pointer; flex-shrink: 0;
		transition: opacity 0.15s;
		align-self: flex-end;
	}
	.send-btn:disabled { opacity: 0.35; cursor: not-allowed; }
	.send-spinner {
		display: inline-block; width: 12px; height: 12px;
		border: 2px solid var(--bg-hover);
		border-top-color: var(--bg);
		border-radius: 50%;
		animation: spin 0.7s linear infinite;
	}
	@keyframes spin { to { transform: rotate(360deg); } }

	.input-bottom {
		display: flex; align-items: center; gap: 0.5rem;
		padding: 0 0.1rem;
	}
	.model-hint, .temp-hint {
		font-size: 0.72rem; color: var(--text-dim);
	}
	.abort-btn {
		margin-left: auto;
		padding: 0.15rem 0.55rem;
		border: 1px solid #ef4444;
		border-radius: var(--radius);
		background: transparent; color: #f87171;
		font-size: 0.75rem; cursor: pointer;
	}
	.abort-btn:hover { background: #450a0a; }

	/* ── Shared drawer/panel ─────────────────────────────────────────── */
	.drawer-header {
		display: flex; align-items: center; justify-content: space-between;
		padding: 0.65rem 0.85rem;
		border-bottom: 1px solid var(--border);
		flex-shrink: 0;
	}
	.drawer-title {
		font-weight: 600; font-size: 0.85rem; color: var(--text);
	}

	/* ── Config drawer ───────────────────────────────────────────────── */
	.config-drawer {
		width: 280px; flex-shrink: 0;
		display: flex; flex-direction: column;
		border-left: 1px solid var(--border);
		background: var(--bg-card);
		overflow: hidden;
	}
	.drawer-body {
		flex: 1; overflow-y: auto;
		padding: 0.75rem 0.85rem;
		display: flex; flex-direction: column; gap: 1rem;
	}
	.drawer-field { display: flex; flex-direction: column; gap: 0.35rem; }
	.drawer-label {
		font-size: 0.75rem; font-weight: 500;
		color: var(--text-muted); display: flex; align-items: center; gap: 0.4rem;
	}
	.drawer-badge {
		font-size: 0.7rem; padding: 0.05rem 0.35rem;
		border-radius: 99px; background: var(--bg-hover);
		color: var(--text-muted); font-weight: 600;
	}
	.drawer-badge.muted { color: var(--text-dim); }
	.drawer-select {
		width: 100%; padding: 0.35rem 0.5rem;
		background: var(--bg); color: var(--text);
		border: 1px solid var(--border); border-radius: var(--radius);
		font-size: 0.82rem;
	}
	.slider-wrap, .input-wrap { display: flex; align-items: center; gap: 0.4rem; }
	.drawer-slider { flex: 1; accent-color: var(--text); }
	.drawer-input {
		flex: 1; padding: 0.3rem 0.5rem;
		background: var(--bg); color: var(--text);
		border: 1px solid var(--border); border-radius: var(--radius);
		font-size: 0.82rem; outline: none;
	}
	.drawer-input:focus { border-color: var(--border-hi); }
	.drawer-textarea {
		width: 100%; min-height: 70px;
		background: var(--bg); color: var(--text);
		border: 1px solid var(--border); border-radius: var(--radius);
		padding: 0.35rem 0.5rem; font-size: 0.82rem;
		font-family: var(--font); resize: vertical; outline: none;
	}
	.drawer-textarea:focus { border-color: var(--border-hi); }
	.clear-x {
		background: none; border: none; color: var(--text-dim);
		cursor: pointer; font-size: 0.9rem; padding: 0 0.2rem; line-height: 1;
		flex-shrink: 0;
	}
	.clear-x:hover { color: var(--text-muted); }
	.toggle-btn {
		padding: 0.3rem 0.65rem;
		border: 1px solid var(--border);
		border-radius: var(--radius);
		background: var(--bg); color: var(--text-muted);
		font-size: 0.8rem; cursor: pointer;
	}
	.toggle-btn.on {
		background: var(--text); color: var(--bg);
		border-color: var(--text);
	}
	.perm-list { display: flex; flex-direction: column; gap: 0.35rem; }
	.perm-row  { display: flex; align-items: center; justify-content: space-between; gap: 0.5rem; }
	.perm-key  {
		font-family: var(--font-mono); font-size: 0.72rem;
		color: var(--text-dim); flex: 1;
	}
	.perm-btns { display: flex; gap: 2px; }
	.perm-btn  {
		padding: 0.15rem 0.4rem;
		border: 1px solid var(--border); border-radius: 4px;
		background: transparent; color: var(--text-dim);
		font-size: 0.72rem; cursor: pointer;
	}
	.perm-btn.active-allow { background: #14532d; border-color: #16a34a; color: #4ade80; }
	.perm-btn.active-deny  { background: #450a0a; border-color: #dc2626; color: #f87171; }
	.perm-btn.active-def   { background: var(--bg-hover); border-color: var(--border-hi); color: var(--text-muted); }
	.drawer-reset { padding-top: 0.5rem; border-top: 1px solid var(--border); }
	.reset-btn {
		padding: 0.3rem 0.75rem;
		border: 1px solid var(--border); border-radius: var(--radius);
		background: transparent; color: var(--text-muted);
		font-size: 0.8rem; cursor: pointer;
	}
	.reset-btn:hover { background: var(--bg-hover); color: var(--text); }

	/* ── Icon button ─────────────────────────────────────────────────── */
	.icon-btn {
		padding: 0.3rem 0.55rem;
		border: 1px solid var(--border);
		border-radius: var(--radius);
		background: transparent; color: var(--text-muted);
		cursor: pointer; font-size: 0.9rem;
		transition: background 0.1s, color 0.1s;
	}
	.icon-btn:hover   { background: var(--bg-hover); color: var(--text); }
	.icon-btn.active  { background: var(--bg-hover); color: var(--text); border-color: var(--border-hi); }
	.icon-btn.sm      { padding: 0.25rem 0.4rem; font-size: 0.85rem; }
	.icon-btn:disabled { opacity: 0.35; cursor: not-allowed; }

	/* ── Permission dialog ───────────────────────────────────────────── */
	.dialog-backdrop {
		position: fixed; inset: 0; z-index: 50;
		background: rgba(0,0,0,0.7);
		display: flex; align-items: center; justify-content: center;
	}
	.dialog {
		background: var(--bg-card);
		border: 1px solid var(--border-hi);
		border-radius: var(--radius-lg);
		padding: 1.25rem 1.5rem;
		min-width: 340px; max-width: 480px;
		display: flex; flex-direction: column; gap: 0.75rem;
	}
	.dialog-title { margin: 0; font-size: 1rem; font-weight: 600; color: var(--text); }
	.dialog-source { margin: 0; font-size: 0.8rem; color: var(--text-dim); }
	.dialog-args {
		margin: 0; font-family: var(--font-mono); font-size: 0.75rem;
		color: var(--text-muted);
		background: var(--bg); padding: 0.5rem; border-radius: var(--radius);
		white-space: pre-wrap; word-break: break-all;
		border: 1px solid var(--border);
	}
	.dialog-actions { display: flex; gap: 0.5rem; justify-content: flex-end; flex-wrap: wrap; }
	.dialog-btn {
		padding: 0.35rem 0.85rem;
		border-radius: var(--radius); font-size: 0.85rem; cursor: pointer;
		border: 1px solid var(--border); font-weight: 500;
	}
	.dialog-btn.deny        { background: transparent; color: var(--text-muted); }
	.dialog-btn.deny:hover  { background: var(--bg-hover); color: var(--text); }
	.dialog-btn.allow-once  { background: var(--bg-hover); color: var(--text); border-color: var(--border-hi); }
	.dialog-btn.allow       { background: var(--accent); color: var(--bg); border-color: transparent; }
</style>
