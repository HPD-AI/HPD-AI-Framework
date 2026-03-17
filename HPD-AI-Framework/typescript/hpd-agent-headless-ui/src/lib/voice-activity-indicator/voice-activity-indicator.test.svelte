<script lang="ts">
	import * as VoiceActivityIndicator from './index.js';

	interface Props {
		onActivityChange?: (active: boolean) => void;
		testId?: string;
		[key: string]: any;
	}

	let { onActivityChange, testId = 'vad', ...restProps }: Props = $props();
</script>

<VoiceActivityIndicator.Root {onActivityChange} data-testid={testId} {...restProps}>
	{#snippet children(state)}
		<div data-testid="{testId}-content">
			<div data-testid="{testId}-active" data-active={state.active}>
				Active: {state.active}
			</div>

			<div data-testid="{testId}-speech-probability">
				Probability: {state.speechProbability}
			</div>

			<div data-testid="{testId}-duration">
				Duration: {state.duration}
			</div>

			<div data-testid="{testId}-intensity" data-intensity={state.intensityLevel}>
				Intensity: {state.intensityLevel}
			</div>
		</div>
	{/snippet}
</VoiceActivityIndicator.Root>
