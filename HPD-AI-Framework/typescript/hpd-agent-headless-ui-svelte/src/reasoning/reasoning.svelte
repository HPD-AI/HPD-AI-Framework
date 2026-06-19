<svelte:options runes={true} />

<script lang="ts">
  import { createReasoningElementProps } from './props.js';
  import type { ReasoningProps } from './types.js';

  let {
    children,
    label = 'Reasoning',
    status = 'complete',
    text,
    ...restProps
  }: ReasoningProps = $props();

  const elementProps = $derived(createReasoningElementProps({
    label,
    restProps,
    status,
    text,
  }));
</script>

{#if children}
  {@render children({ label, props: elementProps, status, text })}
{:else}
  <section {...elementProps}>
    <strong data-hpd-reasoning-label>{label}</strong>
    <p data-hpd-reasoning-text>{text}</p>
  </section>
{/if}
