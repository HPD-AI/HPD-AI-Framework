<svelte:options runes={true} />

<script lang="ts">
  import {
    createDirectiveTextParts,
  } from '@hpd-research/hpd-agent-headless-ui';
  import {
    createDirectiveTextPartElementProps,
    createDirectiveTextRootElementProps,
  } from './props.js';
  import type {
    DirectiveTextChipElementProps,
    DirectiveTextPlainElementProps,
    DirectiveTextProps,
  } from './types.js';

  let {
    directive,
    directives,
    message,
    part: renderPart,
    text,
    textPart,
    children,
    ...restProps
  }: DirectiveTextProps = $props();

  const parts = $derived(createDirectiveTextParts({
    directives,
    message,
    text,
  }));
  const rootProps = $derived(createDirectiveTextRootElementProps(restProps));
</script>

{#if children}
  {@render children({ message, parts, props: rootProps })}
{:else}
  <span {...rootProps}>
    {#each parts as item (item.id)}
      {@const props = createDirectiveTextPartElementProps(item)}
      {#if renderPart}
        {@render renderPart({ message, part: item, props })}
      {:else if item.type === 'directive'}
        {@const chipProps = props as DirectiveTextChipElementProps}
        {#if directive}
          {@render directive({ directive: item.directive, message, part: item, props: chipProps })}
        {:else}
          <span {...chipProps}>{item.text}</span>
        {/if}
      {:else}
        {@const plainProps = props as DirectiveTextPlainElementProps}
        {#if textPart}
          {@render textPart({ message, part: item, props: plainProps })}
        {:else}
          <span {...plainProps}>{item.text}</span>
        {/if}
      {/if}
    {/each}
  </span>
{/if}
