<script lang="ts">
	import { RunConfig } from '$lib/index.js';

	let {
		disabled = false,
		providers = [
			{
				key: 'anthropic',
				label: 'Anthropic',
				models: [
					{ id: 'claude-opus-4-6', label: 'Claude Opus 4.6' },
					{ id: 'claude-sonnet-4-6', label: 'Claude Sonnet 4.6' },
					{ id: 'claude-haiku-4-5-20251001', label: 'Claude Haiku 4.5' },
				],
			},
			{
				key: 'openai',
				label: 'OpenAI',
				models: [
					{ id: 'gpt-4o', label: 'GPT-4o' },
					{ id: 'gpt-4o-mini', label: 'GPT-4o Mini' },
				],
			},
		],
		permissions = ['read_file', 'write_file', 'execute_command'],
		showModel = true,
		showTemperature = true,
		showTopP = false,
		showMaxTokens = false,
		showSystemInstructions = false,
		showPermissions = false,
		showSkipTools = false,
		showRunTimeout = false,
		...restProps
	} = $props();

	const runConfig = new RunConfig.State();
</script>

<div class="demo-container">
	<div class="panel">
		<h3 class="panel-title">Run Configuration</h3>

		{#if showModel}
			<div class="field">
				<!-- svelte-ignore a11y_label_has_associated_control -->
				<label class="field-label">Model</label>
				<RunConfig.ModelSelector {runConfig} {providers} {disabled}>
					{#snippet children(s)}
						<div class="model-selector">
							<select
								class="select"
								onchange={(e) => {
									const [pk, mid] = e.currentTarget.value.split('::');
									s.setModel(pk || undefined, mid || undefined);
								}}
							>
								<option value="">— select model —</option>
								{#each s.providers as provider}
									<optgroup label={provider.label}>
										{#each provider.models as model}
											<option
												value="{provider.key}::{model.id}"
												selected={s.providerKey === provider.key && s.modelId === model.id}
											>
												{model.label}
											</option>
										{/each}
									</optgroup>
								{/each}
							</select>
							{#if s.providerKey && s.modelId}
								<button class="clear-btn" onclick={() => s.setModel(undefined, undefined)}>×</button>
							{/if}
						</div>
					{/snippet}
				</RunConfig.ModelSelector>
			</div>
		{/if}

		{#if showTemperature}
			<div class="field">
				<!-- svelte-ignore a11y_label_has_associated_control -->
				<label class="field-label">
					Temperature
					{#if runConfig.temperature !== undefined}
						<span class="value-badge">{runConfig.temperature.toFixed(2)}</span>
					{:else}
						<span class="value-badge muted">default</span>
					{/if}
				</label>
				<RunConfig.TemperatureSlider {runConfig} {disabled}>
					{#snippet children(s)}
						<div class="slider-row">
							<span class="slider-bound">{s.min}</span>
							<input
								type="range"
								class="slider"
								min={s.min}
								max={s.max}
								step={s.step}
								value={s.value ?? 0.7}
								disabled={s.disabled}
								oninput={(e) => s.setValue(+e.currentTarget.value)}
							/>
							<span class="slider-bound">{s.max}</span>
							{#if s.value !== undefined}
								<button class="clear-btn" onclick={() => s.setValue(undefined)}>×</button>
							{/if}
						</div>
					{/snippet}
				</RunConfig.TemperatureSlider>
			</div>
		{/if}

		{#if showTopP}
			<div class="field">
				<!-- svelte-ignore a11y_label_has_associated_control -->
				<label class="field-label">
					Top-P
					{#if runConfig.topP !== undefined}
						<span class="value-badge">{runConfig.topP.toFixed(2)}</span>
					{:else}
						<span class="value-badge muted">default</span>
					{/if}
				</label>
				<RunConfig.TopPSlider {runConfig} {disabled}>
					{#snippet children(s)}
						<div class="slider-row">
							<span class="slider-bound">{s.min}</span>
							<input
								type="range"
								class="slider"
								min={s.min}
								max={s.max}
								step={s.step}
								value={s.value ?? 1}
								disabled={s.disabled}
								oninput={(e) => s.setValue(+e.currentTarget.value)}
							/>
							<span class="slider-bound">{s.max}</span>
							{#if s.value !== undefined}
								<button class="clear-btn" onclick={() => s.setValue(undefined)}>×</button>
							{/if}
						</div>
					{/snippet}
				</RunConfig.TopPSlider>
			</div>
		{/if}

		{#if showMaxTokens}
			<div class="field">
				<!-- svelte-ignore a11y_label_has_associated_control -->
				<label class="field-label">Max Output Tokens</label>
				<RunConfig.MaxTokensInput {runConfig} {disabled}>
					{#snippet children(s)}
						<div class="input-row">
							<input
								type="number"
								class="number-input"
								min={s.min}
								max={s.max}
								value={s.value ?? ''}
								placeholder="default"
								disabled={s.disabled}
								oninput={(e) => {
									const n = parseInt(e.currentTarget.value, 10);
									s.setValue(isNaN(n) ? undefined : n);
								}}
							/>
							{#if s.value !== undefined}
								<button class="clear-btn" onclick={() => s.setValue(undefined)}>×</button>
							{/if}
						</div>
					{/snippet}
				</RunConfig.MaxTokensInput>
			</div>
		{/if}

		{#if showSystemInstructions}
			<div class="field">
				<!-- svelte-ignore a11y_label_has_associated_control -->
				<label class="field-label">Additional System Instructions</label>
				<RunConfig.SystemInstructionsInput {runConfig} {disabled}>
					{#snippet children(s)}
						<div class="textarea-row">
							<textarea
								class="textarea"
								value={s.value ?? ''}
								placeholder="Extra instructions for this run…"
								disabled={s.disabled}
								oninput={(e) =>
									s.setValue(e.currentTarget.value.trim() || undefined)}
							></textarea>
						</div>
					{/snippet}
				</RunConfig.SystemInstructionsInput>
			</div>
		{/if}

		{#if showPermissions}
			<div class="field">
				<!-- svelte-ignore a11y_label_has_associated_control -->
				<label class="field-label">Permission Overrides</label>
				<RunConfig.PermissionOverridesPanel {runConfig} {permissions} {disabled}>
					{#snippet children(s)}
						<div class="permission-list">
							{#each s.items as item}
								<div class="permission-row">
									<span class="permission-key">{item.key}</span>
									<div class="permission-buttons">
										<button
											class="perm-btn allow"
											class:active={item.value === true}
											onclick={() => s.setOverride(item.key, true)}
										>Allow</button>
										<button
											class="perm-btn deny"
											class:active={item.value === false}
											onclick={() => s.setOverride(item.key, false)}
										>Deny</button>
										<button
											class="perm-btn reset"
											class:active={item.value === undefined}
											onclick={() => s.setOverride(item.key, undefined)}
										>Default</button>
									</div>
								</div>
							{/each}
						</div>
					{/snippet}
				</RunConfig.PermissionOverridesPanel>
			</div>
		{/if}

		{#if showSkipTools}
			<div class="field">
				<!-- svelte-ignore a11y_label_has_associated_control -->
				<label class="field-label">Skip Tools</label>
				<RunConfig.SkipToolsToggle {runConfig} {disabled}>
					{#snippet children(s)}
						<div class="toggle-row">
							<button
								class="toggle"
								class:checked={s.value === true}
								onclick={() => s.setValue(s.value === true ? undefined : true)}
								disabled={s.disabled}
								aria-pressed={s.value === true}
							>
								{s.value === true ? 'Enabled' : s.value === false ? 'Disabled' : 'Default'}
							</button>
							<span class="muted">Bypass tool execution for this run</span>
						</div>
					{/snippet}
				</RunConfig.SkipToolsToggle>
			</div>
		{/if}

		{#if showRunTimeout}
			<div class="field">
				<label class="field-label" for="run-timeout-input">Run Timeout (ISO 8601 duration)</label>
				<RunConfig.RunTimeoutInput {runConfig} {disabled}>
					{#snippet children(s)}
						<div class="input-row">
							<input
								id="run-timeout-input"
								type="text"
								class="text-input"
								value={s.value ?? ''}
								placeholder="e.g. PT5M"
								disabled={s.disabled}
								oninput={(e) =>
									s.setValue(e.currentTarget.value.trim() || undefined)}
							/>
							{#if s.value !== undefined}
								<button class="clear-btn" onclick={() => s.setValue(undefined)}>×</button>
							{/if}
						</div>
					{/snippet}
				</RunConfig.RunTimeoutInput>
			</div>
		{/if}

		<div class="reset-row">
			<button class="reset-btn" onclick={() => runConfig.reset()}>Reset all</button>
		</div>
	</div>

	<div class="output-panel">
		<h4 class="output-title">runConfig.value</h4>
		<pre class="output-code">{JSON.stringify(runConfig.value ?? null, null, 2)}</pre>
	</div>
</div>

<style>
	.demo-container {
		display: flex;
		gap: 1.5rem;
		padding: 2rem;
		font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
		font-size: 0.9rem;
		max-width: 900px;
	}

	.panel {
		flex: 1;
		display: flex;
		flex-direction: column;
		gap: 1.25rem;
		background: #fff;
		border: 1px solid #e5e7eb;
		border-radius: 12px;
		padding: 1.25rem;
	}

	.panel-title {
		margin: 0 0 0.25rem;
		font-size: 1rem;
		font-weight: 600;
		color: #111827;
	}

	.field {
		display: flex;
		flex-direction: column;
		gap: 0.4rem;
	}

	.field-label {
		font-weight: 500;
		color: #374151;
		display: flex;
		align-items: center;
		gap: 0.5rem;
	}

	.value-badge {
		font-size: 0.75rem;
		font-weight: 600;
		padding: 0.1rem 0.45rem;
		border-radius: 99px;
		background: #4f46e5;
		color: #fff;
	}
	.value-badge.muted {
		background: #e5e7eb;
		color: #6b7280;
	}

	/* Model selector */
	.model-selector {
		display: flex;
		gap: 0.5rem;
		align-items: center;
	}
	.select {
		flex: 1;
		padding: 0.4rem 0.6rem;
		border: 1px solid #d1d5db;
		border-radius: 6px;
		background: #fff;
		font-size: 0.875rem;
		color: #111827;
	}

	/* Sliders */
	.slider-row {
		display: flex;
		align-items: center;
		gap: 0.5rem;
	}
	.slider {
		flex: 1;
		accent-color: #4f46e5;
	}
	.slider-bound {
		font-size: 0.75rem;
		color: #6b7280;
		min-width: 1.5rem;
		text-align: center;
	}

	/* Number / text inputs */
	.input-row,
	.textarea-row {
		display: flex;
		gap: 0.5rem;
		align-items: flex-start;
	}
	.number-input,
	.text-input {
		flex: 1;
		padding: 0.4rem 0.6rem;
		border: 1px solid #d1d5db;
		border-radius: 6px;
		font-size: 0.875rem;
		color: #111827;
	}
	.textarea {
		flex: 1;
		padding: 0.4rem 0.6rem;
		border: 1px solid #d1d5db;
		border-radius: 6px;
		font-size: 0.875rem;
		color: #111827;
		resize: vertical;
		min-height: 4rem;
	}

	/* Clear button */
	.clear-btn {
		border: none;
		background: #e5e7eb;
		color: #6b7280;
		border-radius: 50%;
		width: 1.4rem;
		height: 1.4rem;
		cursor: pointer;
		font-size: 0.9rem;
		line-height: 1;
		display: flex;
		align-items: center;
		justify-content: center;
		flex-shrink: 0;
	}
	.clear-btn:hover {
		background: #d1d5db;
		color: #374151;
	}

	/* Toggle */
	.toggle-row {
		display: flex;
		align-items: center;
		gap: 0.75rem;
	}
	.toggle {
		padding: 0.35rem 0.85rem;
		border-radius: 6px;
		border: 1px solid #d1d5db;
		background: #f9fafb;
		cursor: pointer;
		font-size: 0.875rem;
		color: #374151;
		transition: background 0.15s, color 0.15s;
	}
	.toggle.checked {
		background: #4f46e5;
		color: #fff;
		border-color: #4f46e5;
	}
	.toggle:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	/* Permissions */
	.permission-list {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}
	.permission-row {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 1rem;
	}
	.permission-key {
		font-family: monospace;
		font-size: 0.8rem;
		color: #374151;
	}
	.permission-buttons {
		display: flex;
		gap: 0.25rem;
	}
	.perm-btn {
		padding: 0.2rem 0.6rem;
		border-radius: 4px;
		border: 1px solid #d1d5db;
		background: #f9fafb;
		font-size: 0.78rem;
		cursor: pointer;
		color: #374151;
	}
	.perm-btn.allow.active { background: #d1fae5; border-color: #10b981; color: #065f46; }
	.perm-btn.deny.active  { background: #fee2e2; border-color: #ef4444; color: #991b1b; }
	.perm-btn.reset.active { background: #e5e7eb; border-color: #9ca3af; color: #374151; }

	/* Reset */
	.reset-row {
		padding-top: 0.5rem;
		border-top: 1px solid #f3f4f6;
	}
	.reset-btn {
		padding: 0.35rem 0.9rem;
		border: 1px solid #d1d5db;
		border-radius: 6px;
		background: #f9fafb;
		cursor: pointer;
		font-size: 0.875rem;
		color: #374151;
	}
	.reset-btn:hover { background: #e5e7eb; }

	/* Output */
	.output-panel {
		width: 240px;
		flex-shrink: 0;
	}
	.output-title {
		margin: 0 0 0.5rem;
		font-size: 0.8rem;
		font-weight: 600;
		color: #6b7280;
		text-transform: uppercase;
		letter-spacing: 0.05em;
	}
	.output-code {
		background: #1e1e2e;
		color: #cdd6f4;
		border-radius: 8px;
		padding: 0.75rem;
		font-size: 0.75rem;
		line-height: 1.5;
		margin: 0;
		overflow: auto;
		white-space: pre-wrap;
		word-break: break-all;
	}

	.muted {
		color: #9ca3af;
		font-size: 0.8rem;
	}
</style>
