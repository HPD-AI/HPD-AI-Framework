<script lang="ts">
  import { onMount } from 'svelte';
  import { useStudioModuleContext } from '@hpd-research/hpd-studio-core';
  import type { BaseSemanticStudioController, BaseSemanticStudioSnapshot } from './semantic-state.ts';

  const moduleContext = useStudioModuleContext();
  const controller = moduleContext.get<BaseSemanticStudioController>('base-semantic-controller');
  if (!controller) throw new Error('BASE semantic activation controller is unavailable.');
  let snapshot: BaseSemanticStudioSnapshot = $state(controller.snapshot());
  let storeId = $state('');
  let generatedName = $state(controller.definitions[0]?.generatedName ?? '');
  let contextInvalid = $state(false);
  onMount(() => controller.subscribe(next => { snapshot = next; }));
  async function inspect(event: SubmitEvent) {
    event.preventDefault(); contextInvalid = false;
    try { await controller.inspect(storeId, generatedName); }
    catch { contextInvalid = true; }
  }
  const counts = $derived(snapshot.page?.items.reduce((value, item) => {
    if (item.state === 1) value.live++; else if (item.state === 2) value.retired++; else value.compacted++;
    return value;
  }, { live: 0, retired: 0, compacted: 0 }) ?? { live: 0, retired: 0, compacted: 0 });
</script>

<main class="min-h-0 p-4 sm:p-6 lg:p-8">
  <div class="mx-auto grid w-full max-w-[96rem] gap-5">
    <header><p class="studio-label mb-1">HPD BASE Studio · Semantic activations</p><h1 class="text-3xl font-extrabold leading-tight">Semantic activation authority</h1><p class="studio-text-safe mt-2 max-w-3xl text-sm text-studio-muted">Inspect one exact installed definition through the authorized ControlPlane surface. Raw semantic keys, protected scopes, payloads, fences, and provider rows are never requested.</p></header>
    <section class="studio-panel grid gap-4 p-5">
      <div><h2 class="text-lg font-bold">Installed definition</h2><p class="text-sm text-studio-muted">Definitions come only from the finalized generated ControlPlane graph; operators cannot author identities or checksums.</p></div>
      <form class="grid gap-3 lg:grid-cols-[1fr_1fr_auto]" onsubmit={inspect}>
        <label class="grid gap-1 text-sm font-semibold">Store<input class="studio-focus-ring min-h-11 rounded-studio border border-studio-line bg-studio-panel-raised px-3" maxlength="256" bind:value={storeId} /></label>
        <label class="grid gap-1 text-sm font-semibold">Definition<select class="studio-focus-ring min-h-11 rounded-studio border border-studio-line bg-studio-panel-raised px-3" bind:value={generatedName}>{#each controller.definitions as definition}<option value={definition.generatedName}>{definition.id} · v{definition.version}</option>{/each}</select></label>
        <button class="studio-button self-end" type="submit" disabled={snapshot.phase === 'loading'}>{snapshot.phase === 'loading' ? 'Inspecting…' : 'Inspect'}</button>
      </form>
      {#if contextInvalid}<p class="text-sm font-semibold text-studio-danger" role="alert">Select an installed definition and enter a valid configured store.</p>{/if}
    </section>
    {#if snapshot.phase === 'unavailable'}<section class="studio-panel border-studio-warning-soft bg-studio-warning-muted p-6"><h2 class="text-xl font-bold">Semantic authority unavailable</h2><p class="mt-2 text-sm">The protected boundary does not distinguish missing, hidden, foreign, removed, or unauthorized definitions.</p></section>
    {:else if snapshot.phase === 'failed' && !snapshot.page}<section class="studio-panel border-studio-danger-soft bg-studio-danger-muted p-6"><h2 class="text-xl font-bold">Inspection failed</h2><p class="mt-2 text-sm">No semantic authority was disclosed.</p></section>{/if}
    {#if snapshot.page}
      <section class="studio-panel grid gap-4 p-5">
        {#if snapshot.stale}<p class="rounded-studio bg-studio-warning-muted p-3 text-sm font-semibold">Refresh failed. Showing the previously authorized bounded page.</p>{/if}
        <div class="grid gap-3 sm:grid-cols-3"><article class="rounded-studio border border-studio-line p-4"><p class="studio-label">Live</p><p class="mt-1 text-2xl font-bold">{counts.live}</p></article><article class="rounded-studio border border-studio-line p-4"><p class="studio-label">Retired</p><p class="mt-1 text-2xl font-bold">{counts.retired}</p></article><article class="rounded-studio border border-studio-line p-4"><p class="studio-label">Compacted absence</p><p class="mt-1 text-2xl font-bold">{counts.compacted}</p></article></div>
        <div class="overflow-x-auto"><table class="w-full text-left text-sm"><thead><tr><th class="p-2">State</th><th class="p-2">Slot generation</th><th class="p-2">Retirement position</th><th class="p-2">Authorized evidence</th></tr></thead><tbody>{#each snapshot.page.items as item (item.itemToken)}<tr class="border-t border-studio-line"><td class="p-2">{item.state === 1 ? 'Live' : item.state === 2 ? 'Retired' : 'Compacted absence'}</td><td class="p-2 font-mono">{item.slotGeneration}</td><td class="p-2 font-mono">{item.retirementPosition ?? '—'}</td><td class="p-2 font-mono">{item.itemToken}</td></tr>{/each}</tbody></table></div>
        <div class="flex justify-end"><button class="studio-button studio-button-sm" type="button" disabled={snapshot.page.next === null || snapshot.phase === 'loading'} onclick={() => controller.next()}>Next authorized page</button></div>
      </section>
    {/if}
  </div>
</main>
