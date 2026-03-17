<script lang="ts">
	import type { ExportFormat } from '../AppRecorderState.svelte';

	interface Props {
		format: ExportFormat | null;
		percent: number;
	}

	let { format, percent }: Props = $props();
</script>

<div class="export-overlay">
	<div class="export-card">
		<p class="export-label">Exporting {format?.toUpperCase() ?? ''}…</p>
		<div class="progress-track">
			<div class="progress-fill" style:width="{percent}%"></div>
		</div>
		<p class="export-pct">{percent}%</p>
	</div>
</div>

<style>
	.export-overlay {
		position: absolute;
		inset: 0;
		background: rgb(0 0 0 / 0.5);
		display: flex;
		align-items: center;
		justify-content: center;
		z-index: 80;
		backdrop-filter: blur(var(--glass-blur));
	}

	.export-card {
		background: rgb(var(--color-surface-1));
		border: 1px solid rgb(255 255 255 / 0.1);
		border-radius: var(--radius-lg);
		padding: 1.5rem 2rem;
		width: 320px;
		display: flex;
		flex-direction: column;
		gap: 0.75rem;
		align-items: center;
	}

	.export-label {
		margin: 0;
		font-size: 0.95rem;
		font-weight: 600;
	}

	.progress-track {
		width: 100%;
		height: 6px;
		background: rgb(255 255 255 / 0.1);
		border-radius: 3px;
		overflow: hidden;
	}

	.progress-fill {
		height: 100%;
		background: rgb(var(--color-accent-primary));
		border-radius: 3px;
		transition: width 0.2s ease;
	}

	.export-pct {
		margin: 0;
		font-size: 0.8rem;
		opacity: 0.6;
		font-variant-numeric: tabular-nums;
	}
</style>
