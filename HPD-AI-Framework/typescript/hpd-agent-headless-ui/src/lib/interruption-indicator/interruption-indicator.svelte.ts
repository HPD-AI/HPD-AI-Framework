/**
 * InterruptionIndicator - Reactive state for interruption tracking
 *
 * Shows when agent speech is paused/interrupted:
 * - Interruption status
 * - Pause reasons
 * - Pause duration tracking
 *
 * Handles 3 HPD events:
 * - USER_INTERRUPTED
 * - SPEECH_PAUSED
 * - SPEECH_RESUMED
 */

import { createHPDAttrs } from '$lib/internal/attrs.js';
import type { ReadableBoxedValues } from 'svelte-toolbelt';

const interruptionAttrs = createHPDAttrs({
	component: 'interruption-indicator',
	parts: ['root']
} as const);

export type PauseReason = 'user_speaking' | 'potential_interruption' | null;
export type InterruptionStatus = 'interrupted' | 'paused' | 'normal';

export interface InterruptionIndicatorStateProps {
	/**
	 * Called when interruption state changes
	 */
	onInterruptionChange?: (interrupted: boolean) => void;

	/**
	 * Called when pause state changes
	 */
	onPauseChange?: (paused: boolean) => void;
}

export type InterruptionIndicatorStateOpts = ReadableBoxedValues<InterruptionIndicatorStateProps>;

export class InterruptionIndicatorState {
	readonly opts: InterruptionIndicatorStateOpts;

	// Reactive state
	#interrupted = $state(false);
	#paused = $state(false);
	#pauseReason = $state<PauseReason>(null);
	#pauseDuration = $state(0);
	#interruptedText = $state('');

	// Derived state
	readonly interrupted = $derived(this.#interrupted);
	readonly paused = $derived(this.#paused);
	readonly pauseReason = $derived(this.#pauseReason);
	readonly pauseDuration = $derived(this.#pauseDuration);
	readonly interruptedText = $derived(this.#interruptedText);

	readonly status = $derived.by((): InterruptionStatus => {
		if (this.#interrupted) return 'interrupted';
		if (this.#paused) return 'paused';
		return 'normal';
	});

	readonly isInterrupted = $derived(this.status === 'interrupted');
	readonly isPaused = $derived(this.status === 'paused');
	readonly isNormal = $derived(this.status === 'normal');

	// Props for rendering
	readonly props = $derived.by(
		() =>
			({
				[interruptionAttrs.root]: '',
				'data-status': this.status,
				'data-interrupted': this.#interrupted ? '' : undefined,
				'data-paused': this.#paused ? '' : undefined,
				'data-pause-reason': this.#pauseReason ?? undefined,
				role: 'status',
				'aria-label': 'Interruption status',
				'aria-live': 'assertive'
			}) as const
	);

	// Snippet props
	readonly snippetProps = $derived({
		interrupted: this.interrupted,
		paused: this.paused,
		pauseReason: this.pauseReason,
		pauseDuration: this.pauseDuration,
		interruptedText: this.interruptedText,
		status: this.status,
		isInterrupted: this.isInterrupted,
		isPaused: this.isPaused,
		isNormal: this.isNormal
	});

	constructor(opts: InterruptionIndicatorStateOpts) {
		this.opts = opts;

		// Watch for interruption changes
		$effect(() => {
			if (this.opts.onInterruptionChange) {
				this.opts.onInterruptionChange.current?.(this.#interrupted);
			}
		});

		// Watch for pause changes
		$effect(() => {
			if (this.opts.onPauseChange) {
				this.opts.onPauseChange.current?.(this.#paused);
			}
		});
	}

	// Event handlers (called by AgentState)

	onUserInterrupted(transcribedText?: string) {
		this.#interrupted = true;
		this.#interruptedText = transcribedText ?? '';
	}

	onSpeechPaused(synthesisId: string, reason: 'user_speaking' | 'potential_interruption') {
		this.#paused = true;
		this.#pauseReason = reason;
	}

	onSpeechResumed(synthesisId: string, pauseDuration: string) {
		this.#paused = false;
		this.#interrupted = false;
		this.#pauseReason = null;
		this.#pauseDuration = this.#parseDuration(pauseDuration);
		this.#interruptedText = '';
	}

	// Utilities

	#parseDuration(isoDuration: string): number {
		const match = isoDuration.match(/PT([\d.]+)S/);
		return match ? parseFloat(match[1]) : 0;
	}

	// Static factory method
	static create(opts: InterruptionIndicatorStateOpts = {}) {
		return new InterruptionIndicatorState(opts);
	}

	// Cleanup
	destroy() {
		// No cleanup needed
	}
}
