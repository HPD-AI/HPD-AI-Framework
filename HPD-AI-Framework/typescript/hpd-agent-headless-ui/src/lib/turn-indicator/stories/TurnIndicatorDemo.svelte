<script lang="ts">
	import * as TurnIndicator from '../index.js';
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

	// Simulate turn events
	function simulateTurnDetected(probability: number, method: 'heuristic' | 'ml' | 'manual' | 'timeout') {
		if (!internalState) return;

		const transcribedText = 'How can I help you?';
		const silenceDuration = 'PT1.5S';
		internalState.onTurnDetected(transcribedText, probability, silenceDuration, method);
		log(`TURN_DETECTED (probability: ${(probability * 100).toFixed(0)}%, method: ${method})`);
	}

	function simulateVadStart() {
		if (!internalState) return;

		const timestamp = new Date().toISOString();
		internalState.onVadStartOfSpeech(timestamp, 0.95);
		log(`VAD_START_OF_SPEECH (user turn)`);
	}

	function simulateSynthesisStart() {
		if (!internalState) return;

		const synthesisId = 'synth-123';
		internalState.onSynthesisStarted(synthesisId, 'tts-1', 'alloy');
		log(`SYNTHESIS_STARTED (agent turn)`);
	}

	// Auto-simulate if requested
	let hasAutoSimulated = $state(false);
	$effect(() => {
		if (autoSimulate && !hasAutoSimulated && internalState) {
			hasAutoSimulated = true;
			setTimeout(() => {
				simulateVadStart();
				setTimeout(() => simulateTurnDetected(0.95, 'ml'), 2000);
				setTimeout(() => simulateSynthesisStart(), 3000);
			}, 500);
		}
	});
</script>

<div class="demo-container">
	<div class="turn-section">
		<TurnIndicator.Root onTurnChange={(turn) => log(`Turn: ${turn}`)}>
			{#snippet children(state)}
				{@const _ = internalState || (internalState = state)}

				<div class="turn-indicator" data-turn={state.currentTurn}>
					<!-- Turn Badge -->
					<div class="turn-badge" data-turn={state.currentTurn}>
						{#if state.isUserTurn}
							🎤 Your Turn
						{:else if state.isAgentTurn}
							🤖 Agent's Turn
						{:else}
							⚪ Unknown
						{/if}
					</div>

					<!-- Details Panel -->
					<div class="details-panel">
						{#if state.completionProbability > 0}
							<div class="detail probability">
								<strong>Completion Confidence:</strong> {(state.completionProbability * 100).toFixed(0)}%
							</div>
						{/if}

						{#if state.detectionMethod}
							<div class="detail method">
								<strong>Detection Method:</strong> {state.detectionMethod.toUpperCase()}
							</div>
						{/if}
					</div>

					{#if showControls}
						<!-- Simulation Controls -->
						<div class="sim-controls">
							<button onclick={() => simulateVadStart()} class="sim-btn user">
								🎤 User Start Speaking
							</button>
							<button onclick={() => simulateSynthesisStart()} class="sim-btn agent">
								🤖 Agent Start Speaking
							</button>
							<button onclick={() => simulateTurnDetected(0.95, 'ml')} class="sim-btn turn-high">
								🎯 Turn (95% ML)
							</button>
							<button onclick={() => simulateTurnDetected(0.75, 'heuristic')} class="sim-btn turn-med">
								🎯 Turn (75% Heuristic)
							</button>
						</div>
					{/if}
				</div>
			{/snippet}
		</TurnIndicator.Root>
	</div>

	{#if showOutput}
		<div class="output-section">
			<h4>Component State:</h4>
			<pre>
Current Turn: {internalState?.currentTurn ?? 'unknown'}
Completion Probability: {((internalState?.completionProbability ?? 0) * 100).toFixed(0)}%
Detection Method: {internalState?.detectionMethod ?? 'none'}
Is User Turn: {internalState?.isUserTurn ?? false}
Is Agent Turn: {internalState?.isAgentTurn ?? false}
Is Unknown: {internalState?.isUnknown ?? true}</pre>

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

	.turn-section {
		background: #f5f5f5;
		border: 1px solid #ddd;
		border-radius: 8px;
		padding: 1.5rem;
	}

	.turn-indicator {
		display: flex;
		flex-direction: column;
		gap: 1rem;
	}

	.turn-badge {
		display: inline-block;
		padding: 0.5rem 1rem;
		border-radius: 12px;
		font-size: 1rem;
		font-weight: 600;
		width: fit-content;
		transition: all 0.2s;
	}

	.turn-badge[data-turn='user'] {
		background: #dbeafe;
		color: #1e40af;
	}

	.turn-badge[data-turn='agent'] {
		background: #f3e8ff;
		color: #6b21a8;
	}

	.turn-badge[data-turn='unknown'] {
		background: #e5e7eb;
		color: #6b7280;
	}

	.details-panel {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}

	.detail {
		padding: 0.75rem;
		border-radius: 6px;
		font-size: 0.875rem;
	}

	.detail.probability {
		background: #ecfdf5;
		border: 1px solid #86efac;
		color: #166534;
	}

	.detail.method {
		background: #eff6ff;
		border: 1px solid #93c5fd;
		color: #1e40af;
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

	.sim-btn.user {
		border-color: #3b82f6;
		color: #1e40af;
	}

	.sim-btn.agent {
		border-color: #a855f7;
		color: #6b21a8;
	}

	.sim-btn.turn-high,
	.sim-btn.turn-med {
		border-color: #10b981;
		color: #065f46;
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
