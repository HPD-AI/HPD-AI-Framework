<script context="module">
	import { defineMeta } from '@storybook/addon-svelte-csf';
	import TurnIndicatorDemo from './TurnIndicatorDemo.svelte';

	const { Story } = defineMeta({
		title: 'Components/TurnIndicator',
		component: TurnIndicatorDemo,
		tags: ['autodocs'],
		argTypes: {
			showControls: {
				control: 'boolean',
				description: 'Show simulation controls (demo only)',
			},
			showOutput: {
				control: 'boolean',
				description: 'Show component state output (demo only)',
			},
			autoSimulate: {
				control: 'boolean',
				description: 'Auto-simulate turn flow on mount',
			},
		},
	});
</script>

<Story
	name="Default"
	args={{
		showControls: true,
		showOutput: true,
		autoSimulate: false,
	}}
/>

<Story
	name="Auto Simulate"
	args={{
		showControls: true,
		showOutput: true,
		autoSimulate: true,
	}}
/>

<Story
	name="Minimal (No Output)"
	args={{
		showControls: true,
		showOutput: false,
		autoSimulate: false,
	}}
/>

<Story
	name="Clean UI (No Controls)"
	args={{
		showControls: false,
		showOutput: true,
		autoSimulate: false,
	}}
/>
