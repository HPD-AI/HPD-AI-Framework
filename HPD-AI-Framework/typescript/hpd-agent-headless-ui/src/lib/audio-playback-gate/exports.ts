/**
 * AudioPlaybackGate - Public API exports
 */

export { default as Root } from './components/audio-playback-gate.svelte';
export { default as Trigger } from './components/audio-playback-gate-trigger.svelte';

export type {
	AudioPlaybackGateRootProps as RootProps,
	AudioPlaybackGateRootSnippetProps as RootSnippetProps,
	AudioPlaybackGateTriggerProps as TriggerProps,
	AudioPlaybackGateTriggerSnippetProps as TriggerSnippetProps,
	AudioGateStatus as Status
} from './types.ts';

export { AudioPlaybackGateState as State } from './audio-playback-gate.svelte.ts';
