<svelte:options runes={true} />

<script lang="ts">
  import {
    getComposerTriggerPopoverContext,
  } from './context.js';
  import type { ComposerTriggerActionProps } from './types.js';

  let {
    formatter,
    onExecute,
    removeOnExecute = false,
  }: ComposerTriggerActionProps = $props();

  const popover = getComposerTriggerPopoverContext();

  $effect(() => {
    return popover.registerBehavior({
      formatter,
      kind: 'action',
      onExecute,
      removeOnExecute,
    });
  });
</script>
