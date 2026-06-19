<script lang="ts">
  import {
    SelectionToolbarQuote,
    SelectionToolbarRoot,
    type ThreadQuote,
  } from '../../src/index.js';

  let quote = $state<ThreadQuote | null>(null);
</script>

<SelectionToolbarRoot bind:quote>
  {#snippet children({ props, state })}
    <p data-testid="selectable">Custom selected text</p>
    <div {...props.toolbar} data-testid="custom-toolbar">
      <SelectionToolbarQuote data-testid="custom-quote">
        {#snippet children({ selection })}
          Quote {selection?.text.length ?? 0}
        {/snippet}
      </SelectionToolbarQuote>
    </div>
    <p data-testid="quote">{state.quote?.text ?? quote?.text ?? ''}</p>
  {/snippet}
</SelectionToolbarRoot>
