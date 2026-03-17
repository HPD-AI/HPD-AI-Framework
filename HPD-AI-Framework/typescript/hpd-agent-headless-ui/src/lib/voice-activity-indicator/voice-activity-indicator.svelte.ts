/**
 * VoiceActivityIndicator - Reactive state for voice activity detection
 *
 * Shows visual feedback when user is speaking:
 * - Active/inactive state
 * - Speech probability (0-1)
 * - Intensity levels (high/medium/low)
 * - Speech duration tracking
 *
 * Handles 2 HPD events:
 * - VAD_START_OF_SPEECH
 * - VAD_END_OF_SPEECH
 */

import { createHPDAttrs } from '$lib/internal/attrs.js';
import type { ReadableBoxedValues } from 'svelte-toolbelt';

const vadAttrs = createHPDAttrs({
	component: 'voice-activity-indicator',
	parts: ['root']
} as const);

export type IntensityLevel = 'high' | 'medium' | 'low' | 'none';

export interface VoiceActivityIndicatorStateProps {
	/**
	 * Called when voice activity state changes
	 */
	onActivityChange?: (active: boolean) => void;
}

export type VoiceActivityIndicatorStateOpts = ReadableBoxedValues<VoiceActivityIndicatorStateProps>;

export class VoiceActivityIndicatorState {
	readonly opts: VoiceActivityIndicatorStateOpts;

	// Reactive state
	#active = $state(false);
	#speechProbability = $state(0);
	#duration = $state(0);

	// Derived state
	readonly active = $derived(this.#active);
	readonly speechProbability = $derived(this.#speechProbability);
	readonly duration = $derived(this.#duration);

	readonly intensityLevel = $derived.by((): IntensityLevel => {
		if (!this.#active) return 'none';
		if (this.#speechProbability >= 0.9) return 'high';
		if (this.#speechProbability >= 0.7) return 'medium';
		return 'low';
	});

	// Shared props (used in data attributes)
	readonly sharedProps = $derived.by(() => ({
		'data-active': this.#active ? '' : undefined,
		'data-intensity': this.intensityLevel
	}));

	// Snippet props (exposed to consumers via children snippet)
	readonly snippetProps = $derived.by(() => ({
		active: this.active,
		speechProbability: this.speechProbability,
		duration: this.duration,
		intensityLevel: this.intensityLevel
	}));

	// Full props for root element
	readonly props = $derived.by(
		() =>
			({
				...this.sharedProps,
				[vadAttrs.root]: '',
				role: 'status',
				'aria-label': 'Voice activity',
				'aria-live': 'polite'
			}) as const
	);

	constructor(opts: VoiceActivityIndicatorStateOpts) {
		this.opts = opts;

		// Effect to notify activity changes
		$effect(() => {
			const currentActive = this.#active;
			this.opts.onActivityChange?.current?.(currentActive);
		});
	}

	// ============================================================================
	// HPD Event Handlers (called by AgentState)
	// ============================================================================

	/**
	 * Handle VAD_START_OF_SPEECH event
	 * User started speaking
	 */
	onVadStartOfSpeech(timestamp: string, speechProbability: number) {
		this.#active = true;
		this.#speechProbability = speechProbability;

		console.log('[VoiceActivityIndicatorState] Speech started', {
			timestamp,
			speechProbability
		});
	}

	/**
	 * Handle VAD_END_OF_SPEECH event
	 * User stopped speaking
	 */
	onVadEndOfSpeech(timestamp: string, speechDuration: string, speechProbability: number) {
		this.#active = false;
		this.#speechProbability = speechProbability;
		this.#duration = this.#parseDuration(speechDuration);

		console.log('[VoiceActivityIndicatorState] Speech ended', {
			timestamp,
			speechDuration,
			speechProbability
		});
	}

	// ============================================================================
	// Utility Methods
	// ============================================================================

	/**
	 * Parse ISO 8601 duration to seconds
	 * @private
	 */
	#parseDuration(isoDuration: string): number {
		// Parse ISO 8601 duration (PT2.5S → 2.5 seconds)
		const match = isoDuration.match(/PT([\d.]+)S/);
		return match ? parseFloat(match[1]) : 0;
	}

	/**
	 * Cleanup
	 */
	destroy(): void {
		this.#active = false;
		this.#speechProbability = 0;
		this.#duration = 0;

		console.log('[VoiceActivityIndicatorState] Destroyed');
	}

	/**
	 * Factory method - creates and returns state instance
	 */
	static create(opts: VoiceActivityIndicatorStateOpts): VoiceActivityIndicatorState {
		return new VoiceActivityIndicatorState(opts);
	}
}
