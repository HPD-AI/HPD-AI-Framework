<script lang="ts">
  import {
    Message,
    Reasoning,
    RuntimeRequest,
    ThreadTimeline,
    ThreadWorkGroup,
  } from '../src/index.js';
  import type {
    Message as ThreadMessage,
    RuntimeRequest as RuntimeRequestItem,
    ThreadTimelineItem,
    ThreadWorkGroup as WorkGroup,
    ToolCall,
  } from '@hpd-research/hpd-agent-headless-ui';

  type RenderMode = 'default' | 'custom';
  type Scenario = 'mixed' | 'transcript' | 'work' | 'requests' | 'empty';

  let {
    renderMode = 'custom',
    scenario = 'mixed',
    compactWork = true,
  }: {
    renderMode?: RenderMode;
    scenario?: Scenario;
    compactWork?: boolean;
  } = $props();

  const timeline = $derived(createTimeline(scenario, compactWork));

  function createTimeline(mode: Scenario, collapsed: boolean): ThreadTimelineItem[] {
    if (mode === 'empty') return [];

    const user = messageItem(createMessage('m-user', 'user', 'Can you inspect the package and summarize the risk?'));
    const assistant = messageItem(createMessage('m-assistant', 'assistant', 'The risky part is the stale transcript-only rendering path.'));
    const work = workItem(createWorkGroup(collapsed));
    const request = requestItem(createRuntimeRequest());

    if (mode === 'transcript') return [user, assistant];
    if (mode === 'work') return [user, work, assistant];
    if (mode === 'requests') return [user, request];
    return [user, work, request, assistant];
  }

  function messageItem(message: ThreadMessage): ThreadTimelineItem {
    return {
      type: 'message',
      id: `message:${message.id}`,
      message,
      turnId: message.turnId,
      conversationId: message.conversationId,
      executionId: message.executionId,
    };
  }

  function workItem(work: WorkGroup): ThreadTimelineItem {
    return {
      type: 'work',
      id: `work:${work.id}`,
      work,
      turnId: work.turnId,
      conversationId: work.conversationId,
      executionId: work.executionId,
    };
  }

  function requestItem(request: RuntimeRequestItem): ThreadTimelineItem {
    return {
      type: 'runtime-request',
      id: `request:${request.id}`,
      request,
      turnId: 'turn-1',
      conversationId: 'conversation-1',
      executionId: 'run-1',
    };
  }

  function createMessage(id: string, role: string, content: string): ThreadMessage {
    return {
      id,
      role,
      content,
      streaming: false,
      thinking: false,
      timestamp: new Date('2026-01-01T00:00:00.000Z'),
      toolCalls: [],
      turnId: role === 'assistant' ? 'turn-1' : null,
      conversationId: role === 'assistant' ? 'conversation-1' : null,
      executionId: role === 'assistant' ? 'run-1' : null,
      placement: 'transcript',
    };
  }

  function createWorkGroup(collapsed: boolean): WorkGroup {
    return {
      id: 'work-1',
      turnId: 'turn-1',
      conversationId: 'conversation-1',
      executionId: 'run-1',
      status: collapsed ? 'worked' : 'working',
      label: collapsed ? 'Inspected package' : 'Inspecting package',
      openByDefault: !collapsed,
      finalMessageId: collapsed ? 'm-assistant' : undefined,
      parts: [
        {
          type: 'reasoning',
          id: 'reasoning-1',
          messageId: 'draft-1',
          text: 'Need to compare the timeline contract against the old list surface.',
          status: collapsed ? 'complete' : 'streaming',
        },
        {
          type: 'tool',
          id: 'tool-1',
          tool: createToolCall('rg', collapsed ? 'complete' : 'executing'),
        },
        {
          type: 'tool',
          id: 'tool-2',
          tool: createToolCall('svelte-check', collapsed ? 'complete' : 'pending'),
        },
      ],
    };
  }

  function createToolCall(name: string, status: ToolCall['status']): ToolCall {
    return {
      callId: `call-${name}`,
      name,
      messageId: 'draft-1',
      status,
      startTime: new Date('2026-01-01T00:00:01.000Z'),
      args: { target: 'thread timeline' },
      resultText: status === 'complete' ? 'No stale imports found.' : undefined,
      turnId: 'turn-1',
      conversationId: 'conversation-1',
      executionId: 'run-1',
    };
  }

  function createRuntimeRequest(): RuntimeRequestItem {
    return {
      id: 'permission-1',
      kind: 'permission',
      sourceName: 'PermissionMiddleware',
      requestEventType: 'PERMISSION_REQUEST',
      expectedResponseEventType: 'PERMISSION_RESPONSE',
      request: {
        permissionId: 'permission-1',
        sourceName: 'PermissionMiddleware',
        functionName: 'write_file',
        description: 'Allow the agent to write the timeline proposal.',
        callId: 'call-write',
        arguments: { path: 'src/thread-timeline/PROPOSAL.md' },
      },
    };
  }
</script>

<section class="demo">
  <header class="header">
    <div>
      <h1>ThreadTimeline</h1>
      <p>Transcript leaves, grouped work, and runtime requests from one projection.</p>
    </div>
    <div class="metrics">
      <span>{timeline.length} items</span>
      <span>{renderMode}</span>
    </div>
  </header>

  {#if renderMode === 'custom'}
    <ThreadTimeline {timeline} class="timeline">
      {#snippet empty()}
        <div class="empty">No timeline items yet.</div>
      {/snippet}

      {#snippet message({ message })}
        <Message {message} showActions class={`bubble ${message.role}`} />
      {/snippet}

      {#snippet work({ work })}
        <ThreadWorkGroup {work} class="work-card">
          {#snippet workPart({ part })}
            <div class="work-part" data-kind={part.type}>
              {#if part.type === 'reasoning'}
                <Reasoning text={part.text} status={part.status} />
              {:else if part.type === 'tool'}
                <span>{part.tool.name}</span>
                <p>{part.tool.status}</p>
              {:else}
                <span>{part.type}</span>
              {/if}
            </div>
          {/snippet}
        </ThreadWorkGroup>
      {/snippet}

      {#snippet runtimeRequest({ request })}
        <RuntimeRequest item={request} class="request-card" />
      {/snippet}
    </ThreadTimeline>
  {:else}
    <ThreadTimeline {timeline} class="timeline" />
  {/if}
</section>

<style>
  .demo {
    min-height: 520px;
    padding: 28px;
    background: #f6f7f9;
    color: #111827;
    font-family:
      Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  }

  .header {
    display: flex;
    justify-content: space-between;
    gap: 24px;
    align-items: flex-start;
    margin-bottom: 24px;
  }

  h1 {
    margin: 0;
    font-size: 28px;
    line-height: 1.15;
  }

  p {
    margin: 0;
  }

  .header p {
    margin-top: 8px;
    color: #536070;
  }

  .metrics {
    display: flex;
    gap: 8px;
    flex-wrap: wrap;
    justify-content: flex-end;
  }

  .metrics span {
    border: 1px solid #cfd6df;
    border-radius: 6px;
    background: #ffffff;
    padding: 6px 8px;
    font-size: 13px;
  }

  :global(.timeline) {
    display: grid;
    gap: 12px;
    max-width: 860px;
  }

  :global(.bubble) {
    border: 1px solid #d8dee8;
    border-radius: 8px;
    background: #ffffff;
    padding: 14px 16px;
    max-width: 620px;
  }

  :global(.bubble.assistant) {
    margin-left: 48px;
    border-color: #b8c7dd;
    background: #f9fbff;
  }

  :global(.bubble [data-hpd-message-action-bar]) {
    display: flex;
    justify-content: flex-end;
    gap: 8px;
    margin-top: 12px;
    padding-top: 10px;
    border-top: 1px solid #e4e8ee;
  }

  :global(.bubble [data-hpd-message-action]) {
    height: 28px;
    border: 1px solid #cfd6df;
    border-radius: 6px;
    background: #ffffff;
    color: #263241;
    font: inherit;
    font-size: 12px;
    cursor: pointer;
  }

  :global(.work-card) {
    border: 1px solid #c9d2de;
    border-radius: 8px;
    background: #ffffff;
    padding: 10px 12px;
  }

  :global(.work-card summary) {
    cursor: pointer;
    font-weight: 650;
  }

  .work-part {
    display: grid;
    gap: 4px;
    margin-top: 10px;
    border-left: 3px solid #8aa4c6;
    padding-left: 10px;
  }

  .work-part span {
    font-size: 12px;
    font-weight: 700;
    text-transform: uppercase;
    color: #40566f;
  }

  :global(.request-card) {
    border: 1px solid #d2b778;
    border-radius: 8px;
    background: #fff9ea;
    padding: 12px;
  }

  .empty {
    border: 1px dashed #b7c0cc;
    border-radius: 8px;
    background: #ffffff;
    padding: 20px;
    color: #536070;
  }
</style>
