<script lang="ts">
  import {
    ComposerQuote,
    ComposerQuoteDismiss,
    ComposerQuoteText,
    SelectionToolbarQuote,
    SelectionToolbarRoot,
    type SelectionToolbarSelection,
    type ThreadQuote,
  } from '../../src/index.js';

  let {
    disabled = false,
    minLength = 1,
    onQuote,
    quote = $bindable<ThreadQuote | null>(null),
  }: {
    disabled?: boolean;
    minLength?: number;
    onQuote?: (quote: ThreadQuote, selection: SelectionToolbarSelection) => void | Promise<void>;
    quote?: ThreadQuote | null;
  } = $props();
</script>

<SelectionToolbarRoot
  bind:quote
  {disabled}
  {minLength}
  {onQuote}
>
  <p data-message-id="message-1" data-testid="selectable">Alpha selected text</p>
  <div data-testid="toolbar">
    <SelectionToolbarQuote />
  </div>
</SelectionToolbarRoot>

<p data-testid="quote-text">{quote?.text ?? ''}</p>
<p data-testid="quote-message-id">{quote?.messageId ?? ''}</p>

<ComposerQuote bind:quote>
  <ComposerQuoteText data-testid="composer-quote-text" />
  <ComposerQuoteDismiss data-testid="composer-quote-dismiss" />
</ComposerQuote>
