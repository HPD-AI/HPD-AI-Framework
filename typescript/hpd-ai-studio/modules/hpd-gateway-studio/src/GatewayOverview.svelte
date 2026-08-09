<script lang="ts">
  import { onMount } from 'svelte';
  import { useStudioModuleContext, useStudioShell } from '@hpd-research/hpd-studio-core';
  import type { GatewayStudioController, GatewayStudioSnapshot } from './state.ts';

  const moduleContext = useStudioModuleContext();
  const shell = useStudioShell();
  const controller = requireController(moduleContext.get<GatewayStudioController>('gateway-controller'));
  let snapshot: GatewayStudioSnapshot = $state(controller.snapshot());
  let namespaceId = $state('');
  let targetId = $state('');
  let contextError = $state(false);

  onMount(() => controller.subscribe((next) => {
    if (next.draft !== snapshot.draft) {
      namespaceId = next.draft.namespaceId;
      targetId = next.draft.targetId;
    }
    snapshot = next;
  }));

  function selectContext(event: SubmitEvent) {
    event.preventDefault();
    controller.setDraft({ namespaceId, targetId });
    contextError = !controller.selectDraft();
  }

  const verdictClass = $derived(snapshot.verdict === 'Serving Ready' ? 'studio-badge-good' : snapshot.verdict === 'Not Ready' ? 'studio-badge-danger' : 'studio-badge-warning');
  const desiredCandidate = $derived(snapshot.observation?.desired.state === 'value' ? snapshot.observation.desired.value?.candidateId : undefined);
  const activeCandidate = $derived(snapshot.observation?.status.node.publication.state === 'ActiveAcknowledged' ? snapshot.observation.status.node.publication.active.candidateId : undefined);
  const effectiveCandidate = $derived(snapshot.observation?.effective.state === 'value' ? snapshot.observation.effective.value?.candidateId : undefined);
  const identityCorrelation = $derived(!desiredCandidate || !activeCandidate || !effectiveCandidate
    ? 'Incomplete'
    : desiredCandidate === activeCandidate && activeCandidate === effectiveCandidate ? 'Aligned' : 'Diverged');

  function requireController(value: GatewayStudioController | undefined): GatewayStudioController {
    if (!value) throw new Error('Gateway Studio controller is unavailable.');
    return value;
  }
</script>

<main class="min-h-0 p-4 sm:p-6 lg:p-8">
  <div class="mx-auto grid w-full max-w-[96rem] gap-5">
    <header class="flex flex-wrap items-start justify-between gap-4">
      <div>
        <p class="studio-label mb-1">HPD Gateway Studio · Overview</p>
        <h1 class="text-3xl font-extrabold leading-tight">Operational truth</h1>
        <p class="studio-text-safe mt-2 max-w-3xl text-sm text-studio-muted">One selected target, observed through the secured Gateway Admin API. Context entry never discovers or proves a resource.</p>
      </div>
      <span class={`studio-badge whitespace-nowrap ${verdictClass}`}>{snapshot.verdict}</span>
    </header>

    <section class="studio-panel grid gap-4 p-5" aria-labelledby="gateway-context-heading">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 id="gateway-context-heading" class="text-lg font-bold">Target context</h2>
          <p class="text-sm text-studio-muted">Enter exact identifiers. No target list or existence probe is performed.</p>
        </div>
        {#if snapshot.context}<button class="studio-button studio-button-sm" type="button" onclick={() => controller.clearContext()}>Clear context</button>{/if}
      </div>
      <form class="grid gap-3 md:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_auto]" onsubmit={selectContext}>
        <label class="grid gap-1 text-sm font-semibold">Namespace
          <input class="studio-focus-ring min-h-11 rounded-studio border border-studio-line bg-studio-panel-raised px-3" maxlength="128" bind:value={namespaceId} autocomplete="off" />
        </label>
        <label class="grid gap-1 text-sm font-semibold">Target
          <input class="studio-focus-ring min-h-11 rounded-studio border border-studio-line bg-studio-panel-raised px-3" maxlength="128" bind:value={targetId} autocomplete="off" />
        </label>
        <button class="studio-button self-end" type="submit">Observe target</button>
      </form>
      {#if contextError}<p class="text-sm font-semibold text-studio-danger" role="alert">Namespace and target must each be 1–128 UTF-8 bytes of normalized text.</p>{/if}
      {#if snapshot.context}<p class="studio-text-safe text-sm text-studio-muted">Selected: <strong>{snapshot.context.namespaceId}</strong> / <strong>{snapshot.context.targetId}</strong></p>{/if}
    </section>

    {#if snapshot.phase === 'signed-out'}
      <section class="studio-panel grid gap-3 p-6">
        <h2 class="text-xl font-bold">Authentication required</h2>
        <p class="text-sm text-studio-muted">Gateway Studio does not own credentials. Use the host authentication flow to continue.</p>
        {#if shell.authentication.beginSignIn}<button class="studio-button w-fit" type="button" onclick={() => shell.authentication.beginSignIn?.()}>Sign in</button>{/if}
      </section>
    {:else if snapshot.phase === 'context-required'}
      <section class="studio-panel p-6"><h2 class="text-xl font-bold">Choose a target context</h2><p class="mt-2 text-sm text-studio-muted">Overview remains idle until both identifiers are supplied explicitly.</p></section>
    {:else if snapshot.phase === 'unavailable'}
      <section class="studio-panel border-studio-warning-soft bg-studio-warning-muted p-6"><h2 class="text-xl font-bold">Target unavailable or not yet provisioned</h2><p class="mt-2 text-sm">The protected resource boundary does not reveal whether this target is absent, hidden, foreign, unowned, or denied.</p></section>
    {:else if snapshot.phase === 'denied'}
      <section class="studio-panel border-studio-danger-soft bg-studio-danger-muted p-6"><h2 class="text-xl font-bold">Gateway operation access denied</h2><p class="mt-2 text-sm">Navigation visibility is not authorization. The server remains authoritative.</p></section>
    {:else if snapshot.phase === 'failed' && !snapshot.observation}
      <section class="studio-panel border-studio-danger-soft bg-studio-danger-muted p-6"><h2 class="text-xl font-bold">Serving truth unavailable</h2><p class="mt-2 text-sm">The bounded refresh failed without a previous successful observation.</p></section>
    {/if}

    {#if snapshot.observation}
      <section class="studio-panel grid gap-4 p-5" aria-labelledby="gateway-lifecycle-heading">
        <div class="flex flex-wrap items-start justify-between gap-3">
          <div><h2 id="gateway-lifecycle-heading" class="text-lg font-bold">Lifecycle</h2><p class="text-sm text-studio-muted">Authored → Validated → Desired → Delivered → Active → Effective</p></div>
          <button class="studio-button studio-button-sm" type="button" disabled={snapshot.refreshing} onclick={() => controller.refresh()}>{snapshot.refreshing ? 'Refreshing…' : 'Refresh'}</button>
        </div>
        {#if snapshot.stale}<p class="rounded-studio bg-studio-warning-muted p-3 text-sm font-semibold text-studio-warning" role="status">Refresh failed. Showing stale truth observed at {snapshot.lastSuccessfulAt}.</p>{/if}
        {#if snapshot.observation.status.node.publication.state === 'PublicationIndeterminate'}<p class="rounded-studio bg-studio-danger-muted p-3 text-sm font-semibold text-studio-danger" role="alert">Publication is indeterminate. Serving truth remains unknown until a correlated acknowledgement or recovery is observed.</p>{/if}
        {#if snapshot.observation.status.node.host.state === 'RestartRequired'}<p class="rounded-studio bg-studio-warning-muted p-3 text-sm font-semibold text-studio-warning" role="alert">The Gateway host reports RestartRequired. A dynamic candidate activation cannot satisfy the pending host change.</p>{/if}
        <ol class="grid gap-3 sm:grid-cols-2 xl:grid-cols-6">
          {#each snapshot.lifecycle as stage}
            <li class="rounded-studio border border-studio-line bg-studio-panel-soft p-3">
              <p class="studio-label">{stage.label}</p><p class="studio-text-safe mt-1 font-bold">{stage.state}</p>
              {#if stage.identity}<p class="studio-text-safe mt-1 font-mono text-xs text-studio-muted">{stage.identity}</p>{/if}
              <p class="mt-2 text-xs text-studio-muted">Source: {stage.source}</p>
            </li>
          {/each}
        </ol>
        <div class="grid gap-3 md:grid-cols-3">
          <article class="rounded-studio border border-studio-line p-4"><p class="studio-label">Management truth</p><p class="mt-2 font-bold">{snapshot.observation.status.management.code}</p><p class="mt-1 text-sm text-studio-muted">Desired: {snapshot.observation.desired.state}</p></article>
          <article class="rounded-studio border border-studio-line p-4"><p class="studio-label">Node observation</p><p class="mt-2 font-bold">{snapshot.observation.status.nodeObservation}</p><p class="mt-1 text-sm text-studio-muted">Publication: {snapshot.observation.status.node.publication.state}</p></article>
          <article class="rounded-studio border border-studio-line p-4"><p class="studio-label">Effective truth</p><p class="mt-2 font-bold">{snapshot.observation.effective.state}</p><p class="mt-1 text-sm text-studio-muted">Observed at {snapshot.observation.observedAt}</p></article>
        </div>
        <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-5" aria-label="Gateway priority truth">
          <article class="rounded-studio border border-studio-line p-4"><p class="studio-label">Serving</p><p class="studio-text-safe mt-2 font-bold">{snapshot.observation.status.node.readiness.serving}</p></article>
          <article class="rounded-studio border border-studio-line p-4"><p class="studio-label">Desired revision</p><p class="studio-text-safe mt-2 font-mono text-sm">{snapshot.observation.desired.state === 'value' ? snapshot.observation.desired.value?.revisionId : snapshot.observation.desired.state}</p></article>
          <article class="rounded-studio border border-studio-line p-4"><p class="studio-label">Delivery</p><p class="studio-text-safe mt-2 font-bold">{snapshot.observation.status.management.latestNodeOutcome ?? 'NotAttempted'}</p></article>
          <article class="rounded-studio border border-studio-line p-4"><p class="studio-label">Active candidate</p><p class="studio-text-safe mt-2 font-mono text-sm">{activeCandidate ?? 'Not active'}</p></article>
          <article class="rounded-studio border border-studio-line p-4"><p class="studio-label">Identity correlation</p><p class="studio-text-safe mt-2 font-bold">{identityCorrelation}</p><p class="studio-text-safe mt-1 text-xs text-studio-muted">Effective: {effectiveCandidate ?? 'Not observed'}</p></article>
        </div>
        <p class="text-xs text-studio-muted" aria-live="polite">{snapshot.refreshing ? 'Refreshing remote observations.' : `Last successful observation: ${snapshot.lastSuccessfulAt ?? 'none'}.`}</p>
      </section>
    {/if}

    <section class="grid gap-3 md:grid-cols-4" aria-label="Gateway workspaces">
      <article class="studio-panel p-4"><p class="studio-label">Available</p><h2 class="mt-1 font-bold">Overview</h2></article>
      <article class="studio-panel p-4"><p class="studio-label">Available</p><h2 class="mt-1 font-bold"><a class="studio-focus-ring" href="/gateway/configure">Configure</a></h2><p class="mt-1 text-sm text-studio-muted">Author and validate one complete local candidate.</p></article>
      {#each ['Operate', 'Diagnose'] as workspace}
        <article class="studio-panel p-4 opacity-70"><p class="studio-label">Later decision slice</p><h2 class="mt-1 font-bold">{workspace}</h2><p class="mt-1 text-sm text-studio-muted">No route or authority is exposed yet.</p></article>
      {/each}
    </section>
  </div>
</main>
