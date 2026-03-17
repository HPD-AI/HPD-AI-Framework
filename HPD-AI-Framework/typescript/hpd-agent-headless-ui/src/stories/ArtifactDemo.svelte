<script lang="ts">
	import { Artifact } from '$lib/index.js';

	interface ArtifactItem {
		id: string;
		title: string;
		type: 'code' | 'document' | 'image' | 'chart';
		content: string;
		language?: string;
	}

	let {
		artifacts = [
			{
				id: 'artifact-1',
				title: 'Hello World',
				type: 'code',
				content: 'console.log("Hello, World!");',
				language: 'javascript'
			}
		] as ArtifactItem[],
		defaultOpenId = undefined as string | undefined,
		showPanel = true,
		...restProps
	} = $props();

	// Track which artifact is open for demo purposes
	let currentOpen = $state<{ id: string | null; open: boolean }>({ id: null, open: false });

	function handleOpenChange(open: boolean, id: string | null) {
		currentOpen = { id, open };
	}

	// Get type icon
	function getTypeIcon(type: ArtifactItem['type']): string {
		switch (type) {
			case 'code':
				return '{ }';
			case 'document':
				return '📄';
			case 'image':
				return '🖼️';
			case 'chart':
				return '📊';
			default:
				return '📎';
		}
	}
</script>

<div class="demo-container" {...restProps}>
	<Artifact.Provider onOpenChange={handleOpenChange}>
		<!-- Message/Content Area -->
		<div class="content-area">
			<div class="messages">
				<div class="message assistant">
					<div class="message-header">
						<strong>Assistant</strong>
					</div>
					<div class="message-content">
						<p>Here are the artifacts I've created for you:</p>

						<!-- Artifact triggers embedded in message -->
						<div class="artifact-list">
							{#each artifacts as artifact (artifact.id)}
								<Artifact.Root
									id={artifact.id}
									defaultOpen={artifact.id === defaultOpenId}
								>
									{#snippet children({ open, toggle })}
										{#snippet titleSnippet()}
											<span class="artifact-title-text">{artifact.title}</span>
										{/snippet}

										{#snippet contentSnippet()}
											<div class="artifact-content-wrapper" data-type={artifact.type}>
												{#if artifact.type === 'code'}
													<pre class="code-block"><code>{artifact.content}</code></pre>
													{#if artifact.language}
														<span class="language-badge">{artifact.language}</span>
													{/if}
												{:else}
													<div class="content-block">{artifact.content}</div>
												{/if}
											</div>
										{/snippet}

										<Artifact.Slot title={titleSnippet} content={contentSnippet} />

										<Artifact.Trigger>
											{#snippet children({ open: triggerOpen })}
												<div
													class="artifact-card"
													data-type={artifact.type}
													data-open={triggerOpen ? '' : undefined}
												>
													<span class="artifact-icon">{getTypeIcon(artifact.type)}</span>
													<span class="artifact-title">{artifact.title}</span>
													<span class="artifact-action">
														{triggerOpen ? 'Close' : 'Open'}
													</span>
												</div>
											{/snippet}
										</Artifact.Trigger>
									{/snippet}
								</Artifact.Root>
							{/each}
						</div>
					</div>
				</div>
			</div>

			<!-- Status indicator -->
			<div class="status-bar">
				{#if currentOpen.open}
					<span class="status open">Panel open: {currentOpen.id}</span>
				{:else}
					<span class="status closed">Click an artifact to open the panel</span>
				{/if}
			</div>
		</div>

		<!-- Side Panel Area (always present for layout, content conditionally rendered) -->
		{#if showPanel}
			<div class="panel-area" data-has-panel={currentOpen.open ? '' : undefined}>
				<Artifact.Panel>
					{#snippet children({ open, openId, title, content, close })}
						<div class="panel">
							<div class="panel-header">
								<Artifact.Title>
									{#snippet children()}
										<h3 class="panel-title">
											{#if title}
												{@render title()}
											{:else}
												Artifact
											{/if}
										</h3>
									{/snippet}
								</Artifact.Title>
								<Artifact.Close>
									{#snippet children()}
										<button class="close-btn" aria-label="Close panel">×</button>
									{/snippet}
								</Artifact.Close>
							</div>
							<div class="panel-body">
								<Artifact.Content>
									{#snippet children()}
										{#if content}
											{@render content()}
										{:else}
											<p class="empty-content">No content available</p>
										{/if}
									{/snippet}
								</Artifact.Content>
							</div>
							<div class="panel-footer">
								<span class="artifact-id">ID: {openId}</span>
							</div>
						</div>
					{/snippet}
				</Artifact.Panel>
			</div>
		{/if}
	</Artifact.Provider>
</div>

<style>
	.demo-container {
		display: flex;
		gap: 1rem;
		padding: 1rem;
		min-height: 400px;
		font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
		background: #f5f5f5;
		border-radius: 8px;
	}

	.content-area {
		flex: 1;
		display: flex;
		flex-direction: column;
		gap: 1rem;
	}

	.messages {
		flex: 1;
		display: flex;
		flex-direction: column;
		gap: 1rem;
	}

	.message {
		background: white;
		border-radius: 12px;
		padding: 1rem;
		box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
	}

	.message-header {
		margin-bottom: 0.75rem;
	}

	.message-header strong {
		color: #764ba2;
	}

	.message-content p {
		margin: 0 0 1rem 0;
		color: #333;
		line-height: 1.5;
	}

	.artifact-list {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}

	.artifact-card {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		padding: 0.75rem 1rem;
		background: #f8f9fa;
		border: 1px solid #e0e0e0;
		border-radius: 8px;
		cursor: pointer;
		transition: all 0.2s;
	}

	.artifact-card:hover {
		background: #f0f0f0;
		border-color: #ccc;
	}

	.artifact-card[data-open] {
		background: #e8f4fd;
		border-color: #4a90d9;
	}

	.artifact-icon {
		font-size: 1.25rem;
		width: 2rem;
		text-align: center;
	}

	.artifact-title {
		flex: 1;
		font-weight: 500;
		color: #333;
	}

	.artifact-action {
		font-size: 0.875rem;
		color: #666;
		padding: 0.25rem 0.5rem;
		background: rgba(0, 0, 0, 0.05);
		border-radius: 4px;
	}

	.artifact-card[data-open] .artifact-action {
		background: #4a90d9;
		color: white;
	}

	.status-bar {
		padding: 0.5rem 1rem;
		background: white;
		border-radius: 8px;
		font-size: 0.875rem;
	}

	.status.open {
		color: #047857;
	}

	.status.closed {
		color: #666;
	}

	/* Panel Area - always present for layout */
	.panel-area {
		width: 350px;
		flex-shrink: 0;
		display: flex;
		flex-direction: column;
	}

	/* Panel Styles */
	.panel {
		flex: 1;
		background: white;
		border-radius: 12px;
		box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
		display: flex;
		flex-direction: column;
		overflow: hidden;
		animation: slideIn 0.2s ease-out;
	}

	@keyframes slideIn {
		from {
			opacity: 0;
			transform: translateX(20px);
		}
		to {
			opacity: 1;
			transform: translateX(0);
		}
	}

	.panel-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 1rem;
		border-bottom: 1px solid #e0e0e0;
		background: #fafafa;
	}

	.panel-title {
		margin: 0;
		font-size: 1rem;
		font-weight: 600;
		color: #333;
	}

	.artifact-title-text {
		color: #764ba2;
	}

	.close-btn {
		width: 32px;
		height: 32px;
		border: none;
		background: transparent;
		font-size: 1.5rem;
		color: #666;
		cursor: pointer;
		border-radius: 4px;
		display: flex;
		align-items: center;
		justify-content: center;
		transition: all 0.2s;
	}

	.close-btn:hover {
		background: #f0f0f0;
		color: #333;
	}

	.panel-body {
		flex: 1;
		padding: 1rem;
		overflow: auto;
	}

	.artifact-content-wrapper {
		position: relative;
	}

	.code-block {
		background: #1e1e1e;
		color: #d4d4d4;
		padding: 1rem;
		border-radius: 8px;
		overflow-x: auto;
		font-family: 'Fira Code', 'Monaco', monospace;
		font-size: 0.875rem;
		line-height: 1.5;
		margin: 0;
	}

	.code-block code {
		font-family: inherit;
	}

	.language-badge {
		position: absolute;
		top: 0.5rem;
		right: 0.5rem;
		font-size: 0.75rem;
		padding: 0.125rem 0.5rem;
		background: rgba(255, 255, 255, 0.1);
		color: #888;
		border-radius: 4px;
		text-transform: uppercase;
	}

	.content-block {
		padding: 1rem;
		background: #f8f9fa;
		border-radius: 8px;
		color: #333;
		line-height: 1.6;
	}

	.empty-content {
		color: #999;
		font-style: italic;
		text-align: center;
		padding: 2rem;
	}

	.panel-footer {
		padding: 0.75rem 1rem;
		border-top: 1px solid #e0e0e0;
		background: #fafafa;
	}

	.artifact-id {
		font-size: 0.75rem;
		color: #999;
		font-family: monospace;
	}
</style>
