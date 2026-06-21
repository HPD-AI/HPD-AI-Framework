<svelte:options runes={true} />

<script lang="ts">
  import { getThreadTimeline } from '@hpd-research/hpd-agent-headless-ui';
  import ThreadComposer from '../thread-composer/thread-composer.svelte';
  import ThreadRuntimeRequests from '../thread-runtime-requests/thread-runtime-requests.svelte';
  import ThreadScrollToBottom from '../thread-timeline-viewport/thread-scroll-to-bottom.svelte';
  import ThreadStatus from '../thread-status/thread-status.svelte';
  import ThreadTimeline from '../thread-timeline/thread-timeline.svelte';
  import ThreadTimelineViewport from '../thread-timeline-viewport/thread-timeline-viewport.svelte';
  import ThreadTimelineViewportFooter from '../thread-timeline-viewport/thread-timeline-viewport-footer.svelte';
  import type { ThreadStateSnapshot } from '../thread-state.js';
  import { createThreadConversationElementProps } from './props.js';
  import type { ThreadConversationProps } from './types.js';

  let {
    child,
    children,
    composer,
    composerProps = {},
    footer,
    header,
    requests,
    runtimeRequestPlacement = 'composer-panel',
    thread,
    timeline,
    viewport,
    viewportProps = {},
    ...restProps
  }: ThreadConversationProps = $props();

  let current = $state<ThreadStateSnapshot | null>(null);

  $effect(() => {
    current = thread.getSnapshot();
    return thread.subscribe((snapshot) => {
      current = snapshot;
    });
  });

  const snapshot = $derived(current ?? thread.getSnapshot());
  const region = $derived({ snapshot, thread });
  const elementProps = $derived(createThreadConversationElementProps(snapshot, restProps));
  const defaultTimeline = $derived(getThreadTimeline(snapshot.projection, {
    runtimeRequests: runtimeRequestPlacement === 'timeline' ? 'inline' : 'exclude',
  }));
  const shouldRenderRuntimeRequestPanel = $derived(runtimeRequestPlacement === 'composer-panel');
</script>

{#if child}
  {@render child({ ...region, props: elementProps })}
{:else}
  <div {...elementProps}>
    {#if children}
      {@render children(region)}
    {:else}
      {#if header}
        {@render header(region)}
      {:else}
        <ThreadStatus {thread} />
      {/if}

      {#if viewport}
        {@render viewport(region)}
      {:else}
        <ThreadTimelineViewport {thread} {...viewportProps}>
          {#snippet children()}
            {#if timeline}
              {@render timeline(region)}
            {:else}
              <ThreadTimeline {thread} timeline={defaultTimeline} />
            {/if}

            {#if shouldRenderRuntimeRequestPanel}
              {#if requests}
                {@render requests(region)}
              {:else}
                <ThreadRuntimeRequests {thread} />
              {/if}
            {/if}

            <ThreadTimelineViewportFooter>
              {#if footer}
                {@render footer(region)}
              {:else}
                <ThreadScrollToBottom />

                {#if composer}
                  {@render composer(region)}
                {:else}
                  <ThreadComposer {thread} {...composerProps} />
                {/if}
              {/if}
            </ThreadTimelineViewportFooter>
          {/snippet}
        </ThreadTimelineViewport>
      {/if}
    {/if}
  </div>
{/if}
