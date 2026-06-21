<svelte:options runes={true} />

<script lang="ts">
  import { getContextDisplayContext } from './context.js';
  import {
    createContextDisplayRingElementProps,
  } from './props.js';
  import type { ContextDisplayRingProps } from './types.js';

  let {
    child,
    children,
    size = 24,
    strokeWidth = 3,
    ...restProps
  }: ContextDisplayRingProps = $props();

  const context = getContextDisplayContext();
  const model = $derived(context.getModel());
  const radius = $derived((size - strokeWidth) / 2);
  const circumference = $derived(2 * Math.PI * radius);
  const progressOffset = $derived(circumference - ((model.percent ?? 0) / 100) * circumference);
  const elementProps = $derived(createContextDisplayRingElementProps(model, restProps));
</script>

{#if child}
  {@render child({ circumference, model, progressOffset, props: elementProps, radius, size, strokeWidth })}
{:else}
  <svg {...elementProps} width={size} height={size} viewBox={`0 0 ${size} ${size}`}>
    {#if children}
      {@render children({ circumference, model, progressOffset, props: elementProps, radius, size, strokeWidth })}
    {:else}
      <circle
        cx={size / 2}
        cy={size / 2}
        r={radius}
        fill="none"
        stroke="currentColor"
        stroke-opacity="0.2"
        stroke-width={strokeWidth}
      />
      <circle
        cx={size / 2}
        cy={size / 2}
        r={radius}
        fill="none"
        stroke="currentColor"
        stroke-dasharray={circumference}
        stroke-dashoffset={progressOffset}
        stroke-linecap="round"
        stroke-width={strokeWidth}
        transform={`rotate(-90 ${size / 2} ${size / 2})`}
        data-hpd-context-display-ring-progress
      />
    {/if}
  </svg>
{/if}

