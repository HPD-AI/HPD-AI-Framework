<script lang="ts">
  import type { ToolHarnessProjection } from './read-model.ts';

  let { projection }: { projection: ToolHarnessProjection } = $props();

  interface DeclarationNode {
    readonly kind: string;
    readonly label: string;
  }

  let declarations = $derived<DeclarationNode[]>([
    ...projection.skills.map(label => ({ kind: 'Skill', label })),
    ...projection.subAgents.map(label => ({ kind: 'Subagent', label })),
    ...projection.mcpServers.map(label => ({ kind: 'MCP', label })),
    ...projection.openApiSources.map(label => ({ kind: 'OpenAPI', label })),
    ...projection.multiAgentWorkflows.map(label => ({ kind: 'Workflow', label })),
    ...projection.availableFunctions
      .filter(name => !projection.functions.some(item => item.name === name))
      .map(label => ({ kind: 'AIFunction declared', label })),
    ...(projection.settings.available ? [{ kind: 'Settings', label: projection.settings.configurationType || 'Typed settings' }] : []),
    ...projection.dependencies.map(value => ({ kind: 'Dependency', label: value.name })),
    ...projection.middleware.map(value => ({ kind: 'Middleware', label: value.name }))
  ]);
  let functions = $derived(projection.functions);
  let graphHeight = $derived(Math.max(
    520,
    160 + Math.max(declarations.length * 66, functions.reduce((height, item) =>
      height + 112 + item.actions.length * 42, 0))
  ));
  let harnessY = $derived(graphHeight / 2 - 45);

  function studioLink(section: string, token: string | null): string {
    return token ? `/studio/operations/${section}/${encodeURIComponent(token)}` : '#';
  }

  function declarationY(index: number): number {
    return 78 + index * 58;
  }

  function functionY(index: number): number {
    let offset = 72;
    for (let current = 0; current < index; current++)
      offset += 112 + functions[current].actions.length * 42;
    return offset;
  }

  function functionHeight(index: number): number {
    return 94 + functions[index].actions.length * 42;
  }
</script>

<div class="grid gap-3">
  <div class="flex flex-wrap items-center justify-between gap-3">
    <div>
      <p class="studio-label">Capability graph</p>
      <p class="text-body-sm text-studio-muted">The ToolHarness is the ownership boundary. Functions contain their declared action branches.</p>
    </div>
    <div class="flex flex-wrap gap-2 text-meta text-studio-muted">
      <span class="rounded-full border border-studio-line px-2 py-1">Harness</span>
      <span class="rounded-full border border-studio-line px-2 py-1">Declared capability</span>
      <span class="rounded-full border border-studio-line px-2 py-1">AIFunction</span>
      <span class="rounded-full border border-studio-line px-2 py-1">Action</span>
    </div>
  </div>

  <div class="max-h-[42rem] overflow-auto rounded-studio border border-studio-line bg-studio-panel-raised">
    <svg
      class="min-w-[68rem]"
      viewBox={`0 0 1120 ${graphHeight}`}
      role="img"
      aria-label={`Capability graph for ${projection.name}`}>
      <defs>
        <marker id="capability-edge" viewBox="0 0 10 10" refX="9" refY="5"
          markerWidth="5" markerHeight="5" orient="auto-start-reverse">
          <path d="M 0 0 L 10 5 L 0 10 z" class="fill-studio-line-strong" />
        </marker>
      </defs>

      {#each functions as item, index (`function:${item.id}`)}
        <path
          d={`M 565 ${harnessY + 45} C 610 ${harnessY + 45}, 620 ${functionY(index) + functionHeight(index) / 2}, 665 ${functionY(index) + functionHeight(index) / 2}`}
          fill="none"
          class="stroke-studio-line-strong"
          stroke-width="1.25"
          marker-end="url(#capability-edge)" />
      {/each}

      {#each declarations as item, index (`declaration:${item.kind}:${item.label}`)}
        <path
          d={`M 415 ${harnessY + 45} C 370 ${harnessY + 45}, 360 ${declarationY(index) + 21}, 315 ${declarationY(index) + 21}`}
          fill="none"
          class="stroke-studio-line"
          stroke-width="1"
          stroke-dasharray="5 5"
          marker-end="url(#capability-edge)" />
        <g aria-label={`${item.kind}: ${item.label}`}>
          <rect x="70" y={declarationY(index)} width="245" height="42" rx="10"
            class="fill-studio-panel-soft stroke-studio-line" />
          <text x="86" y={declarationY(index) + 17} class="fill-studio-muted text-[11px] font-semibold uppercase tracking-wide">{item.kind}</text>
          <text x="86" y={declarationY(index) + 33} class="fill-studio-text text-[12px]">{item.label.length > 35 ? `${item.label.slice(0, 34)}…` : item.label}</text>
        </g>
      {/each}

      <a href={studioLink('harnesses', projection.resourceToken)} aria-label={`Open ${projection.name}`}>
        <g>
          <rect x="415" y={harnessY} width="150" height="90" rx="14"
            class="fill-studio-accent-soft stroke-studio-accent" stroke-width="1.5" />
          <text x="490" y={harnessY + 25} text-anchor="middle" class="fill-studio-muted text-[10px] font-semibold uppercase tracking-[0.18em]">ToolHarness</text>
          <text x="490" y={harnessY + 49} text-anchor="middle" class="fill-studio-text text-[14px] font-semibold">
            {projection.name.length > 20 ? `${projection.name.slice(0, 19)}…` : projection.name}
          </text>
          <text x="490" y={harnessY + 68} text-anchor="middle" class="fill-studio-muted text-[10px]">
            {projection.availability} · {projection.functionCount} functions
          </text>
        </g>
      </a>

      {#each functions as item, index (`function-node:${item.id}`)}
        {@const y = functionY(index)}
        <g>
          <rect x="665" y={y} width="375" height={functionHeight(index)} rx="12"
            class="fill-studio-panel-soft stroke-studio-line-strong" />
          <text x="685" y={y + 23} class="fill-studio-muted text-[10px] font-semibold uppercase tracking-wide">
            {item.kind.replaceAll(/([a-z])([A-Z])/g, '$1 $2')}
          </text>
          <a href={studioLink('agent-functions', item.resourceToken)} aria-label={`Open ${item.name}`}>
            <text x="685" y={y + 45} class="fill-studio-text text-[14px] font-semibold">{item.name}</text>
          </a>
          <text x="685" y={y + 63} class="fill-studio-muted text-[11px]">
            {item.invocationMode} · replay {item.replayPolicy} · {item.actions.length} actions
          </text>
          {#each item.actions as action, actionIndex (`${item.id}:${action.id}`)}
            <a href={studioLink('agent-actions', action.resourceToken)} aria-label={`Open ${action.name} action`}>
              <g>
                <rect x="690" y={y + 76 + actionIndex * 42} width="325" height="32" rx="8"
                  class="fill-studio-panel-raised stroke-studio-line" />
                <text x="704" y={y + 96 + actionIndex * 42} class="fill-studio-text text-[11px] font-medium">
                  Action · {action.name}
                </text>
                <text x="998" y={y + 96 + actionIndex * 42} text-anchor="end" class="fill-studio-muted text-[10px]">
                  {action.permission.kind}
                </text>
              </g>
            </a>
          {/each}
        </g>
      {/each}

      {#if declarations.length === 0 && functions.length === 0}
        <text x="560" y={harnessY + 130} text-anchor="middle" class="fill-studio-muted text-[12px]">
          No capability declarations are currently materialized for this owner.
        </text>
      {/if}
    </svg>
  </div>
</div>
