<script context="module">
	import { defineMeta } from '@storybook/addon-svelte-csf';
	import AudioPlayerDemo from './AudioPlayerDemo.svelte';

	const { Story } = defineMeta({
		title: 'Components/AudioPlayer',
		component: AudioPlayerDemo,
		tags: ['autodocs'],
		argTypes: {
			useWebAudio: {
				control: 'boolean',
				description: 'Use Web Audio API instead of HTMLAudioElement',
			},
			bufferThreshold: {
				control: { type: 'number', min: 1, max: 5 },
				description: 'Number of chunks to buffer before playback',
			},
			autoStart: {
				control: 'boolean',
				description: 'Auto-start simulation on mount',
			},
			showControls: {
				control: 'boolean',
				description: 'Show playback controls (demo only)',
			},
			showOutput: {
				control: 'boolean',
				description: 'Show component state output (demo only)',
			},
		},
	});
</script>

<Story
	name="Default (Simple Mode)"
	args={{
		useWebAudio: false,
		bufferThreshold: 2,
		autoStart: false,
		showControls: true,
		showOutput: true,
	}}
/>

<Story
	name="Advanced Mode (Web Audio)"
	args={{
		useWebAudio: true,
		bufferThreshold: 2,
		autoStart: false,
		showControls: true,
		showOutput: true,
	}}
/>

<Story
	name="Auto-Start Playback"
	args={{
		useWebAudio: false,
		bufferThreshold: 2,
		autoStart: true,
		showControls: true,
		showOutput: true,
	}}
/>

<Story
	name="High Buffer Threshold"
	args={{
		useWebAudio: false,
		bufferThreshold: 4,
		autoStart: false,
		showControls: true,
		showOutput: true,
	}}
/>

<Story
	name="Web Audio with Auto-Start"
	args={{
		useWebAudio: true,
		bufferThreshold: 2,
		autoStart: true,
		showControls: true,
		showOutput: true,
	}}
/>

<Story
	name="Minimal (No Output)"
	args={{
		useWebAudio: false,
		bufferThreshold: 2,
		autoStart: false,
		showControls: true,
		showOutput: false,
	}}
/>

<Story
	name="Clean UI (No Controls)"
	args={{
		useWebAudio: false,
		bufferThreshold: 2,
		autoStart: false,
		showControls: false,
		showOutput: true,
	}}
/>

<Story
	name="All Features (Web Audio)"
	args={{
		useWebAudio: true,
		bufferThreshold: 2,
		autoStart: false,
		showControls: true,
		showOutput: true,
	}}
/>
