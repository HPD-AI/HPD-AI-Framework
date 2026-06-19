<svelte:options runes={true} />

<script lang="ts">
  import type { RuntimeRequest } from '@hpd-research/hpd-agent-headless-ui';
  import RuntimeRequestComponent from '../../src/runtime-request/runtime-request.svelte';
  import type { ThreadState } from '../../src/thread-state.js';

  let {
    item,
    thread,
  }: {
    item: RuntimeRequest;
    thread: ThreadState;
  } = $props();
</script>

<RuntimeRequestComponent {item} {thread}>
  {#snippet permission({ actions, item, props })}
    <button
      type="button"
      data-kind-prop={props['data-hpd-runtime-request-kind']}
      data-testid="snippet-approve"
      onclick={() => {
        void actions.approve('allow-once');
      }}
    >
      {item.id}
    </button>
  {/snippet}
</RuntimeRequestComponent>
