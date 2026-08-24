<script lang="ts">
  import type { Snippet } from 'svelte';
  import StudioBoundedGrid from './StudioBoundedGrid.svelte';
  import StudioObservationState from './StudioObservationState.svelte';
  import type { StudioDisplayColumn, StudioDisplayObservation, StudioDisplayRailItem, StudioDisplayRow } from './types.ts';
  let { eyebrow, title, description, observation, railLabel = 'Resources', railItems = [], columns = [], rows = [], selectedId = null,
    onselect = () => {}, detail, workbench }: { eyebrow: string; title: string; description: string; observation: StudioDisplayObservation;
    railLabel?: string; railItems?: readonly StudioDisplayRailItem[]; columns?: readonly StudioDisplayColumn[]; rows?: readonly StudioDisplayRow[];
    selectedId?: string | null; onselect?: (id: string) => void; detail?: Snippet; workbench?: Snippet } = $props();
  let railOpen = $state(false); let workbenchOpen = $state(false);
</script>

<main class="studio-workspace" data-workspace-state={observation.state}>
  <header class="studio-workspace-header">
    <div class="min-w-0"><p class="studio-label">{eyebrow}</p><h1>{title}</h1><p class="studio-text-safe text-sm text-studio-muted">{description}</p></div>
    <div class="flex flex-wrap gap-2"><button class="studio-button lg:hidden" type="button" aria-expanded={railOpen} aria-controls="studio-resource-rail" onclick={() => railOpen = !railOpen}>Resources</button>
      {#if workbench}<button class="studio-button" type="button" aria-expanded={workbenchOpen} aria-controls="studio-workbench" onclick={() => workbenchOpen = !workbenchOpen}>Workbench</button>{/if}</div>
  </header>
  <div class="studio-workspace-grid">
    <aside id="studio-resource-rail" class:studio-rail-open={railOpen} class="studio-resource-rail" aria-label={railLabel}>
      <div class="flex items-center justify-between gap-2"><h2>{railLabel}</h2><span class="studio-badge">{railItems.length}</span></div>
      <nav aria-label={`${railLabel} in this finite view`}><ul class="grid gap-1">
        {#each railItems as item (item.id)}<li><button class="studio-rail-item" aria-current={item.selected ? 'true' : undefined} onclick={() => onselect(item.id)}>
          <span class="studio-truncate">{item.label}</span><small>{item.kind}{item.pinned ? ' · pinned' : ''}</small></button></li>
        {:else}<li class="studio-empty"><strong>No resource rail for this view</strong><span>Only server-disclosed resources appear here.</span></li>{/each}
      </ul></nav>
    </aside>
    <section class="studio-workspace-content">
      <StudioObservationState {observation} title={title}>
        <StudioBoundedGrid caption={`${title} finite results`} {columns} {rows} {selectedId} {onselect} />
      </StudioObservationState>
    </section>
    {#if detail}<aside class="studio-context-detail" aria-label="Contextual detail">{@render detail()}</aside>{/if}
  </div>
  {#if workbench}<aside id="studio-workbench" class:studio-workbench-open={workbenchOpen} class="studio-workbench" aria-label="Command and receipt workbench">
    <div class="flex items-center justify-between gap-3"><h2>Workbench</h2><button class="studio-button studio-button-sm" type="button" onclick={() => workbenchOpen = false}>Close</button></div>
    {@render workbench()}</aside>{/if}
</main>
