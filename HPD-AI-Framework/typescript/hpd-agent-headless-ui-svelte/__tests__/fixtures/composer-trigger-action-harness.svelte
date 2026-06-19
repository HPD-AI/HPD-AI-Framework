<script lang="ts">
  import {
    createStaticComposerTriggerAdapter,
  } from '@hpd-research/hpd-agent-headless-ui';
  import {
    ComposerTriggerAction,
    ComposerTriggerItem,
    ComposerTriggerItems,
    ComposerTriggerPopover,
    ComposerTriggerRoot,
  } from '../../src/composer-trigger/index.js';
  import type { ThreadComposerRunConfig } from '../../src/thread-composer/index.js';

  let value = $state('/deep');
  let cursor = $state(5);
  let runConfig = $state<ThreadComposerRunConfig | undefined>();
  let executed = $state('');

  const adapter = createStaticComposerTriggerAdapter({
    items: [
      {
        id: 'deep',
        type: 'command',
        label: '/deep',
        description: 'Use the deep reasoning profile',
      },
    ],
  });
</script>

<ComposerTriggerRoot bind:value bind:cursor bind:runConfig>
  <ComposerTriggerPopover trigger="/" {adapter}>
    <ComposerTriggerAction
      removeOnExecute
      onExecute={({ item }) => {
        executed = item.id;
        return {
          runConfigPatch: {
            modelId: 'deep-model',
            contextOverrides: {
              command: item.id,
            },
          },
        };
      }}
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
<output data-testid="executed">{executed}</output>
<output data-testid="run-config">{JSON.stringify(runConfig ?? null)}</output>
