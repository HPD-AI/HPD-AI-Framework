<svelte:options runes={true} />

<script lang="ts">
  import type { RuntimeRequest as RuntimeRequestItem } from '@hpd-research/hpd-agent-headless-ui';
  import RuntimeRequest from '../runtime-request/runtime-request.svelte';
  import {
    createRuntimeRequestActions,
    createRuntimeRequestElementProps,
  } from '../runtime-request/index.js';
  import type { ThreadStateSnapshot } from '../thread-state.js';
  import type { ThreadRuntimeRequestsProps } from './types.js';

  let {
    thread,
    requests: providedRequests,
    request: renderRequest,
    empty,
    ...restProps
  }: ThreadRuntimeRequestsProps = $props();

  let current = $state<ThreadStateSnapshot | null>(null);

  $effect(() => {
    if (!thread) {
      current = null;
      return;
    }

    return thread.subscribe((snapshot) => {
      current = snapshot;
    });
  });

  const pendingRequests = $derived(providedRequests ?? current?.pendingRuntimeRequests ?? []);
  const rootProps = $derived({
    ...restProps,
    'data-hpd-thread-runtime-requests': '',
    'data-empty': pendingRequests.length === 0 ? '' : undefined,
  });

  function createItemState(item: RuntimeRequestItem) {
    return {
      actions: createRuntimeRequestActions(item, thread),
      props: createRuntimeRequestElementProps({
        item,
      }),
    };
  }
</script>

<div {...rootProps}>
  {#if pendingRequests.length === 0}
    {@render empty?.()}
  {:else}
    {#each pendingRequests as item, index (item.id)}
      {@const itemState = createItemState(item)}
      {#if renderRequest}
        {@render renderRequest({
          item,
          index,
          actions: itemState.actions,
          props: itemState.props,
        })}
      {:else}
        <RuntimeRequest {item} {thread} />
      {/if}
    {/each}
  {/if}
</div>
