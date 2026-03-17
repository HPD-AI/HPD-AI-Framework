<script lang="ts">
	import type { AppRecorderState } from '../../AppRecorderState.svelte';
	import { ZOOM_DEPTH_LABELS, ZOOM_DEPTH_SCALES, type ZoomDepth } from '../../AppRecorderState.svelte';

	let { editor }: { editor: AppRecorderState } = $props();

	const DEPTHS: ZoomDepth[] = [1, 2, 3, 4, 5, 6];

	function formatMs(ms: number): string {
		const s = ms / 1000;
		const m = Math.floor(s / 60);
		const sec = (s % 60).toFixed(1);
		return `${m}:${sec.padStart(4, '0')}`;
	}
</script>

<div class="zoom-props">
	{#if !editor.selectedZoom}
		<p class="hint">Select a zoom region in the timeline to adjust.</p>
	{:else}
		{@const zoom = editor.selectedZoom}

		<div class="prop-card">
			<p class="prop-label">Zoom Level</p>
			<div class="depth-chips">
				{#each DEPTHS as depth}
					<button
						class="depth-chip"
						class:active={zoom.depth === depth}
						onclick={() => editor.updateZoomRegion(zoom.id, { depth })}
					>
						{ZOOM_DEPTH_LABELS[depth]}
					</button>
				{/each}
			</div>
		</div>

		<div class="prop-card">
			<p class="prop-label">Focus Point</p>
			<div class="focus-grid">
				<div class="focus-field">
					<label class="focus-label">X</label>
					<input
						type="range" min="0" max="1" step="0.01"
						value={zoom.cx}
						oninput={(e) => editor.updateZoomRegion(zoom.id, { cx: Number((e.currentTarget as HTMLInputElement).value) })}
						class="slider"
					/>
					<span class="slider-val">{(zoom.cx * 100).toFixed(0)}%</span>
				</div>
				<div class="focus-field">
					<label class="focus-label">Y</label>
					<input
						type="range" min="0" max="1" step="0.01"
						value={zoom.cy}
						oninput={(e) => editor.updateZoomRegion(zoom.id, { cy: Number((e.currentTarget as HTMLInputElement).value) })}
						class="slider"
					/>
					<span class="slider-val">{(zoom.cy * 100).toFixed(0)}%</span>
				</div>
			</div>
		</div>

		<div class="prop-card info-card">
			<div class="info-row">
				<span class="info-key">Start</span>
				<span class="info-val">{formatMs(zoom.startMs)}</span>
			</div>
			<div class="info-row">
				<span class="info-key">End</span>
				<span class="info-val">{formatMs(zoom.endMs)}</span>
			</div>
			<div class="info-row">
				<span class="info-key">Scale</span>
				<span class="info-val">{ZOOM_DEPTH_SCALES[zoom.depth]}×</span>
			</div>
		</div>

		<button class="delete-btn" onclick={() => editor.removeZoomRegion(zoom.id)}>
			Delete Region
		</button>
	{/if}
</div>

<style>
	.zoom-props { display: flex; flex-direction: column; gap: 0.5rem; padding: 0.75rem; }

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

	.depth-chips { display: flex; gap: 0.25rem; flex-wrap: wrap; }
	.depth-chip {
		padding: 0.25rem 0.5rem;
		background: rgb(var(--color-bg-secondary));
		border: 1px solid rgb(var(--color-border-default));
		border-radius: var(--radius-sm);
		color: rgb(var(--color-text-secondary));
		font-size: 0.7rem;
		cursor: pointer;
		transition: all var(--duration-fast);
	}
	.depth-chip.active {
		background: rgb(var(--color-accent-primary) / 0.15);
		border-color: rgb(var(--color-accent-primary));
		color: rgb(var(--color-accent-primary));
	}

	.focus-grid { display: flex; flex-direction: column; gap: 0.35rem; }
	.focus-field { display: flex; align-items: center; gap: 0.5rem; }
	.focus-label { font-size: 0.7rem; color: rgb(var(--color-text-secondary)); width: 12px; }
	.slider { flex: 1; accent-color: rgb(var(--color-accent-primary)); cursor: pointer; }
	.slider-val { font-size: 0.7rem; color: rgb(var(--color-text-tertiary)); min-width: 30px; text-align: right; font-family: monospace; }

	.info-card { gap: 0.3rem; }
	.info-row { display: flex; justify-content: space-between; align-items: center; }
	.info-key { font-size: 0.7rem; color: rgb(var(--color-text-secondary)); }
	.info-val { font-size: 0.7rem; color: rgb(var(--color-text-primary)); font-family: monospace; }

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
