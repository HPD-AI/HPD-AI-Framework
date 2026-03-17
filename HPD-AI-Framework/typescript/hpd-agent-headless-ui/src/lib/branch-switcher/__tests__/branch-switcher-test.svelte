<script lang="ts">
	/**
	 * BranchSwitcher Test Component
	 *
	 * Test harness for the BranchSwitcher compound component.
	 * Renders Root + Prev + Position + Next with data-testid attributes.
	 */
	import * as BranchSwitcher from '../exports.js';
	import type { Branch } from '@hpd/hpd-agent-client';

	interface Props {
		branch?: Branch | null;
		onPrev?: () => void;
		onNext?: () => void;
		prevLabel?: string;
		nextLabel?: string;
	}

	let {
		branch = null,
		onPrev,
		onNext,
		prevLabel = 'Previous branch',
		nextLabel = 'Next branch',
	}: Props = $props();
</script>

<BranchSwitcher.Root {branch} data-testid="root">
	{#snippet children({ hasSiblings, canGoPrevious, canGoNext, position, label, isOriginal })}
		<div data-testid="has-siblings">{hasSiblings}</div>
		<div data-testid="can-go-previous">{canGoPrevious}</div>
		<div data-testid="can-go-next">{canGoNext}</div>
		<div data-testid="position">{position}</div>
		<div data-testid="label">{label}</div>
		<div data-testid="is-original">{isOriginal}</div>

		<BranchSwitcher.Prev
			aria-label={prevLabel}
			onclick={onPrev}
			data-testid="prev"
		/>

		<BranchSwitcher.Position data-testid="position-el" />

		<BranchSwitcher.Next
			aria-label={nextLabel}
			onclick={onNext}
			data-testid="next"
		/>
	{/snippet}
</BranchSwitcher.Root>
