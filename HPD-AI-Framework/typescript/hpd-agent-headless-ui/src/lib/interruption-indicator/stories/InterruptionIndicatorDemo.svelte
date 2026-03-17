<script lang="ts">
	import * as InterruptionIndicator from '../index.js';
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

	// Simulate interruption events
	function simulateUserInterrupted() {
		if (!internalState) return;

		const transcribedText = 'Wait, I have a question';
		internalState.onUserInterrupted(transcribedText);
		log(`USER_INTERRUPTED (text: "${transcribedText}")`);
	}

	function simulateSpeechPaused(reason: 'user_speaking' | 'potential_interruption') {
		if (!internalState) return;

		const synthesisId = 'synth-123';
		internalState.onSpeechPaused(synthesisId, reason);
		log(`SPEECH_PAUSED (reason: ${reason})`);
	}

	function simulateSpeechResumed(duration: number = 2.5) {
		if (!internalState) return;

		const synthesisId = 'synth-123';
		const pauseDuration = `PT${duration}S`;
		internalState.onSpeechResumed(synthesisId, pauseDuration);
		log(`SPEECH_RESUMED (duration: ${duration}s)`);
	}

	// Auto-simulate if requested
	let hasAutoSimulated = $state(false);
	$effect(() => {
		if (autoSimulate && !hasAutoSimulated && internalState) {
			hasAutoSimulated = true;
			setTimeout(() => {
				simulateSpeechPaused('user_speaking');
				setTimeout(() => simulateUserInterrupted(), 1000);
				setTimeout(() => simulateSpeechResumed(2.5), 3000);
			}, 500);
		}
	});
</script>

<div class="demo-container">
	<div class="interruption-section">
		<InterruptionIndicator.Root onInterruptionChange={(interrupted) => log(`Interrupted: ${interrupted}`)} onPauseChange={(paused) => log(`Paused: ${paused}`)}>
			{#snippet children(state)}
				{@const _ = internalState || (internalState = state)}

				<div class="interruption-indicator" data-status={state.status}>
					<!-- Status Badge -->
					<div class="status-badge" data-status={state.status}>
						{#if state.isInterrupted}
							🚫 Interrupted
						{:else if state.isPaused}
							⏸ Paused
						{:else}
							  Normal
						{/if}
					</div>

					<!-- Details Panel -->
					<div class="details-panel">
						{#if state.isInterrupted}
							<div class="detail interrupted">
								<strong>Interruption:</strong> {state.interruptedText || '(no text)'}
							</div>
						{/if}

						{#if state.isPaused}
							<div class="detail paused">
								<strong>Pause Reason:</strong> {state.pauseReason === 'user_speaking' ? 'User Speaking' : 'Potential Interruption'}
							</div>
						{/if}

						{#if state.pauseDuration > 0}
							<div class="detail duration">
								<strong>Last Pause Duration:</strong> {state.pauseDuration.toFixed(1)}s
							</div>
						{/if}
					</div>

					{#if showControls}
						<!-- Simulation Controls -->
						<div class="sim-controls">
							<button onclick={() => simulateUserInterrupted()} class="sim-btn interrupted">
								🚫 User Interrupted
							</button>
							<button onclick={() => simulateSpeechPaused('user_speaking')} class="sim-btn paused-user">
								⏸ Pause (User Speaking)
							</button>
							<button onclick={() => simulateSpeechPaused('potential_interruption')} class="sim-btn paused-potential">
								⏸ Pause (Potential)
							</button>
							<button onclick={() => simulateSpeechResumed(2.5)} disabled={state.isNormal} class="sim-btn resume">
								▶️ Resume
							</button>
						</div>
					{/if}
				</div>
			{/snippet}
		</InterruptionIndicator.Root>
	</div>

	{#if showOutput}
		<div class="output-section">
			<h4>Component State:</h4>
			<pre>
Interrupted: {internalState?.interrupted ?? false}
Paused: {internalState?.paused ?? false}
Status: {internalState?.status ?? 'normal'}
Pause Reason: {internalState?.pauseReason ?? 'none'}
Pause Duration: {(internalState?.pauseDuration ?? 0).toFixed(1)}s
Interrupted Text: {internalState?.interruptedText ?? '(empty)'}</pre>

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

	.interruption-section {
		background: #f5f5f5;
		border: 1px solid #ddd;
		border-radius: 8px;
		padding: 1.5rem;
	}

	.interruption-indicator {
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

	.status-badge[data-status='normal'] {
		background: #d1fae5;
		color: #065f46;
	}

	.status-badge[data-status='paused'] {
		background: #fef3c7;
		color: #92400e;
	}

	.status-badge[data-status='interrupted'] {
		background: #fee2e2;
		color: #991b1b;
		animation: pulse 1s ease-in-out infinite;
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

	.detail.interrupted {
		background: #fef2f2;
		border: 1px solid #fca5a5;
		color: #7f1d1d;
	}

	.detail.paused {
		background: #fffbeb;
		border: 1px solid #fde68a;
		color: #78350f;
	}

	.detail.duration {
		background: #f0fdf4;
		border: 1px solid #86efac;
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

	.sim-btn.interrupted {
		border-color: #ef4444;
		color: #991b1b;
	}

	.sim-btn.paused-user,
	.sim-btn.paused-potential {
		border-color: #f59e0b;
		color: #92400e;
	}

	.sim-btn.resume {
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
