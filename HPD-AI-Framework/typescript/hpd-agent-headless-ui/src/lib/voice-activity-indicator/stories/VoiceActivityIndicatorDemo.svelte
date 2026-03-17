<script lang="ts">
	import * as VoiceActivityIndicator from '../index.js';
	import { untrack } from 'svelte';

	let {
		showControls = true,
		showOutput = true,
		autoSimulate = false
	} = $props();

	let eventLog = $state<string[]>([]);

	function log(message: string) {
		untrack(() => {
			eventLog = [...eventLog.slice(-10), `[${new Date().toLocaleTimeString()}] ${message}`];
		});
	}

	// Expose state reference for simulation
	let internalState: any = $state(null);

	// Simulate voice activity
	function simulateStartSpeaking(probability: number = 0.95) {
		if (!internalState) return;

		const timestamp = new Date().toISOString();
		internalState.onVadStartOfSpeech(timestamp, probability);
		log(`VAD_START_OF_SPEECH (probability: ${(probability * 100).toFixed(0)}%)`);
	}

	function simulateStopSpeaking(duration: number = 2.5) {
		if (!internalState) return;

		const timestamp = new Date().toISOString();
		internalState.onVadEndOfSpeech(timestamp, `PT${duration}S`, 0.9);
		log(`VAD_END_OF_SPEECH (duration: ${duration}s)`);
	}

	// Auto-simulate if requested
	let hasAutoSimulated = $state(false);
	$effect(() => {
		if (autoSimulate && !hasAutoSimulated && internalState) {
			hasAutoSimulated = true;
			setTimeout(() => {
				simulateStartSpeaking(0.95);
				setTimeout(() => simulateStopSpeaking(2.5), 3000);
			}, 500);
		}
	});
</script>

<div class="demo-container">
	<div class="vad-section">
		<VoiceActivityIndicator.Root onActivityChange={(active) => log(`Activity: ${active}`)}>
			{#snippet children(state)}
				{@const _ = internalState || (internalState = state)}

				<div class="vad-indicator" data-active={state.active} data-intensity={state.intensityLevel}>
					<!-- Status Badge -->
					<div class="status-badge" data-active={state.active} data-intensity={state.intensityLevel}>
						{#if state.active}
							🎤 Active ({state.intensityLevel})
						{:else}
							⚫ Silent
						{/if}
					</div>

					<!-- Probability Meter -->
					<div class="meter-container">
						<span>Speech Probability</span>
						<div class="meter">
							<div
								class="meter-fill"
								data-intensity={state.intensityLevel}
								style="width: {state.speechProbability * 100}%"
							></div>
						</div>
						<span class="meter-label">{(state.speechProbability * 100).toFixed(0)}%</span>
					</div>

					<!-- Duration Display -->
					{#if state.duration > 0}
						<div class="duration-display">
							Last spoke for: <strong>{state.duration.toFixed(1)}s</strong>
						</div>
					{/if}

					{#if showControls}
						<!-- Simulation Controls -->
						<div class="sim-controls">
							<button onclick={() => simulateStartSpeaking(0.95)} class="sim-btn high">
								🎤 Start (High)
							</button>
							<button onclick={() => simulateStartSpeaking(0.75)} class="sim-btn medium">
								🎤 Start (Medium)
							</button>
							<button onclick={() => simulateStartSpeaking(0.5)} class="sim-btn low">
								🎤 Start (Low)
							</button>
							<button onclick={() => simulateStopSpeaking(2.5)} disabled={!state.active} class="sim-btn stop">
								⏹ Stop
							</button>
						</div>
					{/if}
				</div>
			{/snippet}
		</VoiceActivityIndicator.Root>
	</div>

	{#if showOutput}
		<div class="output-section">
			<h4>Component State:</h4>
			<pre>
Active: {internalState?.active ?? false}
Speech Probability: {((internalState?.speechProbability ?? 0) * 100).toFixed(0)}%
Intensity Level: {internalState?.intensityLevel ?? 'none'}
Duration: {(internalState?.duration ?? 0).toFixed(1)}s</pre>

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
		max-width: 600px;
		display: flex;
		flex-direction: column;
		gap: 1.5rem;
		font-family: system-ui, -apple-system, sans-serif;
	}

	.vad-section {
		background: #f5f5f5;
		border: 1px solid #ddd;
		border-radius: 8px;
		padding: 1.5rem;
	}

	.vad-indicator {
		display: flex;
		flex-direction: column;
		gap: 1rem;
	}

	.status-badge {
		display: inline-block;
		padding: 0.5rem 1rem;
		border-radius: 12px;
		font-size: 1rem;
		font-weight: 600;
		width: fit-content;
		transition: all 0.2s;
	}

	.status-badge[data-active='false'] {
		background: #e5e7eb;
		color: #6b7280;
	}

	.status-badge[data-active='true'][data-intensity='high'] {
		background: #d1fae5;
		color: #065f46;
		animation: pulse 1s ease-in-out infinite;
	}

	.status-badge[data-active='true'][data-intensity='medium'] {
		background: #fef3c7;
		color: #92400e;
	}

	.status-badge[data-active='true'][data-intensity='low'] {
		background: #dbeafe;
		color: #1e40af;
	}

	@keyframes pulse {
		0%,
		100% {
			transform: scale(1);
		}
		50% {
			transform: scale(1.05);
		}
	}

	.meter-container {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}

	.meter-container span {
		font-size: 0.875rem;
		font-weight: 500;
		color: #374151;
	}

	.meter {
		height: 24px;
		background: #e5e7eb;
		border-radius: 12px;
		overflow: hidden;
		position: relative;
	}

	.meter-fill {
		height: 100%;
		transition: width 0.3s ease, background 0.3s ease;
		border-radius: 12px;
	}

	.meter-fill[data-intensity='high'] {
		background: linear-gradient(90deg, #10b981, #059669);
	}

	.meter-fill[data-intensity='medium'] {
		background: linear-gradient(90deg, #f59e0b, #d97706);
	}

	.meter-fill[data-intensity='low'] {
		background: linear-gradient(90deg, #3b82f6, #2563eb);
	}

	.meter-fill[data-intensity='none'] {
		background: #9ca3af;
	}

	.meter-label {
		font-size: 0.875rem;
		color: #6b7280;
		text-align: right;
	}

	.duration-display {
		padding: 0.75rem;
		background: #f0fdf4;
		border: 1px solid #86efac;
		border-radius: 6px;
		font-size: 0.875rem;
		color: #166534;
	}

	.sim-controls {
		display: flex;
		gap: 0.5rem;
		flex-wrap: wrap;
	}

	.sim-btn {
		padding: 0.5rem 1rem;
		border: 1px solid #d1d5db;
		border-radius: 6px;
		background: white;
		cursor: pointer;
		font-size: 0.875rem;
		transition: all 0.15s;
	}

	.sim-btn:hover:not(:disabled) {
		background: #f9fafb;
		border-color: #9ca3af;
	}

	.sim-btn:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.sim-btn.high {
		border-color: #10b981;
		color: #065f46;
	}

	.sim-btn.medium {
		border-color: #f59e0b;
		color: #92400e;
	}

	.sim-btn.low {
		border-color: #3b82f6;
		color: #1e40af;
	}

	.sim-btn.stop {
		border-color: #ef4444;
		color: #991b1b;
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
