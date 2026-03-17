/**
 * AudioPlayer - Public API exports
 */

export { default as Root } from './components/audio-player.svelte';

export type {
	AudioPlayerRootProps as RootProps,
	AudioPlayerRootSnippetProps as RootSnippetProps,
	AudioPlayerStatus as Status
} from './types.ts';

export { AudioPlayerState as State } from './audio-player.svelte.ts';
