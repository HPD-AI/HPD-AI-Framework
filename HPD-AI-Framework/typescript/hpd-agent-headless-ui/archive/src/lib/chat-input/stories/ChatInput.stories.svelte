<script context="module">
	import { defineMeta } from '@storybook/addon-svelte-csf';
	import ChatInputDemo from './ChatInputDemo.svelte';

	const { Story } = defineMeta({
		title: 'Components/ChatInput',
		component: ChatInputDemo,
		tags: ['autodocs'],
		argTypes: {
			placeholder: {
				control: 'text',
				description: 'Placeholder text for the input',
			},
			disabled: {
				control: 'boolean',
				description: 'Disable the entire input',
			},
			maxRows: {
				control: { type: 'number', min: 1, max: 20 },
				description: 'Maximum number of rows before scrolling',
			},
			showLeading: {
				control: 'boolean',
				description: 'Show leading accessories (attach, voice buttons)',
			},
			showTrailing: {
				control: 'boolean',
				description: 'Show trailing accessories (emoji, send button)',
			},
			showTop: {
				control: 'boolean',
				description: 'Show top accessories (context chips)',
			},
			showBottom: {
				control: 'boolean',
				description: 'Show bottom accessories (character count, status)',
			},
			showOutput: {
				control: 'boolean',
				description: 'Show component state output',
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
		showLeading: true,
		showTrailing: true,
		showTop: false,
		showBottom: false,
		showOutput: true,
	}}
/>

<Story
	name="Full Featured"
	args={{
		placeholder: 'Type a message...',
		disabled: false,
		maxRows: 5,
		showLeading: true,
		showTrailing: true,
		showTop: true,
		showBottom: true,
		showOutput: true,
	}}
/>

<Story
	name="Minimal"
	args={{
		placeholder: 'Type a message...',
		disabled: false,
		maxRows: 5,
		showLeading: false,
		showTrailing: true,
		showTop: false,
		showBottom: false,
		showOutput: true,
	}}
/>

<Story
	name="Disabled"
	args={{
		placeholder: 'This input is disabled',
		disabled: true,
		maxRows: 5,
		showLeading: true,
		showTrailing: true,
		showTop: false,
		showBottom: false,
		showOutput: true,
	}}
/>

<Story
	name="With Top and Bottom"
	args={{
		placeholder: 'Type a message...',
		disabled: false,
		maxRows: 8,
		showLeading: true,
		showTrailing: true,
		showTop: true,
		showBottom: true,
		showOutput: false,
	}}
/>
