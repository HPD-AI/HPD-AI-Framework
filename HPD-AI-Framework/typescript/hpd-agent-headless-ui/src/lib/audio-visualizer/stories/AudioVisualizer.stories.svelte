<script context="module">
	import { defineMeta } from '@storybook/addon-svelte-csf';
	import AudioVisualizerDemo from './AudioVisualizerDemo.svelte';

	const { Story } = defineMeta({
		title: 'Components/AudioVisualizer',
		component: AudioVisualizerDemo,
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
			autoStart: {
				control: 'boolean',
				description: 'Auto-start visualization on mount',
			},
			bands: {
				control: { type: 'range', min: 3, max: 12, step: 1 },
				description: 'Number of frequency bands',
			},
			mode: {
				control: 'select',
				options: ['bar', 'waveform', 'radial'],
				description: 'Visualization mode',
			},
		},
	});
</script>

<Story
	name="Default"
	args={{
		showControls: true,
		showOutput: true,
		autoStart: false,
		bands: 5,
		mode: 'bar',
	}}
/>

<Story
	name="Auto Start"
	args={{
		showControls: true,
		showOutput: true,
		autoStart: true,
		bands: 5,
		mode: 'bar',
	}}
/>

<Story
	name="Seven Bands"
	args={{
		showControls: true,
		showOutput: true,
		autoStart: false,
		bands: 7,
		mode: 'bar',
	}}
/>

<Story
	name="Minimal (No Output)"
	args={{
		showControls: true,
		showOutput: false,
		autoStart: false,
		bands: 5,
		mode: 'bar',
	}}
/>
