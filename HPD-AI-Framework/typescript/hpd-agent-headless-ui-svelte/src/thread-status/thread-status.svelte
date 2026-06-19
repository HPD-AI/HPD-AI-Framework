<svelte:options runes={true} />

<script lang="ts">
  import { createThreadStatusElementProps, createThreadStatusModel } from './props.js';
  import type { ThreadStateSnapshot } from '../thread-state.js';
  import ThreadStatusIndicator from './thread-status-indicator.svelte';
  import type { ThreadStatusProps } from './types.js';

  let {
    thread,
    child,
    children,
    ...restProps
  }: ThreadStatusProps = $props();

  let current = $state<ThreadStateSnapshot | null>(null);

  $effect(() => {
    current = thread.getSnapshot();
    return thread.subscribe((snapshot) => {
      current = snapshot;
    });
  });

  const snapshot = $derived(current ?? thread.getSnapshot());
  const status = $derived(createThreadStatusModel(snapshot));
  const elementProps = $derived(createThreadStatusElementProps(status, restProps));
</script>

{#if child}
  {@render child({ ...status, props: elementProps })}
{:else}
  <div {...elementProps}>
    {#if children}
      {@render children(status)}
    {:else}
      <ThreadStatusIndicator {status} data-hpd-thread-status-label />
    {/if}
  </div>
{/if}
