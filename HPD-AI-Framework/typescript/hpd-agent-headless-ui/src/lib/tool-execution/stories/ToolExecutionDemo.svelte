<script lang="ts">
	/**
	 * Unified ToolExecution Demo Component
	 *
	 * Supports 3 rendering modes:
	 * 1. default - Structured rendering with Trigger/Content/Args/Result
	 * 2. custom - Tool-specific custom UIs (image generator, database, code executor, weather)
	 * 3. simple - Simple inline-styled renders (search, calculator)
	 */
	import * as ToolExecution from '../exports.js';
	import type { ToolCall, ToolCallStatus } from '../../agent/types.js';
	import type {
		ToolExecutionRootHTMLProps,
		ToolExecutionRootSnippetProps
	} from '../types.js';

	interface Props {
		// Rendering mode
		renderMode?: 'default' | 'custom' | 'simple';
		// Tool configuration
		name?: string;
		status?: ToolCallStatus;
		args?: Record<string, unknown>;
		result?: string;
		error?: string;
		// UI controls (only for default mode)
		expanded?: boolean;
		showExpandToggle?: boolean;
		showEventLog?: boolean;
		interactive?: boolean;
	}

	let {
		renderMode = 'default',
		name = 'exampleTool',
		status = $bindable<ToolCallStatus>('pending'),
		args = { param1: 'value1', param2: 42 },
		result = undefined,
		error = undefined,
		expanded = $bindable(false),
		showExpandToggle = true,
		showEventLog = true,
		interactive = true
	}: Props = $props();

	// Create a reactive ToolCall object
	const toolCall = $derived<ToolCall>({
		callId: `demo-${renderMode}-1`,
		name,
		messageId: `msg-${renderMode}-1`,
		status,
		args,
		result,
		error,
		startTime: new Date(Date.now() - 2000),
		endTime: status === 'complete' || status === 'error' ? new Date() : undefined
	});

	// Event log (default mode only)
	let eventLog = $state<string[]>([]);

	function handleExpandChange(newExpanded: boolean, details: any) {
		const timestamp = new Date().toLocaleTimeString();
		eventLog = [
			...eventLog,
			`[${timestamp}] Expand changed: ${newExpanded} (reason: ${details.reason})`
		].slice(-10);
	}

	// Status controls (default mode only)
	function advanceStatus() {
		const statusFlow: ToolCallStatus[] = ['pending', 'executing', 'complete'];
		const currentIndex = statusFlow.indexOf(status);
		if (currentIndex < statusFlow.length - 1) {
			status = statusFlow[currentIndex + 1];
		}
	}

	function setError() {
		status = 'error';
	}

	function reset() {
		status = 'pending';
		expanded = false;
		eventLog = [];
	}

	// Simple mode state (for calculator interactive demo)
	let localCounter = $state(0);
	let globalCounter = $state(0);
</script>

{#if renderMode === 'custom'}
	{#snippet customChild({ props, name, status, args, result, error, isActive, hasError }: ToolExecutionRootSnippetProps & { props: ToolExecutionRootHTMLProps })}
				<!-- Tool-specific custom rendering -->
				{#if name === 'imageGenerator'}
					<!-- Image Generator Tool -->
					<div {...props} class="image-tool">
						<div class="tool-header">
							<span class="tool-icon">🎨</span>
							<h4>Image Generator</h4>
							<span class="status-badge" data-status={status}>
								{isActive ? '⏳ Generating...' : status}
							</span>
						</div>
						<div class="tool-body">
							<div class="prompt">
								<strong>Prompt:</strong> {args.prompt}
							</div>
							<div class="settings">
								<span>Style: {args.style}</span>
								<span>Size: {args.size}</span>
							</div>
							{#if result}
								<div class="image-preview">
									<img src={result} alt={String(args.prompt || 'Generated image')} />
								</div>
							{/if}
						</div>
					</div>
				{:else if name === 'database'}
					<!-- Database Query Tool -->
					<div {...props} class="database-tool">
						<div class="tool-header">
							<span class="tool-icon">📊</span>
							<h4>Database Query</h4>
							<span class="status-badge" data-status={status}>
								{isActive ? '⏳ Executing...' : status}
							</span>
						</div>
						<div class="tool-body">
							<div class="query-box">
								<strong>SQL Query:</strong>
								<pre class="code-block">{args.query}</pre>
							</div>
							{#if result}
								{@const rows = JSON.parse(result)}
								<div class="result-table">
									<strong>Results:</strong>
									<table>
										<thead>
											<tr>
												{#each Object.keys(rows[0]) as col}
													<th>{col}</th>
												{/each}
											</tr>
										</thead>
										<tbody>
											{#each rows as row}
												<tr>
													{#each Object.values(row) as val}
														<td>{val}</td>
													{/each}
												</tr>
											{/each}
										</tbody>
									</table>
								</div>
							{/if}
						</div>
					</div>
				{:else if name === 'codeExecutor'}
					<!-- Code Executor Tool -->
					<div {...props} class="code-tool">
						<div class="tool-header">
							<span class="tool-icon">💻</span>
							<h4>Code Executor</h4>
							<span class="status-badge" data-status={status}>
								{isActive ? '⏳ Running...' : status}
							</span>
						</div>
						<div class="tool-body">
							<div class="code-section">
								<div class="language-badge">{args.language}</div>
								<pre class="code-block">{args.code}</pre>
							</div>
							{#if result}
								<div class="console-output">
									<strong>Output:</strong>
									<pre class="console">{result}</pre>
								</div>
							{/if}
						</div>
					</div>
				{:else if name === 'weather'}
					<!-- Weather Tool -->
					<div {...props} class="weather-tool">
						<div class="tool-header">
							<span class="tool-icon">🌤️</span>
							<h4>Weather Forecast</h4>
							<span class="status-badge" data-status={status}>
								{isActive ? '⏳ Loading...' : status}
							</span>
						</div>
						<div class="tool-body">
							<div class="location">{args.location}</div>
							{#if result}
								{@const weather = JSON.parse(result)}
								<div class="weather-display">
									<div class="temperature">{weather.temperature}°F</div>
									<div class="conditions">{weather.conditions}</div>
									<div class="details">
										<span>💧 Humidity: {weather.humidity}%</span>
										<span>💨 Wind: {weather.wind}</span>
									</div>
								</div>
							{/if}
						</div>
					</div>
				{/if}
		{/snippet}

	<div class="demo-container">
		<h3>Tool-Specific Custom Rendering</h3>
		<p class="description">
			This demonstrates how the <code>child</code> snippet allows completely custom rendering for each
			tool type while maintaining state management and accessibility.
		</p>
	
		<ToolExecution.Root {toolCall} child={customChild} class="custom-tool-root" />
	
		<div class="info-box">
			<h4> Key Benefits:</h4>
			<ul>
				<li>  Each tool has completely custom UI</li>
				<li>  Still gets state management from component</li>
				<li>  Still gets accessibility attributes</li>
				<li>  Data attributes for styling</li>
				<li>  Event handling built-in</li>
			</ul>
		</div>
	</div>
{:else if renderMode === 'simple'}
	{#snippet simpleChild({ props, name, status, args, result, isActive }: ToolExecutionRootSnippetProps & { props: ToolExecutionRootHTMLProps })}
				{#if name === 'search'}
					<!-- Search Tool Render -->
					<div
						{...props}
						style="
							padding: 12px;
							margin: 8px 0;
							background-color: {isActive ? '#f0f4f8' : '#e6f3ff'};
							border-radius: 8px;
							border: 1px solid #cce0ff;
						"
					>
						<div style="font-weight: bold; margin-bottom: 4px;">🔍 Search Tool</div>
						<div style="font-size: 14px; color: #666;">
							Query: {args.query}
							{#if args.filters && Array.isArray(args.filters) && args.filters.length > 0}
								<div>Filters: {args.filters.join(', ')}</div>
							{/if}
						</div>
						{#if isActive}
							<div style="margin-top: 8px; color: #0066cc;">Searching...</div>
						{:else if result}
							<div style="margin-top: 8px; color: #006600;">Results: {result}</div>
						{/if}
					</div>
	
				{:else if name === 'calculator'}
					<!-- Calculator Tool Render with interactive buttons -->
					<div
						{...props}
						style="
							padding: 12px;
							margin: 8px 0;
							background-color: {isActive ? '#fff9e6' : '#fff4cc'};
							border-radius: 8px;
							border: 1px solid #ffcc66;
						"
					>
						<div style="font-weight: bold; margin-bottom: 4px;">🧮 Calculator</div>
						<div style="font-size: 14px; color: #666;">Expression: {args.expression}</div>
						{#if isActive}
							<div style="margin-top: 8px; color: #cc6600;">Calculating...</div>
						{:else if result}
							<div style="margin-top: 8px; color: #006600;">Result: {result}</div>
						{/if}
	
						<!-- Interactive counter demo (shows custom renders can have state) -->
						<div
							style="
								margin-top: 12px;
								padding: 8px;
								background-color: #fff8e6;
								border-radius: 4px;
							"
						>
							<div style="font-size: 13px; color: #666; margin-bottom: 4px;">
								Local counter: {localCounter}
							</div>
							<div style="display: flex; gap: 8px; margin-bottom: 8px;">
								<button
									onclick={() => localCounter--}
									style="
										padding: 4px 12px;
										background-color: #ff9933;
										color: white;
										border: none;
										border-radius: 4px;
										cursor: pointer;
									"
								>
									-
								</button>
								<button
									onclick={() => localCounter++}
									style="
										padding: 4px 12px;
										background-color: #ff9933;
										color: white;
										border: none;
										border-radius: 4px;
										cursor: pointer;
									"
								>
									+
								</button>
							</div>
	
							<div style="border-top: 1px solid #ffcc66; padding-top: 8px;">
								<div
									style="
										font-size: 13px;
										color: #666;
										margin-bottom: 4px;
										font-weight: bold;
									"
								>
									Global counter: {globalCounter}
								</div>
								<div style="display: flex; gap: 8px;">
									<button
										onclick={() => globalCounter--}
										style="
											padding: 4px 12px;
											background-color: #b35900;
											color: white;
											border: none;
											border-radius: 4px;
											cursor: pointer;
										"
									>
										Global -
									</button>
									<button
										onclick={() => globalCounter++}
										style="
											padding: 4px 12px;
											background-color: #b35900;
											color: white;
											border: none;
											border-radius: 4px;
											cursor: pointer;
										"
									>
										Global +
									</button>
								</div>
							</div>
						</div>
					</div>
	
				{:else}
					<!-- Wildcard Render (for any unmatched tool) -->
					<div
						{...props}
						style="
							padding: 12px;
							margin: 8px 0;
							background-color: #f5f5f5;
							border-radius: 8px;
							border: 1px solid #ddd;
						"
					>
						<div style="font-weight: bold; margin-bottom: 4px;"> Tool Execution</div>
						<div style="font-size: 14px; color: #666;">
							<pre style="margin: 0; font-size: 12px;">{JSON.stringify(args, null, 2)}</pre>
						</div>
						{#if isActive}
							<div style="margin-top: 8px; color: #666;">Processing...</div>
						{:else if result}
							<div style="margin-top: 8px; color: #333;">Output: {result}</div>
						{/if}
					</div>
				{/if}
	{/snippet}

	<div class="demo-wrapper">
		<ToolExecution.Root {toolCall} child={simpleChild} />
	
		<div class="info-note">
			<strong> Custom Rendering Pattern:</strong>
			<ul>
				<li>Simple inline styles (no CSS classes needed)</li>
				<li>Status-based background colors (executing vs complete)</li>
				<li>Interactive state (counters persist across renders)</li>
				<li>Wildcard fallback for unknown tools</li>
				<li>Full flexibility while keeping component's state management & accessibility</li>
			</ul>
		</div>
	</div>
{:else}
	<div class="demo-container">
		<div class="tool-section">
			<ToolExecution.Root
				{toolCall}
				bind:expanded
				onExpandChange={handleExpandChange}
				class="tool-execution"
			>
				<ToolExecution.Trigger class="tool-trigger">
					<div class="tool-header">
						<div class="tool-info">
							<span class="tool-icon"></span>
							<span class="tool-name">{toolCall.name}</span>
						</div>
						<div class="tool-meta">
							<ToolExecution.Status class="tool-status">
								{#snippet children({ status, isActive })}
									<span class="status-badge" data-status={status} data-active={isActive ? '' : undefined}>
										{#if isActive}
											<span class="spinner"></span>
										{/if}
										{status}
									</span>
								{/snippet}
							</ToolExecution.Status>
							<span class="chevron" data-expanded={expanded ? '' : undefined}>▼</span>
						</div>
					</div>
				</ToolExecution.Trigger>
	
				<ToolExecution.Content class="tool-content">
					<ToolExecution.Args class="tool-args">
						{#snippet children({ argsJson, hasArgs })}
							{#if hasArgs}
								<div class="section">
									<h4 class="section-title">Arguments:</h4>
									<pre class="code-block">{argsJson}</pre>
								</div>
							{:else}
								<div class="section">
									<p class="empty-state">No arguments</p>
								</div>
							{/if}
						{/snippet}
					</ToolExecution.Args>
	
					<ToolExecution.Result class="tool-result">
						{#snippet children({ result, error, hasResult, hasError })}
							{#if hasError}
								<div class="section error-section">
									<h4 class="section-title">Error:</h4>
									<pre class="error-block">{error}</pre>
								</div>
							{:else if hasResult}
								<div class="section result-section">
									<h4 class="section-title">Result:</h4>
									<pre class="result-block">{result}</pre>
								</div>
							{:else}
								<div class="section">
									<p class="empty-state">
										{#if toolCall.status === 'pending'}
											Waiting to execute...
										{:else if toolCall.status === 'executing'}
											Executing...
										{:else}
											No result yet
										{/if}
									</p>
								</div>
							{/if}
						{/snippet}
					</ToolExecution.Result>
				</ToolExecution.Content>
			</ToolExecution.Root>
		</div>
	
		{#if showExpandToggle || interactive}
			<div class="controls-section">
				{#if showExpandToggle}
					<div class="control-group">
						<label>
							<strong>Expanded:</strong>
							<input type="checkbox" bind:checked={expanded} />
						</label>
					</div>
				{/if}
	
				{#if interactive}
					<div class="control-group">
						<strong>Status Controls:</strong>
						<div class="button-group">
							<button onclick={advanceStatus} disabled={status === 'complete'} class="control-btn">
								Advance Status
							</button>
							<button onclick={setError} class="control-btn error-btn">Set Error</button>
							<button onclick={reset} class="control-btn">Reset</button>
						</div>
					</div>
				{/if}
			</div>
		{/if}
	
		{#if showEventLog}
			<div class="output-section">
				<h4>Event Log:</h4>
				{#if eventLog.length > 0}
					<div class="log">
						{#each eventLog.slice(-5) as log}
							<div class="log-entry">{log}</div>
						{/each}
					</div>
				{:else}
					<p class="empty-state">No events yet</p>
				{/if}
			</div>
		{/if}
	</div>
{/if}

<style>

	.demo-container {
		max-width: 700px;
		display: flex;
		flex-direction: column;
		gap: 1.5rem;
		font-family: system-ui, -apple-system, sans-serif;
	}

	.tool-section {
		display: flex;
		flex-direction: column;
	}

	:global(.tool-execution) {
		border: 2px solid #e5e7eb;
		border-radius: 12px;
		overflow: hidden;
		background: white;
		box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
	}

	:global(.tool-execution[data-active]) {
		border-color: #667eea;
	}

	:global(.tool-execution[data-error]) {
		border-color: #ef4444;
	}

	:global(.tool-execution[data-complete]) {
		border-color: #10b981;
	}

	:global(.tool-trigger) {
		width: 100%;
		padding: 1rem;
		background: transparent;
		border: none;
		cursor: pointer;
		transition: background-color 0.2s;
	}

	:global(.tool-trigger:hover) {
		background: #f9fafb;
	}

	:global(.tool-trigger[data-expanded]) {
		background: #f3f4f6;
		border-bottom: 1px solid #e5e7eb;
	}

	.tool-header {
		display: flex;
		justify-content: space-between;
		align-items: center;
		gap: 1rem;
	}

	.tool-info {
		display: flex;
		align-items: center;
		gap: 0.75rem;
	}

	.tool-icon {
		font-size: 1.25rem;
	}

	.tool-name {
		font-weight: 600;
		font-size: 1rem;
		color: #1f2937;
		font-family: 'Monaco', 'Courier New', monospace;
	}

	.tool-meta {
		display: flex;
		align-items: center;
		gap: 0.75rem;
	}

	.status-badge {
		display: inline-flex;
		align-items: center;
		gap: 0.5rem;
		padding: 0.375rem 0.75rem;
		border-radius: 9999px;
		font-size: 0.75rem;
		font-weight: 600;
		text-transform: uppercase;
		letter-spacing: 0.5px;
	}

	.status-badge[data-status='pending'] {
		background: #fef3c7;
		color: #92400e;
	}

	.status-badge[data-status='executing'] {
		background: #dbeafe;
		color: #1e40af;
	}

	.status-badge[data-status='complete'] {
		background: #d1fae5;
		color: #065f46;
	}

	.status-badge[data-status='error'] {
		background: #fee2e2;
		color: #991b1b;
	}

	.spinner {
		width: 12px;
		height: 12px;
		border: 2px solid currentColor;
		border-top-color: transparent;
		border-radius: 50%;
		animation: spin 0.8s linear infinite;
	}

	@keyframes spin {
		to {
			transform: rotate(360deg);
		}
	}

	.chevron {
		font-size: 0.75rem;
		color: #6b7280;
		transition: transform 0.2s;
	}

	.chevron[data-expanded] {
		transform: rotate(180deg);
	}

	:global(.tool-content) {
		display: grid;
		grid-template-rows: 0fr;
		transition: grid-template-rows 0.3s ease-out;
	}

	:global(.tool-content[data-expanded]) {
		grid-template-rows: 1fr;
	}

	:global(.tool-content > div) {
		overflow: hidden;
	}

	:global(.tool-args),
	:global(.tool-result) {
		padding: 0 1rem;
	}

	.section {
		padding: 1rem 0;
		border-top: 1px solid #f3f4f6;
	}

	.section:first-child {
		border-top: none;
	}

	.section-title {
		margin: 0 0 0.75rem 0;
		font-size: 0.875rem;
		font-weight: 600;
		color: #6b7280;
		text-transform: uppercase;
		letter-spacing: 0.5px;
	}

	.code-block,
	.result-block,
	.error-block {
		margin: 0;
		padding: 0.75rem;
		background: #f9fafb;
		border: 1px solid #e5e7eb;
		border-radius: 6px;
		font-family: 'Monaco', 'Courier New', monospace;
		font-size: 0.8rem;
		overflow-x: auto;
		color: #1f2937;
	}

	.result-block {
		background: #ecfdf5;
		border-color: #a7f3d0;
		color: #065f46;
	}

	.error-block {
		background: #fef2f2;
		border-color: #fecaca;
		color: #991b1b;
	}

	.empty-state {
		margin: 0;
		padding: 1rem;
		text-align: center;
		color: #4b5563;
		font-size: 0.875rem;
		font-style: italic;
	}

	.controls-section {
		padding: 1rem;
		background: #f9fafb;
		border: 1px solid #e5e7eb;
		border-radius: 8px;
		display: flex;
		flex-direction: column;
		gap: 1rem;
	}

	.control-group {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}

	.control-group strong {
		font-size: 0.875rem;
		color: #6b7280;
	}

	.button-group {
		display: flex;
		gap: 0.5rem;
		flex-wrap: wrap;
	}

	.control-btn {
		padding: 0.5rem 1rem;
		background: white;
		border: 1px solid #d1d5db;
		border-radius: 6px;
		font-size: 0.875rem;
		font-weight: 500;
		cursor: pointer;
		transition: all 0.2s;
	}

	.control-btn:hover:not(:disabled) {
		background: #f3f4f6;
		border-color: #9ca3af;
	}

	.control-btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.error-btn {
		background: #fef2f2;
		border-color: #fecaca;
		color: #991b1b;
	}

	.error-btn:hover:not(:disabled) {
		background: #fee2e2;
	}

	.output-section {
		padding: 1rem;
		background: #f5f5f5;
		border-radius: 8px;
	}

	.output-section h4 {
		margin: 0 0 0.75rem 0;
		font-size: 0.875rem;
		color: #666;
		text-transform: uppercase;
		letter-spacing: 0.5px;
	}

	.log {
		display: flex;
		flex-direction: column;
		gap: 0.25rem;
	}

	.log-entry {
		padding: 0.5rem;
		background: white;
		border-left: 3px solid #667eea;
		font-family: 'Monaco', 'Courier New', monospace;
		font-size: 0.75rem;
		border-radius: 2px;
	}

	/* ========================================
	   CUSTOM MODE STYLES
	   ======================================== */

	.demo-container {
		max-width: 800px;
		font-family: system-ui, -apple-system, sans-serif;
	}

	.description {
		color: #666;
		margin-bottom: 1.5rem;
		font-size: 0.9rem;
	}

	code {
		background: #f0f0f0;
		padding: 0.2rem 0.4rem;
		border-radius: 3px;
		font-size: 0.9em;
	}

	/* Tool Cards */
	:global(.custom-tool-root) {
		border: 2px solid #e5e7eb;
		border-radius: 12px;
		overflow: hidden;
		background: white;
		box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
	}

	.tool-header {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		padding: 1rem;
		background: linear-gradient(135deg, #f3f4f6 0%, #e5e7eb 100%);
		border-bottom: 1px solid #d1d5db;
	}

	.tool-icon {
		font-size: 1.5rem;
	}

	.tool-header h4 {
		flex: 1;
		margin: 0;
		font-size: 1rem;
		font-weight: 600;
	}

	.status-badge {
		padding: 0.375rem 0.75rem;
		border-radius: 9999px;
		font-size: 0.75rem;
		font-weight: 600;
		text-transform: uppercase;
	}

	.status-badge[data-status='pending'] {
		background: #fef3c7;
		color: #92400e;
	}

	.status-badge[data-status='executing'] {
		background: #dbeafe;
		color: #1e40af;
	}

	.status-badge[data-status='complete'] {
		background: #d1fae5;
		color: #065f46;
	}

	.tool-body {
		padding: 1rem;
	}

	/* Image Generator Styles */
	.prompt {
		margin-bottom: 0.75rem;
		font-size: 0.95rem;
	}

	.settings {
		display: flex;
		gap: 1rem;
		margin-bottom: 1rem;
		font-size: 0.85rem;
		color: #666;
	}

	.image-preview {
		margin-top: 1rem;
		border-radius: 8px;
		overflow: hidden;
	}

	.image-preview img {
		width: 100%;
		height: auto;
		display: block;
	}

	/* Database Styles */
	.query-box {
		margin-bottom: 1rem;
	}

	.code-block,
	.console {
		margin-top: 0.5rem;
		padding: 0.75rem;
		background: #1e1e1e;
		color: #d4d4d4;
		border-radius: 6px;
		font-family: 'Monaco', 'Courier New', monospace;
		font-size: 0.85rem;
		overflow-x: auto;
	}

	.result-table {
		margin-top: 1rem;
	}

	table {
		width: 100%;
		border-collapse: collapse;
		margin-top: 0.5rem;
		font-size: 0.9rem;
	}

	th,
	td {
		padding: 0.5rem;
		text-align: left;
		border-bottom: 1px solid #e5e7eb;
	}

	th {
		background: #f3f4f6;
		font-weight: 600;
	}

	/* Code Executor Styles */
	.code-section {
		position: relative;
	}

	.language-badge {
		position: absolute;
		top: 0.5rem;
		right: 0.5rem;
		padding: 0.25rem 0.5rem;
		background: #667eea;
		color: white;
		border-radius: 4px;
		font-size: 0.75rem;
		font-weight: 600;
	}

	.console-output {
		margin-top: 1rem;
	}

	/* Weather Styles */
	.location {
		font-size: 0.9rem;
		color: #666;
		margin-bottom: 1rem;
	}

	.weather-display {
		text-align: center;
		padding: 1rem;
	}

	.temperature {
		font-size: 3rem;
		font-weight: 700;
		color: #667eea;
		margin-bottom: 0.5rem;
	}

	.conditions {
		font-size: 1.25rem;
		color: #4b5563;
		margin-bottom: 1rem;
	}

	.details {
		display: flex;
		justify-content: center;
		gap: 2rem;
		font-size: 0.9rem;
		color: #666;
	}

	/* Info Box */
	.info-box {
		margin-top: 2rem;
		padding: 1rem;
		background: #f0f9ff;
		border-left: 4px solid #667eea;
		border-radius: 4px;
	}

	.info-box h4 {
		margin-top: 0;
		color: #4338ca;
	}

	.info-box ul {
		margin: 0;
		padding-left: 1.5rem;
	}

	.info-box li {
		margin-bottom: 0.5rem;
		color: #374151;
	}

	.info-note {
		margin-top: 1.5rem;
		padding: 1rem;
		background: #f0f9ff;
		border-left: 4px solid #3b82f6;
		border-radius: 4px;
		font-size: 0.9rem;
	}
</style>
