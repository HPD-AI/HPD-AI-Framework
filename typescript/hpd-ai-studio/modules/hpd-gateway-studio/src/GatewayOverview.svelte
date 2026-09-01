<script lang="ts">
  import { onMount } from 'svelte';
  import { requireGatewayRuntimeContext } from './runtime-context.ts';
  import type { GatewayStudioController, GatewayStudioSnapshot } from './state.ts';
  import type { GatewayOperationsController } from './operations.ts';
  import { projectGatewayDiscovery, summarizeGatewayDiscovery } from './discovery-projection.ts';

  const moduleContext = requireGatewayRuntimeContext();
  const controller = moduleContext.controller;
  const operations: GatewayOperationsController = moduleContext.operations;
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
  function provision(){if(operations?.openProvisionReview())location.hash='/gateway/operate';}

  const verdictClass = $derived(snapshot.verdict === 'Serving Ready' ? 'studio-badge-good' : snapshot.verdict === 'Not Ready' ? 'studio-badge-danger' : 'studio-badge-warning');
  const desiredCandidate = $derived(snapshot.observation?.desired.state === 'value' ? snapshot.observation.desired.value?.candidateId : undefined);
  const observedNode = $derived(snapshot.observation?.status.node ?? undefined);
  const activeIdentity = $derived(observedNode?.publication.state === 'ActiveAcknowledged' ? observedNode.publication.active ?? undefined : undefined);
  const activeCandidate = $derived(activeIdentity?.candidateId);
  const effectiveRuntime = $derived(snapshot.observation?.effective.state === 'value' ? snapshot.observation.effective.value : undefined);
  const effectiveCandidate = $derived(snapshot.observation?.effective.state === 'value' ? snapshot.observation.effective.value?.candidateId : undefined);
  const identityCorrelation = $derived(!desiredCandidate || !activeIdentity || !effectiveRuntime
    ? 'Incomplete'
    : desiredCandidate === activeIdentity.candidateId &&
      activeIdentity.candidateId === effectiveRuntime.candidateId &&
      activeIdentity.applicationId === effectiveRuntime.applicationId &&
      hashEqual(activeIdentity.symbolicPlanIdentity, effectiveRuntime.symbolicPlanIdentity)
      ? 'Aligned' : 'Diverged');
  const discovery = $derived(projectGatewayDiscovery(observedNode?.upstreams ?? [], effectiveRuntime));
  const discoverySummary = $derived(summarizeGatewayDiscovery(discovery));
  const admissionProfiles = $derived(observedNode?.trafficAdmission.profiles ?? []);
  const admissionDegraded = $derived(admissionProfiles.filter(profile => !['notRequired', 'notObserved', 'healthy'].includes(profile.state)).length);

  function requireController(value: GatewayStudioController | undefined): GatewayStudioController {
    if (!value) throw new Error('Gateway Studio controller is unavailable.');
    return value;
  }
  function hashEqual(left:{readonly algorithm?:string;readonly value?:string},right:{readonly algorithm?:string;readonly value?:string}):boolean{return Boolean(left.algorithm&&left.value&&right.algorithm&&right.value&&left.algorithm===right.algorithm&&left.value===right.value);}
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
      </section>
    {:else if snapshot.phase === 'context-required'}
      <section class="studio-panel p-6"><h2 class="text-xl font-bold">Choose a target context</h2><p class="mt-2 text-sm text-studio-muted">Overview remains idle until both identifiers are supplied explicitly.</p></section>
    {:else if snapshot.phase === 'unavailable'}
      <section class="studio-panel border-studio-warning-soft bg-studio-warning-muted p-6"><h2 class="text-xl font-bold">Target unavailable or not yet provisioned</h2><p class="mt-2 text-sm">The protected resource boundary does not reveal whether this target is absent, hidden, foreign, unowned, or denied.</p>{#if snapshot.capabilities.state==='value'&&snapshot.capabilities.value?.capabilities.includes('gateway.management.target.provision')}<button class="studio-button mt-4" onclick={provision}>Provision target</button><p class="mt-2 text-xs text-studio-muted">This offer is based on the static API catalog. The server may still return the same protected result.</p>{/if}</section>
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
        {#if observedNode?.publication.state === 'PublicationIndeterminate'}<p class="rounded-studio bg-studio-danger-muted p-3 text-sm font-semibold text-studio-danger" role="alert">Publication is indeterminate. Serving truth remains unknown until a correlated acknowledgement or recovery is observed.</p>{/if}
        {#if observedNode?.host.state === 'RestartRequired'}<p class="rounded-studio bg-studio-warning-muted p-3 text-sm font-semibold text-studio-warning" role="alert">The Gateway host reports RestartRequired. A dynamic candidate activation cannot satisfy the pending host change.</p>{/if}
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
          <article class="rounded-studio border border-studio-line p-4"><p class="studio-label">Node observation</p><p class="mt-2 font-bold">{snapshot.observation.status.nodeObservation}</p><p class="mt-1 text-sm text-studio-muted">Publication: {observedNode?.publication.state ?? 'Not observed'}</p></article>
          <article class="rounded-studio border border-studio-line p-4"><p class="studio-label">Effective truth</p><p class="mt-2 font-bold">{snapshot.observation.effective.state}</p><p class="mt-1 text-sm text-studio-muted">Observed at {snapshot.observation.observedAt}</p></article>
        </div>
        <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-5" aria-label="Gateway priority truth">
          <article class="rounded-studio border border-studio-line p-4"><p class="studio-label">Serving</p><p class="studio-text-safe mt-2 font-bold">{observedNode?.readiness.serving ?? 'Not observed'}</p></article>
          <article class="rounded-studio border border-studio-line p-4"><p class="studio-label">Desired revision</p><p class="studio-text-safe mt-2 font-mono text-sm">{snapshot.observation.desired.state === 'value' ? snapshot.observation.desired.value?.revisionId : snapshot.observation.desired.state}</p></article>
          <article class="rounded-studio border border-studio-line p-4"><p class="studio-label">Delivery</p><p class="studio-text-safe mt-2 font-bold">{snapshot.observation.status.management.latestNodeOutcome ?? 'NotAttempted'}</p></article>
          <article class="rounded-studio border border-studio-line p-4"><p class="studio-label">Active candidate</p><p class="studio-text-safe mt-2 font-mono text-sm">{activeCandidate ?? 'Not active'}</p></article>
          <article class="rounded-studio border border-studio-line p-4"><p class="studio-label">Identity correlation</p><p class="studio-text-safe mt-2 font-bold">{identityCorrelation}</p><p class="studio-text-safe mt-1 text-xs text-studio-muted">Effective: {effectiveCandidate ?? 'Not observed'}</p></article>
        </div>
        <section class="grid gap-3" aria-labelledby="gateway-discovery-heading">
          <div class="flex flex-wrap items-start justify-between gap-3">
            <div><h3 id="gateway-discovery-heading" class="font-bold">Applied Upstream discovery</h3><p class="text-sm text-studio-muted">Read-only applied membership correlated with the current native graph. Endpoint addresses are intentionally unavailable.</p></div>
            <p class="studio-text-safe text-xs text-studio-muted">{discoverySummary.discovered} discovered · {discoverySummary.degraded} degraded · {discoverySummary.failed} failed · {discoverySummary.mismatched} mismatched</p>
          </div>
          {#if discovery.length === 0}
            <p class="rounded-studio border border-studio-line p-3 text-sm text-studio-muted">No applied Upstream observation is available.</p>
          {:else}
            <div class="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
              {#each discovery as upstream (upstream.upstreamId)}
                <article class="rounded-studio border border-studio-line p-4">
                  <div class="flex flex-wrap items-start justify-between gap-2"><strong class="studio-text-safe">{upstream.upstreamId}</strong><span class="studio-badge">{upstream.state}</span></div>
                  <p class="studio-text-safe mt-2 text-xs text-studio-muted">{upstream.profile ?? 'Static'} · {upstream.service ?? 'No service query'}{upstream.endpoint ? ` / ${upstream.endpoint}` : ''}</p>
                  <dl class="mt-3 grid grid-cols-2 gap-2 text-xs"><div><dt class="studio-label">Applied / available</dt><dd>{upstream.appliedDestinationCount} / {upstream.availableDestinationCount}</dd></div><div><dt class="studio-label">Generation</dt><dd class="studio-text-safe font-mono">{upstream.membershipGeneration ?? 'static'}</dd></div><div><dt class="studio-label">Native eligibility</dt><dd>{upstream.eligibility}</dd></div><div><dt class="studio-label">Correlation</dt><dd>{upstream.correlation}</dd></div></dl>
                </article>
              {/each}
            </div>
          {/if}
        </section>
        <section class="grid gap-3" aria-labelledby="gateway-admission-heading">
          <div class="flex flex-wrap items-start justify-between gap-3">
            <div><h3 id="gateway-admission-heading" class="font-bold">Traffic admission authorities</h3><p class="text-sm text-studio-muted">Bounded aggregate authority health. Partitions, claims, Redis endpoints, keys, and provider exceptions are never exposed.</p></div>
            <p class="studio-text-safe text-xs text-studio-muted">{admissionProfiles.length} profiles · {admissionDegraded} degraded or unavailable</p>
          </div>
          {#if admissionProfiles.length === 0}
            <p class="rounded-studio border border-studio-line p-3 text-sm text-studio-muted">No traffic-admission profile is installed.</p>
          {:else}
            <div class="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
              {#each admissionProfiles as profile (profile.profile)}
                <article class="rounded-studio border border-studio-line p-4">
                  <div class="flex flex-wrap items-start justify-between gap-2"><strong class="studio-text-safe">{profile.profile}</strong><span class="studio-badge">{profile.state}</span></div>
                  <p class="studio-text-safe mt-2 text-xs text-studio-muted">{profile.scope} · authority {profile.authorityId}</p>
                  <dl class="mt-3 grid grid-cols-2 gap-2 text-xs"><div><dt class="studio-label">Acquired / rejected</dt><dd>{profile.acquired} / {profile.rejected}</dd></div><div><dt class="studio-label">Infrastructure</dt><dd>{profile.infrastructureFailures}</dd></div><div><dt class="studio-label">Bypass / fallback</dt><dd>{profile.degradedBypasses} / {profile.localFallbacks}</dd></div><div><dt class="studio-label">Observed</dt><dd>{profile.lastObservedAt ?? 'Not observed'}</dd></div></dl>
                  {#if profile.safeDiagnosticCode}<p class="studio-text-safe mt-2 text-xs text-studio-muted">{profile.safeDiagnosticCode}</p>{/if}
                </article>
              {/each}
            </div>
          {/if}
        </section>
        <p class="text-xs text-studio-muted" aria-live="polite">{snapshot.refreshing ? 'Refreshing remote observations.' : `Last successful observation: ${snapshot.lastSuccessfulAt ?? 'none'}.`}</p>
      </section>
    {/if}

    <section class="grid gap-3 md:grid-cols-4" aria-label="Gateway workspaces">
      <article class="studio-panel p-4"><p class="studio-label">Available</p><h2 class="mt-1 font-bold">Overview</h2></article>
      <article class="studio-panel p-4"><p class="studio-label">Available</p><h2 class="mt-1 font-bold"><a class="studio-focus-ring" href="#/gateway/configure">Configure</a></h2><p class="mt-1 text-sm text-studio-muted">Author, validate, and review submission of one complete local candidate.</p></article>
      <article class="studio-panel p-4"><p class="studio-label">Available</p><h2 class="mt-1 font-bold"><a class="studio-focus-ring" href="#/gateway/operate">Operate</a></h2><p class="mt-1 text-sm text-studio-muted">Inspect immutable revisions and govern activation with explicit confirmation.</p></article>
      <article class="studio-panel p-4"><p class="studio-label">Available</p><h2 class="mt-1 font-bold"><a class="studio-focus-ring" href="#/gateway/diagnose">Diagnose</a></h2><p class="mt-1 text-sm text-studio-muted">Outcome-first readiness, provenance, and bounded local observation export.</p></article>
    </section>
  </div>
</main>
