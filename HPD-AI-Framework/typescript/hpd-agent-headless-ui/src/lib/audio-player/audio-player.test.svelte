<script lang="ts">
	import * as AudioPlayer from './index.js';

	interface Props {
		useWebAudio?: boolean;
		bufferThreshold?: number;
		onStatusChange?: (status: 'idle' | 'buffering' | 'playing' | 'paused' | 'error') => void;
		onError?: (error: Error) => void;
		testId?: string;
	}

	let {
		useWebAudio = false,
		bufferThreshold = 2,
		onStatusChange,
		onError,
		testId = 'audio-player'
	}: Props = $props();

	// No need to capture state - it's exposed by Root component via data-testid
</script>

<AudioPlayer.Root {useWebAudio} {bufferThreshold} {onStatusChange} {onError} data-testid={testId}>
	{#snippet children(state)}
		{@const {
			status,
			playing,
			paused,
			buffering,
			currentTime,
			duration,
			progress,
			error,
			pause,
			resume,
			stop,
			setVolume,
			analyserNode
		} = state}
		<div data-testid="{testId}-content">
			<div data-testid="{testId}-status" data-status={status}>
				Status: {status}
			</div>

			<div data-testid="{testId}-playing" data-playing={playing}>
				Playing: {playing}
			</div>

			<div data-testid="{testId}-paused" data-paused={paused}>
				Paused: {paused}
			</div>

			<div data-testid="{testId}-buffering" data-buffering={buffering}>
				Buffering: {buffering}
			</div>

			<div data-testid="{testId}-progress">
				Progress: {progress.toFixed(2)}
			</div>

			<div data-testid="{testId}-time">
				{currentTime.toFixed(1)}s / {duration.toFixed(1)}s
			</div>

			{#if error}
				<div data-testid="{testId}-error" data-error>
					Error: {error.message}
				</div>
			{/if}

			<div data-testid="{testId}-controls">
				<button data-testid="{testId}-pause-btn" onclick={pause}>Pause</button>
				<button data-testid="{testId}-resume-btn" onclick={resume}>Resume</button>
				<button data-testid="{testId}-stop-btn" onclick={stop}>Stop</button>

				{#if useWebAudio}
					<button data-testid="{testId}-volume-btn" onclick={() => setVolume(0.5)}>
						Set Volume
					</button>
				{/if}
			</div>

			{#if analyserNode}
				<div data-testid="{testId}-analyser" data-has-analyser>
					Has AnalyserNode
				</div>
			{/if}
		</div>
	{/snippet}
</AudioPlayer.Root>
