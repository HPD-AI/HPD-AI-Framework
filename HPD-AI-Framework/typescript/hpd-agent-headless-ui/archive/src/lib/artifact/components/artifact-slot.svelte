<script lang="ts">
	import { boxWith } from 'svelte-toolbelt';
	import { ArtifactSlotState } from '../artifact.svelte.js';
	import type { ArtifactSlotProps } from '../types.js';

	let { title, content }: ArtifactSlotProps = $props();

	// Create slot state with boxed values
	const slotState = ArtifactSlotState.create({
		title: boxWith(() => title),
		content: boxWith(() => content)
	});

	// Register on mount, update on changes, unregister on destroy
	$effect(() => {
		// Register/update when title or content changes
		slotState.register();

		// Cleanup on unmount
		return () => {
			slotState.unregister();
		};
	});
</script>

<!-- Slot component renders nothing - it just registers content -->
