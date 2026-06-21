<svelte:options runes={true} />

<script lang="ts">
  import {
    createSuggestionActions,
    createSuggestionElementProps,
    createSuggestionModel,
  } from './props.js';
  import type { SuggestionProps } from './types.js';

  let {
    additionalProperties,
    child,
    children,
    description,
    disabled = false,
    mode = 'populate',
    onSelect,
    persistSuggestionMetadata = true,
    populateMode = 'replace',
    prompt,
    runConfig,
    targetValue = $bindable(''),
    title,
    thread,
    ...restProps
  }: SuggestionProps = $props();

  let submitting = $state(false);

  const model = $derived(createSuggestionModel({
    additionalProperties,
    description,
    disabled,
    mode,
    persistSuggestionMetadata,
    populateMode,
    prompt,
    submitting,
    thread,
    title,
  }));

  const actions = $derived(createSuggestionActions({
    model,
    onSelect,
    runConfig,
    getTargetValue: () => targetValue,
    setSubmitting: (value) => {
      submitting = value;
    },
    setTargetValue: (value) => {
      targetValue = value;
    },
  }));

  function handleClick(event: MouseEvent): void {
    event.preventDefault();
    void actions.select();
  }

  const elementProps = $derived(createSuggestionElementProps({
    model,
    onclick: handleClick,
    restProps,
  }));
</script>

{#if child}
  {@render child({ ...model, actions, props: elementProps })}
{:else}
  <button {...elementProps}>
    {#if children}
      {@render children({ ...model, actions, props: elementProps })}
    {:else}
      {model.title}
    {/if}
  </button>
{/if}
