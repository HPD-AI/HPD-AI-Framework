<script lang="ts">
	import type { AppRecorderState } from '../../AppRecorderState.svelte';

	let { editor }: { editor: AppRecorderState } = $props();

	function formatMs(ms: number): string {
		return ms >= 1000 ? `${(ms / 1000).toFixed(1)}s` : `${ms}ms`;
	}
</script>

<div class="transition-props">
	{#if !editor.selectedTransition}
		<p class="hint">Select a transition marker in the timeline to edit.</p>
	{:else}
		{@const t = editor.selectedTransition}

		<div class="prop-card">
			<p class="prop-label">Type</p>
			<div class="type-display">{t.type}</div>
			<button class="change-btn">Change Transition ▾</button>
		</div>

		<div class="prop-card">
			<p class="prop-label">Duration</p>
			<div class="dur-row">
				<input
					type="range" min="100" max="3000" step="50"
					value={t.durationMs}
					oninput={(e) => editor.updateTransition(t.id, { durationMs: Number((e.currentTarget as HTMLInputElement).value) })}
					class="slider"
				/>
				<span class="dur-val">{formatMs(t.durationMs)}</span>
			</div>
		</div>

		<button class="delete-btn" onclick={() => editor.removeTransition(t.id)}>
			Delete Transition
		</button>
	{/if}
</div>

<style>
	.transition-props { display: flex; flex-direction: column; gap: 0.5rem; padding: 0.75rem; }
	.hint { font-size: 0.75rem; color: rgb(var(--color-text-tertiary)); font-style: italic; text-align: center; padding: 1rem 0; margin: 0; }
	.prop-card {
		background: rgb(var(--color-surface-1));
		border: 1px solid rgb(var(--color-border-subtle));
		border-radius: var(--radius-md);
		padding: 0.75rem;
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}
	.prop-label {
		font-size: 0.7rem;
		font-weight: 600;
		text-transform: uppercase;
		letter-spacing: 0.07em;
		color: rgb(var(--color-text-secondary));
		margin: 0;
	}
	.type-display {
		font-size: 0.875rem;
		font-weight: 600;
		color: rgb(168 85 247);
		text-transform: capitalize;
	}
	.change-btn {
		width: 100%;
		padding: 0.35rem;
		background: rgb(var(--color-bg-secondary));
		border: 1px solid rgb(var(--color-border-default));
		border-radius: var(--radius-sm);
		color: rgb(var(--color-text-primary));
		font-size: 0.75rem;
		cursor: pointer;
	}
	.change-btn:hover { border-color: rgb(168 85 247 / 0.5); }
	.dur-row { display: flex; align-items: center; gap: 0.5rem; }
	.slider { flex: 1; accent-color: rgb(168 85 247); cursor: pointer; }
	.dur-val { font-size: 0.7rem; color: rgb(var(--color-text-tertiary)); min-width: 36px; text-align: right; font-family: monospace; }
	.delete-btn {
		width: 100%;
		padding: 0.5rem;
		background: rgb(var(--color-error) / 0.1);
		border: 1px solid rgb(var(--color-error) / 0.3);
		border-radius: var(--radius-sm);
		color: rgb(var(--color-error));
		font-size: 0.75rem;
		cursor: pointer;
	}
	.delete-btn:hover { background: rgb(var(--color-error) / 0.2); }
</style>
