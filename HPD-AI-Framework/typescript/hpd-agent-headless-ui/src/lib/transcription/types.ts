import type { Snippet } from 'svelte';
import type { ConfidenceLevel, TranscriptionStateProps } from './transcription.svelte.ts';

/**
 * Props for Transcription.Root component
 */
export interface TranscriptionRootProps extends TranscriptionStateProps {
	/**
	 * Snippet for rendering with props passed to parent element
	 */
	child?: Snippet<[{ props: Record<string, unknown> } & TranscriptionRootSnippetProps]>;

	/**
	 * Snippet for rendering transcription content
	 */
	children?: Snippet<[TranscriptionRootSnippetProps]>;

	/**
	 * CSS class name
	 */
	class?: string;

	/**
	 * Reference to the root element
	 */
	ref?: HTMLElement | null;

	/**
	 * Test ID for testing
	 */
	'data-testid'?: string;
}

/**
 * Props passed to the children snippet
 */
export interface TranscriptionRootSnippetProps {
	/**
	 * Current transcription text
	 */
	text: string;

	/**
	 * Whether the transcription is final (completed)
	 */
	isFinal: boolean;

	/**
	 * Confidence score (0-1)
	 */
	confidence: number | null;

	/**
	 * Confidence level (high/medium/low)
	 */
	confidenceLevel: ConfidenceLevel;

	/**
	 * Whether the transcription is empty
	 */
	isEmpty: boolean;

	/**
	 * Clear the transcription
	 */
	clear: () => void;
}
