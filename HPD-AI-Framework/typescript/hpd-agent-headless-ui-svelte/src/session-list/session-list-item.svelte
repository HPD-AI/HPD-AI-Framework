<svelte:options runes={true} />

<script lang="ts">
  import { getSessionListRootContext, setSessionListItemContext } from './context.js';
  import { createSessionListItemElementProps } from './props.js';
  import SessionListSubtitle from './session-list-subtitle.svelte';
  import SessionListTitle from './session-list-title.svelte';
  import type { SessionListItemProps } from './types.js';

  let {
    item,
    index,
    children,
    onSelect,
    ...restProps
  }: SessionListItemProps = $props();

  const root = getSessionListRootContext();
  const elementProps = $derived(createSessionListItemElementProps(item, root.snapshot.loading, restProps));

  setSessionListItemContext({
    get actions() {
      return root.actions;
    },
    get item() {
      return item;
    },
    get index() {
      return index;
    },
  });

  async function selectItem() {
    root.actions.select(item.id);
    await onSelect?.(item);
  }
</script>

<button {...elementProps} onclick={selectItem}>
  {#if children}
    {@render children({ actions: root.actions, item, index, props: elementProps, snapshot: root.snapshot })}
  {:else}
    <SessionListTitle />
    <SessionListSubtitle />
  {/if}
</button>
