/**
 * AudioPlayer - Reactive state for TTS audio playback
 *
 * Handles audio chunk playback with support for:
 * - Streaming audio chunks (base64 → Blob)
 * - Out-of-order chunk handling (sort by chunkIndex)
 * - Simple mode: HTMLAudioElement (default)
 * - Advanced mode: Web Audio API (optional, for visualization)
 * - Pause/resume during interruptions
 * - Volume control (Web Audio mode only)
 * - Progress tracking
 *
 * Based on  patterns adapted for Svelte runes + headless design.
 */

import { createHPDAttrs } from '$lib/internal/attrs.js';
import type { ReadableBoxedValues } from 'svelte-toolbelt';

const audioPlayerAttrs = createHPDAttrs({
	component: 'audio-player',
	parts: ['root', 'progress', 'controls']
} as const);

export type AudioPlayerStatus = 'idle' | 'buffering' | 'playing' | 'paused' | 'error';

export interface AudioPlayerStateProps {
	/**
	 * Use Web Audio API instead of HTMLAudioElement
	 * Required for volume control and visualization support
	 * @default false
	 */
	useWebAudio?: boolean;

	/**
	 * Number of chunks to buffer before starting playback
	 * @default 2
	 */
	bufferThreshold?: number;

	/**
	 * Called when playback status changes
	 */
	onStatusChange?: (status: AudioPlayerStatus) => void;

	/**
	 * Called when playback error occurs
	 */
	onError?: (error: Error) => void;
}

export type AudioPlayerStateOpts = ReadableBoxedValues<AudioPlayerStateProps>;

interface AudioChunk {
	index: number;
	blob: Blob;
	duration: number;
	isLast: boolean;
}

export class AudioPlayerState {
	readonly opts: AudioPlayerStateOpts;
	readonly BUFFER_THRESHOLD: number;
	readonly useWebAudio: boolean;

	// Reactive state
	#audioQueue = $state<AudioChunk[]>([]);
	#playing = $state(false);
	#paused = $state(false);
	#buffering = $state(false);
	#currentTime = $state(0);
	#duration = $state(0);
	#synthesisId = $state<string | null>(null);
	#error = $state<Error | null>(null);

	// Web Audio state (only if useWebAudio = true)
	#audioContext = $state<AudioContext | null>(null);
	#gainNode = $state<GainNode | null>(null);
	#analyserNode = $state<AnalyserNode | null>(null);
	#currentSource = $state<AudioBufferSourceNode | null>(null);

	// HTMLAudioElement state (only if useWebAudio = false)
	#currentAudio = $state<HTMLAudioElement | null>(null);

	// Playback control
	#isPlayingChunk = $state(false);

	// Derived state
	readonly playing = $derived(this.#playing);
	readonly paused = $derived(this.#paused);
	readonly buffering = $derived(this.#buffering);
	readonly currentTime = $derived(this.#currentTime);
	readonly duration = $derived(this.#duration);
	readonly hasError = $derived(this.#error !== null);
	readonly error = $derived(this.#error);

	readonly progress = $derived(
		this.#duration > 0 ? this.#currentTime / this.#duration : 0
	);

	readonly status = $derived.by((): AudioPlayerStatus => {
		if (this.#error) return 'error';
		// Show buffering if explicitly buffering, or if we have an active synthesis with insufficient chunks
		if (this.#buffering) return 'buffering';
		if (this.#synthesisId && this.#audioQueue.length < this.BUFFER_THRESHOLD && this.#audioQueue.length === 0) return 'buffering';
		if (this.#paused) return 'paused';
		if (this.#playing) return 'playing';
		return 'idle';
	});

	// Shared props (used in data attributes)
	readonly sharedProps = $derived.by(() => ({
		'data-status': this.status,
		'data-playing': this.#playing ? '' : undefined,
		'data-paused': this.#paused ? '' : undefined,
		'data-buffering': this.#buffering ? '' : undefined,
		'data-error': this.hasError ? '' : undefined,
		'data-web-audio': this.useWebAudio ? '' : undefined
	}));

	// Snippet props (exposed to consumers via children snippet)
	readonly snippetProps = $derived.by(() => ({
		// State (read-only)
		playing: this.playing,
		paused: this.paused,
		buffering: this.buffering,
		currentTime: this.currentTime,
		duration: this.duration,
		progress: this.progress,
		status: this.status,
		error: this.error,
		useWebAudio: this.useWebAudio,

		// User controls
		pause: this.pause,
		resume: this.resume,
		stop: this.stop,
		setVolume: this.setVolume,

		// Web Audio API
		analyserNode: this.#analyserNode,

		// HPD Event handlers - exposed for Agent component coordination and testing/demos
		// In production: Agent.Root calls these in response to HPD server events
		// In demos: Accessed for simulation purposes
		// Not intended for end-user UI controls
		onSynthesisStarted: this.onSynthesisStarted.bind(this),
		onAudioChunk: this.onAudioChunk.bind(this),
		onSynthesisCompleted: this.onSynthesisCompleted.bind(this),
		onSpeechPaused: this.onSpeechPaused.bind(this),
		onSpeechResumed: this.onSpeechResumed.bind(this)
	}));

	// Full props for root element
	readonly props = $derived.by(
		() =>
			({
				...this.sharedProps,
				[audioPlayerAttrs.root]: '',
				role: 'region',
				'aria-label': 'Audio playback',
				'aria-busy': this.#buffering,
				'aria-live': 'polite'
			}) as const
	);

	constructor(opts: AudioPlayerStateOpts) {
		this.opts = opts;
		this.useWebAudio = opts.useWebAudio?.current ?? false;
		this.BUFFER_THRESHOLD = opts.bufferThreshold?.current ?? 2;

		// Bind methods
		this.pause = this.pause.bind(this);
		this.resume = this.resume.bind(this);
		this.stop = this.stop.bind(this);
		this.setVolume = this.setVolume.bind(this);

		// Initialize Web Audio if enabled
		if (this.useWebAudio) {
			this.#initWebAudio();
		}

		// Effect to notify status changes
		$effect(() => {
			const currentStatus = this.status;
			this.opts.onStatusChange?.current?.(currentStatus);
		});

		// Effect to notify errors
		$effect(() => {
			const currentError = this.#error;
			if (currentError) {
				this.opts.onError?.current?.(currentError);
			}
		});
	}

	/**
	 * Initialize Web Audio API
	 * @private
	 */
	#initWebAudio() {
		try {
			this.#audioContext = new AudioContext({ latencyHint: 'interactive' });
			this.#gainNode = this.#audioContext.createGain();
			this.#analyserNode = this.#audioContext.createAnalyser();
			this.#analyserNode.fftSize = 256;

			// Chain: analyser → gain → destination
			this.#analyserNode.connect(this.#gainNode);
			this.#gainNode.connect(this.#audioContext.destination);
		} catch (error) {
			this.#error = error instanceof Error ? error : new Error('Failed to initialize Web Audio');
			console.error('[AudioPlayerState] Web Audio init failed:', error);
		}
	}

	// ============================================================================
	// HPD Event Handlers (called by AgentState)
	// ============================================================================

	/**
	 * Handle SYNTHESIS_STARTED event
	 */
	onSynthesisStarted(synthesisId: string, modelId?: string, voice?: string, streamId?: string) {
		this.#synthesisId = synthesisId;
		this.#audioQueue = []; // Clear queue for new synthesis
		this.#buffering = true;
		this.#playing = false;
		this.#paused = false;
		this.#error = null;
		this.#currentTime = 0;
		this.#duration = 0;

		console.log('[AudioPlayerState] Synthesis started', { synthesisId, modelId, voice, streamId });
	}

	/**
	 * Handle AUDIO_CHUNK event
	 */
	onAudioChunk(
		synthesisId: string,
		base64Audio: string,
		mimeType: string,
		chunkIndex: number,
		duration: string,
		isLast: boolean,
		streamId?: string
	) {
		if (this.#synthesisId !== synthesisId) return;

		try {
			// Decode base64 to Blob
			const blob = this.#decodeBase64ToBlob(base64Audio, mimeType);
			const chunkDuration = this.#parseDuration(duration);

			// Create chunk
			const chunk: AudioChunk = {
				index: chunkIndex,
				blob,
				duration: chunkDuration,
				isLast
			};

			// Add to queue and sort (handles out-of-order)
			this.#audioQueue.push(chunk);
			this.#audioQueue.sort((a, b) => a.index - b.index);

			console.log('[AudioPlayerState] Audio chunk queued', {
				chunkIndex,
				queueLength: this.#audioQueue.length,
				isLast
			});

			// Start playback when buffered
			if (
				this.#audioQueue.length >= this.BUFFER_THRESHOLD &&
				!this.#playing &&
				!this.#isPlayingChunk
			) {
				this.#buffering = false;
				this.#playing = true;
				this.#playNextChunk();
			}
		} catch (error) {
			this.#error = error instanceof Error ? error : new Error('Failed to process audio chunk');
			console.error('[AudioPlayerState] Chunk processing error:', error);
		}
	}

	/**
	 * Handle SYNTHESIS_COMPLETED event
	 */
	onSynthesisCompleted(
		synthesisId: string,
		wasInterrupted: boolean,
		totalChunks: number,
		deliveredChunks: number,
		streamId?: string
	) {
		if (this.#synthesisId !== synthesisId) return;

		this.#buffering = false;

		console.log('[AudioPlayerState] Synthesis completed', {
			wasInterrupted,
			totalChunks,
			deliveredChunks,
			remainingChunks: this.#audioQueue.length
		});

		// If interrupted, stop playback
		if (wasInterrupted) {
			this.stop();
		}
	}

	/**
	 * Handle SPEECH_PAUSED event
	 */
	onSpeechPaused(synthesisId: string, reason: 'user_speaking' | 'potential_interruption') {
		if (this.#synthesisId !== synthesisId) return;

		console.log('[AudioPlayerState] Speech paused', { reason });
		this.pause();
	}

	/**
	 * Handle SPEECH_RESUMED event
	 */
	onSpeechResumed(synthesisId: string, pauseDuration: string) {
		if (this.#synthesisId !== synthesisId) return;

		console.log('[AudioPlayerState] Speech resumed', { pauseDuration });
		this.resume();
	}

	// ============================================================================
	// Playback Methods
	// ============================================================================

	/**
	 * Play next chunk from queue
	 * @private
	 */
	async #playNextChunk(): Promise<void> {
		// Check if paused or stopped
		if (this.#paused || !this.#playing) {
			return;
		}

		// Get next chunk
		const chunk = this.#audioQueue.shift();
		if (!chunk) {
			// Queue is empty, playback complete
			this.#playing = false;
			this.#isPlayingChunk = false;
			console.log('[AudioPlayerState] Playback complete');
			return;
		}

		this.#isPlayingChunk = true;

		try {
			if (this.useWebAudio) {
				await this.#playChunkWithWebAudio(chunk.blob);
			} else {
				await this.#playChunkSimple(chunk.blob);
			}

			// Chain to next chunk (if not paused)
			if (!this.#paused && this.#playing) {
				this.#playNextChunk();
			} else {
				this.#isPlayingChunk = false;
			}
		} catch (error) {
			this.#error = error instanceof Error ? error : new Error('Playback error');
			this.#playing = false;
			this.#isPlayingChunk = false;
			console.error('[AudioPlayerState] Playback error:', error);
		}
	}

	/**
	 * Play chunk with HTMLAudioElement (simple mode)
	 * @private
	 */
	#playChunkSimple(blob: Blob): Promise<void> {
		return new Promise((resolve, reject) => {
			const audio = new Audio(URL.createObjectURL(blob));
			this.#currentAudio = audio;

			audio.addEventListener('ended', () => {
				URL.revokeObjectURL(audio.src); // Cleanup
				this.#currentAudio = null;
				resolve();
			});

			audio.addEventListener('error', (e) => {
				URL.revokeObjectURL(audio.src);
				this.#currentAudio = null;
				reject(e);
			});

			audio.addEventListener('timeupdate', () => {
				this.#currentTime = audio.currentTime;
				this.#duration = audio.duration || 0;
			});

			audio.play().catch(reject);
		});
	}

	/**
	 * Play chunk with Web Audio API (advanced mode)
	 * @private
	 */
	async #playChunkWithWebAudio(blob: Blob): Promise<void> {
		if (!this.#audioContext) {
			throw new Error('AudioContext not initialized');
		}

		const arrayBuffer = await blob.arrayBuffer();
		const audioBuffer = await this.#audioContext.decodeAudioData(arrayBuffer);

		const source = this.#audioContext.createBufferSource();
		source.buffer = audioBuffer;
		source.connect(this.#analyserNode!);

		this.#currentSource = source;
		this.#duration = audioBuffer.duration;

		return new Promise((resolve) => {
			source.onended = () => {
				this.#currentSource = null;
				resolve();
			};
			source.start();
		});
	}

	// ============================================================================
	// Public Control Methods
	// ============================================================================

	/**
	 * Pause playback
	 */
	pause(): void {
		if (!this.#playing || this.#paused) return;

		this.#paused = true;

		// Stop current audio
		if (this.useWebAudio) {
			this.#currentSource?.stop();
			this.#currentSource = null;
		} else {
			if (this.#currentAudio) {
				this.#currentAudio.pause();
			}
		}

		console.log('[AudioPlayerState] Paused');
	}

	/**
	 * Resume playback
	 */
	resume(): void {
		if (!this.#paused) return;

		this.#paused = false;

		// Resume playback
		if (this.#audioQueue.length > 0 && !this.#isPlayingChunk) {
			this.#playing = true;
			this.#playNextChunk();
		}

		console.log('[AudioPlayerState] Resumed');
	}

	/**
	 * Stop playback and clear queue
	 */
	stop(): void {
		this.#playing = false;
		this.#paused = false;
		this.#buffering = false;
		this.#currentTime = 0;
		this.#audioQueue = [];

		// Stop current audio
		if (this.useWebAudio) {
			this.#currentSource?.stop();
			this.#currentSource = null;
		} else {
			if (this.#currentAudio) {
				this.#currentAudio.pause();
				this.#currentAudio = null;
			}
		}

		console.log('[AudioPlayerState] Stopped');
	}

	/**
	 * Set volume (Web Audio mode only)
	 * @param volume - Volume level (0-1)
	 */
	setVolume(volume: number): void {
		if (!this.useWebAudio || !this.#gainNode) {
			console.warn('[AudioPlayerState] Volume control only available in Web Audio mode');
			return;
		}

		const clampedVolume = Math.max(0, Math.min(1, volume));
		this.#gainNode.gain.setTargetAtTime(clampedVolume, this.#audioContext!.currentTime, 0.1);
		console.log('[AudioPlayerState] Volume set to', clampedVolume);
	}

	/**
	 * Get AnalyserNode for visualization (Web Audio mode only)
	 */
	getAnalyserNode(): AnalyserNode | null {
		return this.#analyserNode;
	}

	// ============================================================================
	// Utility Methods
	// ============================================================================

	/**
	 * Decode base64 audio to Blob
	 * @private
	 */
	#decodeBase64ToBlob(base64: string, mimeType: string): Blob {
		const binaryString = atob(base64);
		const bytes = new Uint8Array(binaryString.length);
		for (let i = 0; i < binaryString.length; i++) {
			bytes[i] = binaryString.charCodeAt(i);
		}
		return new Blob([bytes], { type: mimeType });
	}

	/**
	 * Parse ISO 8601 duration to seconds
	 * @private
	 */
	#parseDuration(isoDuration: string): number {
		// Parse ISO 8601 duration (PT0.5S → 0.5 seconds)
		const match = isoDuration.match(/PT([\d.]+)S/);
		return match ? parseFloat(match[1]) : 0;
	}

	/**
	 * Cleanup - close AudioContext and stop playback
	 */
	destroy(): void {
		this.stop();

		if (this.#audioContext) {
			this.#audioContext.close();
			this.#audioContext = null;
			this.#gainNode = null;
			this.#analyserNode = null;
		}

		console.log('[AudioPlayerState] Destroyed');
	}

	/**
	 * Factory method - creates and returns state instance
	 */
	static create(opts: AudioPlayerStateOpts): AudioPlayerState {
		return new AudioPlayerState(opts);
	}
}
