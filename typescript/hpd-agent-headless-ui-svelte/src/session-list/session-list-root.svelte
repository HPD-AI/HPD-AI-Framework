<svelte:options runes={true} />

<script lang="ts">
  import type { SessionListSnapshot } from '@hpd-research/hpd-agent-headless-ui';
  import { createSessionListActions, createSessionListRootElementProps } from './props.js';
  import { setSessionListRootContext } from './context.js';
  import type { SessionListRootProps } from './types.js';

  let {
    sessionList,
    children,
    ...restProps
  }: SessionListRootProps = $props();

  let snapshot = $state<SessionListSnapshot | null>(null);

  $effect(() => {
    return sessionList.subscribe((next) => {
      snapshot = next;
    });
  });

  const current = $derived(snapshot ?? sessionList.getSnapshot());
  const actions = $derived(createSessionListActions(sessionList));
  const elementProps = $derived(createSessionListRootElementProps(current, restProps));

  setSessionListRootContext({
    get actions() {
      return actions;
    },
    get props() {
      return elementProps;
    },
    get sessionList() {
      return sessionList;
    },
    get snapshot() {
      return current;
    },
  });
</script>

<div {...elementProps}>
  {@render children?.({ actions, props: elementProps, snapshot: current })}
</div>
