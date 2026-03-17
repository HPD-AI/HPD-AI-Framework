<script lang="ts">
	import * as AudioVisualizer from './index.js';

	interface Props {
		bands?: number;
		mode?: 'bar' | 'waveform' | 'radial';
		onVolumesChange?: (volumes: number[]) => void;
		testId?: string;
		[key: string]: any;
	}

	let { bands, mode, onVolumesChange, testId = 'visualizer', ...restProps }: Props = $props();
</script>

<AudioVisualizer.Root {bands} {mode} {onVolumesChange} data-testid={testId} {...restProps}>
	{#snippet children(state)}
		<div data-testid="{testId}-content">
			<div data-testid="{testId}-bands">
				Bands: {state.bands}
			</div>

			<div data-testid="{testId}-mode">
				Mode: {state.mode}
			</div>

			<div data-testid="{testId}-max-volume">
				MaxVolume: {state.maxVolume}
			</div>

			<div data-testid="{testId}-avg-volume">
				AvgVolume: {state.avgVolume.toFixed(2)}
			</div>

			<div data-testid="{testId}-is-active" data-is-active={state.isActive}>
				IsActive: {state.isActive}
			</div>

			<div data-testid="{testId}-volumes">
				Volumes: {state.volumes.join(',')}
			</div>
		</div>
	{/snippet}
</AudioVisualizer.Root>
