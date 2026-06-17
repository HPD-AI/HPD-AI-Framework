<script lang="ts">
	/**
	 * ThreadSwitcher Test Component
	 *
	 * Test toolharness for the ThreadSwitcher compound component.
	 * Renders Root + Prev + Position + Next with data-testid attributes.
	 */
	import * as ThreadSwitcher from '../exports.js';
	import type { Thread } from '@hpd-research/hpd-agent-client';

	interface Props {
		thread?: Thread | null;
		onPrev?: () => void;
		onNext?: () => void;
		prevLabel?: string;
		nextLabel?: string;
	}

	let {
		thread = null,
		onPrev,
		onNext,
		prevLabel = 'Previous thread',
		nextLabel = 'Next thread',
	}: Props = $props();
</script>

<ThreadSwitcher.Root {thread} data-testid="root">
	{#snippet children({ hasSiblings, canGoPrevious, canGoNext, position, label, isOriginal })}
		<div data-testid="has-siblings">{hasSiblings}</div>
		<div data-testid="can-go-previous">{canGoPrevious}</div>
		<div data-testid="can-go-next">{canGoNext}</div>
		<div data-testid="position">{position}</div>
		<div data-testid="label">{label}</div>
		<div data-testid="is-original">{isOriginal}</div>

		<ThreadSwitcher.Prev
			aria-label={prevLabel}
			onclick={onPrev}
			data-testid="prev"
		/>

		<ThreadSwitcher.Position data-testid="position-el" />

		<ThreadSwitcher.Next
			aria-label={nextLabel}
			onclick={onNext}
			data-testid="next"
		/>
	{/snippet}
</ThreadSwitcher.Root>
