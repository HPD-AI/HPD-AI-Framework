<script lang="ts">
	import * as InterruptionIndicator from './index.js';

	interface Props {
		onInterruptionChange?: (interrupted: boolean) => void;
		onPauseChange?: (paused: boolean) => void;
		testId?: string;
		[key: string]: any;
	}

	let { onInterruptionChange, onPauseChange, testId = 'interruption', ...restProps }: Props = $props();
</script>

<InterruptionIndicator.Root {onInterruptionChange} {onPauseChange} data-testid={testId} {...restProps}>
	{#snippet children(state)}
		<div data-testid="{testId}-content">
			<div data-testid="{testId}-interrupted" data-interrupted={state.interrupted}>
				Interrupted: {state.interrupted}
			</div>

			<div data-testid="{testId}-paused" data-paused={state.paused}>
				Paused: {state.paused}
			</div>

			<div data-testid="{testId}-status" data-status={state.status}>
				Status: {state.status}
			</div>

			<div data-testid="{testId}-pause-reason" data-reason={state.pauseReason}>
				Reason: {state.pauseReason ?? 'none'}
			</div>

			<div data-testid="{testId}-pause-duration">
				Duration: {state.pauseDuration}
			</div>

			<div data-testid="{testId}-interrupted-text">
				Text: {state.interruptedText}
			</div>

			<div data-testid="{testId}-is-interrupted" data-is-interrupted={state.isInterrupted}>
				IsInterrupted: {state.isInterrupted}
			</div>

			<div data-testid="{testId}-is-paused" data-is-paused={state.isPaused}>
				IsPaused: {state.isPaused}
			</div>

			<div data-testid="{testId}-is-normal" data-is-normal={state.isNormal}>
				IsNormal: {state.isNormal}
			</div>
		</div>
	{/snippet}
</InterruptionIndicator.Root>
