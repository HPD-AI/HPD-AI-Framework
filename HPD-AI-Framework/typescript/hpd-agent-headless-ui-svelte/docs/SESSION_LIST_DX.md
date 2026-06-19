# Session List DX

Session list primitives render HPD sessions from a framework-neutral
`SessionListState`.

Use it for sidebars, recent-session panels, workspace-filtered session lists, or
project-level navigation.

```svelte
<script lang="ts">
  import {
    createSessionListState,
    SessionListItem,
    SessionListItems,
    SessionListRoot,
    SessionListSubtitle,
    SessionListTitle,
  } from '@hpd-research/hpd-agent-headless-ui-svelte';

  const sessionList = createSessionListState({
    client,
    search: {
      metadata: { 'hpdos.workspaceKey': workspaceKey },
    },
  });

  await sessionList.load();
</script>

<SessionListRoot {sessionList}>
  <SessionListItems>
    {#snippet item({ item, index })}
      <SessionListItem
        {item}
        {index}
        onSelect={(item) => openSession(item.id)}
      >
        <SessionListTitle />
        <SessionListSubtitle />
      </SessionListItem>
    {/snippet}
  </SessionListItems>
</SessionListRoot>
```

## Metadata

The primitive does not know about workspaces. Metadata is an application
extension bag.

Recommended app keys should be namespaced:

```ts
{
  'hpdos.workspaceKey': workspaceKey,
  'hpdos.name': 'Planning session'
}
```

The controller passes metadata through unchanged:

```svelte
<SessionListRoot {sessionList}>
  <SessionListItems>
    {#snippet item({ item, index })}
      <SessionListItem {item} {index}>
        {#snippet children({ item })}
          <strong>{item.label}</strong>
          <small>{item.metadata['hpdos.workspaceKey']}</small>
        {/snippet}
      </SessionListItem>
    {/snippet}
  </SessionListItems>
</SessionListRoot>
```

## Actions

`SessionListNew` and `SessionListDelete` read the root state through typed
Svelte context:

```svelte
<SessionListRoot {sessionList}>
  <SessionListNew metadata={{ 'hpdos.workspaceKey': workspaceKey }}>
    New session
  </SessionListNew>

  <SessionListItems>
    {#snippet item({ item, index })}
      <SessionListItem {item} {index}>
        <SessionListTitle />
        <SessionListDelete />
      </SessionListItem>
    {/snippet}
  </SessionListItems>
</SessionListRoot>
```

## State

`createSessionListState()` wraps the headless core controller and exposes:

- `load`
- `refresh`
- `select`
- `create`
- `update`
- `delete`
- `clearError`

Session metadata patching follows backend semantics. A `null` metadata value
removes that key:

```ts
await sessionList.update(sessionId, {
  metadata: {
    'hpdos.name': 'Renamed',
    'hpdos.workspaceKey': null,
  },
});
```

## Ownership

Handled by the library:

- loading/searching sessions
- selected-session state
- create/update/delete actions
- loading/empty/error rendering hooks
- generic metadata-aware labels

Handled by the app:

- workspace/project concepts
- grouping
- routing after selection
- whether creating a session also creates/loads a thread

## Styling Hooks

Session list primitives expose stable HPD-owned attributes:

```css
[data-hpd-session-list] {
}

[data-hpd-session-list][data-loading] {
}

[data-hpd-session-list][data-empty] {
}

[data-hpd-session-list-item] {
}

[data-hpd-session-list-item][data-selected] {
}

[data-hpd-session-list-item][data-session-id] {
}

[data-hpd-session-list-item-label] {
}

[data-hpd-session-list-item-subtitle] {
}

[data-hpd-session-list-new] {
}

[data-hpd-session-list-delete] {
}

[data-hpd-session-list-error] {
}

[data-hpd-session-list-empty] {
}
```

Use item/title/subtitle/delete snippets when the app needs grouped sidebars,
workspace badges, timestamps, or metadata-specific layouts.
