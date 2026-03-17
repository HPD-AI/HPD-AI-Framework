import type { Snippet } from 'svelte';
import type {
	AudioVisualizerState,
	AudioVisualizerStateProps,
	VisualizerMode
} from './audio-visualizer.svelte.ts';

export type { AudioVisualizerStateProps, VisualizerMode };

export interface AudioVisualizerRootSnippetProps {
	volumes: number[];
	bands: number;
	mode: VisualizerMode;
	maxVolume: number;
	avgVolume: number;
	isActive: boolean;
}

export interface AudioVisualizerRootProps extends AudioVisualizerStateProps {
	child?: Snippet<[{ props: Record<string, unknown> } & AudioVisualizerRootSnippetProps]>;
	children?: Snippet<[AudioVisualizerState]>;
	class?: string;
	'data-testid'?: string;
}
