<script lang="ts">
	/**
	 * Simple Artifact Test Component
	 *
	 * Minimal test to verify basic rendering works
	 */
	import * as Artifact from '../exports.js';

	interface Props {
		artifactIds?: string[];
		onProviderOpenChange?: (open: boolean, id: string | null) => void;
	}

	let { artifactIds = ['artifact-1'], onProviderOpenChange }: Props = $props();
</script>

{#snippet titleSnippet()}
	<span data-testid="title-content">Test Title</span>
{/snippet}

{#snippet contentSnippet()}
	<div data-testid="content-inner">Test Content</div>
{/snippet}

<Artifact.Provider onOpenChange={onProviderOpenChange} data-testid="provider">
	{#each artifactIds as id (id)}
		<Artifact.Root {id} data-testid="root-{id}">
			<Artifact.Slot title={titleSnippet} content={contentSnippet} />
			<Artifact.Trigger data-testid="trigger-{id}">
				Open
			</Artifact.Trigger>
		</Artifact.Root>
	{/each}
	<Artifact.Panel data-testid="panel">
		<Artifact.Title data-testid="title" />
		<Artifact.Content data-testid="content" />
		<Artifact.Close data-testid="close">Close</Artifact.Close>
	</Artifact.Panel>
</Artifact.Provider>
