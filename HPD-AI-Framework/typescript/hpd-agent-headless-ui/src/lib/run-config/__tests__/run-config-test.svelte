<script lang="ts">
	/**
	 * RunConfig Test Harness
	 *
	 * Renders every RunConfig component in a single harness so the browser tests
	 * can verify data attributes, snippet props, and state mutation through the
	 * DOM. Each component is wrapped in a div with a data-testid so locators are
	 * stable regardless of how mergeProps reorders attributes.
	 */
	import * as RunConfig from '../exports.js';
	import { RunConfigState } from '../run-config.svelte.js';
	import type { ProviderOption } from '../types.js';

	interface Props {
		disabled?: boolean;
		providers?: ProviderOption[];
		permissions?: string[];
		initialTemperature?: number;
		initialSkipTools?: boolean;
	}

	let {
		disabled = false,
		providers = [
			{
				key: 'anthropic',
				label: 'Anthropic',
				models: [
					{ id: 'claude-sonnet-4-6', label: 'Claude Sonnet 4.6' },
					{ id: 'claude-opus-4-6', label: 'Claude Opus 4.6' },
				],
			},
		],
		permissions = ['read_file', 'write_file'],
		initialTemperature = undefined,
		initialSkipTools = undefined,
	}: Props = $props();

	// Single shared RunConfigState — all components mutate this
	const runConfig = new RunConfigState();

	$effect(() => {
		if (initialTemperature !== undefined) runConfig.setTemperature(initialTemperature);
	});
	$effect(() => {
		if (initialSkipTools !== undefined) runConfig.setSkipTools(initialSkipTools);
	});
</script>

<!-- ModelSelector -->
<div data-testid="model-selector-wrapper">
	<RunConfig.ModelSelector {runConfig} {providers} {disabled}>
		{#snippet children(s)}
			<div data-testid="model-selector-inner" data-provider-key={s.providerKey ?? ''} data-model-id={s.modelId ?? ''}>
				<button
					data-testid="set-model-btn"
					onclick={() => s.setModel('anthropic', 'claude-sonnet-4-6')}
				>Set Model</button>
				<button
					data-testid="clear-model-btn"
					onclick={() => s.setModel(undefined, undefined)}
				>Clear Model</button>
			</div>
		{/snippet}
	</RunConfig.ModelSelector>
</div>

<!-- TemperatureSlider -->
<div data-testid="temperature-wrapper">
	<RunConfig.TemperatureSlider {runConfig} {disabled}>
		{#snippet children(s)}
			<div data-testid="temperature-inner" data-value={s.value ?? ''}>
				<button data-testid="set-temp-btn" onclick={() => s.setValue(0.7)}>Set 0.7</button>
				<button data-testid="clear-temp-btn" onclick={() => s.setValue(undefined)}>Clear</button>
				<span data-testid="temp-min">{s.min}</span>
				<span data-testid="temp-max">{s.max}</span>
				<span data-testid="temp-step">{s.step}</span>
				<span data-testid="temp-disabled">{s.disabled}</span>
			</div>
		{/snippet}
	</RunConfig.TemperatureSlider>
</div>

<!-- SkipToolsToggle -->
<div data-testid="skip-tools-wrapper">
	<RunConfig.SkipToolsToggle {runConfig} {disabled}>
		{#snippet children(s)}
			<div data-testid="skip-tools-inner" data-value={s.value ?? ''}>
				<button data-testid="skip-tools-on-btn" onclick={() => s.setValue(true)}>Enable</button>
				<button data-testid="skip-tools-off-btn" onclick={() => s.setValue(false)}>Disable</button>
				<button data-testid="skip-tools-clear-btn" onclick={() => s.setValue(undefined)}>Clear</button>
			</div>
		{/snippet}
	</RunConfig.SkipToolsToggle>
</div>

<!-- PermissionOverridesPanel -->
<div data-testid="permission-overrides-wrapper">
	<RunConfig.PermissionOverridesPanel {runConfig} {permissions} {disabled}>
		{#snippet children(s)}
			<div data-testid="permission-overrides-inner">
				{#each s.items as item}
					<div data-testid="perm-item-{item.key}" data-value={item.value ?? ''}>
						<span data-testid="perm-key-{item.key}">{item.key}</span>
						<button
							data-testid="perm-allow-{item.key}"
							onclick={() => s.setOverride(item.key, true)}
						>Allow</button>
						<button
							data-testid="perm-deny-{item.key}"
							onclick={() => s.setOverride(item.key, false)}
						>Deny</button>
					</div>
				{/each}
				<span data-testid="perm-count">{s.items.length}</span>
			</div>
		{/snippet}
	</RunConfig.PermissionOverridesPanel>
</div>

<!-- child snippet pattern (TemperatureSlider with child) -->
<RunConfig.TemperatureSlider {runConfig} {disabled}>
	{#snippet child(s)}
		<div {...s.props} data-testid="temperature-child-pattern">
			<span data-testid="child-pattern-value">{s.value ?? 'none'}</span>
		</div>
	{/snippet}
</RunConfig.TemperatureSlider>

<!-- Expose the shared runConfig value as JSON for assertions -->
<div data-testid="run-config-value">{JSON.stringify(runConfig.value ?? null)}</div>
