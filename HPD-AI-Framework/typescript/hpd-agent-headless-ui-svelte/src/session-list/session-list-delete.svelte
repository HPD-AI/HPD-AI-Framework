<svelte:options runes={true} />

<script lang="ts">
  import { getSessionListItemContext, getSessionListRootContext } from './context.js';
  import { createSessionListDeleteElementProps } from './props.js';
  import type { SessionListDeleteProps } from './types.js';

  let {
    children,
    item,
    selectFallback,
    onDelete,
    ...restProps
  }: SessionListDeleteProps = $props();

  const root = getSessionListRootContext();
  const itemContext = getSessionListItemContext();
  const target = $derived(item ?? itemContext?.item ?? null);
  const elementProps = $derived(createSessionListDeleteElementProps(target, root.snapshot, restProps));

  async function deleteSession(event: MouseEvent) {
    event.stopPropagation();
    if (!target) return;
    await root.actions.delete(target.id, { selectFallback });
    await onDelete?.(target);
  }
</script>

<button {...elementProps} onclick={deleteSession}>
  {#if children}
    {@render children({ actions: root.actions, item: target, props: elementProps, snapshot: root.snapshot })}
  {:else}
    Delete
  {/if}
</button>
