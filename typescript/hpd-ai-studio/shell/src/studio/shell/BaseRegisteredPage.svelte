<script lang="ts">
  import { StudioRegisteredWorkspace, type StudioDisplayObservation } from '@hpd-research/hpd-studio-design';
  import type { StudioLinkProjection, StudioObservation, StudioPageProps } from '@hpd-research/hpd-studio-core';

  type Value = Readonly<{ views: Readonly<Record<string, unknown>>; links: readonly StudioLinkProjection[] }>;
  let { page, resource, observation, commands, navigation }: StudioPageProps = $props();
  const observed = $derived(displayObservation(observation as StudioObservation<Value>));
  const payload = $derived(currentValue(observation as StudioObservation<Value>));

  function displayObservation(value: StudioObservation<Value>): StudioDisplayObservation {
    switch (value.state) {
      case 'value': return { state: 'current', observedAt: value.observedAt };
      case 'stale': return { state: 'stale', code: value.code, observedAt: value.observedAt };
      case 'loading': return { state: 'loading', hasPrevious: value.previous !== null };
      case 'unobserved': return { state: 'unobserved' };
      default: return { state: value.state, code: value.code };
    }
  }

  function currentValue(value: StudioObservation<Value>): Value | null {
    return value.state === 'value' || value.state === 'stale' ? value.value : value.state === 'loading' ? value.previous : null;
  }
</script>

<StudioRegisteredWorkspace eyebrow="HPD BASE Studio" {page} {resource} observation={observed}
  views={payload?.views ?? {}} links={payload?.links ?? []} {commands}
  onnavigate={(link: StudioLinkProjection) => navigation.navigate({ link })}/>
