<svelte:options runes={true} />

<script lang="ts">
  import {
    SessionListDelete,
    SessionListItem,
    SessionListItems,
    SessionListNew,
    SessionListRoot,
    SessionListTitle,
    type SessionListState,
  } from '../../src/index.js';

  let {
    sessionList,
  }: {
    sessionList: SessionListState;
  } = $props();
</script>

<SessionListRoot {sessionList}>
  <SessionListNew
    data-testid="new-session"
    metadata={{ name: 'Created', 'hpdos.workspaceKey': 'alpha' }}
  />

  <SessionListItems>
    {#snippet item({ item, index })}
      <SessionListItem {item} {index}>
        <SessionListTitle />
        <SessionListDelete data-testid={`delete-${item.id}`} />
      </SessionListItem>
    {/snippet}
  </SessionListItems>
</SessionListRoot>
