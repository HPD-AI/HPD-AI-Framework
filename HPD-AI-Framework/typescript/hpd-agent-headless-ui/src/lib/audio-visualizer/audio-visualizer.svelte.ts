/**
 * AudioVisualizer - Reactive state for audio visualization
 *
 * Visualizes audio levels with frequency analysis:
 * - Multiband frequency analysis (5-7 bands)
 * - Multiple modes: bar, waveform, radial
 * - Real-time updates (60fps)
 * - Web Audio API integration
 *
 * Handles audio from:
 * - AUDIO_CHUNK events (agent speech)
 * - AnalyserNode (microphone/playback)

 */

import { createHPDAttrs } from '$lib/internal/attrs.js';
import type { ReadableBoxedValues } from 'svelte-toolbelt';

const visualizerAttrs = createHPDAttrs({
	component: 'audio-visualizer',
	parts: ['root']
} as const);

export type VisualizerMode = 'bar' | 'waveform' | 'radial';

export interface AudioVisualizerStateProps {
	/**
	 * Number of frequency bands (default: 5)
	 */
	bands?: number;

	/**
	 * Visualization mode (default: 'bar')
	 */
	mode?: VisualizerMode;

	/**
	 * Called when volumes update
	 */
	onVolumesChange?: (volumes: number[]) => void;
}

export type AudioVisualizerStateOpts = ReadableBoxedValues<AudioVisualizerStateProps>;

export class AudioVisualizerState {
	readonly opts: AudioVisualizerStateOpts;

	// Reactive state
	#volumes = $state<number[]>([]);
	#bands = $state(5);
	#mode = $state<VisualizerMode>('bar');
	#animationFrameId = $state<number | null>(null);
	#analyserNode = $state<AnalyserNode | null>(null);

	// Derived state
	readonly volumes = $derived(this.#volumes);
	readonly bands = $derived(this.#bands);
	readonly mode = $derived(this.#mode);
	readonly isActive = $derived(this.#animationFrameId !== null);

	readonly maxVolume = $derived(Math.max(...this.#volumes, 0));
	readonly avgVolume = $derived(
		this.#volumes.length > 0
			? this.#volumes.reduce((a, b) => a + b, 0) / this.#volumes.length
			: 0
	);

	// Props for rendering
	readonly props = $derived.by(
		() =>
			({
				[visualizerAttrs.root]: '',
				'data-mode': this.#mode,
				'data-bands': this.#bands,
				'data-active': this.isActive ? '' : undefined,
				role: 'img',
				'aria-label': 'Audio visualization'
			}) as const
	);

	// Snippet props
	readonly snippetProps = $derived({
		volumes: this.volumes,
		bands: this.bands,
		mode: this.mode,
		maxVolume: this.maxVolume,
		avgVolume: this.avgVolume,
		isActive: this.isActive
	});

	constructor(opts: AudioVisualizerStateOpts) {
		this.opts = opts;

		// Initialize from props
		if (opts.bands?.current !== undefined) {
			this.#bands = opts.bands.current;
		}
		if (opts.mode?.current !== undefined) {
			this.#mode = opts.mode.current;
		}

		// Initialize volumes array with zeros
		this.#volumes = new Array(this.#bands).fill(0);

		// Watch for volumes changes
		$effect(() => {
			if (this.opts.onVolumesChange) {
				this.opts.onVolumesChange.current?.(this.#volumes);
			}
		});

		// Cleanup on destroy
		$effect(() => {
			return () => this.stopVisualization();
		});
	}

	// ============================================================================
	// Visualization Control
	// ============================================================================

	/**
	 * Start visualization with an AnalyserNode
	 * Begins requestAnimationFrame loop
	 */
	startVisualization(analyserNode: AnalyserNode) {
		this.#analyserNode = analyserNode;

		// Stop existing animation if any
		if (this.#animationFrameId !== null) {
			this.stopVisualization();
		}

		const animate = () => {
			if (this.#analyserNode) {
				this.#volumes = this.#analyzeFrequencyBands(this.#analyserNode, this.#bands);
			}
			this.#animationFrameId = requestAnimationFrame(animate);
		};

		animate();
	}

	/**
	 * Stop visualization
	 * Cancels requestAnimationFrame loop
	 */
	stopVisualization() {
		if (this.#animationFrameId !== null) {
			cancelAnimationFrame(this.#animationFrameId);
			this.#animationFrameId = null;
		}
		this.#analyserNode = null;
		// Reset volumes to zero
		this.#volumes = new Array(this.#bands).fill(0);
	}

	// ============================================================================
	// Frequency Analysis ( pattern)
	// ============================================================================

	/**
	 * Analyze frequency bands from AnalyserNode
	 * Returns array of normalized volumes (0-1) for each band
	 */
	#analyzeFrequencyBands(analyserNode: AnalyserNode, bands: number): number[] {
		analyserNode.fftSize = 256;
		const bufferLength = analyserNode.frequencyBinCount;
		const dataArray = new Uint8Array(bufferLength);

		// Get frequency data
		analyserNode.getByteFrequencyData(dataArray);

		// Split into bands (like 's multiband)
		const bandSize = Math.floor(bufferLength / bands);
		const volumes: number[] = [];

		for (let i = 0; i < bands; i++) {
			const start = i * bandSize;
			const end = start + bandSize;
			const bandData = dataArray.slice(start, end);

			// Calculate average for this band
			const sum = bandData.reduce((a, b) => a + b, 0);
			const avg = sum / bandSize;

			// Normalize to 0-1 range
			volumes.push(avg / 255);
		}

		return volumes;
	}

	// ============================================================================
	// Public Methods
	// ============================================================================

	/**
	 * Update number of frequency bands
	 */
	setBands(bands: number) {
		this.#bands = Math.max(1, Math.min(bands, 32)); // Clamp 1-32
		this.#volumes = new Array(this.#bands).fill(0);
	}

	/**
	 * Update visualization mode
	 */
	setMode(mode: VisualizerMode) {
		this.#mode = mode;
	}

	// Static factory method
	static create(opts: AudioVisualizerStateOpts = {}) {
		return new AudioVisualizerState(opts);
	}

	// Cleanup
	destroy() {
		this.stopVisualization();
	}
}
