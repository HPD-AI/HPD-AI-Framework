<svelte:options runes={true} />

<script lang="ts">
  import ThreadTimeline from '../../src/thread-timeline/thread-timeline.svelte';
  import type { ThreadTimelineItem } from '@hpd-research/hpd-agent-headless-ui';

  let {
    timeline,
  }: {
    timeline: ThreadTimelineItem[];
  } = $props();
</script>

<ThreadTimeline {timeline}>
  {#snippet message({ message, index })}
    <article data-testid="custom-message" data-index={index}>
      {message.role}: {message.content}
    </article>
  {/snippet}

  {#snippet work({ work, props })}
    <section {...props} data-testid="custom-work">
      {work.label} ({work.parts.length})
    </section>
  {/snippet}

  {#snippet runtimeRequest({ request })}
    <section data-testid="custom-request">
      {request.kind}:{request.id}
    </section>
  {/snippet}
</ThreadTimeline>
