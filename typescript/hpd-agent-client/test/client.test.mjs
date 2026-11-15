import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  AgentClient,
  ClientToolRegistry,
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
  assert.equal((await thread.upload(new Blob(['Hello']), 'hello.txt')).id, 'uploaded');
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
