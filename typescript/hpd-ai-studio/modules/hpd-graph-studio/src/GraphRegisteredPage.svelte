<script lang="ts">
  import { StudioResourceWorkspace, type StudioDisplayObservation } from '@hpd-research/hpd-studio-design';
  import type { StudioObservation, StudioPageProps } from '@hpd-research/hpd-studio-core';
  let { route, resource, observation }: StudioPageProps = $props();
  const pageId = $derived(route.routeId.endsWith('.route') ? route.routeId.slice(0, -6) : route.routeId);
  const title = $derived(pageId.split('.').slice(1).map(part => part.replace(/([A-Z])/g, ' $1')).join(' · '));
  const observed = $derived(displayObservation(observation as StudioObservation<unknown>));
  const railItems = $derived(resource ? [{ id: resource.authorityChecksum, label: resource.kind, kind: 'Graph resource', selected: true }] : []);
  const columns = [{ id: 'kind', label: 'Graph resource', width: 'standard' as const }, { id: 'authority', label: 'Current authority', width: 'wide' as const }];
  const rows = $derived(resource ? [{ id: resource.authorityChecksum, label: resource.kind, cells: { kind: resource.kind,
    authority: resource.authorityChecksum.slice(0, 16) + '…' } }] : []);
  function displayObservation(value: StudioObservation<unknown>): StudioDisplayObservation { switch (value.state) {
    case 'value': return { state: 'current', observedAt: value.observedAt }; case 'stale': return { state: 'stale', code: value.code, observedAt: value.observedAt };
    case 'loading': return { state: 'loading', hasPrevious: value.previous !== null }; case 'unobserved': return { state: 'unobserved' };
    default: return { state: value.state, code: value.code }; } }
</script>

<StudioResourceWorkspace eyebrow="HPD Graph Studio" {title} description="Graph-owned definition and execution evidence linked to BASE-owned durable work."
  observation={observed} railLabel="Graph context" {railItems} {columns} {rows} selectedId={resource?.authorityChecksum ?? null}>
  {#snippet detail()}
    <p class="studio-label">Graph context</p><h2 class="mt-2 text-lg font-bold">{resource?.kind ?? 'No graph resource selected'}</h2>
    <p class="studio-text-safe mt-2 text-sm text-studio-muted">Graph semantics remain module-owned; linked BASE activation authority is not reconstructed here.</p>
  {/snippet}
</StudioResourceWorkspace>
