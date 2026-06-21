<svelte:options runes={true} />

<script lang="ts">
  import Message from '../message/message.svelte';
  import RuntimeRequest from '../runtime-request/runtime-request.svelte';
  import {
    createRuntimeRequestActions,
    createRuntimeRequestElementProps,
  } from '../runtime-request/index.js';
  import type { ThreadStateSnapshot } from '../thread-state.js';
  import ThreadWorkGroup from '../thread-work-group/thread-work-group.svelte';
  import { createThreadWorkGroupElementProps } from '../thread-work-group/index.js';
  import { createThreadTimelineElementProps } from './props.js';
  import type { ThreadTimelineProps } from './types.js';

  let {
    thread,
    timeline: providedTimeline,
    message: renderMessage,
    work: renderWork,
    runtimeRequest: renderRuntimeRequest,
    progress: renderProgress,
    warning: renderWarning,
    empty,
    ...restProps
  }: ThreadTimelineProps = $props();

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

  const items = $derived(providedTimeline ?? current?.timeline ?? []);
  const rootProps = $derived(createThreadTimelineElementProps(items.length === 0, restProps));
</script>

<div {...rootProps}>
  {#if items.length === 0}
    {@render empty?.({ props: rootProps })}
  {:else}
    {#each items as item, index (item.id)}
      {#if item.type === 'message'}
        {#if renderMessage}
          {@render renderMessage({ item, index, message: item.message })}
        {:else}
          <Message message={item.message} />
        {/if}
      {:else if item.type === 'work'}
        {@const workProps = createThreadWorkGroupElementProps(item.work)}
        {#if renderWork}
          {@render renderWork({ item, index, props: workProps, work: item.work })}
        {:else}
          <ThreadWorkGroup work={item.work} />
        {/if}
      {:else if item.type === 'runtime-request'}
        {@const actions = createRuntimeRequestActions(item.request, thread)}
        {@const requestProps = createRuntimeRequestElementProps({ item: item.request })}
        {#if renderRuntimeRequest}
          {@render renderRuntimeRequest({
            item,
            index,
            request: item.request,
            actions,
            props: requestProps,
          })}
        {:else}
          <RuntimeRequest item={item.request} {thread} />
        {/if}
      {:else if item.type === 'progress'}
        {#if renderProgress}
          {@render renderProgress({ item, index, label: item.label })}
        {:else}
          <div data-hpd-thread-timeline-progress>{item.label}</div>
        {/if}
      {:else if item.type === 'warning'}
        {#if renderWarning}
          {@render renderWarning({ item, index, message: item.message })}
        {:else}
          <div data-hpd-thread-timeline-warning>{item.message}</div>
        {/if}
      {/if}
    {/each}
  {/if}
</div>
