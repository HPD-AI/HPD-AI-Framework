/**
 * AudioPlaybackGate Type Definitions
 */

import type { HPDPrimitiveDivAttributes, HPDPrimitiveButtonAttributes } from '$lib/shared/types.js';
import type { WithChild } from 'svelte-toolbelt';
import type { AudioGateStatus } from './audio-playback-gate.svelte.ts';

/**
 * Snippet props exposed to children render functions
 */
export type AudioPlaybackGateRootSnippetProps = {
	/**
	 * Whether audio playback is enabled
	 */
	canPlayAudio: boolean;

	/**
	 * Current gate status: 'blocked' | 'ready' | 'error'
	 */
	status: AudioGateStatus;

	/**
	 * Error object if status is 'error'
	 */
	error: Error | null;

	/**
	 * Function to enable audio (must be called from user gesture)
	 */
	enableAudio: () => Promise<void>;
};

/**
 * Props for AudioPlaybackGate.Root component (without HTML attributes)
 */
export type AudioPlaybackGateRootPropsWithoutHTML = WithChild<
	{
		onStatusChange?: (status: AudioGateStatus) => void;
		audioContext?: AudioContext;
	},
	AudioPlaybackGateRootSnippetProps
>;

/**
 * Full props for AudioPlaybackGate.Root component
 */
export type AudioPlaybackGateRootProps = AudioPlaybackGateRootPropsWithoutHTML &
	Omit<HPDPrimitiveDivAttributes, keyof AudioPlaybackGateRootPropsWithoutHTML>;

/**
 * Snippet props for Trigger component
 */
export type AudioPlaybackGateTriggerSnippetProps = {
	/**
	 * Whether audio playback is enabled
	 */
	canPlayAudio: boolean;

	/**
	 * Current gate status
	 */
	status: AudioGateStatus;

	/**
	 * Whether the trigger is disabled (audio already enabled)
	 */
	disabled: boolean;
};

/**
 * Props for AudioPlaybackGate.Trigger component (without HTML attributes)
 */
export type AudioPlaybackGateTriggerPropsWithoutHTML = WithChild<
	Record<string, never>,
	AudioPlaybackGateTriggerSnippetProps
>;

/**
 * Full props for AudioPlaybackGate.Trigger component
 */
export type AudioPlaybackGateTriggerProps = AudioPlaybackGateTriggerPropsWithoutHTML &
	Omit<HPDPrimitiveButtonAttributes, keyof AudioPlaybackGateTriggerPropsWithoutHTML>;

// Re-export state types
export type { AudioGateStatus };
