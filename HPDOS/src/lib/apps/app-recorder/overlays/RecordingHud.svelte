<script lang="ts">
	interface Props {
		startedAt: number | null;
	}

	let { startedAt }: Props = $props();

	let elapsed = $state(0);

	$effect(() => {
		if (!startedAt) return;
		const interval = setInterval(() => {
			elapsed = Date.now() - startedAt!;
		}, 500);
		return () => clearInterval(interval);
	});

	function fmt(ms: number): string {
		const s = Math.floor(ms / 1000);
		const m = Math.floor(s / 60);
		return `${String(m).padStart(2, '0')}:${String(s % 60).padStart(2, '0')}`;
	}
</script>

<div class="hud">
	<span class="dot"></span>
	<span class="timer">{fmt(elapsed)}</span>
</div>

<style>
	.hud {
		position: absolute;
		top: 1rem;
		left: 50%;
		transform: translateX(-50%);
		background: rgb(var(--color-error) / 0.9);
		border-radius: 2rem;
		padding: 0.35rem 0.9rem;
		display: flex;
		align-items: center;
		gap: 0.5rem;
		z-index: 90;
		backdrop-filter: blur(var(--glass-blur));
		border: 1px solid rgb(255 255 255 / 0.2);
	}

	.dot {
		width: 8px;
		height: 8px;
		border-radius: 50%;
		background: rgb(var(--color-text-primary));
		animation: pulse 1s ease-in-out infinite;
	}

	.timer {
		font-size: 0.875rem;
		font-weight: 600;
		font-variant-numeric: tabular-nums;
		color: rgb(var(--color-text-primary));
		letter-spacing: 0.05em;
	}

	@keyframes pulse {
		0%, 100% { opacity: 1; }
		50%       { opacity: 0.3; }
	}
</style>
