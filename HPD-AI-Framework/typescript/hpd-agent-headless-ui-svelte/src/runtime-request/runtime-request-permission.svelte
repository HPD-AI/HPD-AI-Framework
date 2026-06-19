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
    onApprove,
    onDeny,
    thread,
    ...restProps
  }: RuntimeRequestLeafProps = $props();

  let denyReason = $state('');

  const actions = $derived(providedActions ?? createRuntimeRequestActions(item, thread));
  const canUseActions = $derived(Boolean(thread || providedActions));
  const actionProps = $derived(providedActionProps ?? createRuntimeRequestActionProps({
    canApprove: canUseActions && item.kind === 'permission',
    canDeny: canUseActions && item.kind === 'permission',
    canSubmit: false,
    onApproveClick: (event) => {
      event.preventDefault();
      void approve();
    },
    onDenyClick: (event) => {
      event.preventDefault();
      void deny();
    },
  }));
  const kindProps = $derived(createRuntimeRequestKindElementProps({ item, restProps }));
  const request = $derived(('request' in item ? item.request : {}) as {
    arguments?: unknown;
    description?: string;
    functionName?: string;
  });

  async function approve() {
    await actions.approve();
    await onApprove?.({ item });
  }

  async function deny() {
    const reason = denyReason || undefined;
    await actions.deny(reason);
    await onDeny?.({ item, reason });
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
    <div data-hpd-runtime-request-body>
      <p>{request.description ?? request.functionName}</p>
      {#if request.arguments}
        <pre data-hpd-runtime-request-arguments>{formatUnknown(request.arguments)}</pre>
      {/if}
    </div>

    <label data-hpd-runtime-request-field>
      Deny reason
      <input
        value={denyReason}
        oninput={(event) => {
          denyReason = event.currentTarget.value;
        }}
      />
    </label>

    <div data-hpd-runtime-request-actions>
      <button {...actionProps.deny}>Deny</button>
      <button {...actionProps.approve}>Allow</button>
    </div>
  {/if}
</div>
