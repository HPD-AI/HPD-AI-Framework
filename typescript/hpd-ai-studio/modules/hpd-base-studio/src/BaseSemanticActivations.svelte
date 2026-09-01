<script lang="ts">
  import type { StudioObservation, StudioPageProps } from '@hpd-research/hpd-studio-core';

  type SemanticDefinition = Readonly<{
    capturedAuthorityGeneration: string | null; compactedCount: string; definitionChecksum: string;
    hasMore: string; id: string; inspectionState: string; liveCount: string; owningModuleId: string;
    pageChecksum: string | null; retiredCount: string; storeId: string; version: string;
  }>;
  type Value = Readonly<{ views: Readonly<Record<string, unknown>> }>;

  let { observation }: StudioPageProps = $props();
  const semanticObservation = $derived(observation as StudioObservation<Value>);
  const payload = $derived(currentValue(semanticObservation));
  const definitions = $derived(readDefinitions(payload?.views['base.semanticActivations.definitions.list']));

  function currentValue(value: StudioObservation<Value>): Value | null {
    return value.state === 'value' || value.state === 'stale' ? value.value : value.state === 'loading' ? value.previous : null;
  }
  function readDefinitions(value: unknown): readonly SemanticDefinition[] {
    if (!Array.isArray(value)) return [];
    return value.filter((item): item is SemanticDefinition => {
      if (!item || typeof item !== 'object') return false;
      const row = item as Record<string, unknown>;
      return typeof row.id === 'string' && typeof row.version === 'string' && typeof row.storeId === 'string'
        && typeof row.inspectionState === 'string' && typeof row.liveCount === 'string'
        && typeof row.retiredCount === 'string' && typeof row.compactedCount === 'string';
    });
  }
</script>

<main class="min-h-0 p-4 sm:p-6 lg:p-8">
  <div class="mx-auto grid w-full max-w-[96rem] gap-5">
    <header>
      <p class="studio-label mb-1">HPD BASE Studio · Automations</p>
      <h1 class="text-3xl font-extrabold leading-tight">Semantic activation authority</h1>
      <p class="studio-text-safe mt-2 max-w-3xl text-sm text-studio-muted">Bounded inspection of the exact L53 definitions installed in the current graph. Protected semantic keys, scopes, payloads, and provider rows are never disclosed.</p>
    </header>
    {#if semanticObservation.state === 'loading' && definitions.length === 0}
      <section class="studio-panel p-6"><h2 class="text-lg font-bold">Inspecting current authority…</h2></section>
    {:else if semanticObservation.state === 'failed' || semanticObservation.state === 'unavailable'}
      <section class="studio-panel border-studio-warning-soft bg-studio-warning-muted p-6"><h2 class="text-lg font-bold">Semantic authority unavailable</h2><p class="mt-2 text-sm">No earlier authority is presented as current.</p></section>
    {:else if definitions.length === 0}
      <section class="studio-panel p-6"><h2 class="text-lg font-bold">No semantic definitions installed</h2><p class="mt-2 text-sm text-studio-muted">The finalized application graph currently contributes no L53 semantic activation definitions.</p></section>
    {:else}
      {#if semanticObservation.state === 'stale'}<p class="rounded-studio bg-studio-warning-muted p-3 text-sm font-semibold">Refresh failed. Showing the last authorized bounded observation.</p>{/if}
      <section class="grid gap-4">
        {#each definitions as definition (`${definition.storeId}:${definition.id}:${definition.version}`)}
          <article class="studio-panel grid gap-4 p-5">
            <div class="flex flex-wrap items-start justify-between gap-3">
              <div><p class="studio-label">{definition.storeId}</p><h2 class="text-xl font-bold">{definition.id} · v{definition.version}</h2><p class="text-sm text-studio-muted">Owned by {definition.owningModuleId}</p></div>
              <span class="rounded-studio-sm border border-studio-line px-3 py-1 text-xs font-bold">{definition.inspectionState}</span>
            </div>
            {#if definition.inspectionState === 'current'}
              <div class="grid gap-3 sm:grid-cols-3">
                <div class="rounded-studio border border-studio-line p-4"><p class="studio-label">Live</p><p class="mt-1 text-2xl font-bold">{definition.liveCount}</p></div>
                <div class="rounded-studio border border-studio-line p-4"><p class="studio-label">Retired</p><p class="mt-1 text-2xl font-bold">{definition.retiredCount}</p></div>
                <div class="rounded-studio border border-studio-line p-4"><p class="studio-label">Compacted absence</p><p class="mt-1 text-2xl font-bold">{definition.compactedCount}</p></div>
              </div>
              <dl class="grid gap-2 text-xs sm:grid-cols-2">
                <div><dt class="studio-label">Authority generation</dt><dd class="studio-text-safe font-mono">{definition.capturedAuthorityGeneration}</dd></div>
                <div><dt class="studio-label">More bounded results</dt><dd>{definition.hasMore}</dd></div>
                <div><dt class="studio-label">Definition checksum</dt><dd class="studio-text-safe font-mono">{definition.definitionChecksum}</dd></div>
                <div><dt class="studio-label">Sanitized page checksum</dt><dd class="studio-text-safe font-mono">{definition.pageChecksum}</dd></div>
              </dl>
            {:else}
              <p class="text-sm text-studio-muted">This store disclosed no inspectable semantic authority. Missing, unsupported, and unauthorized states intentionally share this presentation.</p>
            {/if}
          </article>
        {/each}
      </section>
    {/if}
  </div>
</main>
