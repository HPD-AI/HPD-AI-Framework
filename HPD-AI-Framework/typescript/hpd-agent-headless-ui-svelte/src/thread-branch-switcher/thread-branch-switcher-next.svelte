<svelte:options runes={true} />

<script lang="ts">
  import {
    createThreadBranchSwitcherActionProps,
    createThreadBranchSwitcherSelectDetails,
  } from './props.js';
  import type { ThreadBranchSwitcherNextProps } from './types.js';

  let {
    control,
    onSelect,
    onclick,
    ...restProps
  }: ThreadBranchSwitcherNextProps = $props();

  const actionProps = $derived(createThreadBranchSwitcherActionProps('next', !control.next));

  function handleClick(event: MouseEvent): void {
    (onclick as ((event: MouseEvent) => void) | undefined)?.(event);
    if (event.defaultPrevented) return;

    const details = createThreadBranchSwitcherSelectDetails(control, 'next');
    if (!details) return;
    onSelect?.(details);
  }
</script>

<button {...actionProps} {...restProps} onclick={handleClick}>
  Next
</button>
