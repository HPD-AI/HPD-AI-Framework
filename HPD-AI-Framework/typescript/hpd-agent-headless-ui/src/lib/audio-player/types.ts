/**
 * AudioPlayer Type Definitions
 */

import type { HPDPrimitiveDivAttributes } from '$lib/shared/types.js';
import type { WithChild } from 'svelte-toolbelt';
import type { AudioPlayerStatus } from './audio-player.svelte.ts';

/**
 * Snippet props exposed to children render functions
 */
export type AudioPlayerRootSnippetProps = {
	/**
	 * Whether audio is currently playing
	 */
	playing: boolean;

	/**
	 * Whether audio is paused
	 */
	paused: boolean;

	/**
	 * Whether audio is buffering
	 */
	buffering: boolean;

	/**
	 * Current playback time in seconds
	 */
	currentTime: number;

	/**
	 * Total duration in seconds
	 */
	duration: number;

	/**
	 * Playback progress (0-1)
	 */
	progress: number;

	/**
	 * Current playback status
	 */
	status: AudioPlayerStatus;

	/**
	 * Error object if status is 'error'
	 */
	error: Error | null;

	/**
	 * Whether Web Audio API is being used
	 */
	useWebAudio: boolean;

	/**
	 * Pause playback
	 */
	pause: () => void;

	/**
	 * Resume playback
	 */
	resume: () => void;

	/**
	 * Stop playback and clear queue
	 */
	stop: () => void;

	/**
	 * Set volume (Web Audio mode only)
	 * @param volume - Volume level (0-1)
	 */
	setVolume: (volume: number) => void;

	/**
	 * Get AnalyserNode for visualization (Web Audio mode only)
	 */
	analyserNode: AnalyserNode | null;

	/**
	 * @internal
	 * HPD event handlers - Called by Agent component in response to server events.
	 * Also exposed for testing and demo purposes.
	 * Not intended for end-user UI controls.
	 */
	onSynthesisStarted: (synthesisId: string, modelId?: string, voice?: string, streamId?: string) => void;
	onAudioChunk: (
		synthesisId: string,
		base64Audio: string,
		mimeType: string,
		chunkIndex: number,
		duration: string,
		isLast: boolean,
		streamId?: string
	) => void;
	onSynthesisCompleted: (synthesisId: string, wasInterrupted: boolean, totalChunks: number, deliveredChunks: number) => void;
	onSpeechPaused: (synthesisId: string, reason: 'user_speaking' | 'potential_interruption') => void;
	onSpeechResumed: (synthesisId: string, pauseDuration: string) => void;
};

/**
 * Props for AudioPlayer.Root component (without HTML attributes)
 */
export type AudioPlayerRootPropsWithoutHTML = WithChild<
	{
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
	},
	AudioPlayerRootSnippetProps
>;

/**
 * Full props for AudioPlayer.Root component
 */
export type AudioPlayerRootProps = AudioPlayerRootPropsWithoutHTML &
	Omit<HPDPrimitiveDivAttributes, keyof AudioPlayerRootPropsWithoutHTML>;

// Re-export state types
export type { AudioPlayerStatus };
