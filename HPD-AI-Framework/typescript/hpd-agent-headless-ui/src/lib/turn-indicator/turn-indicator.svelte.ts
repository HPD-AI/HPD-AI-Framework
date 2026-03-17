/**
 * TurnIndicator - Reactive state for turn detection
 *
 * Shows whose turn it is to speak:
 * - User turn, agent turn, or unknown
 * - Completion probability
 * - Detection method
 *
 * Handles 3 HPD events:
 * - TURN_DETECTED
 * - VAD_START_OF_SPEECH
 * - SYNTHESIS_STARTED
 */

import { createHPDAttrs } from '$lib/internal/attrs.js';
import type { ReadableBoxedValues } from 'svelte-toolbelt';

const turnAttrs = createHPDAttrs({
	component: 'turn-indicator',
	parts: ['root']
} as const);

export type CurrentTurn = 'user' | 'agent' | 'unknown';
export type DetectionMethod = 'heuristic' | 'ml' | 'manual' | 'timeout' | null;

export interface TurnIndicatorStateProps {
	/**
	 * Called when turn changes
	 */
	onTurnChange?: (turn: CurrentTurn) => void;
}

export type TurnIndicatorStateOpts = ReadableBoxedValues<TurnIndicatorStateProps>;

export class TurnIndicatorState {
	readonly opts: TurnIndicatorStateOpts;

	// Reactive state
	#currentTurn = $state<CurrentTurn>('unknown');
	#completionProbability = $state(0);
	#detectionMethod = $state<DetectionMethod>(null);

	// Derived state
	readonly currentTurn = $derived(this.#currentTurn);
	readonly completionProbability = $derived(this.#completionProbability);
	readonly detectionMethod = $derived(this.#detectionMethod);

	readonly isUserTurn = $derived(this.#currentTurn === 'user');
	readonly isAgentTurn = $derived(this.#currentTurn === 'agent');
	readonly isUnknown = $derived(this.#currentTurn === 'unknown');

	// Props for rendering
	readonly props = $derived.by(
		() =>
			({
				[turnAttrs.root]: '',
				'data-turn': this.#currentTurn,
				'data-user-turn': this.isUserTurn ? '' : undefined,
				'data-agent-turn': this.isAgentTurn ? '' : undefined,
				'data-unknown': this.isUnknown ? '' : undefined,
				role: 'status',
				'aria-label': `Current turn: ${this.#currentTurn}`,
				'aria-live': 'polite'
			}) as const
	);

	// Snippet props
	readonly snippetProps = $derived({
		currentTurn: this.currentTurn,
		completionProbability: this.completionProbability,
		detectionMethod: this.detectionMethod,
		isUserTurn: this.isUserTurn,
		isAgentTurn: this.isAgentTurn,
		isUnknown: this.isUnknown
	});

	constructor(opts: TurnIndicatorStateOpts) {
		this.opts = opts;

		// Watch for turn changes
		$effect(() => {
			if (this.opts.onTurnChange) {
				this.opts.onTurnChange.current?.(this.#currentTurn);
			}
		});
	}

	// ============================================================================
	// HPD Event Handlers (called by AgentState)
	// ============================================================================

	/**
	 * Handle TURN_DETECTED event
	 * Turn change detected by ML or heuristics
	 */
	onTurnDetected(
		transcribedText: string,
		completionProbability: number,
		silenceDuration: string,
		detectionMethod: 'heuristic' | 'ml' | 'manual' | 'timeout'
	) {
		this.#completionProbability = completionProbability;
		this.#detectionMethod = detectionMethod;
		// High probability means agent's turn (user finished speaking)
		this.#currentTurn = completionProbability >= 0.8 ? 'agent' : 'user';
	}

	/**
	 * Handle VAD_START_OF_SPEECH event
	 * User started speaking -> user's turn
	 */
	onVadStartOfSpeech(timestamp: string, speechProbability: number) {
		this.#currentTurn = 'user';
	}

	/**
	 * Handle SYNTHESIS_STARTED event
	 * Agent started speaking -> agent's turn
	 */
	onSynthesisStarted(synthesisId: string, modelId?: string, voice?: string, streamId?: string) {
		this.#currentTurn = 'agent';
	}

	// Static factory method
	static create(opts: TurnIndicatorStateOpts = {}) {
		return new TurnIndicatorState(opts);
	}

	// Cleanup
	destroy() {
		// No cleanup needed
	}
}
