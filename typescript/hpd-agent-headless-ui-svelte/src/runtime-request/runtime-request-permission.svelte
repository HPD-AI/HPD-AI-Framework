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
    permissionId?: string;
    sourceName?: string;
    reason?: string;
    risk?: string;
    permissionResource?: { displayName: string };
    evidence?: { payload: unknown };
    persistenceUnavailableReason?: string;
    choices?: { id: string; label: string; kind: string }[];
    functionName?: string;
    action?: string;
    arguments?: unknown;
  });

  async function answer(choice: { id: string; kind: string }) {
    if (!request.permissionId || !request.sourceName) return;
    await actions.respond({
      type: 'PERMISSION_RESPONSE', permissionId: request.permissionId,
      sourceName: request.sourceName, choiceId: choice.id,
      feedback: choice.kind === 'Allow' ? undefined : denyReason || undefined,
    });
    if (choice.kind === 'Allow') await onApprove?.({ item });
    else await onDeny?.({ item, reason: denyReason || undefined });
  }

  async function approve() {
    const choice = request.choices?.find(choice => choice.kind === 'Allow');
    if (choice) await answer(choice);
  }
  async function deny() {
    const choice = request.choices?.find(choice => choice.kind === 'Deny');
    if (choice) await answer(choice);
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
      <p>{request.reason ?? request.permissionResource?.displayName ?? 'Approval required'}</p>
      {#if request.functionName}
        <p data-hpd-runtime-request-function>
          Function: {request.functionName}{request.action ? ` / ${request.action}` : ''}
        </p>
      {/if}
      <p>Risk: {request.risk ?? 'Unspecified'}</p>
      {#if request.persistenceUnavailableReason}<p>Reusable approval unavailable: {request.persistenceUnavailableReason}</p>{/if}
      {#if request.arguments !== undefined}
        <pre data-hpd-runtime-request-arguments>{formatUnknown(request.arguments)}</pre>
      {/if}
      {#if request.evidence}
        <pre data-hpd-runtime-request-evidence>{formatUnknown(request.evidence.payload)}</pre>
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
      {#each request.choices ?? [] as choice (choice.id)}
        <button
          type="button"
          aria-disabled={!canUseActions}
          data-hpd-runtime-request-approve={choice.kind === 'Allow' ? '' : undefined}
          data-hpd-runtime-request-deny={choice.kind === 'Deny' ? '' : undefined}
          disabled={!canUseActions}
          onclick={() => void answer(choice)}
        >{choice.label}</button>
      {/each}
    </div>
  {/if}
</div>
