<svelte:options runes={true} />

<script lang="ts">
  import {
    createThreadBranchSwitcherActionProps,
    createThreadBranchSwitcherElementProps,
    createThreadBranchSwitcherSelectDetails,
    getThreadBranchSwitcherLabel,
  } from './props.js';
  import ThreadBranchSwitcherLabel from './thread-branch-switcher-label.svelte';
  import ThreadBranchSwitcherNext from './thread-branch-switcher-next.svelte';
  import ThreadBranchSwitcherPrevious from './thread-branch-switcher-previous.svelte';
  import type { ThreadBranchSwitcherProps } from './types.js';

  let {
    control,
    onSelect,
    children,
    ...restProps
  }: ThreadBranchSwitcherProps = $props();

  const elementProps = $derived(createThreadBranchSwitcherElementProps(control, restProps));
  const label = $derived(getThreadBranchSwitcherLabel(control));
  const previousProps = $derived(createThreadBranchSwitcherActionProps('previous', !control.previous));
  const nextProps = $derived(createThreadBranchSwitcherActionProps('next', !control.next));

  function selectPrevious(): void {
    const details = createThreadBranchSwitcherSelectDetails(control, 'previous');
    if (!details) return;
    onSelect?.(details);
  }

  function selectNext(): void {
    const details = createThreadBranchSwitcherSelectDetails(control, 'next');
    if (!details) return;
    onSelect?.(details);
  }
</script>

{#if control.position.total > 1}
  <div {...elementProps}>
    {#if children}
      {@render children({
        control,
        current: control.position.current,
        label,
        next: control.next,
        nextProps,
        previous: control.previous,
        previousProps,
        props: elementProps,
        selectNext,
        selectPrevious,
        total: control.position.total,
      })}
    {:else}
      <ThreadBranchSwitcherPrevious {control} {onSelect} />
      <ThreadBranchSwitcherLabel {control} />
      <ThreadBranchSwitcherNext {control} {onSelect} />
    {/if}
  </div>
{/if}
