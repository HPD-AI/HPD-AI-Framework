<svelte:options runes={true} />

<script lang="ts">
  import { createThreadErrorElementProps, createThreadErrorModel } from './props.js';
  import type { ThreadStateSnapshot } from '../thread-state.js';
  import type { ThreadErrorProps } from './types.js';

  let {
    thread,
    child,
    children,
    clearLabel = 'Dismiss error',
    showAll = false,
    ...restProps
  }: ThreadErrorProps = $props();

  let current = $state<ThreadStateSnapshot | null>(null);

  $effect(() => {
    current = thread.getSnapshot();
    return thread.subscribe((snapshot) => {
      current = snapshot;
    });
  });

  const snapshot = $derived(current ?? thread.getSnapshot());
  const model = $derived(createThreadErrorModel(thread, { snapshot }));
  const elementProps = $derived(createThreadErrorElementProps(model, restProps, clearLabel));
</script>

{#if model.hasError}
  {#if child}
    {@render child({ ...model, props: elementProps })}
  {:else}
    <div {...elementProps.root}>
      {#if children}
        {@render children(model)}
      {:else}
        <div data-hpd-thread-error-message>{model.error?.message}</div>

        {#if showAll && model.errors.length > 1}
          <ul data-hpd-thread-error-list>
            {#each model.errors as error (error.id)}
              <li data-hpd-thread-error-list-item data-error-kind={error.kind}>
                {error.message}
              </li>
            {/each}
          </ul>
        {/if}

        {#if model.error?.recoverable}
          <button {...elementProps.clearButton} onclick={model.actions.clear}>
            {clearLabel}
          </button>
        {/if}
      {/if}
    </div>
  {/if}
{/if}
