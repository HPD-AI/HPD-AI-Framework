<svelte:options runes={true} />

<script lang="ts">
  import { getThreadBranchSwitcherCount } from './props.js';
  import type { ThreadBranchSwitcherCountProps } from './types.js';

  let {
    control,
    ...restProps
  }: ThreadBranchSwitcherCountProps = $props();

  const count = $derived(getThreadBranchSwitcherCount(control));
</script>

<span data-hpd-thread-branch-switcher-count {...restProps}>{count}</span>
