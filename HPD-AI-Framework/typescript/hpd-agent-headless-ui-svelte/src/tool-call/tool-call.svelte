<svelte:options runes={true} />

<script lang="ts">
  import {
    createToolCallActions,
    createToolCallElementProps,
    createToolCallState,
    formatToolCallDuration,
    getDefaultToolCallExpanded,
    getToolCallVisibility,
  } from './props.js';
  import type { ToolCallActions, ToolCallProps } from './types.js';

  let {
    children,
    defaultExpanded,
    expanded = $bindable(undefined),
    inspectable = false,
    inspectLabel,
    label,
    onExpandedChange,
    onInspect,
    showArgs = true,
    showResult = true,
    statusLabel,
    tool,
    ...restProps
  }: ToolCallProps = $props();

  const uid = $props.id();
  const contentId = $derived(`${uid}-content`);
  const labelId = $derived(`${uid}-trigger`);
  const currentExpanded = $derived(expanded ?? defaultExpanded ?? getDefaultToolCallExpanded(tool));
  const actions = $derived<ToolCallActions>(createToolCallActions({
    getExpanded: () => currentExpanded,
    getInspectDetails: () => ({ state, tool }),
    onExpandedChange,
    onInspect,
    setExpanded: (nextExpanded) => {
      expanded = nextExpanded;
    },
  }));
  const state = $derived(createToolCallState({
    expanded: currentExpanded,
    inspectable,
    inspectLabel,
    label,
    onInspect,
    statusLabel,
    tool,
  }));
  const elementProps = $derived(createToolCallElementProps({
    actions,
    contentId,
    labelId,
    restProps,
    state,
  }));
  const visibility = $derived(getToolCallVisibility({ showArgs, showResult }));
  const durationLabel = $derived(formatToolCallDuration(state.durationMs));
</script>

{#if children}
  {@render children({ actions, elementProps, state, tool })}
{:else}
  <section {...elementProps.root}>
    <header {...elementProps.header}>
      <button {...elementProps.trigger}>
        <strong data-hpd-tool-call-name>{state.label}</strong>
      </button>
      <span data-hpd-tool-call-status>{state.statusLabel}</span>
      {#if durationLabel}
        <span data-hpd-tool-call-duration>{durationLabel}</span>
      {/if}
      {#if state.inspectable}
        <button {...elementProps.inspect}>{state.inspectLabel}</button>
      {/if}
    </header>

    <div {...elementProps.content}>
      {#if tool.toolharnessName || tool.callType}
        <div {...elementProps.meta}>
          {#if tool.toolharnessName}{tool.toolharnessName}{/if}
          {#if tool.toolharnessName && tool.callType} · {/if}
          {#if tool.callType}{tool.callType}{/if}
        </div>
      {/if}

      {#if tool.error}
        <div {...elementProps.error}>{tool.error}</div>
      {/if}

      {#if visibility.showArgs && state.argsText}
        <div {...elementProps.args}>
          <strong>Arguments</strong>
          <pre>{state.argsText}</pre>
        </div>
      {/if}

      {#if visibility.showResult && state.resultText}
        <div {...elementProps.result}>
          <strong>Result</strong>
          <pre>{state.resultText}</pre>
        </div>
      {/if}
    </div>
  </section>
{/if}
