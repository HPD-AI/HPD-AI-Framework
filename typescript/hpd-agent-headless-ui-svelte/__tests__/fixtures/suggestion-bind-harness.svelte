<script lang="ts">
  import {
    Suggestion,
    type SuggestionSelectDetails,
  } from '../../src/index.js';

  let {
    onSelect,
    prompt = 'Explain this code',
  }: {
    onSelect?: (details: SuggestionSelectDetails) => void | Promise<void>;
    prompt?: string;
  } = $props();

  let draft = $state('');
</script>

<Suggestion {prompt} bind:targetValue={draft} {onSelect} />
<output data-testid="draft">{draft}</output>
