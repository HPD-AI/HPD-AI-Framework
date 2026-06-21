<svelte:options runes={true} />

<script lang="ts">
  import {
    createThreadBranchSwitcherActionProps,
    createThreadBranchSwitcherSelectDetails,
  } from './props.js';
  import type { ThreadBranchSwitcherPreviousProps } from './types.js';

  let {
    control,
    onSelect,
    onclick,
    ...restProps
  }: ThreadBranchSwitcherPreviousProps = $props();

  const actionProps = $derived(createThreadBranchSwitcherActionProps('previous', !control.previous));

  function handleClick(event: MouseEvent): void {
    (onclick as ((event: MouseEvent) => void) | undefined)?.(event);
    if (event.defaultPrevented) return;

    const details = createThreadBranchSwitcherSelectDetails(control, 'previous');
    if (!details) return;
    onSelect?.(details);
  }
</script>

<button {...actionProps} {...restProps} onclick={handleClick}>
  Previous
</button>
