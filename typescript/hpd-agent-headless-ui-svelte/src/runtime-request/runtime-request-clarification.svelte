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
    onClarify,
    thread,
    ...restProps
  }: RuntimeRequestLeafProps = $props();

  let answer = $state('');

  const actions = $derived(providedActions ?? createRuntimeRequestActions(item, thread));
  const canUseActions = $derived(Boolean(thread || providedActions));
  const actionProps = $derived(providedActionProps ?? createRuntimeRequestActionProps({
    canApprove: false,
    canDeny: false,
    canSubmit: canUseActions && item.kind === 'clarification',
    onApproveClick: (event) => event.preventDefault(),
    onDenyClick: (event) => event.preventDefault(),
  }));
  const kindProps = $derived(createRuntimeRequestKindElementProps({ item, restProps }));
  const request = $derived(('request' in item ? item.request : {}) as {
    options?: string[];
    question?: string;
  });

  async function clarify(value: string) {
    await actions.clarify(value);
    await onClarify?.({ item, answer: value });
  }

  function submitClarification(event: SubmitEvent) {
    event.preventDefault();
    const trimmed = answer.trim();
    if (!trimmed) return;
    void clarify(trimmed);
  }
</script>

<div {...kindProps}>
  {#if children}
    {@render children({ item, actions, actionProps, props: kindProps })}
  {:else}
    <form data-hpd-runtime-request-form onsubmit={submitClarification}>
      <p>{request.question}</p>

      {#if request.options?.length}
        <div data-hpd-runtime-request-options>
          {#each request.options as option}
            <button
              type="button"
              onclick={() => {
                void clarify(option);
              }}
            >
              {option}
            </button>
          {/each}
        </div>
      {/if}

      <label data-hpd-runtime-request-field>
        Answer
        <input
          value={answer}
          oninput={(event) => {
            answer = event.currentTarget.value;
          }}
        />
      </label>
      <button {...actionProps.submit}>Submit</button>
    </form>
  {/if}
</div>
