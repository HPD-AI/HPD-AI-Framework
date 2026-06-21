<script lang="ts">
  import {
    DiffViewer,
    ToolCall,
    formatToolCallValue,
  } from '../src/index.js';
  import type { ToolCall as ToolCallModel } from '@hpd-research/hpd-agent-headless-ui';
  import type { ToolCallInspectDetails } from '../src/index.js';

  type RenderMode = 'default' | 'custom' | 'artifact-replacement';
  type Scenario = 'complete' | 'executing' | 'error' | 'json-result' | 'edit-file';

  let {
    renderMode = 'custom',
    scenario = 'complete',
  }: {
    renderMode?: RenderMode;
    scenario?: Scenario;
  } = $props();

  const editPatch = `diff --git a/src/agent.ts b/src/agent.ts
index 3a1c0dd..90f4a31 100644
--- a/src/agent.ts
+++ b/src/agent.ts
@@ -1,8 +1,10 @@
 export type AgentRun = {
   id: string;
   status: 'idle' | 'working' | 'done';
+  contextWindow?: number;
 };
 
 export function describeRun(run: AgentRun): string {
-  if (run.status === 'working') return 'Agent is working';
+  if (run.status === 'working') return 'Agent is working on the current thread';
   if (run.status === 'done') return 'Agent is ready';
   return 'Waiting';
 }
`;

  const tool = $derived(createToolCall(scenario));
  let inspectedTool = $state<ToolCallModel | null>(null);
  const inspectedPatch = $derived(getToolPatch(inspectedTool));

  function createToolCall(currentScenario: Scenario): ToolCallModel {
    if (currentScenario === 'edit-file') {
      return {
        callId: 'tool-call-1',
        name: 'edit_file',
        messageId: 'message-1',
        status: 'complete',
        startTime: new Date('2026-01-01T00:00:00.000Z'),
        endTime: new Date('2026-01-01T00:00:02.100Z'),
        args: { path: 'src/agent.ts', instruction: 'Tighten active run copy.' },
        result: {
          resultType: 'patch',
          text: editPatch,
        },
        resultText: 'Updated src/agent.ts.',
        toolharnessName: 'workspace',
        callType: 'Function',
        turnId: 'turn-1',
        conversationId: 'conversation-1',
        runId: 'run-1',
      };
    }

    const base: ToolCallModel = {
      callId: 'tool-call-1',
      name: currentScenario === 'json-result' ? 'query_database' : 'read_file',
      messageId: 'message-1',
      status: 'complete',
      startTime: new Date('2026-01-01T00:00:00.000Z'),
      endTime: new Date('2026-01-01T00:00:01.450Z'),
      args: currentScenario === 'json-result'
        ? { table: 'sessions', limit: 3 }
        : { path: 'README.md' },
      resultText: 'Read 42 lines from README.md.',
      toolharnessName: 'workspace',
      callType: 'Function',
      turnId: 'turn-1',
      conversationId: 'conversation-1',
      runId: 'run-1',
    };

    if (currentScenario === 'executing') {
      return {
        ...base,
        endTime: undefined,
        resultText: undefined,
        status: 'executing',
      };
    }

    if (currentScenario === 'error') {
      return {
        ...base,
        error: 'File does not exist.',
        resultText: undefined,
        status: 'error',
      };
    }

    if (currentScenario === 'json-result') {
      return {
        ...base,
        resultText: undefined,
        result: {
          json: [
            { id: 'session-1', status: 'active' },
            { id: 'session-2', status: 'complete' },
          ],
          resultType: 'rows',
        },
      };
    }

    return base;
  }

  function inspectTool(details: ToolCallInspectDetails): void {
    inspectedTool = details.tool;
  }

  function getToolPatch(currentTool: ToolCallModel | null): string | null {
    if (!currentTool?.result) return null;
    if (typeof currentTool.result.text === 'string') return currentTool.result.text;
    return null;
  }

</script>

<section class="demo">
  <header>
    <div>
      <h1>ToolCall</h1>
      <p>Standard HPD tool envelope with an escape hatch for tool-specific UIs.</p>
    </div>
    <span>{scenario}</span>
  </header>

  {#if renderMode === 'artifact-replacement'}
    <div class="inspector-layout">
      <ToolCall
        {tool}
        class="tool"
        inspectable={tool.name === 'edit_file'}
        inspectLabel="Inspect"
        onInspect={inspectTool}
      />

      <aside class="inspector" aria-label="Tool inspector">
        {#if inspectedTool && inspectedPatch}
          <header>
            <div>
              <h2>{inspectedTool.name}</h2>
              <p>{inspectedTool.resultText}</p>
            </div>
            <button type="button" onclick={() => inspectedTool = null}>Close</button>
          </header>
          <DiffViewer patch={inspectedPatch} viewMode="split" maxLines={40} />
        {:else}
          <h2>Inspector</h2>
          <p>Open an inspectable tool call to render a rich app-owned panel.</p>
        {/if}
      </aside>
    </div>
  {:else if renderMode === 'custom'}
    <ToolCall {tool} class="tool">
      {#snippet children({ actions, elementProps, state, tool })}
        <section {...elementProps.root} class="tool custom-tool">
          <header {...elementProps.header}>
            <button {...elementProps.trigger}>{state.expanded ? 'Hide' : 'Show'} {state.label}</button>
            <span>{state.statusLabel}</span>
            {#if state.inspectable}
              <button {...elementProps.inspect}>Open inspector</button>
            {/if}
          </header>

          <div {...elementProps.content}>
            {#if tool.name === 'query_database'}
              <div class="result-grid">
                <strong>Rows</strong>
                <pre>{state.resultText}</pre>
              </div>
            {:else}
              <dl>
                <div><dt>path</dt><dd>{formatToolCallValue(tool.args)}</dd></div>
                <div><dt>result</dt><dd>{state.resultText ?? tool.error ?? 'waiting'}</dd></div>
              </dl>
            {/if}

            <button type="button" onclick={() => actions.collapse()}>Collapse</button>
          </div>
        </section>
      {/snippet}
    </ToolCall>
  {:else}
    <ToolCall {tool} class="tool" />
  {/if}
</section>

<style>
  .demo {
    min-height: 360px;
    padding: 28px;
    background: #f6f7f9;
    color: #111827;
    font-family:
      Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  }

  header {
    display: flex;
    justify-content: space-between;
    gap: 20px;
    margin-bottom: 24px;
  }

  h1 {
    margin: 0;
    font-size: 28px;
    line-height: 1.15;
  }

  p {
    margin: 8px 0 0;
    color: #536070;
  }

  header span {
    align-self: flex-start;
    border: 1px solid #cfd6df;
    border-radius: 6px;
    background: #ffffff;
    padding: 6px 8px;
    font-size: 13px;
  }

  :global(.tool) {
    max-width: 720px;
    border: 1px solid #d4dae3;
    border-radius: 8px;
    background: #ffffff;
    padding: 14px;
  }

  .inspector-layout {
    display: grid;
    gap: 16px;
    grid-template-columns: minmax(320px, 520px) minmax(0, 1fr);
  }

  .inspector {
    min-height: 260px;
    border: 1px solid #cfd6df;
    border-radius: 8px;
    background: #ffffff;
    padding: 14px;
  }

  .inspector header {
    align-items: start;
    margin: 0 0 12px;
  }

  .inspector h2 {
    margin: 0;
    font-size: 18px;
  }

  .custom-tool header {
    margin: 0 0 14px;
  }

  dl {
    display: grid;
    gap: 10px;
    margin: 0;
  }

  dl div {
    display: grid;
    grid-template-columns: 80px minmax(0, 1fr);
    gap: 12px;
  }

  dt {
    color: #6b7280;
  }

  dd {
    margin: 0;
  }

  pre {
    overflow: auto;
    margin: 8px 0 0;
    border-radius: 6px;
    background: #f1f4f8;
    padding: 10px;
  }

  :global([data-hpd-tool-call-inspect]),
  .inspector button {
    border: 1px solid #b8c2ce;
    border-radius: 6px;
    background: #ffffff;
    color: #111827;
    padding: 6px 9px;
  }

  @media (max-width: 800px) {
    .inspector-layout {
      grid-template-columns: 1fr;
    }
  }
</style>
