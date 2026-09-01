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

  it('republishes a manifest only when subscribed context changes', async () => {
    const socket = new FakeWebSocket();
    let context = { documentId: 'document-1', appStateVersion: '1' };
    let notifyContextChanged: (() => void) | undefined;
    let subscriptionDisposed = false;
    const provider = createClientToolProvider({
      url: 'ws://localhost/api/hpd/client-tool-providers/connect',
      identity: {
        providerName: 'design-editor',
        appKind: 'design-editor',
        instanceId: 'workspace-1',
      },
      appProvider: {
        name: 'design-editor',
        displayName: 'Design Editor',
      },
      context: () => context,
      contextUpdateDebounceMs: 0,
      subscribeContextChanges: listener => {
        notifyContextChanged = listener;
        return () => {
          subscriptionDisposed = true;
        };
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
    await nextTick();
    socket.clear();

    notifyContextChanged?.();
    notifyContextChanged?.();
    await nextTick();
    expect(socket.sent.filter(isManifestMessage)).toHaveLength(0);

    context = { documentId: 'document-1', appStateVersion: '2' };
    notifyContextChanged?.();
    notifyContextChanged?.();
    await nextTick();
    expect(socket.sent.filter(isManifestMessage)).toHaveLength(1);
    expect(socket.sent.find(isManifestMessage)?.context).toEqual(context);

    await provider.disconnect();
    expect(subscriptionDisposed).toBe(true);
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
      error: {
        kind: 'provider_not_ready',
        message: 'Provider invocation queue is full.',
      },
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
        policy: { invocationModePolicy: 'ModelChoice' },
        handler: (_args, context) => {
          context.acceptBackground({
            content: 'Export started.',
            operationKind: 'Provider',
            operationCapabilities: 'Cancel',
          });
        },
      });
    await provider.updateManifest();

    socket.receive(createInvocation({
      toolName: 'export_selection',
      requestedInvocationMode: 'Background',
      resolvedInvocationMode: 'Background',
      clientOperationId: 'op_1',
    }));

    await nextTick();
    expect(socket.sent.find(isInvokeOutcomeMessage)).toMatchObject({
      type: 'provider.invokeOutcome',
      bindingId: 'bind_1',
      outcome: 'AcceptedBackground',
      clientOperationId: 'op_1',
      operationKind: 'Provider',
      operationCapabilities: 'Cancel',
      content: [{ type: 'text', text: 'Export started.' }],
    });
  });

  it('sends a terminal background operation outcome for accepted work', async () => {
    const { provider, socket } = await connectProvider();
    provider.harness('export')
      .tool('export_selection', {
        description: 'Exports selection.',
        parametersSchema: { type: 'object', properties: {} },
        policy: { invocationModePolicy: 'ModelChoice' },
        handler: (_args, context) => {
          context.acceptBackground({
            content: 'Export started.',
          });
        },
      });
    await provider.updateManifest();

    socket.receive(createInvocation({
      toolName: 'export_selection',
      requestedInvocationMode: 'Background',
      resolvedInvocationMode: 'Background',
      clientOperationId: 'op_1',
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
      error: undefined,
      cancellationReason: undefined,
      metadata: { artifactId: 'file_1' },
    });
  });

  it('validates and dispatches a typed compound operation', async () => {
    const { provider, socket } = await connectProvider();
    provider.harness('design')
      .operationTool<
        | { action: 'inspect'; nodeId: string }
        | { action: 'delete'; nodeIds: string[] }
      >('penpot', {
        description: 'Operates on the active design.',
        discriminator: 'action',
        parametersSchema: {
          type: 'object',
          oneOf: [
            {
              type: 'object',
              properties: {
                action: { const: 'inspect' },
                nodeId: { type: 'string' },
              },
              required: ['action', 'nodeId'],
              additionalProperties: false,
            },
            {
              type: 'object',
              properties: {
                action: { const: 'delete' },
                nodeIds: { type: 'array', items: { type: 'string' } },
              },
              required: ['action', 'nodeIds'],
              additionalProperties: false,
            },
          ],
        },
        actions: {
          inspect: {
            requiresPermission: false,
            requiresFreshContext: false,
          },
          delete: {
            requiresPermission: true,
            permissionScope: 'penpot.delete',
            destructive: true,
            requiresFreshContext: true,
          },
        },
        parse: value => {
          const request = value as { action?: unknown; nodeId?: unknown };
          if (request.action !== 'inspect' || typeof request.nodeId !== 'string') {
            throw new Error('nodeId is required.');
          }
          return { action: request.action, nodeId: request.nodeId };
        },
        handlers: {
          inspect: request => ({ type: 'json', value: { nodeId: request.nodeId } }),
          delete: request => ({ type: 'json', value: { deleted: request.nodeIds } }),
        },
      });
    await provider.updateManifest();

    socket.receive(createInvocation({
      toolName: 'penpot',
      arguments: { action: 'inspect', nodeId: 'node-1' },
      operation: {
        discriminator: 'action',
        action: 'inspect',
        policy: {
          requiresPermission: false,
          mutatesState: false,
          requiresFreshContext: false,
          destructive: false,
          idempotent: false,
          invocationModePolicy: 'SynchronousOnly',
        },
      },
    }));
    await nextTick();

    expect(socket.sent.find(isInvokeOutcomeMessage)).toMatchObject({
      outcome: 'Completed',
      content: [{ type: 'json', value: { nodeId: 'node-1' } }],
    });
    expect(socket.sent.find(isManifestMessage)?.clientToolHarnesses?.[0]?.tools[0])
      .toMatchObject({
        operationContract: {
          discriminator: 'action',
          actions: { inspect: { requiresPermission: false } },
        },
      });
  });

  it('rejects stale context before a mutating operation handler runs', async () => {
    let invoked = false;
    const socket = new FakeWebSocket();
    const provider = createClientToolProvider({
      url: 'ws://localhost/provider',
      identity: { providerName: 'test', appKind: 'test' },
      appProvider: { name: 'test' },
      contextSnapshot: () => ({ documentId: 'doc-2', appStateVersion: '2' }),
      webSocketFactory: () => socket.asWebSocket(),
    });
    provider.harness('design').operationTool<{ action: 'update' }>('penpot', {
      description: 'Updates a design.',
      discriminator: 'action',
      parametersSchema: {
        type: 'object',
        oneOf: [{
          type: 'object',
          properties: { action: { const: 'update' } },
          required: ['action'],
          additionalProperties: false,
        }],
      },
      actions: {
        update: {
          requiresPermission: true,
          permissionScope: 'penpot.write.update',
          mutatesState: true,
          requiresFreshContext: true,
        },
      },
      parse: value => value as { action: 'update' },
      handler: () => {
        invoked = true;
      },
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

    socket.receive(createInvocation({
      toolName: 'penpot',
      arguments: { action: 'update' },
      operation: {
        discriminator: 'action',
        action: 'update',
        policy: {
          requiresPermission: true,
          permissionScope: 'penpot.write.update',
          mutatesState: true,
          requiresFreshContext: true,
          destructive: false,
          idempotent: false,
          invocationModePolicy: 'SynchronousOnly',
        },
      },
      expectedContext: { documentId: 'doc-1', appStateVersion: '1' },
    }));
    await nextTick();

    expect(invoked).toBe(false);
    expect(socket.sent.find(isInvokeOutcomeMessage)).toMatchObject({
      outcome: 'Rejected',
      error: {
        kind: 'stale_context',
        currentContext: { documentId: 'doc-2', appStateVersion: '2' },
      },
    });
  });

  it('accepts additional live fields when expected context is partial', async () => {
    let invoked = false;
    const socket = new FakeWebSocket();
    const provider = createClientToolProvider({
      url: 'ws://localhost/provider',
      identity: { providerName: 'test', appKind: 'test' },
      appProvider: { name: 'test' },
      contextSnapshot: () => ({
        workspaceId: 'workspace-1',
        documentId: 'doc-1',
        appStateVersion: '42',
      }),
      webSocketFactory: () => socket.asWebSocket(),
    });
    provider.harness('design').operationTool<{ action: 'update' }>('penpot', {
      description: 'Updates a design.',
      discriminator: 'action',
      parametersSchema: {
        type: 'object',
        oneOf: [{
          type: 'object',
          properties: { action: { const: 'update' } },
          required: ['action'],
          additionalProperties: false,
        }],
      },
      actions: {
        update: {
          requiresPermission: true,
          permissionScope: 'penpot.write.update',
          mutatesState: true,
          requiresFreshContext: true,
        },
      },
      parse: value => value as { action: 'update' },
      handler: () => {
        invoked = true;
      },
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

    socket.receive(createInvocation({
      toolName: 'penpot',
      arguments: { action: 'update' },
      operation: {
        discriminator: 'action',
        action: 'update',
        policy: {
          requiresPermission: true,
          permissionScope: 'penpot.write.update',
          mutatesState: true,
          requiresFreshContext: true,
          destructive: false,
          idempotent: false,
          invocationModePolicy: 'SynchronousOnly',
        },
      },
      expectedContext: { documentId: 'doc-1', appStateVersion: '42' },
    }));
    await nextTick();

    expect(invoked).toBe(true);
    expect(socket.sent.find(isInvokeOutcomeMessage)).toMatchObject({
      outcome: 'Completed',
    });
  });

  it('accepts the lowercase .NET wire mode for a background-only action', async () => {
    const { provider, socket } = await connectProvider();
    provider.harness('import').operationTool<{ action: 'importFile' }>('document', {
      description: 'Imports a document.',
      discriminator: 'action',
      parametersSchema: {
        type: 'object',
        oneOf: [{
          type: 'object',
          properties: { action: { const: 'importFile' } },
          required: ['action'],
          additionalProperties: false,
        }],
      },
      actions: {
        importFile: {
          requiresPermission: true,
          permissionScope: 'document.import',
          invocationModePolicy: 'BackgroundOnly',
        },
      },
      parse: value => value as { action: 'importFile' },
      handler: (_request, context) => {
        expect(context.requestedInvocationMode).toBeUndefined();
        expect(context.resolvedInvocationMode).toBe('Background');
        context.acceptBackground();
      },
    });
    await provider.updateManifest();

    socket.receive(createInvocation({
      toolName: 'document',
      arguments: { action: 'importFile' },
      requestedInvocationMode: undefined,
      resolvedInvocationMode: 'background',
      clientOperationId: 'import-1',
      operation: {
        discriminator: 'action',
        action: 'importFile',
        policy: {
          requiresPermission: true,
          permissionScope: 'document.import',
          mutatesState: false,
          requiresFreshContext: false,
          destructive: false,
          idempotent: false,
          invocationModePolicy: 'BackgroundOnly',
        },
      },
    }));
    await nextTick();

    expect(socket.sent.find(isInvokeOutcomeMessage)).toMatchObject({
      outcome: 'AcceptedBackground',
      clientOperationId: 'import-1',
    });
  });

  it('resolves fresh authority and republishes the manifest after reconnect', async () => {
    const sockets: FakeWebSocket[] = [];
    const resolutionReasons: string[] = [];
    const states: string[] = [];
    let context = { documentId: 'document-1', appStateVersion: '1' };
    const provider = createClientToolProvider({
      connection: {
        resolveEndpoint: async reason => {
          resolutionReasons.push(reason);
          return {
            url: `wss://app.example/_hpd/client-tools/${resolutionReasons.length}`,
            protocols: [`authority-${resolutionReasons.length}`],
          };
        },
        retry: {
          initialDelayMs: 1,
          maxDelayMs: 1,
          jitterRatio: 0,
        },
      },
      identity: {
        providerName: 'penpot-browser',
        appKind: 'design-editor',
        instanceId: 'frontend-runtime',
      },
      appProvider: { name: 'penpot' },
      context: () => context,
      onConnectionStateChange: change => states.push(change.current),
      webSocketFactory: () => {
        const socket = new FakeWebSocket();
        sockets.push(socket);
        return socket.asWebSocket();
      },
    });
    provider.harness('design').tool('inspect', {
      description: 'Inspects the design.',
      parametersSchema: { type: 'object', properties: {} },
      handler: () => 'ok',
    });

    const connected = provider.connect();
    await waitUntil(() => sockets.length === 1);
    sockets[0]!.open();
    sockets[0]!.receive({
      type: 'provider.welcome',
      clientRuntimeId: 'crt_1',
      connectionId: 'cpc_1',
      heartbeatIntervalMs: 60_000,
    });
    await connected;
    expect(sockets[0]!.sent.find(isManifestMessage)?.context).toEqual(context);

    context = { documentId: 'document-1', appStateVersion: '2' };
    sockets[0]!.close();
    await waitUntil(() => sockets.length === 2);
    sockets[1]!.open();
    sockets[1]!.receive({
      type: 'provider.welcome',
      clientRuntimeId: 'crt_2',
      connectionId: 'cpc_2',
      heartbeatIntervalMs: 60_000,
    });
    await waitUntil(() => provider.status === 'ready');

    expect(resolutionReasons).toEqual(['initial', 'reconnect']);
    expect(provider.runtimeIds).toEqual({
      clientRuntimeId: 'crt_2',
      connectionId: 'cpc_2',
    });
    expect(sockets[1]!.sent.find(isManifestMessage)?.context).toEqual(context);
    expect(states).toContain('disconnected');
    expect(states).toContain('resolving_endpoint');
    expect(states).toContain('registering');
    await provider.disconnect();
  });

  it('treats authority revocation as terminal and abandons background work', async () => {
    const sockets: FakeWebSocket[] = [];
    const abandoned: string[] = [];
    const provider = createClientToolProvider({
      connection: {
        resolveEndpoint: async () => ({
          url: 'wss://app.example/_hpd/client-tools',
          protocols: ['one-time-authority'],
        }),
        retry: {
          initialDelayMs: 1,
          maxDelayMs: 1,
          jitterRatio: 0,
        },
      },
      identity: {
        providerName: 'penpot-browser',
        appKind: 'design-editor',
        instanceId: 'frontend-runtime',
      },
      appProvider: { name: 'penpot' },
      onBackgroundOperationAbandoned: operation => {
        abandoned.push(`${operation.clientOperationId}:${operation.reason}`);
      },
      webSocketFactory: () => {
        const socket = new FakeWebSocket();
        sockets.push(socket);
        return socket.asWebSocket();
      },
    });
    provider.harness('design').tool('export', {
      description: 'Exports the design.',
      parametersSchema: { type: 'object', properties: {} },
      policy: { invocationModePolicy: 'ModelChoice' },
      handler: (_args, context) => {
        context.acceptBackground();
      },
    });

    const connected = provider.connect();
    await waitUntil(() => sockets.length === 1);
    sockets[0]!.open();
    sockets[0]!.receive({
      type: 'provider.welcome',
      clientRuntimeId: 'crt_1',
      connectionId: 'cpc_1',
      heartbeatIntervalMs: 60_000,
    });
    await connected;
    sockets[0]!.receive(createInvocation({
      toolName: 'export',
      resolvedInvocationMode: 'Background',
      clientOperationId: 'export-1',
    }));
    await nextTick();

    sockets[0]!.receive({
      type: 'provider.error',
      code: 'authority_revoked',
      message: 'The browser launch was revoked.',
    });
    await nextTick();

    expect(provider.status).toBe('revoked');
    expect(abandoned).toEqual(['export-1:provider_revoked']);
    sockets[0]!.close();
    await new Promise(resolve => setTimeout(resolve, 5));
    expect(sockets).toHaveLength(1);
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
    protocolVersion: '2',
    clientRuntimeId: 'crt_1',
    connectionId: 'cpc_1',
    bindingId: 'bind_1',
    invocationId: 'inv_1',
    requestId: 'req_1',
    toolName: 'get_selected_text',
    visibleToolName: 'test_app_editor_get_selected_text',
    callId: 'call_1',
    arguments: {},
    resolvedInvocationMode: 'Synchronous',
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

async function waitUntil(condition: () => boolean): Promise<void> {
  const deadline = Date.now() + 1_000;
  while (!condition()) {
    if (Date.now() >= deadline) {
      throw new Error('Condition was not met before the test timeout.');
    }
    await nextTick();
  }
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
