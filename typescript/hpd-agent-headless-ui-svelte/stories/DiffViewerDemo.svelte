<script lang="ts">
  import {
    DiffViewer,
    MarkdownText,
  } from '../src/index.js';

  type Variant = 'unified' | 'split' | 'folded' | 'markdown' | 'custom';

  let {
    variant = 'unified',
  }: {
    variant?: Variant;
  } = $props();

  const patch = `diff --git a/src/agent.ts b/src/agent.ts
index 3a1c0dd..90f4a31 100644
--- a/src/agent.ts
+++ b/src/agent.ts
@@ -1,13 +1,16 @@
 export type AgentRun = {
   id: string;
   status: 'idle' | 'working' | 'done';
+  contextWindow?: number;
 };
 
 export function describeRun(run: AgentRun): string {
-  if (run.status === 'working') return 'Agent is working';
+  if (run.status === 'working') {
+    return 'Agent is working on the current thread';
+  }
 
   if (run.status === 'done') {
     return 'Agent is ready';
   }
 
-  return 'Waiting';
+  return 'Waiting for user input';
 }
@@ -20,6 +23,9 @@ export function createRun(id: string): AgentRun {
   return {
     id,
     status: 'idle',
+    contextWindow: 128000,
   };
 }
`;

  const markdown = `The tool produced this patch:

\`\`\`diff
${patch}
\`\`\`
`;
</script>

<section class="tutorial">
  <header>
    <p class="eyebrow">Source change primitive</p>
    <h1>Diff viewer</h1>
    <p>
      Render patch content from markdown, tools, revisions, or direct old/new
      file comparisons without teaching the renderer how to apply changes.
    </p>
  </header>

  <div class="layout">
    <aside>
      <h2>Policy</h2>
      <dl>
        <div><dt>variant</dt><dd>{variant}</dd></div>
        <div><dt>source</dt><dd>{variant === 'markdown' ? 'MarkdownText' : 'patch'}</dd></div>
        <div><dt>ownership</dt><dd>render only</dd></div>
      </dl>
      <p>
        Core parses the diff. Svelte renders it through stable data attributes
        and snippets.
      </p>
    </aside>

    <main>
      {#if variant === 'split'}
        <DiffViewer {patch} viewMode="split" />
      {:else if variant === 'folded'}
        <DiffViewer {patch} contextLines={1} maxLines={12} />
      {:else if variant === 'markdown'}
        <MarkdownText text={markdown}>
          {#snippet code({ lang, text })}
            {#if lang === 'diff' || lang === 'patch'}
              <DiffViewer patch={text} />
            {:else}
              <pre><code>{text}</code></pre>
            {/if}
          {/snippet}
        </MarkdownText>
      {:else if variant === 'custom'}
        <DiffViewer {patch}>
          {#snippet header({ displayName, additions, deletions })}
            <header class="custom-header">
              <strong>{displayName}</strong>
              <span>{additions} added / {deletions} removed</span>
            </header>
          {/snippet}

          {#snippet line({ line, props, segments })}
            <div {...props} class="custom-line">
              <span class="custom-gutter">
                {line.type === 'add' ? '+' : line.type === 'del' ? '-' : ' '}
              </span>
              <span>
                {#each segments ?? [{ text: line.content, changed: false }] as segment}
                  <span class:changed={segment.changed}>{segment.text}</span>
                {/each}
              </span>
            </div>
          {/snippet}
        </DiffViewer>
      {:else}
        <DiffViewer {patch} />
      {/if}
    </main>
  </div>
</section>

<style>
  .tutorial {
    color: #202629;
    display: grid;
    gap: 1.5rem;
    padding: 2rem;
  }

  .eyebrow {
    color: #2b7a68;
    font-size: 0.82rem;
    font-weight: 800;
    letter-spacing: 0;
    margin: 0 0 0.5rem;
    text-transform: uppercase;
  }

  h1 {
    font-size: 2.5rem;
    line-height: 1.05;
    margin: 0 0 1rem;
  }

  h2,
  p {
    margin-top: 0;
  }

  .layout {
    align-items: start;
    display: grid;
    gap: 1.5rem;
    grid-template-columns: minmax(18rem, 24rem) minmax(0, 1fr);
  }

  aside,
  main {
    background: #fff;
    border: 1px solid #d8dde0;
    border-radius: 8px;
    padding: 1.25rem;
  }

  dl {
    display: grid;
    gap: 0.5rem;
  }

  dl div {
    display: flex;
    gap: 1rem;
    justify-content: space-between;
  }

  dt {
    color: #5d686e;
    font-weight: 700;
  }

  :global([data-hpd-diff-viewer]) {
    border: 1px solid #ccd6dc;
    border-radius: 8px;
    font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
    font-size: 0.9rem;
    overflow: hidden;
  }

  :global([data-hpd-diff-header]) {
    align-items: center;
    background: #edf2f5;
    display: flex;
    gap: 0.75rem;
    justify-content: space-between;
    padding: 0.6rem 0.75rem;
  }

  :global([data-hpd-diff-line]),
  :global([data-hpd-diff-split-line]) {
    display: grid;
  }

  :global([data-hpd-diff-line]) {
    grid-template-columns: 4rem 1.5rem minmax(0, 1fr);
    padding: 0.12rem 0.5rem;
  }

  :global([data-hpd-diff-line][data-line-type='add']),
  :global([data-hpd-diff-split-side][data-line-type='add']) {
    background: #e8f8ef;
  }

  :global([data-hpd-diff-line][data-line-type='del']),
  :global([data-hpd-diff-split-side][data-line-type='del']) {
    background: #fdeceb;
  }

  :global([data-hpd-diff-line-number]),
  :global([data-hpd-diff-indicator]) {
    color: #68757d;
    user-select: none;
  }

  :global([data-hpd-diff-segment][data-changed]),
  .changed {
    background: rgba(226, 137, 45, 0.28);
    border-radius: 3px;
  }

  :global([data-hpd-diff-split-line]) {
    grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  }

  :global([data-hpd-diff-split-side]) {
    display: grid;
    grid-template-columns: 4rem 1.5rem minmax(0, 1fr);
    padding: 0.12rem 0.5rem;
  }

  :global([data-hpd-diff-fold]),
  :global([data-hpd-diff-truncated]) {
    color: #68757d;
    padding: 0.4rem 0.75rem;
  }

  .custom-header {
    align-items: center;
    background: #172026;
    color: #f7fbfc;
    display: flex;
    justify-content: space-between;
    padding: 0.75rem;
  }

  .custom-line {
    display: grid;
    gap: 0.75rem;
    grid-template-columns: 1.5rem minmax(0, 1fr);
  }

  .custom-gutter {
    color: #68757d;
  }
</style>
