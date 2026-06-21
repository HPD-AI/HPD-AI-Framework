<svelte:options runes={true} />

<script lang="ts">
  import {
    createComposerTriggerRootElementProps,
  } from './props.js';
  import {
    setComposerTriggerRootContext,
    type ComposerTriggerRootContext,
  } from './context.js';
  import type { ComposerTriggerRootProps } from './types.js';

  let {
    additionalProperties = $bindable(),
    children,
    cursor = $bindable(0),
    inputRef = $bindable(null),
    runConfig = $bindable(),
    value = $bindable(''),
    ...restProps
  }: ComposerTriggerRootProps = $props();

  function focusInputAt(nextCursor: number): void {
    const input = inputRef;
    if (!input) return;

    queueMicrotask(() => {
      input.focus();
      input.setSelectionRange(nextCursor, nextCursor);
    });
  }

  function mergeAdditionalProperties(patch: Record<string, unknown> | undefined): void {
    if (!patch) return;
    additionalProperties = {
      ...(additionalProperties ?? {}),
      ...patch,
    };
  }

  function mergeRunConfig(patch: typeof runConfig): void {
    if (!patch) return;
    runConfig = {
      ...(runConfig ?? {}),
      ...patch,
    };
  }

  function setCursor(nextCursor: number): void {
    cursor = nextCursor;
    focusInputAt(nextCursor);
  }

  const context: ComposerTriggerRootContext = {
    applyResult(result) {
      value = result.text;
      cursor = result.nextCursor;
      mergeAdditionalProperties(result.additionalPropertiesPatch);
      mergeRunConfig(result.runConfigPatch);
      focusInputAt(result.nextCursor);
    },
    getAdditionalProperties: () => additionalProperties,
    getCursor: () => cursor,
    getInput: () => inputRef,
    getRunConfig: () => runConfig,
    getValue: () => value,
    mergeAdditionalProperties,
    mergeRunConfig,
    setCursor,
    setValue(nextValue) {
      value = nextValue;
    },
  };

  setComposerTriggerRootContext(context);

  $effect(() => {
    const input = inputRef;
    if (!input) return;

    const updateCursor = (): void => {
      cursor = input.selectionStart ?? input.value.length;
    };

    input.addEventListener('click', updateCursor);
    input.addEventListener('input', updateCursor);
    input.addEventListener('keyup', updateCursor);
    input.addEventListener('select', updateCursor);
    updateCursor();

    return () => {
      input.removeEventListener('click', updateCursor);
      input.removeEventListener('input', updateCursor);
      input.removeEventListener('keyup', updateCursor);
      input.removeEventListener('select', updateCursor);
    };
  });

  const elementProps = $derived(createComposerTriggerRootElementProps(restProps));
</script>

<div {...elementProps}>
  {@render children?.({
    additionalProperties,
    cursor,
    inputRef,
    props: elementProps,
    runConfig,
    value,
  })}
</div>
