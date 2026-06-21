<svelte:options runes={true} />

<script lang="ts">
  import RuntimeRequestPermission from './runtime-request-permission.svelte';
  import RuntimeRequestClarification from './runtime-request-clarification.svelte';
  import RuntimeRequestClientTool from './runtime-request-client-tool.svelte';
  import RuntimeRequestCustom from './runtime-request-custom.svelte';
  import {
    createRuntimeRequestActions,
    createRuntimeRequestActionProps,
    createRuntimeRequestElementProps,
    createRuntimeRequestKindElementProps,
  } from './props.js';
  import type { RuntimeRequestProps } from './types.js';

  let {
    item,
    thread,
    child,
    children,
    permission,
    clarification,
    clientTool,
    custom,
    onApprove,
    onClarify,
    onClientToolRespond,
    onDeny,
    onRespond,
    ...restProps
  }: RuntimeRequestProps = $props();

  const actions = $derived(createRuntimeRequestActions(item, thread));
  const canUseActions = $derived(Boolean(thread));
  const actionProps = $derived(createRuntimeRequestActionProps({
    canApprove: canUseActions && item.kind === 'permission',
    canDeny: canUseActions && item.kind === 'permission',
    canSubmit: canUseActions && item.kind !== 'permission',
    onApproveClick: (event) => event.preventDefault(),
    onDenyClick: (event) => event.preventDefault(),
  }));
  const elementProps = $derived(createRuntimeRequestElementProps({
    item,
    restProps,
  }));
  const kindProps = $derived(createRuntimeRequestKindElementProps({ item }));
</script>

{#if child}
  {@render child({ item, actions, actionProps, props: elementProps })}
{:else}
  <div {...elementProps}>
    {#if children}
      {@render children({ item, actions, actionProps })}
    {:else}
      <header data-hpd-runtime-request-header>
        <strong>{item.kind}</strong>
        <span>{item.sourceName}</span>
      </header>

      {#if item.kind === 'permission'}
        {#if permission}
          {@render permission({ item, actions, actionProps, props: kindProps })}
        {:else}
          <RuntimeRequestPermission
            {actions}
            {item}
            {onApprove}
            {onDeny}
            {thread}
          />
        {/if}
      {:else if item.kind === 'clarification'}
        {#if clarification}
          {@render clarification({ item, actions, actionProps, props: kindProps })}
        {:else}
          <RuntimeRequestClarification
            {actions}
            {item}
            {onClarify}
            {thread}
          />
        {/if}
      {:else if item.kind === 'client-tool'}
        {#if clientTool}
          {@render clientTool({ item, actions, actionProps, props: kindProps })}
        {:else}
          <RuntimeRequestClientTool
            {actions}
            {item}
            {onClientToolRespond}
            {thread}
          />
        {/if}
      {:else if custom}
        {@render custom({ item, actions, actionProps, props: kindProps })}
      {:else}
        <RuntimeRequestCustom
          {actions}
          {item}
          {onRespond}
          {thread}
        />
      {/if}
    {/if}
  </div>
{/if}
