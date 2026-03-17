/**
 * Transcription - Reactive state for live STT transcription
 *
 * Displays speech-to-text transcription with:
 * - Interim text (while speaking)
 * - Final text (when completed)
 * - Confidence levels (high/medium/low)
 *
 * Handles 2 HPD events:
 * - TRANSCRIPTION_DELTA
 * - TRANSCRIPTION_COMPLETED
 */

import { createHPDAttrs } from '$lib/internal/attrs.js';
import type { ReadableBoxedValues } from 'svelte-toolbelt';

const transcriptionAttrs = createHPDAttrs({
	component: 'transcription',
	parts: ['root']
} as const);

export type ConfidenceLevel = 'high' | 'medium' | 'low' | null;

export interface TranscriptionStateProps {
	/**
	 * Called when transcription text changes
	 */
	onTextChange?: (text: string, isFinal: boolean) => void;

	/**
	 * Called when transcription is cleared
	 */
	onClear?: () => void;
}

export type TranscriptionStateOpts = ReadableBoxedValues<TranscriptionStateProps>;

export class TranscriptionState {
	readonly opts: TranscriptionStateOpts;

	// Reactive state
	#text = $state('');
	#isFinal = $state(false);
	#confidence = $state<number | null>(null);
	#transcriptionId = $state<string | null>(null);

	// Derived state
	readonly text = $derived(this.#text);
	readonly isFinal = $derived(this.#isFinal);
	readonly confidence = $derived(this.#confidence);
	readonly isEmpty = $derived(this.#text.length === 0);

	readonly confidenceLevel = $derived.by((): ConfidenceLevel => {
		if (!this.#confidence) return null;
		if (this.#confidence >= 0.9) return 'high';
		if (this.#confidence >= 0.7) return 'medium';
		return 'low';
	});

	// Shared props (used in data attributes)
	readonly sharedProps = $derived.by(() => ({
		'data-final': this.#isFinal ? '' : undefined,
		'data-confidence': this.confidenceLevel ?? undefined,
		'data-empty': this.isEmpty ? '' : undefined
	}));

	// Snippet props (exposed to consumers via children snippet)
	readonly snippetProps = $derived.by(() => ({
		text: this.text,
		isFinal: this.isFinal,
		confidence: this.confidence,
		confidenceLevel: this.confidenceLevel,
		isEmpty: this.isEmpty,
		clear: this.clear
	}));

	// Full props for root element
	readonly props = $derived.by(
		() =>
			({
				...this.sharedProps,
				[transcriptionAttrs.root]: '',
				role: 'status',
				'aria-label': 'Voice transcription',
				'aria-live': 'polite',
				'aria-busy': !this.#isFinal
			}) as const
	);

	constructor(opts: TranscriptionStateOpts) {
		this.opts = opts;

		// Bind methods
		this.clear = this.clear.bind(this);

		// Effect to notify text changes
		$effect(() => {
			const currentText = this.#text;
			const currentIsFinal = this.#isFinal;
			this.opts.onTextChange?.current?.(currentText, currentIsFinal);
		});
	}

	// ============================================================================
	// HPD Event Handlers (called by AgentState)
	// ============================================================================

	/**
	 * Handle TRANSCRIPTION_DELTA event
	 * Shows interim or final transcription text
	 */
	onTranscriptionDelta(
		transcriptionId: string,
		text: string,
		isFinal: boolean,
		confidence?: number,
		_streamId?: string
	) {
		this.#transcriptionId = transcriptionId;
		this.#text = text;
		this.#isFinal = isFinal;
		this.#confidence = confidence ?? null;

		console.log('[TranscriptionState] Delta', {
			transcriptionId,
			text,
			isFinal,
			confidence
		});
	}

	/**
	 * Handle TRANSCRIPTION_COMPLETED event
	 * Shows final transcription text
	 */
	onTranscriptionCompleted(
		transcriptionId: string,
		finalText: string,
		confidence?: number,
		_streamId?: string
	) {
		if (this.#transcriptionId !== transcriptionId) return;

		this.#text = finalText;
		this.#isFinal = true;
		this.#confidence = confidence ?? null;

		console.log('[TranscriptionState] Completed', {
			transcriptionId,
			finalText,
			confidence
		});
	}

	// ============================================================================
	// Public Methods
	// ============================================================================

	/**
	 * Clear transcription text
	 */
	clear(): void {
		this.#text = '';
		this.#isFinal = false;
		this.#confidence = null;
		this.#transcriptionId = null;

		this.opts.onClear?.current?.();

		console.log('[TranscriptionState] Cleared');
	}

	/**
	 * Cleanup
	 */
	destroy(): void {
		this.clear();
		console.log('[TranscriptionState] Destroyed');
	}

	/**
	 * Factory method - creates and returns state instance
	 */
	static create(opts: TranscriptionStateOpts): TranscriptionState {
		return new TranscriptionState(opts);
	}
}
