<script lang="ts">
  import {
    ComposerQuote,
    ComposerQuoteDismiss,
    ComposerQuoteText,
    SelectionToolbarQuote,
    SelectionToolbarRoot,
    type ThreadQuote,
  } from '../src/index.js';

  type Placement = 'above' | 'below';

  let {
    placement = 'above',
  }: {
    placement?: Placement;
  } = $props();

  let quote = $state<ThreadQuote | null>(null);
</script>

<section class="tutorial">
  <header class="intro">
    <p class="eyebrow">Headless Svelte adapter</p>
    <h1>SelectionToolbar playground</h1>
    <p>
      Select text in the message region, capture it as structured quote state,
      then inspect or dismiss the composer preview.
    </p>
  </header>

  <div class="layout">
    <aside class="guide">
      <h2>Quote state</h2>
      <dl>
        <div>
          <dt>Text</dt>
          <dd>{quote?.text ?? 'none'}</dd>
        </div>
        <div>
          <dt>Message</dt>
          <dd>{quote?.messageId ?? 'none'}</dd>
        </div>
      </dl>
    </aside>

    <main class="preview">
      <SelectionToolbarRoot bind:quote {placement}>
        {#snippet children({ props, state, actions })}
          <article class="message-region" data-message-id="story-message-1">
            <h2>Agent answer</h2>
            <p>
              The durable event log stays delta-shaped, while projections
              concatenate text into a UI-ready snapshot.
            </p>
            <p>
              Forking copies a coherent event prefix, so custom events remain
              attached to the selected branch history.
            </p>
          </article>

          <div {...props.toolbar} class="floating">
            <SelectionToolbarQuote>
              {#snippet children({ selection })}
                Quote {selection?.text.length ?? 0}
              {/snippet}
            </SelectionToolbarQuote>
            <button type="button" onclick={actions.close}>Close</button>
          </div>
        {/snippet}
      </SelectionToolbarRoot>

      <ComposerQuote bind:quote class="quote-preview">
        <span class="quote-mark">“</span>
        <ComposerQuoteText />
        <ComposerQuoteDismiss>Remove</ComposerQuoteDismiss>
      </ComposerQuote>
    </main>
  </div>
</section>

<style>
  .tutorial {
    min-height: 100%;
    padding: 28px;
    background: #f5f6f0;
    color: #20231f;
    font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  }

  .intro,
  .layout {
    max-width: 980px;
    margin: 0 auto;
  }

  .intro {
    margin-bottom: 24px;
  }

  .eyebrow {
    margin: 0 0 6px;
    color: #94651c;
    font-size: 12px;
    font-weight: 700;
  }

  h1,
  h2,
  p {
    margin-top: 0;
  }

  .layout {
    display: grid;
    grid-template-columns: 260px minmax(0, 1fr);
    gap: 20px;
  }

  .guide,
  .message-region,
  .quote-preview {
    border: 1px solid #d5d7cf;
    border-radius: 8px;
    background: #ffffff;
  }

  .guide {
    padding: 18px;
  }

  dl {
    display: grid;
    gap: 14px;
    margin: 0;
  }

  dt {
    color: #76796f;
    font-size: 12px;
    font-weight: 700;
    text-transform: uppercase;
  }

  dd {
    margin: 4px 0 0;
    word-break: break-word;
  }

  .preview {
    display: grid;
    gap: 14px;
  }

  .message-region {
    padding: 22px;
    line-height: 1.65;
  }

  .floating {
    display: flex;
    gap: 6px;
    padding: 6px;
    border: 1px solid #23251f;
    border-radius: 8px;
    background: #20231f;
    box-shadow: 0 12px 30px rgb(0 0 0 / 18%);
  }

  .floating button,
  .quote-preview button {
    border: 0;
    border-radius: 6px;
    padding: 7px 10px;
    background: #f8d36b;
    color: #20231f;
    font: inherit;
    font-weight: 700;
  }

  .floating button:disabled {
    opacity: 0.5;
  }

  .quote-preview {
    display: flex;
    align-items: flex-start;
    gap: 10px;
    padding: 14px;
  }

  .quote-mark {
    color: #94651c;
    font-size: 28px;
    line-height: 1;
  }

  [data-hpd-composer-quote-text] {
    flex: 1;
  }

  @media (max-width: 760px) {
    .layout {
      grid-template-columns: 1fr;
    }
  }
</style>
