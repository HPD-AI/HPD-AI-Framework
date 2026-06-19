<script lang="ts">
  import {
    ContextDisplayBar,
    ContextDisplayBreakdown,
    ContextDisplayRing,
    ContextDisplayRoot,
    ContextDisplayText,
  } from '../src/index.js';

  let {
    scenario = 'normal',
    variant = 'bar',
  }: {
    scenario?: 'normal' | 'warning' | 'critical';
    variant?: 'bar' | 'ring' | 'text' | 'custom';
  } = $props();

  const modelContextWindow = 128000;
  const usage = $derived(getUsage(scenario));

  function getUsage(currentScenario: typeof scenario) {
    if (currentScenario === 'critical') {
      return {
        inputTokenCount: 98000,
        outputTokenCount: 14000,
        totalTokenCount: 112000,
        cachedInputTokenCount: 18000,
        reasoningTokenCount: 3500,
      };
    }

    if (currentScenario === 'warning') {
      return {
        inputTokenCount: 76000,
        outputTokenCount: 11000,
        totalTokenCount: 87000,
        cachedInputTokenCount: 9000,
        reasoningTokenCount: 1200,
      };
    }

    return {
      inputTokenCount: 18000,
      outputTokenCount: 3500,
      totalTokenCount: 21500,
      cachedInputTokenCount: 2000,
      reasoningTokenCount: 500,
    };
  }
</script>

<section class="tutorial">
  <header class="intro">
    <p class="eyebrow">Thread telemetry primitive</p>
    <h1>Context display</h1>
    <p>
      Token usage comes from HPD turn-finished events and is projected into
      thread state before the Svelte primitive renders it.
    </p>
  </header>

  <div class="layout">
    <aside class="guide">
      <h2>Inspect</h2>
      <dl>
        <div>
          <dt>scenario</dt>
          <dd>{scenario}</dd>
        </div>
        <div>
          <dt>window</dt>
          <dd>{modelContextWindow.toLocaleString()} tokens</dd>
        </div>
        <div>
          <dt>usage</dt>
          <dd>{JSON.stringify(usage)}</dd>
        </div>
      </dl>
    </aside>

    <main class="preview">
      <ContextDisplayRoot {usage} {modelContextWindow} class="display">
        {#if variant === 'ring'}
          <ContextDisplayRing class="ring" size={44} strokeWidth={5} />
        {:else if variant === 'text'}
          <ContextDisplayText class="text" />
        {:else if variant === 'custom'}
          <ContextDisplayBar class="bar">
            {#snippet children({ fillProps, model })}
              <div class="custom-bar">
                <strong>{Math.round(model.percent ?? 0)}%</strong>
                <div class="track">
                  <div {...fillProps} class="fill"></div>
                </div>
              </div>
            {/snippet}
          </ContextDisplayBar>
        {:else}
          <ContextDisplayBar class="bar" />
        {/if}

        <ContextDisplayBreakdown class="breakdown" />
      </ContextDisplayRoot>
    </main>
  </div>
</section>

<style>
  .tutorial {
    color: #171b1f;
    display: grid;
    gap: 1.5rem;
    padding: 2rem;
  }

  .eyebrow {
    color: #2b7a68;
    font-size: 0.82rem;
    font-weight: 700;
    letter-spacing: 0;
    margin: 0 0 0.5rem;
    text-transform: uppercase;
  }

  h1 {
    font-size: 2.5rem;
    line-height: 1.05;
    margin: 0 0 1rem;
  }

  .intro p:last-child {
    font-size: 1.1rem;
    line-height: 1.45;
    margin: 0;
    max-width: 56rem;
  }

  .layout {
    display: grid;
    gap: 1.5rem;
    grid-template-columns: minmax(16rem, 24rem) minmax(0, 1fr);
  }

  .guide,
  .preview {
    border: 1px solid #d6d0c2;
    border-radius: 8px;
    padding: 1.25rem;
  }

  dl {
    display: grid;
    gap: 1rem;
    margin: 0;
  }

  dt {
    color: #66706e;
    font-size: 0.78rem;
    font-weight: 700;
    text-transform: uppercase;
  }

  dd {
    font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
    margin: 0;
    overflow-wrap: anywhere;
  }

  .display {
    display: grid;
    gap: 1rem;
    max-width: 32rem;
  }

  .bar {
    align-items: center;
    display: flex;
    gap: 0.75rem;
  }

  .bar :global([data-hpd-context-display-bar-fill]) {
    background: #2b7a68;
    border-radius: 999px;
    height: 100%;
  }

  .bar::before,
  .track {
    background: #e5e0d7;
    border-radius: 999px;
    content: '';
    display: block;
    height: 0.5rem;
    overflow: hidden;
    width: 12rem;
  }

  .custom-bar {
    display: grid;
    gap: 0.5rem;
    width: 100%;
  }

  .custom-bar .track {
    width: 100%;
  }

  .fill {
    background: #2b7a68;
    border-radius: 999px;
    height: 100%;
  }

  .ring {
    color: #2b7a68;
  }

  .text {
    font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  }

  .breakdown {
    border-top: 1px solid #d6d0c2;
    display: grid;
    gap: 0.4rem;
    padding-top: 1rem;
  }

  .breakdown :global([data-hpd-context-display-breakdown-row]) {
    display: flex;
    justify-content: space-between;
  }
</style>

