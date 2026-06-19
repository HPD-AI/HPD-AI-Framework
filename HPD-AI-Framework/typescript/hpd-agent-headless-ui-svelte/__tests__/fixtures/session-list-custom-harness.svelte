<svelte:options runes={true} />

<script lang="ts">
  import {
    SessionListItem,
    SessionListItems,
    SessionListRoot,
    type SessionListState,
  } from '../../src/index.js';

  let {
    sessionList,
  }: {
    sessionList: SessionListState;
  } = $props();
</script>

<SessionListRoot {sessionList}>
  <SessionListItems>
    {#snippet item({ item, index })}
      <SessionListItem {item} {index} data-testid={`custom-${item.id}`}>
        {#snippet children({ item })}
          {item.label}:{item.metadata['hpdos.workspaceKey']}
        {/snippet}
      </SessionListItem>
    {/snippet}
  </SessionListItems>
</SessionListRoot>
