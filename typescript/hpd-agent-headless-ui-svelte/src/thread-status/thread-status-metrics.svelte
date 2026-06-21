<svelte:options runes={true} />

<script lang="ts">
  import { createThreadStatusMetricsElementProps } from './props.js';
  import type { ThreadStatusMetricsProps } from './types.js';

  let {
    child,
    children,
    status,
    ...restProps
  }: ThreadStatusMetricsProps = $props();

  const elementProps = $derived(createThreadStatusMetricsElementProps(status, restProps));
</script>

{#if child}
  {@render child({ props: elementProps, status })}
{:else}
  <span {...elementProps}>
    {#if children}
      {@render children({ status })}
    {:else}
      {#if status.activeToolCount > 0}
        <span data-hpd-thread-status-tools>
          {status.activeToolCount === 1 ? '1 tool' : `${status.activeToolCount} tools`}
        </span>
      {/if}

      {#if status.pendingRequestCount > 0}
        <span data-hpd-thread-status-requests>
          {status.pendingRequestCount === 1 ? '1 request' : `${status.pendingRequestCount} requests`}
        </span>
      {/if}

      {#if status.blockedReason}
        <span data-hpd-thread-status-blocked>{status.blockedReason}</span>
      {/if}
    {/if}
  </span>
{/if}
