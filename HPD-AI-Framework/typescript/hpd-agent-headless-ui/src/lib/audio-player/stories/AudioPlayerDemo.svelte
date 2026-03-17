<script lang="ts">
	import * as AudioPlayer from '../index.js';
	import { boxWith } from 'svelte-toolbelt';
	import { untrack } from 'svelte';

	let {
		useWebAudio = false,
		bufferThreshold = 2,
		showControls = true,
		showOutput = true,
		autoStart = false
	} = $props();

	let eventLog = $state<string[]>([]);

	function log(message: string) {
		untrack(() => {
			eventLog = [...eventLog.slice(-10), `[${new Date().toLocaleTimeString()}] ${message}`];
		});
	}

	// Capture state reference for simulation (passed via snippet)
	let internalState: any = $state(null);

	/**
	 * Generate a valid silent WAV file
	 * @param durationSeconds Duration in seconds
	 * @returns Base64-encoded WAV file
	 */
	function generateSilentWAV(durationSeconds: number): string {
		const sampleRate = 44100;
		const numChannels = 1;
		const bitsPerSample = 16;
		const numSamples = Math.floor(sampleRate * durationSeconds);
		const dataSize = numSamples * numChannels * (bitsPerSample / 8);
		const fileSize = 44 + dataSize;

		const buffer = new ArrayBuffer(fileSize);
		const view = new DataView(buffer);

		// RIFF header
		view.setUint32(0, 0x52494646, false); // 'RIFF'
		view.setUint32(4, fileSize - 8, true); // File size - 8
		view.setUint32(8, 0x57415645, false); // 'WAVE'

		// fmt chunk
		view.setUint32(12, 0x666d7420, false); // 'fmt '
		view.setUint32(16, 16, true); // Chunk size
		view.setUint16(20, 1, true); // Audio format (PCM)
		view.setUint16(22, numChannels, true); // Number of channels
		view.setUint32(24, sampleRate, true); // Sample rate
		view.setUint32(28, sampleRate * numChannels * (bitsPerSample / 8), true); // Byte rate
		view.setUint16(32, numChannels * (bitsPerSample / 8), true); // Block align
		view.setUint16(34, bitsPerSample, true); // Bits per sample

		// data chunk
		view.setUint32(36, 0x64617461, false); // 'data'
		view.setUint32(40, dataSize, true); // Data size

		// Silent audio data (all zeros)
		for (let i = 44; i < fileSize; i++) {
			view.setUint8(i, 0);
		}

		// Convert to base64
		const bytes = new Uint8Array(buffer);
		let binary = '';
		for (let i = 0; i < bytes.length; i++) {
			binary += String.fromCharCode(bytes[i]);
		}
		return btoa(binary);
	}

	// Simulate audio chunk streaming
	function simulateAudioPlayback() {
		if (!internalState) return;

		log('Starting simulation...');

		// Start synthesis
		internalState.onSynthesisStarted('sim-001', 'tts-1', 'alloy');
		log('SYNTHESIS_STARTED');

		// Simulate chunks arriving
		const totalChunks = 5;
		for (let i = 0; i < totalChunks; i++) {
			setTimeout(() => {
				if (!internalState) return;

				// Generate valid silent WAV file (0.5 seconds each)
				const base64Audio = generateSilentWAV(0.5);
				internalState.onAudioChunk(
					'sim-001',
					base64Audio,
					'audio/wav',
					i,
					'PT0.5S',
					i === totalChunks - 1
				);
				log(`AUDIO_CHUNK ${i + 1}/${totalChunks}`);

				if (i === totalChunks - 1) {
					setTimeout(() => {
						if (internalState) {
							internalState.onSynthesisCompleted('sim-001', false, totalChunks, totalChunks);
							log('SYNTHESIS_COMPLETED');
						}
					}, 1000);
				}
			}, i * 500);
		}
	}

	function simulateInterruption() {
		if (!internalState) return;

		log('Simulating interruption...');
		internalState.onSpeechPaused('sim-001', 'user_speaking');
		log('SPEECH_PAUSED');

		setTimeout(() => {
			if (internalState) {
				internalState.onSpeechResumed('sim-001', 'PT2S');
				log('SPEECH_RESUMED');
			}
		}, 2000);
	}

	// Auto-start effect - runs when state becomes available
	let hasAutoStarted = $state(false);
	$effect(() => {
		if (autoStart && internalState && !hasAutoStarted) {
			hasAutoStarted = true;
			setTimeout(() => simulateAudioPlayback(), 300);
		}
	});
</script>

<div class="demo-container">
	<div class="player-section">
		<AudioPlayer.Root
			{useWebAudio}
			{bufferThreshold}
			onStatusChange={(s) => log(`Status: ${s}`)}
			onError={(error) => log(`Error: ${error.message}`)}
		>
			{#snippet children(state)}
				{@const _ = internalState || (internalState = state)}
				<div class="audio-player" data-status={state.status}>
					<!-- Status Badge -->
					<div class="status-badge" data-status={state.status}>
						{state.status}
					</div>

					<!-- Progress Bar -->
					<div class="progress-container">
						<div class="progress-bar">
							<div class="progress-fill" style="width: {state.progress * 100}%"></div>
						</div>
						<div class="time-display">
							{state.currentTime.toFixed(1)}s / {state.duration.toFixed(1)}s
							{#if state.duration > 0}
								({(state.progress * 100).toFixed(0)}%)
							{/if}
						</div>
					</div>

					{#if showControls}
						<!-- Playback Controls -->
						<div class="controls">
							<button onclick={state.pause} disabled={!state.playing || state.paused} class="control-btn">
								⏸ Pause
							</button>
							<button onclick={state.resume} disabled={!state.paused} class="control-btn"> ▶ Resume </button>
							<button onclick={state.stop} disabled={state.status === 'idle'} class="control-btn stop-btn">
								⏹ Stop
							</button>
						</div>

						<!-- Web Audio Controls -->
						{#if useWebAudio}
							<div class="advanced-controls">
								<label>
									Volume:
									<input
										type="range"
										min="0"
										max="1"
										step="0.1"
										value="1"
										oninput={(e) => state.setVolume(parseFloat(e.currentTarget.value))}
										class="volume-slider"
									/>
								</label>

								{#if state.analyserNode}
									<div class="analyser-badge">🎵 AnalyserNode Available</div>
								{/if}
							</div>
						{/if}

						<!-- Simulation Controls -->
						<div class="sim-controls">
							<button onclick={simulateAudioPlayback} class="sim-btn"> ▶ Simulate Playback </button>
							<button onclick={simulateInterruption} disabled={!state.playing} class="sim-btn">
								⚠ Simulate Interruption
							</button>
						</div>
					{/if}
				</div>
			{/snippet}
		</AudioPlayer.Root>
	</div>

	{#if showOutput}
		<div class="output-section">
			<h4>Component State:</h4>
			<pre>
Status: {internalState?.status ?? 'idle'}
Playing: {internalState?.playing ?? false}
Paused: {internalState?.paused ?? false}
Buffering: {internalState?.buffering ?? false}
Progress: {((internalState?.progress ?? 0) * 100).toFixed(0)}%
Web Audio: {useWebAudio}
Buffer Threshold: {bufferThreshold}</pre>

			{#if eventLog.length > 0}
				<h4>Event Log:</h4>
				<div class="log">
					{#each eventLog.slice(-8) as logEntry}
						<div class="log-entry">{logEntry}</div>
					{/each}
				</div>
			{/if}
		</div>
	{/if}
</div>

<style>
	.demo-container {
		max-width: 700px;
		display: flex;
		flex-direction: column;
		gap: 1.5rem;
		font-family: system-ui, -apple-system, sans-serif;
	}

	.player-section {
		background: #f5f5f5;
		border: 1px solid #ddd;
		border-radius: 8px;
		padding: 1.5rem;
	}

	.audio-player {
		display: flex;
		flex-direction: column;
		gap: 1rem;
	}

	.status-badge {
		display: inline-block;
		padding: 0.25rem 0.75rem;
		border-radius: 12px;
		font-size: 0.875rem;
		font-weight: 600;
		text-transform: uppercase;
		width: fit-content;
	}

	.status-badge[data-status='idle'] {
		background: #e5e7eb;
		color: #6b7280;
	}

	.status-badge[data-status='buffering'] {
		background: #fef3c7;
		color: #92400e;
	}

	.status-badge[data-status='playing'] {
		background: #d1fae5;
		color: #065f46;
	}

	.status-badge[data-status='paused'] {
		background: #dbeafe;
		color: #1e40af;
	}

	.status-badge[data-status='error'] {
		background: #fee2e2;
		color: #991b1b;
	}

	.progress-container {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}

	.progress-bar {
		height: 8px;
		background: #e5e7eb;
		border-radius: 4px;
		overflow: hidden;
	}

	.progress-fill {
		height: 100%;
		background: linear-gradient(90deg, #3b82f6, #8b5cf6);
		transition: width 0.2s ease;
	}

	.time-display {
		font-size: 0.875rem;
		color: #6b7280;
		text-align: right;
	}

	.controls,
	.advanced-controls,
	.sim-controls {
		display: flex;
		gap: 0.5rem;
		flex-wrap: wrap;
	}

	.control-btn,
	.sim-btn {
		padding: 0.5rem 1rem;
		border: 1px solid #d1d5db;
		border-radius: 6px;
		background: white;
		cursor: pointer;
		font-size: 0.875rem;
		transition: all 0.15s;
	}

	.control-btn:hover:not(:disabled),
	.sim-btn:hover:not(:disabled) {
		background: #f9fafb;
		border-color: #9ca3af;
	}

	.control-btn:disabled,
	.sim-btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.stop-btn {
		color: #dc2626;
	}

	.sim-btn {
		background: #eff6ff;
		border-color: #3b82f6;
		color: #1e40af;
	}

	.volume-slider {
		margin-left: 0.5rem;
	}

	.analyser-badge {
		padding: 0.5rem 1rem;
		background: #f0fdf4;
		border: 1px solid #86efac;
		border-radius: 6px;
		font-size: 0.875rem;
		color: #166534;
	}

	.output-section {
		background: #fafafa;
		border: 1px solid #e5e7eb;
		border-radius: 8px;
		padding: 1rem;
	}

	.output-section h4 {
		margin: 0 0 0.5rem 0;
		font-size: 0.875rem;
		font-weight: 600;
		color: #374151;
	}

	.output-section pre {
		background: white;
		border: 1px solid #e5e7eb;
		border-radius: 4px;
		padding: 0.75rem;
		font-size: 0.8125rem;
		line-height: 1.5;
		margin: 0 0 1rem 0;
		overflow-x: auto;
	}

	.log {
		display: flex;
		flex-direction: column;
		gap: 0.25rem;
		max-height: 200px;
		overflow-y: auto;
	}

	.log-entry {
		padding: 0.5rem;
		background: white;
		border: 1px solid #e5e7eb;
		border-radius: 4px;
		font-size: 0.75rem;
		font-family: 'Monaco', 'Courier New', monospace;
	}
</style>
