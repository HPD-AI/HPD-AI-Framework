<script module>
	import { defineMeta } from '@storybook/addon-svelte-csf';
	import MessageDemo from './MessageDemo.svelte';

	const { Story } = defineMeta({
		title: 'Components/Message',
		component: MessageDemo,
		tags: ['autodocs'],
		argTypes: {
			role: {
				control: { type: 'select' },
				options: ['user', 'assistant', 'system'],
				description: 'Message role'
			},
			content: {
				control: 'text',
				description: 'Message content'
			},
			streaming: {
				control: 'boolean',
				description: 'Whether the message is streaming'
			},
			thinking: {
				control: 'boolean',
				description: 'Whether AI is thinking/reasoning'
			},
			reasoning: {
				control: 'text',
				description: 'Reasoning text (if any)'
			}
		}
	});
</script>

<!-- Default Story -->
<Story
	name="Default"
	args={{
		role: 'assistant',
		content: 'Hello! I am an AI assistant. How can I help you today?',
		streaming: false,
		thinking: false,
		reasoning: ''
	}}
/>

<!-- User Message Story -->
<Story
	name="User Message"
	args={{
		role: 'user',
		content: 'What is the weather like today?',
		streaming: false,
		thinking: false
	}}
/>

<!-- Streaming Message Story -->
<Story
	name="Streaming"
	args={{
		role: 'assistant',
		content: 'I am currently streaming this respo',
		streaming: true,
		thinking: false
	}}
/>

<!-- Thinking Message Story -->
<Story
	name="Thinking"
	args={{
		role: 'assistant',
		content: '',
		streaming: false,
		thinking: true
	}}
/>

<!-- With Reasoning Story -->
<Story
	name="With Reasoning"
	args={{
		role: 'assistant',
		content: 'Based on the analysis, I recommend using TypeScript for this project.',
		reasoning: 'Analyzing project requirements... Considering team expertise...',
		streaming: false,
		thinking: false
	}}
/>

<!-- Long Message Story -->
<Story
	name="Long Message"
	args={{
		role: 'assistant',
		content:
			'This is a much longer message that demonstrates how the component handles extensive content. It includes multiple sentences and paragraphs to show text wrapping and layout behavior. The Message component is truly headless, so you have complete control over how this content is displayed, including line height, font size, colors, and spacing.',
		streaming: false,
		thinking: false
	}}
/>

<!-- Complete Status -->
<Story
	name="Complete Status"
	args={{
		role: 'assistant',
		content: 'Task completed successfully!',
		streaming: false,
		thinking: false
	}}
/>
