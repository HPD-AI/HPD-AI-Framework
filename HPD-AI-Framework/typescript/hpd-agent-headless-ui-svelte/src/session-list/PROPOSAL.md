# Session List Proposal

The session-list primitive family renders and controls HPD sessions.

The lower stack already owns session persistence, metadata search, and session CRUD. The headless core owns the generic session-list controller. The Svelte adapter should only render the list and expose snippets/actions.

## Boundaries

- Backend/client: durable session metadata and CRUD.
- Headless core: load/search/select/create/update/delete state.
- Svelte adapter: DOM rendering, typed context, snippets, stable data attributes.
- App: workspace/project meaning, grouping, labels, and custom metadata keys.

## Metadata

The primitive keeps metadata generic. A workspace UI can search with:

```ts
createSessionListState({
  client,
  search: {
    metadata: { 'hpdos.workspaceKey': workspaceKey },
  },
});
```

The primitives do not hardcode workspace behavior.

## API

```svelte
<SessionListRoot {sessionList}>
  <SessionListNew metadata={{ 'hpdos.workspaceKey': workspaceKey }} />
  <SessionListItems>
    {#snippet item({ item, index })}
      <SessionListItem {item} {index}>
        <SessionListTitle />
        <SessionListSubtitle />
        <SessionListDelete />
      </SessionListItem>
    {/snippet}
  </SessionListItems>
</SessionListRoot>
```

Custom row:

```svelte
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
```

## Clean Break

This replaces archive-style app-owned session list logic with reusable session
primitives. The old monolithic `SessionList` component was removed. The library
still does not own app concepts like workspace.
