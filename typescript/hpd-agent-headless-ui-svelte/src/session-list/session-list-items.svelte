<svelte:options runes={true} />

<script lang="ts">
  import SessionListItem from './session-list-item.svelte';
  import { getSessionListRootContext } from './context.js';
  import type { SessionListItemsProps } from './types.js';

  let {
    children,
    empty,
    error,
    item: renderItem,
  }: SessionListItemsProps = $props();

  const context = getSessionListRootContext();
</script>

{#if children}
  {@render children({
    actions: context.actions,
    snapshot: context.snapshot,
  })}
{:else if context.snapshot.error}
  {#if error}
    {@render error({
      actions: context.actions,
      error: context.snapshot.error,
      snapshot: context.snapshot,
    })}
  {:else}
    <div data-hpd-session-list-error>{context.snapshot.error}</div>
  {/if}
{:else if context.snapshot.items.length === 0}
  {#if empty}
    {@render empty({
      actions: context.actions,
      snapshot: context.snapshot,
    })}
  {:else}
    <div data-hpd-session-list-empty>No sessions</div>
  {/if}
{:else}
  {#each context.snapshot.items as sessionItem, index (sessionItem.id)}
    {#if renderItem}
      {@render renderItem({
        actions: context.actions,
        item: sessionItem,
        index,
        snapshot: context.snapshot,
      })}
    {:else}
      <SessionListItem item={sessionItem} {index} />
    {/if}
  {/each}
{/if}
