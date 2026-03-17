<script context="module">
	import { defineMeta } from '@storybook/addon-svelte-csf';
	import InputDemo from './InputDemo.svelte';

	const { Story } = defineMeta({
		title: 'Components/Input',
		component: InputDemo,
		tags: ['autodocs'],
		argTypes: {
			placeholder: {
				control: 'text',
				description: 'Placeholder text',
			},
			disabled: {
				control: 'boolean',
				description: 'Disable the input',
			},
			maxRows: {
				control: { type: 'number', min: 1, max: 10 },
				description: 'Maximum number of rows before scrolling',
			},
			autoFocus: {
				control: 'boolean',
				description: 'Auto-focus on mount',
			},
			showSubmitButton: {
				control: 'boolean',
				description: 'Show submit button (demo only)',
			},
			showOutput: {
				control: 'boolean',
				description: 'Show component state output (demo only)',
			},
		},
	});
</script>

<Story
	name="Default"
	args={{
		placeholder: 'Type a message...',
		disabled: false,
		maxRows: 5,
		autoFocus: false,
		showSubmitButton: true,
		showOutput: true,
	}}
/>

<Story
	name="Disabled"
	args={{
		placeholder: 'This input is disabled',
		disabled: true,
		maxRows: 5,
		defaultValue: 'Cannot edit this text',
		showSubmitButton: true,
		showOutput: true,
	}}
/>

<Story
	name="Auto Resize (Max 3 Rows)"
	args={{
		placeholder: 'Type multiple lines to see auto-resize...',
		disabled: false,
		maxRows: 3,
		autoFocus: false,
		showSubmitButton: true,
		showOutput: true,
	}}
/>

<Story
	name="Auto Resize (Max 10 Rows)"
	args={{
		placeholder: 'Type many lines to see auto-resize up to 10 rows...',
		disabled: false,
		maxRows: 10,
		autoFocus: false,
		showSubmitButton: true,
		showOutput: true,
	}}
/>

<Story
	name="Chat Input Style"
	args={{
		placeholder: 'Message...',
		disabled: false,
		maxRows: 5,
		autoFocus: false,
		showSubmitButton: true,
		showOutput: false,
	}}
/>

<Story
	name="Minimal (No Submit Button)"
	args={{
		placeholder: 'Press Enter to submit, Shift+Enter for new line',
		disabled: false,
		maxRows: 5,
		autoFocus: false,
		showSubmitButton: false,
		showOutput: true,
	}}
/>

<Story
	name="With Default Value"
	args={{
		defaultValue: 'This is some default text that can be edited',
		placeholder: 'Type a message...',
		disabled: false,
		maxRows: 5,
		autoFocus: false,
		showSubmitButton: true,
		showOutput: true,
	}}
/>
