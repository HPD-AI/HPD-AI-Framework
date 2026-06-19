<svelte:options runes={true} />

<script lang="ts">
  import {
    createCustomResponseInput,
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
    onRespond,
    thread,
    ...restProps
  }: RuntimeRequestLeafProps = $props();

  let responseValue = $state('');

  const actions = $derived(providedActions ?? createRuntimeRequestActions(item, thread));
  const canUseActions = $derived(Boolean(thread || providedActions));
  const actionProps = $derived(providedActionProps ?? createRuntimeRequestActionProps({
    canApprove: false,
    canDeny: false,
    canSubmit: canUseActions,
    onApproveClick: (event) => event.preventDefault(),
    onDenyClick: (event) => event.preventDefault(),
  }));
  const kindProps = $derived(createRuntimeRequestKindElementProps({ item, restProps }));

  async function submitCustomResponse(event: SubmitEvent) {
    event.preventDefault();
    const input = createCustomResponseInput(item, responseValue);
    await actions.respond(input);
    await onRespond?.({ item, input });
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
    <form data-hpd-runtime-request-form onsubmit={submitCustomResponse}>
      <p>{item.requestEventType}</p>
      {#if item.event}
        <pre data-hpd-runtime-request-event>{formatUnknown(item.event)}</pre>
      {/if}
      <label data-hpd-runtime-request-field>
        Response value
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
