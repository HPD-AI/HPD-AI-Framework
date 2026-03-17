<script lang="ts">
	import type { AppRecorderState } from '../AppRecorderState.svelte';
	let { editor }: { editor: AppRecorderState } = $props();
</script>

<div class="page-placeholder">
	<span>Audio Page — Phase 5</span>
</div>

<style>
	.page-placeholder {
		flex: 1; display: flex; align-items: center; justify-content: center;
		background: rgb(var(--color-bg-primary)); font-size: 0.75rem; opacity: 0.3; font-style: italic;
	}
</style>
