<script lang="ts">
	import type { AppRecorderState } from '../../AppRecorderState.svelte';

	let { editor }: { editor: AppRecorderState } = $props();

	function formatMs(ms: number): string {
		const s = ms / 1000;
		const m = Math.floor(s / 60);
		const sec = (s % 60).toFixed(3);
		return `${m}:${sec.padStart(6, '0')}`;
	}
</script>

<div class="trim-props">
	{#if !editor.selectedTrim}
		<p class="hint">Select a trim region in the timeline to adjust.</p>
	{:else}
		{@const trim = editor.selectedTrim}
		{@const durationMs = trim.endMs - trim.startMs}

		<div class="prop-card">
			<div class="info-row">
				<span class="info-key">Start</span>
				<span class="info-val mono">{formatMs(trim.startMs)}</span>
			</div>
			<div class="info-row">
				<span class="info-key">End</span>
				<span class="info-val mono">{formatMs(trim.endMs)}</span>
			</div>
			<div class="info-row">
				<span class="info-key">Duration cut</span>
				<span class="info-val mono">{formatMs(durationMs)}</span>
			</div>
		</div>

		<p class="hint-small">Drag the chip edges in the timeline to change start/end.</p>

		<button
			class="preview-btn"
			onclick={() => {
				editor.seekTo(Math.max(0, trim.startMs - 500));
				editor.isPlaying = true;
			}}
		>
			▶ Preview Cut
		</button>

		<button class="delete-btn" onclick={() => editor.removeTrimRegion(trim.id)}>
			Delete Region
		</button>
	{/if}
</div>

<style>
	.trim-props { display: flex; flex-direction: column; gap: 0.5rem; padding: 0.75rem; }
	.hint { font-size: 0.75rem; color: rgb(var(--color-text-tertiary)); font-style: italic; text-align: center; padding: 1rem 0; margin: 0; }
	.hint-small { font-size: 0.7rem; color: rgb(var(--color-text-tertiary)); text-align: center; margin: 0; }

	.prop-card {
		background: rgb(var(--color-surface-1));
		border: 1px solid rgb(var(--color-border-subtle));
		border-radius: var(--radius-md);
		padding: 0.75rem;
		display: flex;
		flex-direction: column;
		gap: 0.35rem;
	}
	.info-row { display: flex; justify-content: space-between; align-items: center; }
	.info-key { font-size: 0.7rem; color: rgb(var(--color-text-secondary)); }
	.info-val { font-size: 0.7rem; color: rgb(var(--color-text-primary)); }
	.mono { font-family: monospace; }

	.preview-btn {
		width: 100%;
		padding: 0.5rem;
		background: rgb(var(--color-surface-1));
		border: 1px solid rgb(var(--color-border-default));
		border-radius: var(--radius-sm);
		color: rgb(var(--color-text-primary));
		font-size: 0.75rem;
		cursor: pointer;
	}
	.preview-btn:hover { background: rgb(var(--color-surface-2)); }

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
