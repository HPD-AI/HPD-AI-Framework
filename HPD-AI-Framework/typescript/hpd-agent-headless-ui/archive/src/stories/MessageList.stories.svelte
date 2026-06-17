<script module>
	import { defineMeta } from '@storybook/addon-svelte-csf';
	import MessageListDemo from './MessageListDemo.svelte';

	const { Story } = defineMeta({
		title: 'Components/MessageList',
		component: MessageListDemo,
		tags: ['autodocs'],
		argTypes: {
			messageCount: {
				control: { type: 'number', min: 0, max: 20 },
				description: 'Number of messages to display'
			},
			scrollBehavior: {
				control: { type: 'select' },
				options: ['bottom', 'sent-message', 'none'],
				description:
					'Controls automatic scroll behavior.\n\n' +
					'**bottom** — always scroll to the end (classic chat).\n\n' +
					'**sent-message** — scroll so your latest message is at the top of the viewport; the response streams below it.\n\n' +
					'**none** — no automatic scrolling.'
			},
			streaming: {
				control: 'boolean',
				description: 'Show streaming indicator on last message'
			}
		}
	});
</script>

<!-- Default — classic bottom scroll -->
<Story
	name="Default (bottom)"
	args={{
		messageCount: 5,
		scrollBehavior: 'bottom',
		streaming: false
	}}
/>

<!-- Sent-message mode -->
<Story
	name="Sent-message scroll"
	args={{
		messageCount: 6,
		scrollBehavior: 'sent-message',
		streaming: false
	}}
/>

<!-- No auto-scroll -->
<Story
	name="No auto-scroll"
	args={{
		messageCount: 10,
		scrollBehavior: 'none',
		streaming: false
	}}
/>

<!-- Empty List -->
<Story
	name="Empty List"
	args={{
		messageCount: 0,
		scrollBehavior: 'bottom',
		streaming: false
	}}
/>

<!-- Single Message -->
<Story
	name="Single Message"
	args={{
		messageCount: 1,
		scrollBehavior: 'bottom',
		streaming: false
	}}
/>

<!-- Long conversation — bottom mode so jump button appears -->
<Story
	name="Long Conversation"
	args={{
		messageCount: 15,
		scrollBehavior: 'bottom',
		streaming: false
	}}
/>

<!-- Active Streaming — bottom mode -->
<Story
	name="Active Streaming"
	args={{
		messageCount: 6,
		scrollBehavior: 'bottom',
		streaming: true
	}}
/>

<!-- Active Streaming — sent-message mode -->
<Story
	name="Active Streaming (sent-message)"
	args={{
		messageCount: 6,
		scrollBehavior: 'sent-message',
		streaming: true
	}}
/>

<!-- Short Chat -->
<Story
	name="Short Chat"
	args={{
		messageCount: 3,
		scrollBehavior: 'bottom',
		streaming: false
	}}
/>
