<svelte:options runes={true} />

<script lang="ts">
  import type { ThreadStateSnapshot } from '../thread-state.js';
  import { setContextDisplayContext } from './context.js';
  import {
    createContextDisplayModel,
    createContextDisplayRootElementProps,
  } from './props.js';
  import type { ContextDisplayRootProps } from './types.js';

  let {
    child,
    children,
    modelContextWindow = null,
    thread,
    usage = null,
    ...restProps
  }: ContextDisplayRootProps = $props();

  let current = $state<ThreadStateSnapshot | null>(null);

  $effect(() => {
    if (!thread) {
      current = null;
      return;
    }

    current = thread.getSnapshot();
    return thread.subscribe((snapshot) => {
      current = snapshot;
    });
  });

  const resolvedUsage = $derived(usage ?? current?.contextUsage ?? null);
  const model = $derived(createContextDisplayModel({
    modelContextWindow,
    usage: resolvedUsage,
  }));
  const elementProps = $derived(createContextDisplayRootElementProps(model, restProps));

  setContextDisplayContext({
    getModel: () => model,
  });
</script>

{#if child}
  {@render child({ ...model, props: elementProps })}
{:else}
  <div {...elementProps}>
    {#if children}
      {@render children(model)}
    {/if}
  </div>
{/if}

