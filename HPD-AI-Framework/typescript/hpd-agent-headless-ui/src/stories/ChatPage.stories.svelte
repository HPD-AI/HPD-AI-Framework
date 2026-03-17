<script module>
	import { defineMeta } from '@storybook/addon-svelte-csf';
	import ChatPageDemo from './ChatPageDemo.svelte';

	const { Story } = defineMeta({
		title: 'Pages/ChatPage',
		component: ChatPageDemo,
		tags: ['autodocs'],
		parameters: {
			layout: 'fullscreen',
			docs: {
				description: {
					component: `
A **full chat page** composed entirely from HPD Agent headless components.

No backend required — powered by \`createMockWorkspace()\`.

## Components used
| Zone | Components |
|------|-----------|
| Sidebar | \`SessionList.Root\`, \`SessionList.Item\`, \`SessionList.Empty\`, \`SessionList.CreateButton\` |
| Messages | \`MessageList.Root\`, \`Message\`, \`ToolExecution.*\`, \`Artifact.Root/Slot/Trigger\` |
| Actions | \`MessageActions.Root/EditButton/RetryButton/CopyButton/Prev/Next/Position\` |
| Edit | \`MessageEdit.Root/Textarea/SaveButton/CancelButton\` |
| Input | \`ChatInput.Root/Input/Leading/Trailing/Bottom\`, \`FileAttachment.Root\` |
| Config | \`RunConfig.ModelSelector/TemperatureSlider/TopPSlider/MaxTokensInput/SystemInstructionsInput/SkipToolsToggle/PermissionOverridesPanel/RunTimeoutInput\` |
| Permissions | \`PermissionDialog.Root/Overlay/Content/Header/Description/Actions/Approve/Deny\` |
| Artifacts | \`Artifact.Provider/Panel/Title/Content/Close\` |

## Theme
shadcn-inspired dark: zinc-950 background, zinc-800 borders, white text.
`,
				},
			},
		},
		argTypes: {
			enableReasoning: {
				control: 'boolean',
				description: 'Simulate reasoning/thinking before assistant responses',
			},
			typingDelay: {
				control: { type: 'range', min: 0, max: 150, step: 5 },
				description: 'Typing speed in ms per character (0 = instant)',
			},
			showSidebar: {
				control: 'boolean',
				description: 'Show the session list sidebar (toggle off to simulate mobile/narrow layout)',
			},
			showRunConfig: {
				control: 'boolean',
				description: 'Open the run config panel on load',
			},
			initialSessionCount: {
				control: { type: 'range', min: 1, max: 8, step: 1 },
				description: 'Number of mock sessions to pre-populate in the sidebar',
			},
		},
	});
</script>

<!-- ── Default ─────────────────────────────────────────────────────────── -->
<Story
	name="Default"
	args={{
		enableReasoning: false,
		typingDelay: 25,
		showSidebar: true,
		showRunConfig: false,
		initialSessionCount: 2,
	}}
/>

<!-- ── With Reasoning ──────────────────────────────────────────────────── -->
<Story
	name="With Reasoning"
	args={{
		enableReasoning: true,
		typingDelay: 20,
		showSidebar: true,
		showRunConfig: false,
		initialSessionCount: 2,
	}}
/>

<!-- ── Fast (no typing delay) ─────────────────────────────────────────── -->
<Story
	name="Fast"
	args={{
		enableReasoning: false,
		typingDelay: 0,
		showSidebar: true,
		showRunConfig: false,
		initialSessionCount: 2,
	}}
/>

<!-- ── No Sidebar (mobile-like) ───────────────────────────────────────── -->
<Story
	name="No Sidebar"
	args={{
		enableReasoning: false,
		typingDelay: 25,
		showSidebar: false,
		showRunConfig: false,
		initialSessionCount: 2,
	}}
/>

<!-- ── Config Open ─────────────────────────────────────────────────────── -->
<Story
	name="Config Open"
	args={{
		enableReasoning: false,
		typingDelay: 25,
		showSidebar: true,
		showRunConfig: true,
		initialSessionCount: 2,
	}}
/>

<!-- ── Many Sessions ──────────────────────────────────────────────────── -->
<Story
	name="Many Sessions"
	args={{
		enableReasoning: false,
		typingDelay: 25,
		showSidebar: true,
		showRunConfig: false,
		initialSessionCount: 6,
	}}
/>
