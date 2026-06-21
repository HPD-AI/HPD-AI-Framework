<svelte:options runes={true} />

<script lang="ts">
  import { getSessionListRootContext } from './context.js';
  import { createSessionListNewElementProps } from './props.js';
  import type { SessionListNewProps } from './types.js';

  let {
    children,
    metadata,
    name,
    select,
    sessionId,
    onCreate,
    ...restProps
  }: SessionListNewProps = $props();

  const context = getSessionListRootContext();
  const elementProps = $derived(createSessionListNewElementProps(context.snapshot, restProps));

  async function createSession() {
    const session = await context.actions.create({
      metadata,
      select,
      sessionId,
    });

    if (name !== undefined) {
      await context.actions.update(session.id, { metadata: { name } });
    }

    await onCreate?.(session);
  }
</script>

<button {...elementProps} onclick={createSession}>
  {#if children}
    {@render children({ actions: context.actions, props: elementProps, snapshot: context.snapshot })}
  {:else}
    New session
  {/if}
</button>
