import { describe, expect, it } from 'vitest';
import { ClientToolRegistry, normalizeClientToolName } from '../src/tools.js';
import { completeClientToolWithJson, completeClientToolWithText } from '../src/types/client-tools.js';
import { EventTypes } from '../src/types/events.js';

describe('ClientToolRegistry', () => {
  it('normalizes toolharness-qualified tool names', () => {
    expect(normalizeClientToolName('browser.create_artifact')).toBe('create_artifact');
  });

  it('dispatches registered tool handlers and wraps JSON results', async () => {
    const registry = new ClientToolRegistry();
    registry.register('get_active_view', () => ({ activeView: 'chat' }));

    const response = await registry.handleInvoke({
      type: EventTypes.CLIENT_TOOL_INVOKE_REQUEST,
      requestId: 'r1',
      callId: 'c1',
      toolName: 'browser.get_active_view',
      arguments: {},
    });

    expect(response).toEqual({
      requestId: 'r1',
      outcome: 'Completed',
      content: [{ type: 'json', value: { activeView: 'chat' } }],
      augmentation: undefined,
    });
  });

  it('registers every tool in a toolharness to one handler', async () => {
    const registry = new ClientToolRegistry();
    registry.registerToolHarness({
      name: 'browser',
      tools: [{ name: 'ping', description: 'Ping', parametersSchema: {} }],
    }, () => 'pong');

    expect(registry.clientToolHarnesses).toHaveLength(1);
    expect(await registry.handleInvoke({
      type: EventTypes.CLIENT_TOOL_INVOKE_REQUEST,
      requestId: 'r1',
      callId: 'c1',
      toolName: 'browser.ping',
      arguments: {},
    })).toMatchObject({
      requestId: 'r1',
      outcome: 'Completed',
      content: [{ type: 'text', text: 'pong' }],
    });
  });

  it('returns a failed outcome for unknown tools', async () => {
    const registry = new ClientToolRegistry();
    const response = await registry.handleInvoke({
      type: EventTypes.CLIENT_TOOL_INVOKE_REQUEST,
      requestId: 'r1',
      callId: 'c1',
      toolName: 'missing',
      arguments: {},
    });

    expect(response).toMatchObject({
      requestId: 'r1',
      outcome: 'Failed',
      errorMessage: 'Unknown client tool: missing',
    });
  });

  it('uses a fallback handler for tools without explicit registrations', async () => {
    const registry = new ClientToolRegistry();
    registry.registerFallback((request) => ({ toolName: request.toolName }));

    expect(registry.canHandle('anything')).toBe(true);
    await expect(registry.handleInvoke({
      type: EventTypes.CLIENT_TOOL_INVOKE_REQUEST,
      requestId: 'r1',
      callId: 'c1',
      toolName: 'browser.dynamic_tool',
      arguments: {},
    })).resolves.toMatchObject({
      requestId: 'r1',
      outcome: 'Completed',
      content: [{ type: 'json', value: { toolName: 'browser.dynamic_tool' } }],
    });
  });

  it('creates direct text and JSON client tool outcomes', () => {
    expect(completeClientToolWithText('r1', 'done')).toEqual({
      requestId: 'r1',
      outcome: 'Completed',
      content: [{ type: 'text', text: 'done' }],
      augmentation: undefined,
    });

    expect(completeClientToolWithJson('r2', { ok: true })).toEqual({
      requestId: 'r2',
      outcome: 'Completed',
      content: [{ type: 'json', value: { ok: true } }],
      augmentation: undefined,
    });
  });
});
