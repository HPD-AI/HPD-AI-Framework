<svelte:options runes={true} />

<script lang="ts">
  import { getThreadBranchSwitcherLabel } from './props.js';
  import type { ThreadBranchSwitcherLabelProps } from './types.js';

  let {
    control,
    ...restProps
  }: ThreadBranchSwitcherLabelProps = $props();

  const label = $derived(getThreadBranchSwitcherLabel(control));
</script>

<span data-hpd-thread-branch-switcher-label {...restProps}>{label}</span>
