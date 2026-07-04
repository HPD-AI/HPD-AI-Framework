<svelte:options runes={true} />

<script lang="ts">
  import {
    createRuntimeRequestActions,
    createRuntimeRequestActionProps,
    createRuntimeRequestKindElementProps,
  } from './props.js';
  import type { RuntimeRequestLeafProps } from './types.js';

  let {
    actions: providedActions,
    actionProps: providedActionProps,
    children,
    item,
    onClientToolRespond,
    thread,
    ...restProps
  }: RuntimeRequestLeafProps = $props();

  let responseValue = $state('');

  const actions = $derived(providedActions ?? createRuntimeRequestActions(item, thread));
  const canUseActions = $derived(Boolean(thread || providedActions));
  const actionProps = $derived(providedActionProps ?? createRuntimeRequestActionProps({
    canApprove: false,
    canDeny: false,
    canSubmit: canUseActions && item.kind === 'client-tool',
    onApproveClick: (event) => event.preventDefault(),
    onDenyClick: (event) => event.preventDefault(),
  }));
  const kindProps = $derived(createRuntimeRequestKindElementProps({ item, restProps }));
  const request = $derived(('request' in item ? item.request : {}) as {
    arguments?: unknown;
    description?: string;
    toolName?: string;
  });

  async function submitClientTool(event: SubmitEvent) {
    event.preventDefault();
    await actions.answerClientToolRequest(responseValue);
    await onClientToolRespond?.({ item, outcome: responseValue });
  }

  function formatUnknown(value: unknown): string {
    if (value === undefined || value === null) return '';
    if (typeof value === 'string') return value;
    try {
      return JSON.stringify(value, null, 2);
    } catch {
      return String(value);
    }
  }
</script>

<div {...kindProps}>
  {#if children}
    {@render children({ item, actions, actionProps, props: kindProps })}
  {:else}
    <form data-hpd-runtime-request-form onsubmit={submitClientTool}>
      <p>{request.description ?? request.toolName}</p>
      <pre data-hpd-runtime-request-arguments>{formatUnknown(request.arguments)}</pre>
      <label data-hpd-runtime-request-field>
        Tool response
        <textarea
          value={responseValue}
          oninput={(event) => {
            responseValue = event.currentTarget.value;
          }}
        ></textarea>
      </label>
      <button {...actionProps.submit}>Respond</button>
    </form>
  {/if}
</div>
