/**
 * Public API exports for Transcription component
 */

export { default as Root } from './components/transcription.svelte';

export type {
	TranscriptionRootProps as RootProps,
	TranscriptionRootSnippetProps as RootSnippetProps
} from './types.ts';

export type { ConfidenceLevel } from './transcription.svelte.ts';

export { TranscriptionState as State } from './transcription.svelte.ts';
