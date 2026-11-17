<script lang="ts">
  import { StudioButton, StudioPanel, StudioStatusBadge } from '@hpd-research/hpd-studio-design';
  import type { AIFunctionProjection } from './read-model.ts';

  let { projection, expanded = false }: { projection: AIFunctionProjection; expanded?: boolean } = $props();

  function studioLink(section: string, token: string | null): string {
    return token ? `/studio/operations/${section}/${encodeURIComponent(token)}` : "#";
  }

  function permissionTone(kind: string): "neutral" | "info" | "warning" | "danger" | "success" {
    if (kind.toLowerCase() === "deny") return "danger";
    if (kind.toLowerCase() === "ask") return "warning";
    if (kind.toLowerCase() === "allow") return "success";
    return "neutral";
  }
</script>

<StudioPanel>
  <div class="flex flex-wrap items-start justify-between gap-3">
    <div class="min-w-0">
      <p class="studio-label">{projection.kind.replaceAll(/([a-z])([A-Z])/g, '$1 $2')}</p>
      <h2 class="mt-1 text-heading">{projection.name}</h2>
      <p class="studio-text-safe mt-1 text-body-sm text-studio-muted">{projection.description || "Model-facing callable capability."}</p>
    </div>
    {#if !expanded}
      <StudioButton size="small" onclick={() => location.assign(studioLink("agent-functions", projection.resourceToken))}>Inspect function</StudioButton>
    {/if}
  </div>

  <div class="flex flex-wrap gap-2">
    <StudioStatusBadge tone={permissionTone(projection.permission.kind)}>Permission: {projection.permission.kind}</StudioStatusBadge>
    <StudioStatusBadge>Invocation: {projection.invocationMode}</StudioStatusBadge>
    <StudioStatusBadge>Handling: {projection.invocationHandling}</StudioStatusBadge>
    <StudioStatusBadge>Replay: {projection.replayPolicy}</StudioStatusBadge>
    <StudioStatusBadge>{projection.actions.length} actions</StudioStatusBadge>
    {#if projection.variantCount > 1}<StudioStatusBadge tone="warning">{projection.variantCount} bindings</StudioStatusBadge>{/if}
  </div>

  {#if projection.permission.policyId}
    <p class="text-body-sm text-studio-muted">Policy: <span class="studio-text-safe font-mono">{projection.permission.policyId}</span></p>
  {/if}
  {#if expanded}
    <dl class="studio-disclosure-list">
      <div><dt>Capability ID</dt><dd class="font-mono text-xs">{projection.id}</dd></div>
      <div><dt>Owner ToolHarness</dt><dd class="font-mono text-xs">{projection.ownerHarnessIdentity || "Unavailable"}</dd></div>
      <div><dt>Declaration</dt><dd class="font-mono text-xs">{projection.declarationMemberName || "Runtime-defined"}</dd></div>
      <div><dt>Schema fingerprint</dt><dd class="font-mono text-xs">{projection.schemaFingerprint || "Unavailable"}</dd></div>
      <div><dt>Attached Agents</dt><dd>{projection.agents.length > 0 ? projection.agents.join(", ") : "None disclosed"}</dd></div>
    </dl>
    {@render Schema(projection.schema, "Closed function schema")}
    {@render Schema(projection.returnSchema, "Declared result schema")}
  {/if}

  <div class="grid gap-2">
    <h3 class="studio-label">AIFunctionActions</h3>
    {#each projection.actions as action (action.id)}
      <article class="grid gap-2 rounded-studio border border-studio-line bg-studio-panel-soft p-3">
        <div class="flex flex-wrap items-start justify-between gap-2">
          <div class="min-w-0">
            <p class="studio-label">Action</p>
            <h4 class="text-body font-semibold">{action.name}</h4>
            <p class="studio-text-safe text-body-sm text-studio-muted">{action.description || "Statically declared action branch."}</p>
          </div>
          <StudioButton size="small" onclick={() => location.assign(studioLink("agent-actions", action.resourceToken))}>Inspect action</StudioButton>
        </div>
        <div class="flex flex-wrap gap-2">
          <StudioStatusBadge tone={permissionTone(action.permission.kind)}>Permission: {action.permission.kind}</StudioStatusBadge>
          <StudioStatusBadge>Invocation: {action.invocationMode}</StudioStatusBadge>
          <StudioStatusBadge>Handling: {action.invocationHandling}</StudioStatusBadge>
          <StudioStatusBadge>Replay: {action.replayPolicy}</StudioStatusBadge>
        </div>
      </article>
    {:else}
      <p class="text-body-sm text-studio-muted">This function has no declared action branches.</p>
    {/each}
  </div>
</StudioPanel>

{#snippet Schema(value: unknown, label: string)}
  {#if value}
    <div>
      <p class="studio-label">{label}</p>
      <pre class="mt-1 max-h-[28rem] overflow-auto rounded-studio border border-studio-line bg-studio-panel-raised p-4 text-code studio-text-safe">{JSON.stringify(value, null, 2)}</pre>
    </div>
  {/if}
{/snippet}
