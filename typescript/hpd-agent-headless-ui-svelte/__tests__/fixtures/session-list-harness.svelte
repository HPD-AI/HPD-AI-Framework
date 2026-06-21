<svelte:options runes={true} />

<script lang="ts">
  import {
    SessionListItem,
    SessionListItems,
    SessionListRoot,
    type SessionListState,
  } from '../../src/index.js';
  import type { SessionListItem as SessionListItemModel } from '@hpd-research/hpd-agent-headless-ui';

  let {
    sessionList,
    onSelect,
  }: {
    sessionList: SessionListState;
    onSelect?: (item: SessionListItemModel) => void | Promise<void>;
  } = $props();
</script>

<SessionListRoot {sessionList}>
  <SessionListItems>
    {#snippet item({ item, index })}
      <SessionListItem {item} {index} {onSelect} />
    {/snippet}
  </SessionListItems>
</SessionListRoot>
