<script lang="ts">
  import type { ContentReference } from '@hpd-research/hpd-agent-client';
  import type { ThreadProjectionSnapshot } from '@hpd-research/hpd-agent-headless-ui';
  import {
    FileAttachment,
    FileAttachmentDropzone,
    FileAttachmentState,
    ThreadComposer,
    type ThreadState,
    type ThreadStateSnapshot,
  } from '../src/index.js';

  type RenderMode = 'default' | 'child';
  type UploadMode = 'ready' | 'slow' | 'error';

  let {
    renderMode = 'default',
    uploadMode = 'ready',
    includeDropzone = true,
    disabled = false,
  }: {
    renderMode?: RenderMode;
    uploadMode?: UploadMode;
    includeDropzone?: boolean;
    disabled?: boolean;
  } = $props();

  let draft = $state('');
  let submitted = $state<string[]>([]);
  let uploadCount = $state(0);

  const thread = createStoryThread();
  const attachments = $derived(new FileAttachmentState({
    disabled,
    sessionId: 'storybook-session',
    threadId: 'main',
    upload: async ({ file }) => {
      uploadCount += 1;
      if (uploadMode === 'slow') await delay(800);
      if (uploadMode === 'error') throw new Error(`Could not upload ${file.name}`);
      return createContentReference(file);
    },
  }));

  $effect(() => {
    attachments.disabled = disabled;
  });

  function createStoryThread(): ThreadState {
    const snapshot = createSnapshot();
    return {
      controller: {} as ThreadState['controller'],
      subscribe(run) {
        run(snapshot);
        return () => {};
      },
      getSnapshot: () => snapshot,
      clearError: () => {},
      start: async () => {},
      rehydrate: async () => {},
      connect: async () => {},
      disconnect: async () => {},
      dispose: async () => {},
      sendMessage: async (input) => {
        const text = input.contents
          ?.map((content) => content.$type === 'text' ? content.text : content.$type)
          .join(' + ') ?? 'empty';
        submitted = [text, ...submitted].slice(0, 5);
      },
      run: async () => undefined,
      respond: async () => undefined,
      interrupt: async () => {},
      approve: async () => undefined,
      deny: async () => undefined,
      clarify: async () => undefined,
      answerClientToolRequest: async () => undefined,
    };
  }

  function createSnapshot(): ThreadStateSnapshot {
    const activity = {
      status: 'idle' as const,
      streaming: false,
      reasoning: false,
      activeToolCount: 0,
      pendingRequestCount: 0,
    };
    const projection: ThreadProjectionSnapshot = {
      thread: null,
      timeline: [],
      workGroups: [],
      transcriptMessages: [],
      activeTools: [],
      pendingRuntimeRequests: [],
      threadRun: null,
      activity,
      currentTurnId: null,
      currentConversationId: null,
      currentRunId: null,
      error: null,
      canSend: true,
    };

    return {
      projection,
      timeline: [],
      workGroups: [],
      transcriptMessages: [],
      activity,
      activeTools: [],
      pendingRuntimeRequests: [],
      textSubmissionState: { canSubmit: true, reason: null },
      canSubmitText: true,
      loading: false,
      connected: true,
      error: null,
    };
  }

  function createContentReference(file: File): ContentReference {
    return {
      contentId: `story-${file.name.replace(/[^a-z0-9.-]/gi, '-')}`,
      version: 'v1',
      contentType: file.type || 'application/octet-stream',
      name: file.name,
      sizeBytes: file.size,
    };
  }

  function delay(ms: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }
</script>

<section class="tutorial">
  <header class="intro">
    <p class="eyebrow">Headless Svelte adapter</p>
    <h1>FileAttachment tutorial playground</h1>
    <p>
      `FileAttachment` uploads files into HPD content references. `ThreadComposer`
      submits only ready contents.
    </p>
  </header>

  <div class="layout">
    <aside class="guide">
      <h2>What to inspect</h2>
      <ol>
        <li>Attach a file and watch upload state become ready.</li>
        <li>Switch to slow or error mode to see composer blocking.</li>
        <li>Use custom DOM mode to inspect `state/actions/props`.</li>
      </ol>

      <h2>Live state</h2>
      <dl>
        <div><dt>uploads</dt><dd>{uploadCount}</dd></div>
        <div><dt>attachments</dt><dd>{attachments.attachments.length}</dd></div>
        <div><dt>ready</dt><dd>{attachments.readyContents.length}</dd></div>
        <div><dt>can submit</dt><dd>{attachments.canSubmit ? 'yes' : 'no'}</dd></div>
      </dl>
    </aside>

    <main class="preview">
      {#if includeDropzone}
        <FileAttachmentDropzone state={attachments} class="dropzone">
          {#snippet children({ state })}
            <strong>{state.dragging ? 'Release to attach' : 'Drop files here'}</strong>
            <span>{state.disabled ? 'Disabled' : 'Shared with the picker below'}</span>
          {/snippet}
        </FileAttachmentDropzone>
      {/if}

      {#if renderMode === 'child'}
        <FileAttachment state={attachments} {disabled} accept="text/*,image/*">
          {#snippet child({ state, actions, props })}
            <section {...props.root} class="attachment-panel">
              <input {...props.input} {@attach props.inputAttachment} />
              <div class="attachment-heading">
                <button {...props.trigger}>Attach file</button>
                <button type="button" onclick={() => actions.clear()} disabled={state.empty}>Clear</button>
              </div>

              {#if state.empty}
                <p>No files attached.</p>
              {:else}
                <ul>
                  {#each state.attachments as attachment}
                    <li data-status={attachment.status}>
                      <span>{attachment.file.name}</span>
                      <small>{attachment.status}{attachment.error ? `: ${attachment.error}` : ''}</small>
                      {#if attachment.status === 'error'}
                        <button type="button" onclick={() => actions.retry(attachment.id)}>Retry</button>
                      {/if}
                      <button type="button" onclick={() => actions.remove(attachment.id)}>Remove</button>
                    </li>
                  {/each}
                </ul>
              {/if}
            </section>
          {/snippet}
        </FileAttachment>
      {:else}
        <FileAttachment state={attachments} {disabled} accept="text/*,image/*" class="default-picker" />
      {/if}

      <ThreadComposer
        {thread}
        bind:value={draft}
        attachments={attachments}
        class="composer"
      />

      <section class="submitted">
        <h2>Submitted contents</h2>
        {#if submitted.length === 0}
          <p>No submissions yet.</p>
        {:else}
          <ul>
            {#each submitted as item}
              <li>{item}</li>
            {/each}
          </ul>
        {/if}
      </section>
    </main>
  </div>
</section>

<style>
  .tutorial {
    min-height: 100%;
    padding: 28px;
    background: #f5f7f8;
    color: #1f2528;
    font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  }

  .intro {
    max-width: 760px;
    margin-bottom: 24px;
  }

  .eyebrow {
    margin: 0 0 6px;
    color: #b35c22;
    font-size: 12px;
    font-weight: 800;
    letter-spacing: 0;
    text-transform: uppercase;
  }

  h1, h2, p {
    margin-top: 0;
  }

  .layout {
    display: grid;
    grid-template-columns: minmax(220px, 320px) minmax(0, 1fr);
    gap: 24px;
    align-items: start;
  }

  .guide {
    display: grid;
    gap: 18px;
  }

  .guide ol {
    margin: 0;
    padding-left: 20px;
  }

  dl {
    display: grid;
    gap: 8px;
    margin: 0;
  }

  dl div {
    display: flex;
    justify-content: space-between;
    gap: 16px;
    border-bottom: 1px solid #d9dedf;
    padding-bottom: 8px;
  }

  dt {
    color: #677174;
    font-weight: 700;
  }

  .preview {
    display: grid;
    gap: 16px;
    max-width: 800px;
  }

  .dropzone {
    display: grid;
    gap: 4px;
    min-height: 110px;
    place-items: center;
    border: 2px dashed #a8b4b8;
    border-radius: 8px;
    background: #ffffff;
    color: #3b4548;
  }

  .dropzone[data-dragging] {
    border-color: #b35c22;
    background: #fff5ee;
  }

  .attachment-panel,
  .default-picker,
  .composer,
  .submitted {
    border: 1px solid #d3d9db;
    border-radius: 8px;
    background: #ffffff;
    padding: 14px;
  }

  .attachment-heading {
    display: flex;
    gap: 8px;
    margin-bottom: 12px;
  }

  button {
    border: 1px solid #b7c0c3;
    border-radius: 6px;
    background: #f9fbfb;
    color: inherit;
    cursor: pointer;
    font: inherit;
    font-weight: 700;
    padding: 8px 12px;
  }

  button:disabled {
    cursor: not-allowed;
    opacity: 0.5;
  }

  input[type="file"] {
    position: absolute;
    width: 1px;
    height: 1px;
    overflow: hidden;
    clip: rect(0 0 0 0);
  }

  ul {
    display: grid;
    gap: 8px;
    margin: 0;
    padding: 0;
    list-style: none;
  }

  li {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 8px;
    border: 1px solid #e0e5e6;
    border-radius: 6px;
    padding: 8px;
  }

  li[data-status="ready"] {
    border-color: #78ad87;
  }

  li[data-status="error"] {
    border-color: #ce675e;
  }

  small {
    color: #687376;
  }

  .submitted p {
    margin-bottom: 0;
    color: #687376;
  }

  @media (max-width: 760px) {
    .tutorial {
      padding: 18px;
    }

    .layout {
      grid-template-columns: 1fr;
    }
  }
</style>
