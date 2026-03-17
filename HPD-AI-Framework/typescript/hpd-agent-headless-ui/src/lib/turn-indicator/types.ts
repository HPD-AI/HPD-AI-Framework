import type { Snippet } from 'svelte';
import type {
	TurnIndicatorState,
	TurnIndicatorStateProps,
	CurrentTurn,
	DetectionMethod
} from './turn-indicator.svelte.ts';

export type { TurnIndicatorStateProps, CurrentTurn, DetectionMethod };

export interface TurnIndicatorRootSnippetProps {
	currentTurn: CurrentTurn;
	completionProbability: number;
	detectionMethod: DetectionMethod;
	isUserTurn: boolean;
	isAgentTurn: boolean;
	isUnknown: boolean;
}

export interface TurnIndicatorRootProps extends TurnIndicatorStateProps {
	child?: Snippet<[{ props: Record<string, unknown> } & TurnIndicatorRootSnippetProps]>;
	children?: Snippet<[TurnIndicatorState]>;
	class?: string;
	'data-testid'?: string;
}
