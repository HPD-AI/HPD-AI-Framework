import type { Snippet } from 'svelte';
import type {
	InterruptionIndicatorState,
	InterruptionIndicatorStateProps,
	PauseReason,
	InterruptionStatus
} from './interruption-indicator.svelte.ts';

export type { InterruptionIndicatorStateProps, PauseReason, InterruptionStatus };

export interface InterruptionIndicatorRootSnippetProps {
	interrupted: boolean;
	paused: boolean;
	pauseReason: PauseReason;
	pauseDuration: number;
	interruptedText: string;
	status: InterruptionStatus;
	isInterrupted: boolean;
	isPaused: boolean;
	isNormal: boolean;
}

export interface InterruptionIndicatorRootProps extends InterruptionIndicatorStateProps {
	child?: Snippet<[{ props: Record<string, unknown> } & InterruptionIndicatorRootSnippetProps]>;
	children?: Snippet<[InterruptionIndicatorState]>;
	class?: string;
	'data-testid'?: string;
}
