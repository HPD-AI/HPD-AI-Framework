<script lang="ts">
	import * as AudioVisualizer from '../index.js';
	import { untrack } from 'svelte';

	let {
		showControls = true,
		showOutput = true,
		autoStart = false,
		bands = 5,
		mode = 'bar' as 'bar' | 'waveform' | 'radial'
	} = $props();

	let eventLog = $state<string[]>([]);

	function log(message: string) {
		untrack(() => {
			eventLog = [...eventLog.slice(-10), `[${new Date().toLocaleTimeString()}] ${message}`];
		});
	}

	// Expose state reference
	let visualizerState: any = $state(null);

	// Audio context for simulation
	let audioContext: AudioContext | null = $state(null);
	let analyserNode: AnalyserNode | null = $state(null);
	let oscillator: OscillatorNode | null = $state(null);
	let gainNode: GainNode | null = $state(null);

	// Create audio context for demo
	function createAudioContext() {
		if (audioContext) return;

		audioContext = new AudioContext();
		analyserNode = audioContext.createAnalyser();
		analyserNode.fftSize = 256;

		// Create oscillator for demo audio
		oscillator = audioContext.createOscillator();
		oscillator.type = 'sine';
		oscillator.frequency.value = 440; // A4 note

		gainNode = audioContext.createGain();
		gainNode.gain.value = 0.3;

		// Connect: oscillator → gain → analyser → destination
		oscillator.connect(gainNode);
		gainNode.connect(analyserNode);
		analyserNode.connect(audioContext.destination);

		oscillator.start();
		log('Audio context created');
	}

	// Start visualization
	function startVisualization() {
		if (!analyserNode) {
			createAudioContext();
		}

		if (analyserNode && visualizerState) {
			visualizerState.startVisualization(analyserNode);
			log('Visualization started');
		}
	}

	// Stop visualization
	function stopVisualization() {
		if (visualizerState) {
			visualizerState.stopVisualization();
			log('Visualization stopped');
		}
	}

	// Sweep frequency
	function sweepFrequency() {
		if (!oscillator) return;

		const startFreq = 200;
		const endFreq = 800;
		const duration = 2;

		oscillator.frequency.setValueAtTime(startFreq, audioContext!.currentTime);
		oscillator.frequency.linearRampToValueAtTime(endFreq, audioContext!.currentTime + duration);
		log(`Frequency sweep: ${startFreq}Hz → ${endFreq}Hz`);
	}

	// Change oscillator type
	function changeWaveType(type: OscillatorType) {
		if (!oscillator) return;

		oscillator.type = type;
		log(`Wave type: ${type}`);
	}

	// Auto-start if requested
	let hasAutoStarted = $state(false);
	$effect(() => {
		if (autoStart && !hasAutoStarted && visualizerState) {
			hasAutoStarted = true;
			setTimeout(() => startVisualization(), 500);
		}
	});

	// Cleanup on unmount
	$effect(() => {
		return () => {
			oscillator?.stop();
			audioContext?.close();
		};
	});
</script>

<div class="demo-container">
	<div class="visualizer-section">
		<AudioVisualizer.Root {bands} {mode} onVolumesChange={(volumes) => log(`Volumes updated: ${volumes.map(v => (v * 100).toFixed(0)).join(', ')}`)}>
			{#snippet children(state)}
				{@const _ = visualizerState || (visualizerState = state)}

				<div class="visualizer-container" data-mode={state.mode}>
					<!-- Bars Visualization -->
					<div class="bars">
						{#each state.volumes as volume, i}
							{@const height = Math.max(volume * 100, 2)}
							{@const hue = 200 + (i * 40)}
							{@const intensity = Math.min(volume * 2, 1)}
							<div
								class="bar"
								style="
									height: {height}%;
									background: hsl({hue}, 80%, {50 + intensity * 30}%);
								"
								data-band={i}
							></div>
						{/each}
					</div>

					<!-- Stats -->
					<div class="stats">
						<span>Max: {(state.maxVolume * 100).toFixed(0)}%</span>
						<span>Avg: {(state.avgVolume * 100).toFixed(0)}%</span>
						<span>{state.isActive ? '🔊 Active' : '🔇 Inactive'}</span>
					</div>

					{#if showControls}
						<!-- Controls -->
						<div class="controls">
							{#if !state.isActive}
								<button onclick={startVisualization} class="btn start">
									▶️ Start
								</button>
							{:else}
								<button onclick={stopVisualization} class="btn stop">
									⏹ Stop
								</button>
							{/if}

							<button onclick={sweepFrequency} disabled={!state.isActive} class="btn sweep">
								🎵 Sweep
							</button>

							<button onclick={() => changeWaveType('sine')} disabled={!state.isActive} class="btn">
								Sine
							</button>
							<button onclick={() => changeWaveType('square')} disabled={!state.isActive} class="btn">
								Square
							</button>
							<button onclick={() => changeWaveType('sawtooth')} disabled={!state.isActive} class="btn">
								Sawtooth
							</button>
						</div>
					{/if}
				</div>
			{/snippet}
		</AudioVisualizer.Root>
	</div>

	{#if showOutput}
		<div class="output-section">
			<h4>Component State:</h4>
			<pre>
Bands: {visualizerState?.bands ?? bands}
Mode: {visualizerState?.mode ?? mode}
Active: {visualizerState?.isActive ?? false}
Max Volume: {((visualizerState?.maxVolume ?? 0) * 100).toFixed(0)}%
Avg Volume: {((visualizerState?.avgVolume ?? 0) * 100).toFixed(0)}%</pre>

			{#if eventLog.length > 0}
				<h4>Event Log:</h4>
				<div class="log">
					{#each eventLog.slice(-6) as logEntry}
						<div class="log-entry">{logEntry}</div>
					{/each}
				</div>
			{/if}
		</div>
	{/if}
</div>

<style>
	.demo-container {
		max-width: 600px;
		display: flex;
		flex-direction: column;
		gap: 1.5rem;
		font-family: system-ui, -apple-system, sans-serif;
	}

	.visualizer-section {
		background: #1a1a1a;
		border: 1px solid #333;
		border-radius: 8px;
		padding: 1.5rem;
	}

	.visualizer-container {
		display: flex;
		flex-direction: column;
		gap: 1rem;
	}

	.bars {
		display: flex;
		gap: 4px;
		align-items: flex-end;
		height: 150px;
		padding: 0.5rem;
		background: #0a0a0a;
		border-radius: 6px;
	}

	.bar {
		flex: 1;
		min-height: 2%;
		border-radius: 2px 2px 0 0;
		transition: height 0.05s ease-out;
	}

	.stats {
		display: flex;
		gap: 1rem;
		justify-content: center;
		color: #999;
		font-size: 0.875rem;
		font-weight: 500;
	}

	.controls {
		display: flex;
		gap: 0.5rem;
		flex-wrap: wrap;
		justify-content: center;
	}

	.btn {
		padding: 0.5rem 1rem;
		border: 1px solid #444;
		border-radius: 6px;
		background: #2a2a2a;
		color: #fff;
		cursor: pointer;
		font-size: 0.875rem;
		transition: all 0.15s;
	}

	.btn:hover:not(:disabled) {
		background: #3a3a3a;
		border-color: #555;
	}

	.btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.btn.start {
		border-color: #10b981;
		color: #10b981;
	}

	.btn.stop {
		border-color: #ef4444;
		color: #ef4444;
	}

	.btn.sweep {
		border-color: #a855f7;
		color: #a855f7;
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
		max-height: 150px;
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
