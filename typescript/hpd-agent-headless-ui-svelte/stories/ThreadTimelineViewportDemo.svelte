<script lang="ts">
  import {
    Message,
    ThreadScrollToBottom,
    ThreadTimeline,
    ThreadTimelineViewport,
    ThreadWorkGroup,
  } from '../src/index.js';
  import type {
    Message as ThreadMessage,
    ThreadTimelineItem,
    ThreadWorkGroup as WorkGroup,
    ToolCall,
  } from '@hpd-research/hpd-agent-headless-ui';
  import type {
    ThreadTimelineViewportScrollContainer,
    ThreadTimelineViewportTurnAnchor,
  } from '../src/index.js';

  type RenderMode = 'default' | 'custom';
  type Scenario = 'long' | 'streaming' | 'empty';

  let {
    anchorBlock = 'start',
    anchorInline = 'nearest',
    atBottomThreshold = 48,
    autoScroll = true,
    renderMode = 'custom',
    scenario = 'long',
    scrollBehavior = 'auto',
    scrollContainer = 'nearest',
    showJumpControl = true,
    turnAnchor = 'top',
  }: {
    anchorBlock?: ScrollLogicalPosition;
    anchorInline?: ScrollLogicalPosition;
    atBottomThreshold?: number;
    autoScroll?: boolean;
    renderMode?: RenderMode;
    scenario?: Scenario;
    scrollBehavior?: ScrollBehavior;
    scrollContainer?: ThreadTimelineViewportScrollContainer;
    showJumpControl?: boolean;
    turnAnchor?: ThreadTimelineViewportTurnAnchor;
  } = $props();

  const timeline = $derived(createTimeline(scenario));

  function createTimeline(mode: Scenario): ThreadTimelineItem[] {
    if (mode === 'empty') return [];

    const items: ThreadTimelineItem[] = [];
    for (let index = 0; index < 10; index += 1) {
      const user = createMessage(
        `user-${index}`,
        'user',
        index === 9
          ? 'Can you make the viewport follow the new user message?'
          : `Question ${index + 1}: summarize the architecture decision.`,
      );
      const assistant = createMessage(
        `assistant-${index}`,
        'assistant',
        [
          'The core keeps semantic thread projection out of the framework adapter.',
          'The Svelte layer can own DOM behavior such as scrolling without teaching core about pixels.',
        ].join(' '),
      );
      items.push(messageItem(user));
      if (mode === 'streaming' && index === 9) {
        items.push(workItem(createStreamingWork()));
      } else {
        items.push(messageItem(assistant));
      }
    }

    return items;
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

  function createMessage(id: string, role: string, content: string): ThreadMessage {
    return {
      id,
      role,
      content,
      streaming: false,
      thinking: false,
      timestamp: new Date('2026-01-01T00:00:00.000Z'),
      toolCalls: [],
      turnId: role === 'assistant' ? `turn-${id}` : null,
      conversationId: role === 'assistant' ? 'conversation-1' : null,
      executionId: role === 'assistant' ? 'run-1' : null,
      placement: 'transcript',
    };
  }

  function createStreamingWork(): WorkGroup {
    const draft = createMessage(
      'assistant-draft',
      'assistant',
      'I am checking the viewport scroll policy and keeping the latest work visible...',
    );
    draft.streaming = true;

    return {
      id: 'work-streaming',
      turnId: 'turn-streaming',
      conversationId: 'conversation-1',
      executionId: 'run-1',
      status: 'working',
      label: 'Viewport working',
      openByDefault: true,
      parts: [
        {
          type: 'reasoning',
          id: 'reasoning-streaming',
          messageId: 'assistant-draft',
          text: 'The user sent a new message, so top-anchor mode should anchor to that row.',
          status: 'streaming',
        },
        {
          type: 'assistant-draft',
          id: 'draft-streaming',
          message: draft,
        },
        {
          type: 'tool',
          id: 'tool-storybook',
          tool: createToolCall(),
        },
      ],
    };
  }

  function createToolCall(): ToolCall {
    return {
      callId: 'call-storybook',
      name: 'storybook',
      messageId: 'assistant-draft',
      status: 'executing',
      startTime: new Date('2026-01-01T00:00:01.000Z'),
      args: { target: 'ThreadTimelineViewport' },
      turnId: 'turn-streaming',
      conversationId: 'conversation-1',
      executionId: 'run-1',
    };
  }
</script>

<section class="demo">
  <header class="header">
    <div>
      <h1>ThreadTimelineViewport</h1>
      <p>Svelte-only scroll behavior around the framework-neutral timeline model.</p>
    </div>
    <div class="metrics">
      <span>{autoScroll}</span>
      <span>{turnAnchor}</span>
      <span>{scrollBehavior}</span>
      <span>{timeline.length} items</span>
      <span>{renderMode}</span>
    </div>
  </header>

  <ThreadTimelineViewport
    ariaLabel="Story thread timeline"
    {anchorBlock}
    {anchorInline}
    {atBottomThreshold}
    {autoScroll}
    {scrollBehavior}
    {scrollContainer}
    {timeline}
    {turnAnchor}
    class="viewport"
  >
    {#snippet children({ timeline: items, viewport })}
      {#if renderMode === 'default'}
        <ThreadTimeline timeline={items} />
      {:else}
        {#if showJumpControl}
          <div class="viewport-toolbar">
            <span>{viewport.isAtBottom ? 'Pinned to bottom' : 'Reading earlier history'}</span>
            <ThreadScrollToBottom>Jump to bottom</ThreadScrollToBottom>
          </div>
        {/if}

        <ThreadTimeline timeline={items} class="timeline">
          {#snippet empty()}
            <div class="empty">No timeline items yet.</div>
          {/snippet}

          {#snippet message({ message })}
            <Message {message} showActions class={`bubble ${message.role}`} />
          {/snippet}

          {#snippet work({ work })}
            <ThreadWorkGroup {work} class="work-card" />
          {/snippet}
        </ThreadTimeline>
      {/if}
    {/snippet}
  </ThreadTimelineViewport>
</section>

<style>
  .demo {
    min-height: 640px;
    padding: 28px;
    background: #f6f7f9;
    color: #111827;
    font-family:
      Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  }

  .header {
    display: flex;
    align-items: flex-start;
    justify-content: space-between;
    gap: 24px;
    margin-bottom: 20px;
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
    justify-content: flex-end;
    flex-wrap: wrap;
    gap: 8px;
  }

  .metrics span,
  :global(.viewport-toolbar [data-hpd-thread-scroll-to-bottom]) {
    border: 1px solid #cfd6df;
    border-radius: 6px;
    background: #ffffff;
    padding: 6px 8px;
    font-size: 13px;
  }

  :global(.viewport) {
    max-width: 900px;
    height: 430px;
    overflow: auto;
    border: 1px solid #d5dce5;
    border-radius: 8px;
    background: #ffffff;
    padding: 16px;
  }

  .viewport-toolbar {
    position: sticky;
    top: 0;
    z-index: 1;
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    margin: -16px -16px 14px;
    padding: 10px 16px;
    border-bottom: 1px solid #e2e8f0;
    background: rgba(255, 255, 255, 0.94);
    backdrop-filter: blur(8px);
    color: #536070;
    font-size: 13px;
  }

  :global(.viewport-toolbar [data-hpd-thread-scroll-to-bottom]) {
    cursor: pointer;
    color: #111827;
  }

  :global(.timeline) {
    display: grid;
    gap: 12px;
  }

  :global(.bubble) {
    max-width: 76%;
    border: 1px solid #d5dce5;
    border-radius: 8px;
    padding: 12px;
    background: #ffffff;
  }

  :global(.bubble.user) {
    margin-left: auto;
    border-color: #f3c371;
    background: #fff8eb;
  }

  :global(.bubble.assistant) {
    background: #f9fafb;
  }

  :global(.work-card) {
    border: 1px solid #bfd7ef;
    border-radius: 8px;
    padding: 12px;
    background: #f5fbff;
  }

  .empty {
    border: 1px dashed #cfd6df;
    border-radius: 8px;
    padding: 28px;
    text-align: center;
    color: #536070;
  }
</style>
