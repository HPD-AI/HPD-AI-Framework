import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  AgentClient,
  ClientToolRegistry,
  ThreadHistoryProjection,
  createExpandedToolHarness,
} from '../dist/index.js';

const origin = { sessionId: 's', threadId: 'main' };
const child = { sessionId: 's', threadId: 'child' };

function delta(text, id, route = origin) {
  const event = { version: '1.0', type: 'TEXT_DELTA', text, messageId: 'm' };
  if (id) event.threadSequenceNumber = Number(id.split(':')[1]);
  return (id ? `id: ${id}\n` : '') + 'event: live-agent-event\n'
    + `data: ${JSON.stringify({ event, route: { origin: route, path: route === origin ? [origin] : [origin, route] } })}\n\n`;
}

test('session input and observation resume from the last applied anchor event', async () => {
  const urls = [];
  const seen = [];
  const client = new AgentClient({
    baseUrl: 'https://example.test',
    fetch: async (url, init) => {
      urls.push(url);
      if (url.pathname === '/sessions') return Response.json({ id: 's' });
      if (url.pathname.endsWith('/state')) return Response.json({ observedCursor: { generation: 1, sequenceNumber: 9 } });
      if (url.pathname.endsWith('/inputs')) {
        const input = JSON.parse(init.body);
        assert.equal(input.version, '1.0');
        assert.deepEqual(input.messages[0].contents, [{ $type: 'text', text: 'Hello' }]);
        return Response.json({ disposition: 'queued', threadExecutionId: 'e' });
      }
      if (url.pathname.endsWith('/executions/e')) return Response.json({ status: 'succeeded', finishedAt: 'now' });
      const after = url.searchParams.get('after');
      if (after === '1:0') return new Response(delta('one', '1:1') + delta('child', undefined, child));
      if (after === '1:1') return new Response(delta('duplicate', '1:1') + delta('two', '1:2'));
      assert.fail(`Unexpected request ${url}`);
    },
  });
  const { thread } = await client.sessions.create({ agentId: 'assistant' });
  const controller = new AbortController();
  const observation = await thread.observe({
    hierarchy: 'threadAndDescendants',
    signal: controller.signal,
    handlers: {
      TEXT_DELTA: async ({ event, route }) => {
        await Promise.resolve();
        seen.push([event.text, route.origin.threadId]);
        if (event.text === 'two') controller.abort();
      },
    },
  });
  const result = await thread.send('Hello');
  assert.equal(result.status, 'queued');
  assert.equal((await result.execution.wait()).status, 'succeeded');
  await observation.done;
  assert.deepEqual(observation.appliedCursor, { generation: 1, sequence: 2 });
  assert.deepEqual(seen, [['one', 'main'], ['child', 'child'], ['two', 'main']]);
  assert.deepEqual(urls.filter(url => url.pathname.endsWith('/events')).map(url => url.searchParams.get('after')), ['1:0', '1:1']);
});

test('a failed handler leaves the event unapplied and stops observation', async () => {
  const client = new AgentClient({
    baseUrl: 'https://example.test',
    fetch: async url => url.pathname === '/sessions'
      ? Response.json({ id: 's' })
      : new Response(delta('one', '1:1')),
  });
  const { thread } = await client.sessions.create({ agentId: 'assistant' });
  const observation = await thread.observe({
    after: { generation: 1, sequence: 0 },
    onEvent: () => { throw new Error('projection failed'); },
  });
  await assert.rejects(observation.done, /projection failed/);
  assert.deepEqual(observation.appliedCursor, { generation: 1, sequence: 0 });
});

test('the hosted execution conflict maps to a busy result', async () => {
  const client = new AgentClient({
    baseUrl: 'https://example.test',
    fetch: async url => url.pathname === '/sessions'
      ? Response.json({ id: 's' })
      : Response.json({ ThreadExecutionActive: ['Thread is busy'] }, { status: 409 }),
  });
  const { thread } = await client.sessions.create({ agentId: 'assistant' });
  assert.deepEqual(await thread.send('Hello'), { status: 'busy' });
});

test('journal rebase resets the projection before replay and delivers unknown events with their route', async () => {
  const seen = [];
  const controller = new AbortController();
  const client = new AgentClient({
    baseUrl: 'https://example.test',
    fetch: async url => {
      if (url.pathname === '/sessions') return Response.json({ id: 's' });
      if (url.searchParams.get('after') === '1:8') {
        return new Response('event: thread-journal-rebased\ndata: {"previousGeneration":1,"currentGeneration":2}\n\n');
      }
      assert.equal(url.searchParams.get('after'), '2:0');
      return new Response('event: agent-event\nid: 2:1\ndata: ' + JSON.stringify({
        event: { version: '1.0', type: 'APP_EVENT', threadSequenceNumber: 1, value: 'custom' },
        route: { origin, path: [origin] },
      }) + '\n\n');
    },
  });
  const { thread } = await client.sessions.create({ agentId: 'assistant' });
  const observation = await thread.observe({
    after: { generation: 1, sequence: 8 },
    signal: controller.signal,
    onRebase: cursor => { seen.push(cursor); },
    onUnknownEvent: ({ event, route }) => {
      seen.push([event.value, route.origin]);
      controller.abort();
    },
  });
  await observation.done;
  assert.deepEqual(seen, [{ generation: 2, sequence: 0 }, ['custom', origin]]);
  assert.deepEqual(observation.appliedCursor, { generation: 2, sequence: 1 });
});

test('multipart upload delegates boundary creation to fetch and uses the file field', async () => {
  const client = new AgentClient({
    baseUrl: 'https://example.test',
    headers: { 'Content-Type': 'application/json' },
    fetch: async (url, init) => {
      if (url.pathname === '/sessions') return Response.json({ id: 's' });
      assert.equal(url.pathname, '/sessions/s/threads/main/content');
      assert.equal(init.headers.has('Content-Type'), false);
      const file = init.body.get('file');
      assert.equal(file.name, 'hello.txt');
      assert.equal(await file.text(), 'Hello');
      return Response.json({ id: 'uploaded' }, { status: 201 });
    },
  });
  const { thread } = await client.sessions.create({ agentId: 'assistant' });
  assert.equal((await thread.content.upload(new Blob(['Hello']), 'hello.txt')).id, 'uploaded');
});

test('client tool harnesses register local handlers and expose model schemas', async () => {
  const harness = createExpandedToolHarness('ui', 'Connected UI tools', [{
    name: 'show_toast',
    description: 'Shows a toast.',
    parametersSchema: {
      type: 'object',
      properties: { message: { type: 'string' } },
      required: ['message'],
    },
  }]);
  const registry = new ClientToolRegistry()
    .registerHarness(harness, ({ arguments: input }) => `Toast: ${String(input.message)}`);

  assert.deepEqual(registry.clientToolHarnesses, [harness]);
  const result = await registry.invoke({
    requestId: 'request-1',
    toolName: 'ui.show_toast',
    callId: 'call-1',
    arguments: { message: 'Ready' },
  });
  assert.deepEqual(result, {
    outcome: 'Completed',
    content: [{ type: 'text', text: 'Toast: Ready' }],
  });
});

test('history projects committed messages through the observed journal head', async () => {
  const client = new AgentClient({
    baseUrl: 'https://example.test',
    fetch: async url => {
      if (url.pathname === '/sessions') return Response.json({ id: 's' });
      if (url.pathname.endsWith('/state')) {
        return Response.json({ observedCursor: { generation: 1, sequenceNumber: 3 } });
      }
      if (url.pathname.endsWith('/events')) {
        const deliveries = [
          ['1:1', { version: '1.0', type: 'USER_MESSAGE', messageId: 'u', text: 'Hello', threadSequenceNumber: 1 }],
          ['1:2', {
            version: '1.0',
            type: 'TEXT_MESSAGE_START',
            messageId: 'a',
            role: 'assistant',
            source: 2,
            visibility: 0,
            threadSequenceNumber: 2,
          }],
          ['1:3', { version: '1.0', type: 'TEXT_DELTA', messageId: 'a', text: 'Hi', threadSequenceNumber: 3 }],
        ];
        return new Response(deliveries.map(([id, event]) => `id: ${id}\nevent: agent-event\n`
          + `data: ${JSON.stringify({ event, route: { origin, path: [origin] } })}\n\n`).join(''));
      }
      throw new Error(`Unexpected request ${url}`);
    },
  });
  const { thread } = await client.sessions.create({ agentId: 'assistant' });

  const history = await thread.history();

  assert.deepEqual(history.appliedCursor, { generation: 1, sequence: 3 });
  assert.equal(history.messages.length, 2);
  assert.deepEqual(
    history.messages.map(message => [message.role, message.text, message.source]),
    [['user', 'Hello', 'UserInput'], ['assistant', 'Hi', 'AssistantOutput']]);
});

test('client-wide and per-request signals and headers reach JSON requests', async () => {
  const clientAbort = new AbortController();
  let calls = 0;
  const client = new AgentClient({
    baseUrl: 'https://example.test',
    abortSignal: clientAbort.signal,
    fetch: async (_url, init) => {
      calls += 1;
      assert.equal(new Headers(init.headers).get('x-request-test'), 'present');
      return await new Promise((_resolve, reject) => {
        init.signal.addEventListener('abort', () => reject(init.signal.reason), { once: true });
      });
    },
  });

  const call = client.sessions.get('s', {
    headers: { 'x-request-test': 'present' },
    timeoutMs: 5,
  });
  await assert.rejects(call, error => error instanceof Error && error.name === 'AbortError');
  assert.equal(calls, 1);
});

test('resource facades preserve agent, thread, content, execution, and evaluation scope', async () => {
  const paths = [];
  const client = new AgentClient({
    baseUrl: 'https://example.test',
    fetch: async url => {
      paths.push(url.pathname);
      if (url.pathname === '/sessions/s') return Response.json({ id: 's' });
      if (url.pathname === '/sessions/s/threads/main') {
        return Response.json({
          id: 'main',
          sessionId: 's',
          defaultAgentId: 'a',
          name: 'main',
          description: null,
          forkedFrom: null,
          forkedAtMessageId: null,
          forkedAtMessageIndex: null,
          createdAt: 'now',
          lastActivity: 'now',
          messageCount: 0,
          tags: null,
          ancestors: null,
          totalForks: 0,
        });
      }
      if (url.pathname === '/agents/a') return Response.json({ id: 'a', name: 'Agent', config: {}, createdAt: 'now', updatedAt: 'now' });
      if (url.pathname.endsWith('/thread-graph')) return Response.json({ threads: [] });
      if (url.pathname.endsWith('/subagents')) return Response.json([]);
      if (url.pathname.endsWith('/content')) return Response.json([]);
      if (url.pathname.endsWith('/executions')) return Response.json([]);
      if (url.pathname.endsWith('/context-usage')) return Response.json({ inputTokenCount: 1 });
      if (url.pathname === '/evals/evaluators/catalog') return Response.json([]);
      if (url.pathname === '/agents') return Response.json([]);
      throw new Error(`Unexpected request ${url}`);
    },
  });

  const session = await client.sessions.get('s');
  const thread = await session.threads.get('main');
  assert.equal((await client.agents.get('a')).id, 'a');
  assert.deepEqual(await client.agents.list(), []);
  await thread.graph();
  await thread.subagents();
  await thread.content.list();
  await thread.executions.list();
  await thread.contextUsage();
  await client.evaluations.catalog();

  assert.deepEqual(paths, [
    '/sessions/s',
    '/sessions/s/threads/main',
    '/agents/a',
    '/agents',
    '/sessions/s/thread-graph',
    '/sessions/s/threads/main/subagents',
    '/sessions/s/threads/main/content',
    '/agents/a/sessions/s/threads/main/executions',
    '/agents/a/sessions/s/threads/main/context-usage',
    '/evals/evaluators/catalog',
  ]);
});

test('history projection preserves tool-call status, arguments, and result', () => {
  const projection = new ThreadHistoryProjection();
  projection.project({ type: 'TEXT_MESSAGE_START', messageId: 'a', role: 'assistant', source: 2 });
  projection.project({
    type: 'TOOL_CALL_START',
    messageId: 'a',
    callId: 'call-1',
    name: 'lookup',
    toolHarnessName: 'support',
  });
  projection.project({
    type: 'TOOL_CALL_ARGS',
    callId: 'call-1',
    argsJson: '{"ticket":"T-1"}',
  });
  projection.project({
    type: 'TOOL_CALL_RESULT',
    callId: 'call-1',
    name: 'lookup',
    result: { text: 'Open' },
  });

  assert.deepEqual(projection.snapshot()[0]?.toolCalls, [{
    callId: 'call-1',
    name: 'lookup',
    messageId: 'a',
    status: 'complete',
    arguments: { ticket: 'T-1' },
    result: { text: 'Open' },
    toolHarnessName: 'support',
  }]);
});
