<script context="module">
	import { defineMeta } from '@storybook/addon-svelte-csf';
	import PermissionDialogDemo from './PermissionDialogDemo.svelte';

	const { Story } = defineMeta({
		title: 'Components/PermissionDialog',
		component: PermissionDialogDemo,
		tags: ['autodocs'],
		argTypes: {
			variant: {
				control: { type: 'select' },
				options: ['default', 'minimal', 'detailed', 'child'],
				description: 'Display variant for arguments'
			},
			showTrigger: {
				control: 'boolean',
				description: 'Show trigger button (demo only)'
			},
			showStatus: {
				control: 'boolean',
				description: 'Show status messages (demo only)'
			}
		}
	});
</script>

<Story
	name="Default"
	args={{
		variant: 'default',
		showTrigger: true,
		showStatus: true
	}}
/>

<Story
	name="Minimal"
	args={{
		variant: 'minimal',
		showTrigger: true,
		showStatus: false
	}}
/>

<Story
	name="Detailed"
	args={{
		variant: 'detailed',
		showTrigger: true,
		showStatus: true
	}}
/>

<Story
	name="Custom Element (child snippet)"
	args={{
		variant: 'child',
		showTrigger: true,
		showStatus: true
	}}
/>

