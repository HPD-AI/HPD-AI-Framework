<script lang="ts">
  import {
    createComposerDirectiveAdditionalProperties,
    createStaticComposerTriggerAdapter,
  } from '@hpd-research/hpd-agent-headless-ui';
  import {
    ComposerTriggerDirective,
    ComposerTriggerItem,
    ComposerTriggerItems,
    ComposerTriggerPopover,
    ComposerTriggerRoot,
  } from '../../src/composer-trigger/index.js';

  let value = $state('ask @wor');
  let cursor = $state(8);
  let additionalProperties = $state<Record<string, unknown> | undefined>();

  const adapter = createStaticComposerTriggerAdapter({
    items: [
      {
        id: 'workspace',
        type: 'tool',
        label: 'Workspace',
        description: 'Workspace tools',
      },
    ],
  });
</script>

<ComposerTriggerRoot bind:value bind:cursor bind:additionalProperties>
  <ComposerTriggerPopover trigger="@" {adapter}>
    <ComposerTriggerDirective
      additionalProperties={({ item, result }) => createComposerDirectiveAdditionalProperties({
        item,
        trigger: result.trigger,
      })}
    />

    <ComposerTriggerItems>
      {#snippet children({ items })}
        {#each items as item, index (item.id)}
          <ComposerTriggerItem {item} {index} />
        {/each}
      {/snippet}
    </ComposerTriggerItems>
  </ComposerTriggerPopover>
</ComposerTriggerRoot>

<output data-testid="value">{value}</output>
<output data-testid="cursor">{cursor}</output>
<output data-testid="metadata">{JSON.stringify(additionalProperties ?? null)}</output>
