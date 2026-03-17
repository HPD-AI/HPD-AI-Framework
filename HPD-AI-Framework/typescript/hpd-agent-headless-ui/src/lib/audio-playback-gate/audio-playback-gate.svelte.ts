/**
 * AudioPlaybackGate - Handle browser autoplay restrictions
 *
 * Modern browsers block audio playback until a user interaction occurs.
 * This component manages the "audio gate" - detecting when audio is blocked
 * and providing a mechanism to enable it via user gesture.
 *
 *
 * @see https://developer.mozilla.org/en-US/docs/Web/API/Web_Audio_API/Best_practices#autoplay_policy
 */

import { createHPDAttrs } from '$lib/internal/attrs.js';
import type { ReadableBoxedValues } from 'svelte-toolbelt';

const audioGateAttrs = createHPDAttrs({
	component: 'audio-gate',
	parts: ['root', 'trigger', 'status']
} as const);

export type AudioGateStatus = 'blocked' | 'ready' | 'error';

export interface AudioPlaybackGateStateProps {
	/**
	 * Called when audio gate status changes
	 */
	onStatusChange?: (status: AudioGateStatus) => void;

	/**
	 * Custom AudioContext instance (optional)
	 * If not provided, one will be created when enableAudio() is called
	 */
	audioContext?: AudioContext;
}

export type AudioPlaybackGateStateOpts = ReadableBoxedValues<AudioPlaybackGateStateProps>;

export class AudioPlaybackGateState {
	readonly opts: AudioPlaybackGateStateOpts;

	// Reactive state
	#canPlayAudio = $state(false);
	#audioContext = $state<AudioContext | null>(null);
	#error = $state<Error | null>(null);

	// Derived state
	readonly canPlayAudio = $derived(this.#canPlayAudio);
	readonly hasError = $derived(this.#error !== null);
	readonly error = $derived(this.#error);

	readonly status = $derived.by((): AudioGateStatus => {
		if (this.#error) return 'error';
		if (this.#canPlayAudio) return 'ready';
		return 'blocked';
	});

	// Shared props (used in data attributes)
	readonly sharedProps = $derived.by(() => ({
		'data-status': this.status,
		'data-can-play': this.#canPlayAudio ? '' : undefined,
		'data-error': this.hasError ? '' : undefined
	}));

	// Snippet props (exposed to consumers via children snippet)
	readonly snippetProps = $derived.by(() => ({
		canPlayAudio: this.canPlayAudio,
		status: this.status,
		error: this.error,
		enableAudio: this.enableAudio
	}));

	// Full props for root element
	readonly props = $derived.by(
		() =>
			({
				...this.sharedProps,
				[audioGateAttrs.root]: '',
				role: 'status',
				'aria-label': 'Audio playback status',
				'aria-live': 'polite'
			}) as const
	);

	// Trigger button props
	readonly triggerProps = $derived.by(
		() =>
			({
				...this.sharedProps,
				[audioGateAttrs.trigger]: '',
				type: 'button' as const,
				role: 'button',
				'aria-label': 'Enable audio playback',
				disabled: this.#canPlayAudio,
				onclick: this.enableAudio
			}) as const
	);

	constructor(opts: AudioPlaybackGateStateOpts) {
		this.opts = opts;

		// Bind methods
		this.enableAudio = this.enableAudio.bind(this);
		this.destroy = this.destroy.bind(this);

		// Use provided AudioContext if available
		if (opts.audioContext?.current) {
			this.#audioContext = opts.audioContext.current;
			this.#checkAudioContextState();
		}

		// Effect to notify status changes
		$effect(() => {
			const currentStatus = this.status;
			this.opts.onStatusChange?.current?.(currentStatus);
		});
	}

	/**
	 * Check AudioContext state and update canPlayAudio flag
	 * @private
	 */
	#checkAudioContextState() {
		if (!this.#audioContext) return;

		const state = this.#audioContext.state;
		this.#canPlayAudio = state === 'running';
	}

	/**
	 * Enable audio playback
	 *
	 * MUST be called from a user gesture event (click, tap, keydown)
	 * Browsers will reject attempts to resume AudioContext without user interaction
	 *
	 * @throws {Error} If AudioContext creation or resumption fails
	 */
	async enableAudio(): Promise<void> {
		try {
			// Create AudioContext if it doesn't exist
			if (!this.#audioContext) {
				// @ts-expect-error - webkitAudioContext for Safari
				const AudioContextClass = window.AudioContext || window.webkitAudioContext;

				if (!AudioContextClass) {
					throw new Error('AudioContext not supported in this browser');
				}

				this.#audioContext = new AudioContextClass();
			}

			// Resume the context (required for autoplay policy)
			if (this.#audioContext.state === 'suspended') {
				await this.#audioContext.resume();
			}

			// Verify state
			this.#checkAudioContextState();

			if (!this.#canPlayAudio) {
				throw new Error('Failed to enable audio playback');
			}

			// Clear any previous errors
			this.#error = null;
		} catch (err) {
			this.#error = err instanceof Error ? err : new Error('Unknown error enabling audio');
			this.#canPlayAudio = false;
			throw this.#error;
		}
	}

	/**
	 * Get the AudioContext instance
	 *
	 * Useful for components that need direct access to the AudioContext
	 * (e.g., AudioVisualizer for creating AnalyserNode)
	 */
	getAudioContext(): AudioContext | null {
		return this.#audioContext;
	}

	/**
	 * Cleanup - close AudioContext
	 *
	 * Should be called when component is destroyed
	 */
	destroy(): void {
		if (this.#audioContext) {
			this.#audioContext.close();
			this.#audioContext = null;
			this.#canPlayAudio = false;
		}
	}

	/**
	 * Factory method - creates and returns state instance
	 */
	static create(opts: AudioPlaybackGateStateOpts = {}): AudioPlaybackGateState {
		return new AudioPlaybackGateState(opts);
	}
}
