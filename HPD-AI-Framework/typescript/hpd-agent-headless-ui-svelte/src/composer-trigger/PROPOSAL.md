# Composer Trigger Proposal

`ComposerTrigger` is the Svelte adapter primitive family for inline composer
triggers such as `@mentions` and `/commands`.

The feature is split intentionally:

- `hpd-agent-headless-ui` owns trigger detection, adapters, directive
  formatting, metadata helpers, and run-config patch helpers.
- `hpd-agent-headless-ui-svelte` owns DOM composition, snippets, bindable
  composer state, popover rendering, and user interaction.

This keeps trigger behavior reusable without pushing editor or popover logic
into the framework-neutral core.

## Goals

- Detect active trigger text near the composer cursor.
- Render trigger suggestions from a generic adapter.
- Insert directives into bound composer text.
- Persist structured directive metadata through `additionalProperties`.
- Let slash commands patch `ThreadComposer` run config.
- Let apps render categories, items, and custom actions with snippets.
- Avoid global providers and React-style runtime stores.

## Non-Goals

- Do not create a full rich-text editor.
- Do not own command catalogs globally.
- Do not send messages directly from the trigger primitive.
- Do not interpret slash commands in the transport layer.
- Do not inject mention/command metadata into model context automatically.

## API

Mention directive:

```svelte
<script lang="ts">
  import {
    ComposerTriggerDirective,
    ComposerTriggerItem,
    ComposerTriggerItems,
    ComposerTriggerPopover,
    ComposerTriggerRoot,
    ThreadComposer,
  } from '@hpd-research/hpd-agent-headless-ui-svelte';
  import {
    createComposerDirectiveAdditionalProperties,
    createStaticComposerTriggerAdapter,
  } from '@hpd-research/hpd-agent-headless-ui';

  let value = $state('');
  let cursor = $state(0);
  let textareaRef = $state<HTMLTextAreaElement | null>(null);
  let additionalProperties = $state<Record<string, unknown> | undefined>();

  const mentionAdapter = createStaticComposerTriggerAdapter({
    items: [
      { id: 'workspace', type: 'tool', label: 'Workspace' },
    ],
  });
</script>

<ComposerTriggerRoot
  bind:value
  bind:cursor
  bind:inputRef={textareaRef}
  bind:additionalProperties
>
  <ThreadComposer
    {thread}
    bind:value
    bind:textareaRef={textareaRef}
    {additionalProperties}
  />

  <ComposerTriggerPopover trigger="@" adapter={mentionAdapter}>
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
```

Slash action:

```svelte
<ComposerTriggerPopover trigger="/" adapter={commandAdapter}>
  <ComposerTriggerAction
    removeOnExecute
    onExecute={({ item }) => ({
      runConfigPatch: {
        modelId: item.metadata?.modelId,
        contextOverrides: {
          command: item.id,
        },
      },
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
```

## Primitive Family

- `ComposerTriggerRoot` provides bound composer state.
- `ComposerTriggerPopover` detects one trigger and provides suggestion state.
- `ComposerTriggerDirective` inserts text and can attach metadata.
- `ComposerTriggerAction` executes a behavior and can patch run config.
- `ComposerTriggerItems` renders the current filtered items.
- `ComposerTriggerItem` renders/selects one item.
- `ComposerTriggerCategories`, `ComposerTriggerCategory`, and
  `ComposerTriggerBack` support category-first menus.

## Boundary

The primitive does not know what a command means. It only patches bound
`runConfig` and `additionalProperties`. The app or backend decides how those
fields affect the actual run.
