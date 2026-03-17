/**
 * Public API exports for VoiceActivityIndicator component
 */

export { default as Root } from './components/voice-activity-indicator.svelte';

export type {
	VoiceActivityIndicatorRootProps as RootProps,
	VoiceActivityIndicatorRootSnippetProps as RootSnippetProps
} from './types.ts';

export type { IntensityLevel } from './voice-activity-indicator.svelte.ts';

export { VoiceActivityIndicatorState as State } from './voice-activity-indicator.svelte.ts';
