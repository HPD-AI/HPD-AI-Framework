<script lang="ts">
	import * as Transcription from './index.js';

	interface Props {
		onTextChange?: (text: string, isFinal: boolean) => void;
		onClear?: () => void;
		testId?: string;
		[key: string]: any;
	}

	let { onTextChange, onClear, testId = 'transcription', ...restProps }: Props = $props();
</script>

<Transcription.Root {onTextChange} {onClear} data-testid={testId} {...restProps}>
	{#snippet children(state)}
		<div data-testid="{testId}-content">
			<div data-testid="{testId}-text" data-final={state.isFinal} data-empty={state.isEmpty}>
				Text: {state.text}
			</div>

			<div data-testid="{testId}-is-final" data-final={state.isFinal}>
				Final: {state.isFinal}
			</div>

			<div data-testid="{testId}-confidence">
				Confidence: {state.confidence ?? 'null'}
			</div>

			<div data-testid="{testId}-confidence-level" data-level={state.confidenceLevel}>
				Level: {state.confidenceLevel ?? 'null'}
			</div>

			<div data-testid="{testId}-is-empty" data-empty={state.isEmpty}>
				Empty: {state.isEmpty}
			</div>

			<div data-testid="{testId}-controls">
				<button data-testid="{testId}-clear-btn" onclick={state.clear}>Clear</button>
			</div>
		</div>
	{/snippet}
</Transcription.Root>
