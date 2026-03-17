<script lang="ts">
	import * as AudioGate from './index.js';

	interface Props {
		onStatusChange?: (status: 'blocked' | 'ready' | 'error') => void;
		testId?: string;
		audioContext?: AudioContext;
	}

	let { onStatusChange, testId = 'audio-gate', audioContext }: Props = $props();
</script>

<AudioGate.Root {onStatusChange} {audioContext} data-testid={testId}>
	{#snippet children({ canPlayAudio, enableAudio, status, error })}
		<div data-testid="{testId}-content">
			<div data-testid="{testId}-status" data-status={status}>
				Status: {status}
			</div>

			<div data-testid="{testId}-can-play" data-can-play={canPlayAudio}>
				Can Play: {canPlayAudio}
			</div>

			{#if error}
				<div data-testid="{testId}-error" data-error>
					Error: {error.message}
				</div>
			{/if}

			{#if !canPlayAudio}
				<button data-testid="{testId}-enable-btn" onclick={() => enableAudio().catch(() => {})}>
					Enable Audio
				</button>
			{:else}
				<div data-testid="{testId}-ready">Audio Ready</div>
			{/if}
		</div>
	{/snippet}
</AudioGate.Root>
