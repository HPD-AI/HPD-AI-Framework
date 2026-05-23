<script lang="ts">
  import type { ArtifactView } from "../../core/hpdosArtifacts.js";
  import type { HpdosState } from "../../core/hpdosState.js";
  import ArtifactCard from "./ArtifactCard.svelte";
  import WorkspaceDashboard from "./WorkspaceDashboard.svelte";
  import type { ViewActions } from "./types.js";

  let {
    appState,
    actions,
    artifactViews,
    showDashboard
  }: {
    appState: HpdosState;
    actions: ViewActions;
    artifactViews: ReadonlyMap<string, ArtifactView>;
    showDashboard: boolean;
  } = $props();
</script>

<div class="hpd-workspace-surface" id="workspaceSurface">
  {#if showDashboard}
    <WorkspaceDashboard {appState} {actions} />
  {:else if appState.artifacts.length}
    <div class="hpd-workspace-surface-stack">
      {#each appState.artifacts as artifact (artifact.id)}
        <ArtifactCard
          {artifact}
          view={artifactViews.get(artifact.id) || "preview"}
          open={appState.openArtifactId === artifact.id}
          {actions} />
      {/each}
    </div>
  {:else}
    <div class="hpd-surface-empty" aria-hidden="true"></div>
  {/if}
</div>
