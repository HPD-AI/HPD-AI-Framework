<script lang="ts">
	import type { AppRecorderState } from '../../AppRecorderState.svelte';

	let { editor }: { editor: AppRecorderState } = $props();

	const SPEED_STOPS = [0.25, 0.5, 1, 1.5, 2, 3, 4] as const;
</script>

<div class="speed-props">
	{#if !editor.selectedSpeed}
		<p class="hint">Select a speed region in the timeline to adjust.</p>
	{:else}
		{@const speed = editor.selectedSpeed}

		<div class="prop-card">
			<p class="prop-label">Speed</p>
			<div class="speed-badge">{speed.multiplier}×</div>
			<input
				type="range"
				min="0.25" max="4" step="0.05"
				value={speed.multiplier}
				oninput={(e) => editor.updateSpeedRegion(speed.id, { multiplier: Number((e.currentTarget as HTMLInputElement).value) })}
				class="slider"
			/>
			<div class="speed-stops">
				{#each SPEED_STOPS as stop}
					<button
						class="stop-tick"
						class:active={Math.abs(speed.multiplier - stop) < 0.05}
						onclick={() => editor.updateSpeedRegion(speed.id, { multiplier: stop })}
					>{stop}×</button>
				{/each}
			</div>
		</div>

		<div class="prop-card">
			<div class="toggle-row">
				<label class="toggle-label">Speed Ramping (ease in/out)</label>
				<button
					class="toggle-btn"
					class:on={speed.ramping}
					onclick={() => editor.updateSpeedRegion(speed.id, { ramping: !speed.ramping })}
					role="switch"
					aria-checked={speed.ramping}
				>
					<span class="toggle-thumb"></span>
				</button>
			</div>
		</div>

		<button class="delete-btn" onclick={() => editor.removeSpeedRegion(speed.id)}>
			Delete Region
		</button>
	{/if}
</div>

<style>
	.speed-props { display: flex; flex-direction: column; gap: 0.5rem; padding: 0.75rem; }
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

	.speed-badge {
		font-size: 1.5rem;
		font-weight: 700;
		color: rgb(var(--color-info));
		text-align: center;
		font-family: monospace;
	}

	.slider { width: 100%; accent-color: rgb(var(--color-info)); cursor: pointer; }

	.speed-stops { display: flex; justify-content: space-between; }
	.stop-tick {
		font-size: 0.6rem;
		background: none;
		border: none;
		color: rgb(var(--color-text-tertiary));
		cursor: pointer;
		padding: 0.1rem 0.2rem;
		border-radius: var(--radius-sm);
		transition: color var(--duration-fast);
	}
	.stop-tick:hover  { color: rgb(var(--color-text-primary)); }
	.stop-tick.active { color: rgb(var(--color-info)); font-weight: 600; }

	.toggle-row { display: flex; align-items: center; justify-content: space-between; gap: 0.5rem; }
	.toggle-label { font-size: 0.75rem; color: rgb(var(--color-text-primary)); }
	.toggle-btn {
		position: relative;
		width: 32px;
		height: 18px;
		border-radius: 9px;
		background: rgb(var(--color-border-default));
		border: none;
		cursor: pointer;
		transition: background var(--duration-fast);
		flex-shrink: 0;
	}
	.toggle-btn.on { background: rgb(var(--color-accent-primary)); }
	.toggle-thumb {
		position: absolute;
		top: 2px; left: 2px;
		width: 14px; height: 14px;
		border-radius: 50%;
		background: #fff;
		transition: left var(--duration-fast);
		display: block;
	}
	.toggle-btn.on .toggle-thumb { left: 16px; }

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
