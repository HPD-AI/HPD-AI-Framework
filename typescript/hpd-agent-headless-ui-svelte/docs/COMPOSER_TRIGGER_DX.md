# Composer Trigger DX

Composer triggers add inline `@mention` and `/command` behavior to a Svelte
composer without turning the composer into a global runtime.

Use them when the user should pick structured context while typing:

- `@workspace` to attach a tool, file, project, agent, or other entity.
- `/deep` to choose a run profile or command before sending.

## Architecture

The framework-neutral core handles pure logic:

- trigger detection
- static or custom adapters
- directive formatting
- metadata/run-config patch result shapes

The Svelte adapter handles UI:

- bound composer value and cursor
- bound textarea reference
- popover rendering
- item/category snippets
- applying selected results into Svelte state

## Mentions

```svelte
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

Selecting a directive updates the draft text and patches
`additionalProperties.directives`.

## Commands

```svelte
<ComposerTriggerRoot bind:value bind:cursor bind:runConfig>
  <ThreadComposer {thread} bind:value {runConfig} />

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
</ComposerTriggerRoot>
```

Selecting a command can remove the typed command and patch the next run config.

## Custom Rendering

```svelte
<ComposerTriggerItem {item} {index}>
  {#snippet children({ highlighted, item, props, select })}
    <button {...props} class:active={highlighted} onclick={() => select()}>
      <strong>{item.label}</strong>
      <small>{item.description}</small>
    </button>
  {/snippet}
</ComposerTriggerItem>
```

## Styling Hooks

Composer trigger primitives expose stable HPD-owned attributes:

```css
[data-hpd-composer-trigger-root] {
}

[data-hpd-composer-trigger-popover][data-open] {
}

[data-hpd-composer-trigger-popover][data-trigger="@"] {
}

[data-hpd-composer-trigger-item] {
}

[data-hpd-composer-trigger-item][data-item-type="tool"] {
}

[data-hpd-composer-trigger-item][data-highlighted] {
}

[data-hpd-composer-trigger-category] {
}

[data-hpd-composer-trigger-category][data-category-id] {
}

[data-hpd-composer-trigger-back] {
}
```

Use snippets when styling is not enough. `ComposerTriggerItem`,
`ComposerTriggerCategory`, and `ComposerTriggerBack` all allow custom DOM while
keeping trigger detection and selection behavior in the primitive.
