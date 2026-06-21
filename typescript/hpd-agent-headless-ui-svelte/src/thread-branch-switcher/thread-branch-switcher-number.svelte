<svelte:options runes={true} />

<script lang="ts">
  import { getThreadBranchSwitcherNumber } from './props.js';
  import type { ThreadBranchSwitcherNumberProps } from './types.js';

  let {
    control,
    ...restProps
  }: ThreadBranchSwitcherNumberProps = $props();

  const number = $derived(getThreadBranchSwitcherNumber(control));
</script>

<span data-hpd-thread-branch-switcher-number {...restProps}>{number}</span>
