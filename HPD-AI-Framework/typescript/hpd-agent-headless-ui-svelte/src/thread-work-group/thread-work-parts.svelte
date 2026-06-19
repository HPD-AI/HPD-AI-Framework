<svelte:options runes={true} />

<script lang="ts">
  import Reasoning from '../reasoning/reasoning.svelte';
  import ToolCall from '../tool-call/tool-call.svelte';
  import {
    createThreadWorkPartElementProps,
    createThreadWorkPartsElementProps,
    createThreadWorkPartsState,
    getVisibleThreadWorkParts,
  } from './props.js';
  import type { ThreadWorkPartsProps } from './types.js';

  let {
    work,
    children,
    showFinalDraft = false,
    workPart,
    ...restProps
  }: ThreadWorkPartsProps = $props();

  const parts = $derived(getVisibleThreadWorkParts(work, showFinalDraft));
  const state = $derived(createThreadWorkPartsState(work, parts));
  const elementProps = $derived(createThreadWorkPartsElementProps(parts, restProps));
</script>

{#if children}
  {@render children(state)}
{:else}
  <div {...elementProps}>
    {#each parts as part, index (part.id)}
      {@const partProps = createThreadWorkPartElementProps(part)}
      {#if workPart}
        {@render workPart({ part, index, props: partProps, work })}
      {:else if part.type === 'reasoning'}
        <Reasoning
          text={part.text}
          status={part.status}
          {...partProps}
        />
      {:else if part.type === 'assistant-draft'}
        <section {...partProps}>
          <strong>Draft</strong>
          <p>{part.message.content}</p>
        </section>
      {:else if part.type === 'tool'}
        <ToolCall tool={part.tool} {...partProps} />
      {:else if part.type === 'tool-group'}
        <section {...partProps}>
          <strong>{part.group.label}</strong>
          <span>{part.group.summary}</span>
        </section>
      {:else if part.type === 'warning'}
        <section {...partProps}>
          <strong>Warning</strong>
          <p>{part.message}</p>
        </section>
      {:else}
        <section {...partProps}>
          <strong>{part.label}</strong>
        </section>
      {/if}
    {/each}
  </div>
{/if}
