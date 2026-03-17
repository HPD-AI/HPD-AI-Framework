<script lang="ts">
	import * as TurnIndicator from './index.js';

	interface Props {
		onTurnChange?: (turn: 'user' | 'agent' | 'unknown') => void;
		testId?: string;
		[key: string]: any;
	}

	let { onTurnChange, testId = 'turn', ...restProps }: Props = $props();
</script>

<TurnIndicator.Root {onTurnChange} data-testid={testId} {...restProps}>
	{#snippet children(state)}
		<div data-testid="{testId}-content">
			<div data-testid="{testId}-current-turn" data-turn={state.currentTurn}>
				Turn: {state.currentTurn}
			</div>

			<div data-testid="{testId}-completion-probability">
				Probability: {state.completionProbability}
			</div>

			<div data-testid="{testId}-detection-method">
				Method: {state.detectionMethod ?? 'none'}
			</div>

			<div data-testid="{testId}-is-user-turn" data-is-user-turn={state.isUserTurn}>
				IsUserTurn: {state.isUserTurn}
			</div>

			<div data-testid="{testId}-is-agent-turn" data-is-agent-turn={state.isAgentTurn}>
				IsAgentTurn: {state.isAgentTurn}
			</div>

			<div data-testid="{testId}-is-unknown" data-is-unknown={state.isUnknown}>
				IsUnknown: {state.isUnknown}
			</div>
		</div>
	{/snippet}
</TurnIndicator.Root>
