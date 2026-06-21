<svelte:options runes={true} />

<script lang="ts">
  import Suggestion from './suggestion.svelte';
  import { createSuggestionListElementProps } from './props.js';
  import type { SuggestionListProps } from './types.js';

  let {
    additionalProperties,
    child,
    children,
    disabled = false,
    mode = 'populate',
    onSelect,
    persistSuggestionMetadata = true,
    populateMode = 'replace',
    runConfig,
    suggestion,
    suggestions,
    targetValue = $bindable(''),
    thread,
    ...restProps
  }: SuggestionListProps = $props();

  const elementProps = $derived(createSuggestionListElementProps(restProps));
</script>

{#if child}
  {@render child({ props: elementProps, suggestions })}
{:else}
  <div {...elementProps}>
    {#if children}
      {@render children({ props: elementProps, suggestions })}
    {:else}
      {#each suggestions as item, index (`${item.prompt}-${index}`)}
        <Suggestion
          additionalProperties={{
            ...(additionalProperties ?? {}),
            ...(item.additionalProperties ?? {}),
          }}
          description={item.description}
          {disabled}
          {mode}
          {onSelect}
          {persistSuggestionMetadata}
          {populateMode}
          prompt={item.prompt}
          {runConfig}
          bind:targetValue
          thread={thread}
          title={item.title}
        >
          {#snippet child(childProps)}
            {#if suggestion}
              {@render suggestion({ ...childProps, suggestion: item })}
            {:else}
              <button {...childProps.props}>
                <span>{childProps.title}</span>
                {#if childProps.description}
                  <small>{childProps.description}</small>
                {/if}
              </button>
            {/if}
          {/snippet}
        </Suggestion>
      {/each}
    {/if}
  </div>
{/if}
