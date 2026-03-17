<script lang="ts">
	import type { RecordingSource } from '../AppRecorderState.svelte';

	interface Props {
		sources: RecordingSource[];
		onpick: (id: string) => void;
		oncancel: () => void;
	}

	let { sources, onpick, oncancel }: Props = $props();
</script>

<div class="backdrop">
	<div class="picker-card">
		<div class="picker-header">
			<h3>Choose a recording source</h3>
			<button class="close-btn" onclick={oncancel}>✕</button>
		</div>

		<div class="source-list">
			{#each sources as source (source.id)}
				<button class="source-item" onclick={() => onpick(source.id)}>
					<span class="source-icon">{source.type === 'screen' ? '🖥' : '🪟'}</span>
					<span class="source-name">{source.name}</span>
					{#if source.width && source.height}
						<span class="source-dims">{source.width}×{source.height}</span>
					{/if}
				</button>
			{/each}

			{#if sources.length === 0}
				<p class="empty">No sources available.</p>
			{/if}
		</div>

		<div class="picker-footer">
			<button class="btn-ghost" onclick={oncancel}>Cancel</button>
		</div>
	</div>
</div>

<style>
	.backdrop {
		position: absolute;
		inset: 0;
		background: rgb(0 0 0 / 0.65);
		display: flex;
		align-items: center;
		justify-content: center;
		z-index: 100;
		backdrop-filter: blur(var(--glass-blur));
	}

	.picker-card {
		background: rgb(var(--color-surface-1));
		border: 1px solid rgb(255 255 255 / 0.1);
		border-radius: var(--radius-lg);
		width: 420px;
		max-height: 70vh;
		display: flex;
		flex-direction: column;
		overflow: hidden;
	}

	.picker-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 1rem 1.25rem;
		border-bottom: 1px solid rgb(255 255 255 / 0.08);
	}

	.picker-header h3 {
		margin: 0;
		font-size: 0.95rem;
		font-weight: 600;
	}

	.close-btn {
		background: none;
		border: none;
		color: inherit;
		cursor: pointer;
		opacity: 0.5;
		font-size: 0.875rem;
		padding: 0.25rem;
	}
	.close-btn:hover { opacity: 1; }

	.source-list {
		flex: 1;
		overflow-y: auto;
		padding: 0.5rem;
		display: flex;
		flex-direction: column;
		gap: 0.25rem;
	}

	.source-item {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		padding: 0.75rem 1rem;
		border-radius: 0.5rem;
		border: 1px solid transparent;
		background: transparent;
		color: inherit;
		cursor: pointer;
		text-align: left;
		width: 100%;
		transition: background 0.1s;
	}

	.source-item:hover {
		background: rgb(255 255 255 / 0.05);
		border-color: rgb(255 255 255 / 0.08);
	}

	.source-icon { font-size: 1.1rem; }
	.source-name { flex: 1; font-size: 0.875rem; }
	.source-dims { font-size: 0.75rem; opacity: 0.4; font-variant-numeric: tabular-nums; }

	.empty { font-size: 0.875rem; opacity: 0.5; text-align: center; padding: 2rem; margin: 0; }

	.picker-footer {
		padding: 0.75rem 1.25rem;
		border-top: 1px solid rgb(255 255 255 / 0.08);
		display: flex;
		justify-content: flex-end;
	}

	.btn-ghost {
		padding: 0.45rem 0.9rem;
		background: transparent;
		color: inherit;
		border: 1px solid rgb(255 255 255 / 0.15);
		border-radius: 0.375rem;
		cursor: pointer;
		font-size: 0.8rem;
	}
	.btn-ghost:hover { background: rgb(255 255 255 / 0.05); }
</style>
