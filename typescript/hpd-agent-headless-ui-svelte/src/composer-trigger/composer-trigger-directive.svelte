<svelte:options runes={true} />

<script lang="ts">
  import {
    getComposerTriggerPopoverContext,
  } from './context.js';
  import type { ComposerTriggerDirectiveProps } from './types.js';

  let {
    additionalProperties,
    formatter,
    onInserted,
  }: ComposerTriggerDirectiveProps = $props();

  const popover = getComposerTriggerPopoverContext();

  $effect(() => {
    return popover.registerBehavior({
      formatter,
      kind: 'directive',
      onInserted(details) {
        const patch = additionalProperties?.(details);
        if (patch) {
          details.result.additionalPropertiesPatch = patch;
        }
        return onInserted?.(details);
      },
    });
  });
</script>
