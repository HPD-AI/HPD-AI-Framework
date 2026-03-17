<script module>
	import { defineMeta } from '@storybook/addon-svelte-csf';
	import RunConfigDemo from './RunConfigDemo.svelte';

	const { Story } = defineMeta({
		title: 'Components/RunConfig',
		component: RunConfigDemo,
		tags: ['autodocs'],
		argTypes: {
			disabled: {
				control: 'boolean',
				description: 'Disables all controls',
			},
			showModel: {
				control: 'boolean',
				description: 'Show the ModelSelector component',
			},
			showTemperature: {
				control: 'boolean',
				description: 'Show the TemperatureSlider component',
			},
			showTopP: {
				control: 'boolean',
				description: 'Show the TopPSlider component',
			},
			showMaxTokens: {
				control: 'boolean',
				description: 'Show the MaxTokensInput component',
			},
			showSystemInstructions: {
				control: 'boolean',
				description: 'Show the SystemInstructionsInput component',
			},
			showPermissions: {
				control: 'boolean',
				description: 'Show the PermissionOverridesPanel component',
			},
			showSkipTools: {
				control: 'boolean',
				description: 'Show the SkipToolsToggle component',
			},
			showRunTimeout: {
				control: 'boolean',
				description: 'Show the RunTimeoutInput component',
			},
		},
		parameters: {
			docs: {
				description: {
					component: `
The **RunConfig** family of headless components provides per-send model and sampling controls.

Callers own a single \`RunConfigState\` instance (via \`new RunConfig.State()\`) and pass it as
the \`runConfig\` prop to any child component. Each child reads from and writes to the shared state.

The final serializable value is exposed as \`runConfig.value: RunConfig | undefined\` — ready to
pass directly to \`workspace.send({ runConfig: runConfig.value })\`.

## Components
- \`RunConfig.ModelSelector\` — provider + model picker; exposes \`providers\`, \`providerKey\`, \`modelId\`, \`setModel\`
- \`RunConfig.TemperatureSlider\` — 0–1 range slider; exposes \`value\`, \`min\`, \`max\`, \`step\`, \`setValue\`
- \`RunConfig.TopPSlider\` — 0–1 top-p slider; same shape as TemperatureSlider
- \`RunConfig.MaxTokensInput\` — integer token cap; exposes \`value\`, \`min\`, \`max\`, \`setValue\`
- \`RunConfig.SystemInstructionsInput\` — freeform text; exposes \`value\`, \`setValue\`
- \`RunConfig.PermissionOverridesPanel\` — per-tool allow/deny list; exposes \`items\`, \`setOverride\`
- \`RunConfig.SkipToolsToggle\` — boolean toggle; exposes \`value\`, \`setValue\`
- \`RunConfig.RunTimeoutInput\` — ISO 8601 duration string; exposes \`value\`, \`setValue\`

## Usage pattern
\`\`\`svelte
<script>
  import { RunConfig } from '@hpd/hpd-agent-headless-ui';
  const runConfig = new RunConfig.State();
<\/script>

<RunConfig.TemperatureSlider {runConfig}>
  {#snippet children(s)}
    <input type="range" min={s.min} max={s.max} step={s.step}
      value={s.value ?? 0.7}
      oninput={(e) => s.setValue(+e.currentTarget.value)} />
  {/snippet}
</RunConfig.TemperatureSlider>

<!-- Pass to workspace -->
<button onclick={() => workspace.send({ runConfig: runConfig.value })}>Send</button>
\`\`\`
`,
				},
			},
		},
	});
</script>

<!-- ─── ModelSelector ─────────────────────────────────────── -->
<Story
	name="ModelSelector"
	args={{
		showModel: true,
		showTemperature: false,
	}}
/>

<!-- ─── TemperatureSlider ─────────────────────────────────── -->
<Story
	name="TemperatureSlider"
	args={{
		showModel: false,
		showTemperature: true,
	}}
/>

<!-- ─── TopPSlider ────────────────────────────────────────── -->
<Story
	name="TopPSlider"
	args={{
		showModel: false,
		showTemperature: false,
		showTopP: true,
	}}
/>

<!-- ─── MaxTokensInput ────────────────────────────────────── -->
<Story
	name="MaxTokensInput"
	args={{
		showModel: false,
		showTemperature: false,
		showMaxTokens: true,
	}}
/>

<!-- ─── SystemInstructionsInput ───────────────────────────── -->
<Story
	name="SystemInstructionsInput"
	args={{
		showModel: false,
		showTemperature: false,
		showSystemInstructions: true,
	}}
/>

<!-- ─── PermissionOverridesPanel ──────────────────────────── -->
<Story
	name="PermissionOverridesPanel"
	args={{
		showModel: false,
		showTemperature: false,
		showPermissions: true,
		permissions: ['read_file', 'write_file', 'execute_command'],
	}}
/>

<!-- ─── SkipToolsToggle ───────────────────────────────────── -->
<Story
	name="SkipToolsToggle"
	args={{
		showModel: false,
		showTemperature: false,
		showSkipTools: true,
	}}
/>

<!-- ─── RunTimeoutInput ───────────────────────────────────── -->
<Story
	name="RunTimeoutInput"
	args={{
		showModel: false,
		showTemperature: false,
		showRunTimeout: true,
	}}
/>

<!-- ─── Full panel — all controls ─────────────────────────── -->
<Story
	name="Full Panel"
	args={{
		showModel: true,
		showTemperature: true,
		showTopP: true,
		showMaxTokens: true,
		showSystemInstructions: true,
		showPermissions: true,
		showSkipTools: true,
		showRunTimeout: true,
		permissions: ['read_file', 'write_file', 'execute_command'],
	}}
/>

<!-- ─── Disabled ──────────────────────────────────────────── -->
<Story
	name="Disabled"
	args={{
		disabled: true,
		showModel: true,
		showTemperature: true,
		showSkipTools: true,
		showPermissions: true,
		permissions: ['read_file', 'write_file'],
	}}
/>
