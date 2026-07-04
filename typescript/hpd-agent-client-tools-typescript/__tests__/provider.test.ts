import { describe, expect, it } from 'vitest';
import { createClientToolProvider } from '../src/provider.js';
import type {
  ClientToolProviderInvokeOutcomeMessage,
  ClientToolProviderInvokeToolMessage,
  ClientToolProviderBackgroundOperationOutcomeMessage,
  ClientToolProviderManifestMessage,
  ClientToolProviderToServerMessage,
  ClientToolProviderWelcomeMessage,
  ServerToClientToolProviderMessage,
} from '@hpd-research/hpd-agent-client';

describe('ClientToolProvider', () => {
  it('opens with hello, receives welcome, and publishes a manifest', async () => {
    const socket = new FakeWebSocket();
    const provider = createClientToolProvider({
      url: 'ws://localhost/api/hpd/client-tool-providers/connect',
      identity: {
        providerName: 'code-server-extension',
        appKind: 'code-server',
        instanceId: 'workspace-1',
      },
      appProvider: {
        name: 'code-server',
        displayName: 'Code Server',
      },
      context: {
        workspaceId: 'current',
      },
      webSocketFactory: () => socket.asWebSocket(),
    });

    provider.harness('editor', { description: 'Editor tools.' })
      .tool('get_selected_text', {
        description: 'Gets selected text.',
        parametersSchema: { type: 'object', properties: {} },
        handler: () => 'selected',
      });

    const connected = provider.connect();
    socket.open();
    expect(socket.sent[0]).toMatchObject({
      type: 'provider.hello',
      identity: {
        providerName: 'code-server-extension',
        appKind: 'code-server',
      },
    });

    socket.receive({
      type: 'provider.welcome',
      clientRuntimeId: 'crt_1',
      connectionId: 'cpc_1',
      heartbeatIntervalMs: 60_000,
    });
    await connected;

    expect(provider.status).toBe('ready');
    expect(provider.runtimeIds).toEqual({
      clientRuntimeId: 'crt_1',
      connectionId: 'cpc_1',
    });

    const manifest = socket.sent.find(isManifestMessage);
    expect(manifest).toMatchObject({
      type: 'provider.manifest',
      appProvider: { name: 'code-server' },
      context: { workspaceId: 'current' },
      readiness: 'Ready',
    });
    expect(manifest?.clientToolHarnesses?.[0]).toMatchObject({
      name: 'editor',
      tools: [{ name: 'get_selected_text' }],
    });
  });

  it('routes provider.invoke to the registered tool and returns a completed outcome', async () => {
    const { provider, socket } = await connectProvider();
    provider.harness('editor')
      .tool('get_selected_text', {
        description: 'Gets selected text.',
        parametersSchema: { type: 'object', properties: {} },
        handler: args => `selected:${String(args['suffix'])}`,
      });
    await provider.updateManifest();

    socket.receive(createInvocation({
      toolName: 'get_selected_text',
      arguments: { suffix: 'ok' },
    }));

    await nextTick();
    expect(socket.sent.find(isInvokeOutcomeMessage)).toMatchObject({
      type: 'provider.invokeOutcome',
      bindingId: 'bind_1',
      invocationId: 'inv_1',
      requestId: 'req_1',
      outcome: 'Completed',
      content: [{ type: 'text', text: 'selected:ok' }],
    });
  });

  it('rejects an invocation when the single-flight queue is full', async () => {
    let releaseFirst!: () => void;
    const { provider, socket } = await connectProvider({ maxQueueDepth: 1 });
    provider.harness('editor')
      .tool('slow', {
        description: 'Slow tool.',
        parametersSchema: { type: 'object', properties: {} },
        handler: async () => {
          await new Promise<void>(resolve => {
            releaseFirst = resolve;
          });
          return 'done';
        },
      });
    await provider.updateManifest();

    socket.receive(createInvocation({ invocationId: 'inv_1', requestId: 'req_1', toolName: 'slow' }));
    socket.receive(createInvocation({ invocationId: 'inv_2', requestId: 'req_2', toolName: 'slow' }));
    socket.receive(createInvocation({ invocationId: 'inv_3', requestId: 'req_3', toolName: 'slow' }));

    await nextTick();
    expect(socket.sent.find(message =>
      isInvokeOutcomeMessage(message) && message.invocationId === 'inv_3',
    )).toMatchObject({
      outcome: 'Rejected',
      errorMessage: 'Provider invocation queue is full.',
    });

    releaseFirst();
    await nextTick();
  });

  it('can accept background work with handle metadata', async () => {
    const { provider, socket } = await connectProvider();
    provider.harness('export')
      .tool('export_selection', {
        description: 'Exports selection.',
        parametersSchema: { type: 'object', properties: {} },
        invocationModePolicy: 'ModelChoice',
        handler: (_args, context) => {
          context.acceptBackground('op_1', {
            content: 'Export started.',
            handleKind: 'ClientToolOperation',
            supportedOperations: 'Cancel',
          });
        },
      });
    await provider.updateManifest();

    socket.receive(createInvocation({
      toolName: 'export_selection',
      requestedInvocationMode: 'Background',
    }));

    await nextTick();
    expect(socket.sent.find(isInvokeOutcomeMessage)).toMatchObject({
      type: 'provider.invokeOutcome',
      bindingId: 'bind_1',
      outcome: 'AcceptedBackground',
      clientOperationId: 'op_1',
      handleKind: 'ClientToolOperation',
      supportedOperations: 'Cancel',
      content: [{ type: 'text', text: 'Export started.' }],
    });
  });

  it('sends a terminal background operation outcome for accepted work', async () => {
    const { provider, socket } = await connectProvider();
    provider.harness('export')
      .tool('export_selection', {
        description: 'Exports selection.',
        parametersSchema: { type: 'object', properties: {} },
        invocationModePolicy: 'ModelChoice',
        handler: (_args, context) => {
          context.acceptBackground('op_1', {
            content: 'Export started.',
          });
        },
      });
    await provider.updateManifest();

    socket.receive(createInvocation({
      toolName: 'export_selection',
      requestedInvocationMode: 'Background',
    }));
    await nextTick();
    socket.clear();

    provider.completeBackgroundOperation('op_1', [{ type: 'json', value: { artifactId: 'file_1' } }], {
      metadata: { artifactId: 'file_1' },
    });

    expect(socket.sent.find(isBackgroundOutcomeMessage)).toEqual({
      type: 'provider.backgroundOperationOutcome',
      bindingId: 'bind_1',
      clientOperationId: 'op_1',
      state: 'Completed',
      content: [{ type: 'json', value: { artifactId: 'file_1' } }],
      augmentation: undefined,
      errorMessage: undefined,
      errorType: undefined,
      cancellationReason: undefined,
      metadata: { artifactId: 'file_1' },
    });
  });
});

async function connectProvider(options: { maxQueueDepth?: number } = {}) {
  const socket = new FakeWebSocket();
  const provider = createClientToolProvider({
    url: 'ws://localhost/api/hpd/client-tool-providers/connect',
    identity: {
      providerName: 'test-provider',
      appKind: 'test-app',
      instanceId: 'instance-1',
    },
    appProvider: {
      name: 'test-app',
    },
    concurrency: {
      maxQueueDepth: options.maxQueueDepth,
      invocationTimeoutMs: 1_000,
    },
    webSocketFactory: () => socket.asWebSocket(),
  });

  const connected = provider.connect();
  socket.open();
  socket.receive({
    type: 'provider.welcome',
    clientRuntimeId: 'crt_1',
    connectionId: 'cpc_1',
    heartbeatIntervalMs: 60_000,
  });
  await connected;
  socket.clear();

  return { provider, socket };
}

function createInvocation(
  overrides: Partial<ClientToolProviderInvokeToolMessage> = {},
): ClientToolProviderInvokeToolMessage {
  return {
    type: 'provider.invoke',
    clientRuntimeId: 'crt_1',
    connectionId: 'cpc_1',
    bindingId: 'bind_1',
    invocationId: 'inv_1',
    requestId: 'req_1',
    toolName: 'get_selected_text',
    visibleToolName: 'test_app_editor_get_selected_text',
    callId: 'call_1',
    arguments: {},
    ...overrides,
  };
}

function isManifestMessage(message: ClientToolProviderToServerMessage): message is ClientToolProviderManifestMessage {
  return message.type === 'provider.manifest';
}

function isInvokeOutcomeMessage(message: ClientToolProviderToServerMessage): message is ClientToolProviderInvokeOutcomeMessage {
  return message.type === 'provider.invokeOutcome';
}

function isBackgroundOutcomeMessage(
  message: ClientToolProviderToServerMessage,
): message is ClientToolProviderBackgroundOperationOutcomeMessage {
  return message.type === 'provider.backgroundOperationOutcome';
}

function nextTick(): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, 0));
}

class FakeWebSocket {
  public static readonly OPEN = 1;
  public readyState = 0;
  public sent: ClientToolProviderToServerMessage[] = [];
  public onopen: (() => void) | null = null;
  public onclose: (() => void) | null = null;
  public onerror: (() => void) | null = null;
  public onmessage: ((event: { data: string }) => void | Promise<void>) | null = null;

  public asWebSocket(): WebSocket {
    return this as unknown as WebSocket;
  }

  public open(): void {
    this.readyState = FakeWebSocket.OPEN;
    this.onopen?.();
  }

  public close(): void {
    this.readyState = 3;
    this.onclose?.();
  }

  public send(text: string): void {
    this.sent.push(JSON.parse(text) as ClientToolProviderToServerMessage);
  }

  public receive(message: ServerToClientToolProviderMessage): void {
    void this.onmessage?.({ data: JSON.stringify(message) });
  }

  public clear(): void {
    this.sent = [];
  }
}
