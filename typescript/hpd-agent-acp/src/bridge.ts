import { EventTypes, type AgentClient, type AgentResponseInput, type EventSubscription } from '@hpd-research/hpd-agent-client';
import type { AcpReader } from './acp/reader.js';
import type { AcpWriter } from './acp/writer.js';
import type { SessionRegistry } from './bridge/session.js';
import { hpdEventToAcpUpdate } from './bridge/events.js';
import { handlePermissionRequest } from './bridge/permissions.js';
import { handleClientToolInvoke, capsToToolHarnesses } from './bridge/client-tools.js';
import { handleClarificationRequest, tryResolveClarification } from './bridge/clarification.js';
import type {
  InboundMessage,
  InitializeRequest,
  SessionNewRequest,
  SessionLoadRequest,
  SessionPromptRequest,
  SessionCancelNotification,
  AcpClientCapabilities,
  JsonRpcResponse,
  JsonRpcError,
} from './types/acp.js';
import { JsonRpcErrorCode } from './types/acp.js';
import type { BridgeConfig } from './config.js';

export function createBridge(
  client: AgentClient,
  reader: AcpReader,
  writer: AcpWriter,
  sessions: SessionRegistry,
  config: BridgeConfig,
): void {
  let clientCapabilities: AcpClientCapabilities = {};

  // ── Inbound message dispatch ────────────────────────────────────────────────

  reader.onMessage((message: InboundMessage) => {
    // Responses to agent→client requests (editor replying to our outbound requests)
    if ('result' in message || ('error' in message && !('method' in message))) {
      const asResponse = message as JsonRpcResponse<unknown> | JsonRpcError;
      const id = asResponse.id;
      const error = 'error' in asResponse ? asResponse.error : undefined;
      const result = 'result' in asResponse ? asResponse.result : undefined;
      sessions.resolveOutboundRequest(id, result, error);
      return;
    }

    const req = message as { method?: string; id?: unknown };
    if (!req.method) return;

    void dispatch(message);
  });

  reader.onError((err) => {
    process.stderr.write(`ACP parse error: ${err.message}\n`);
  });

  // ── Method handlers ─────────────────────────────────────────────────────────

  async function dispatch(message: InboundMessage): Promise<void> {
    const m = message as { method?: string };
    try {
      switch (m.method) {
        case 'initialize':        return handleInitialize(message as InitializeRequest);
        case 'authenticate':      return handleAuthenticate(message as { id: unknown });
        case 'session/new':       return await handleSessionNew(message as SessionNewRequest);
        case 'session/load':      return await handleSessionLoad(message as SessionLoadRequest);
        case 'session/prompt':    return await handleSessionPrompt(message as SessionPromptRequest);
        case 'session/cancel':    return handleSessionCancel(message as SessionCancelNotification);
        case 'session/set_mode':  return handleSetMode(message as { id: unknown });
        default: {
          const withId = message as { id?: unknown };
          if (withId.id !== undefined) {
            writer.respondError(
              withId.id as import('./types/acp.js').RequestId,
              JsonRpcErrorCode.MethodNotFound,
              `Method not found: ${m.method ?? ''}`,
            );
          }
        }
      }
    } catch (err) {
      const withId = message as { id?: unknown };
      if (withId.id !== undefined) {
        writer.respondError(
          withId.id as import('./types/acp.js').RequestId,
          JsonRpcErrorCode.InternalError,
          err instanceof Error ? err.message : String(err),
        );
      }
      process.stderr.write(`Unhandled error in ${m.method ?? 'unknown'}: ${err}\n`);
    }
  }

  // ── initialize ──────────────────────────────────────────────────────────────

  function handleInitialize(req: InitializeRequest): void {
    clientCapabilities = req.params.clientCapabilities ?? {};

    writer.respondInitialize(req.id, {
      protocolVersion: req.params.protocolVersion,
      agentCapabilities: {
        loadSession: true,
        promptCapabilities: {
          image: false,
          audio: false,
          embeddedContext: true,
        },
        mcpCapabilities: {
          http: true,
          sse: false,
        },
      },
      agentInfo: {
        name: config.agentName ?? 'hpd-agent',
        title: 'HPD Agent',
        version: '0.1.0',
      },
    });
  }

  // ── authenticate ────────────────────────────────────────────────────────────

  function handleAuthenticate(req: { id: unknown }): void {
    writer.respondAuthenticate(req.id as import('./types/acp.js').RequestId);
  }

  // ── session/new ─────────────────────────────────────────────────────────────

  async function handleSessionNew(req: SessionNewRequest): Promise<void> {
    const hpdSession = await client.createSession();
    const hpdThread  = await client.createThread(hpdSession.id);

    const state = sessions.create(hpdSession.id, hpdThread.id, req.params.cwd);
    writer.respondSessionNew(req.id, { sessionId: state.acpSessionId });
  }

  // ── session/load ────────────────────────────────────────────────────────────

  async function handleSessionLoad(req: SessionLoadRequest): Promise<void> {
    const hpdSessionId = req.params.sessionId;

    let session = sessions.get(hpdSessionId);

    if (!session) {
      try {
        const threads = await client.listThreads(hpdSessionId);
        const thread = threads[0];
        if (!thread) throw new Error('No threads found');
        session = sessions.create(hpdSessionId, thread.id, req.params.cwd);
      } catch {
        writer.respondError(req.id, JsonRpcErrorCode.ResourceNotFound, `Session not found: ${hpdSessionId}`);
        return;
      }
    }

    const messages = await client.getThreadMessages(session.hpdSessionId, session.hpdThreadId);
    for (const msg of messages) {
      for (const part of msg.contents) {
        if (part.$type === 'text') {
          const textPart = part as { $type: 'text'; text: string };
          const updateType = msg.role === 'user' ? 'user_message_chunk' : 'agent_message_chunk';
          writer.notifySessionUpdate(hpdSessionId, {
            sessionUpdate: updateType,
            content: { type: 'text', text: textPart.text },
          });
        }
      }
    }

    writer.respondSessionLoad(req.id);
  }

  // ── session/prompt ──────────────────────────────────────────────────────────

  async function handleSessionPrompt(req: SessionPromptRequest): Promise<void> {
    const session = sessions.get(req.params.sessionId);
    if (!session) {
      writer.respondError(req.id, JsonRpcErrorCode.ResourceNotFound, `Session not found: ${req.params.sessionId}`);
      return;
    }

    const promptText = req.params.prompt
      .filter((b): b is import('./types/acp.js').AcpTextContent => b.type === 'text')
      .map(b => b.text)
      .join('\n');

    if (tryResolveClarification(session, promptText)) {
      return;
    }

    session.pendingPromptRequestId = req.id;
    session.streamAbort = new AbortController();
    let cleanupPrompt = () => {};

    try {
      const subscriptions: EventSubscription[] = [];
      const cleanup = () => {
        for (const sub of subscriptions.splice(0)) sub.dispose();
      };
      cleanupPrompt = cleanup;
      const finish = (stopReason: 'end_turn' | 'cancelled') => {
        if (!session.pendingPromptRequestId) return;
        writer.respondSessionPrompt(session.pendingPromptRequestId, stopReason);
        session.pendingPromptRequestId = null;
        session.streamAbort = null;
        cleanup();
      };
      const fail = (streamErr: unknown) => {
        if (!session.pendingPromptRequestId) return;
        writer.respondError(
          session.pendingPromptRequestId,
          JsonRpcErrorCode.InternalError,
          streamErr instanceof Error ? streamErr.message : String(streamErr),
        );
        session.pendingPromptRequestId = null;
        session.streamAbort = null;
        cleanup();
      };
      const respond = (input: AgentResponseInput) => {
        void client.respond(input).catch(fail);
      };

      subscriptions.push(
        client.onAny((event) => {
          const update = hpdEventToAcpUpdate(event);
          if (update) writer.notifySessionUpdate(session.acpSessionId, update);
        }),
        client.on(EventTypes.PERMISSION_REQUEST, (event) => {
          void handlePermissionRequest(event, session, writer)
            .then((response) => respond({
              type: EventTypes.PERMISSION_RESPONSE,
              permissionId: event.permissionId,
              sourceName: event.sourceName,
              approved: response.approved,
              choice: response.choice,
            }))
            .catch(fail);
        }),
        client.on(EventTypes.CLIENT_TOOL_INVOKE_REQUEST, (request) => {
          void handleClientToolInvoke(request, session, writer, clientCapabilities)
            .then((response) => respond({
              type: EventTypes.CLIENT_TOOL_INVOKE_OUTCOME,
              requestId: response.requestId,
              outcome: response.outcome,
              content: response.content,
              errorMessage: response.errorMessage,
              clientOperationId: response.clientOperationId,
              handleKind: response.handleKind,
              supportedOperations: response.supportedOperations,
              augmentation: response.augmentation,
            }))
            .catch(fail);
        }),
        client.on(EventTypes.CLARIFICATION_REQUEST, (event) => {
          void handleClarificationRequest(event, session, writer)
            .then((answer) => respond({
              type: EventTypes.CLARIFICATION_RESPONSE,
              requestId: event.requestId,
              sourceName: event.sourceName,
              question: event.question,
              answer,
            }))
            .catch(fail);
        }),
        client.on(EventTypes.CONTINUATION_REQUEST, (event) => {
          respond({
            type: EventTypes.CONTINUATION_RESPONSE,
            continuationId: event.continuationId,
            sourceName: event.sourceName,
            approved: true,
          });
        }),
        client.on(EventTypes.MESSAGE_TURN_FINISHED, () => finish('end_turn')),
        client.on(EventTypes.MESSAGE_TURN_ERROR, (event) => fail(event.errorMessage)),
        client.onError(fail),
      );

      await client.run({
        type: EventTypes.USER_MESSAGES_INPUT,
        sessionId: session.hpdSessionId,
        threadId: session.hpdThreadId,
        agentId: config.agentName ?? 'default',
        messages: [{
          role: 'user',
          contents: [{ $type: 'text', text: promptText }],
        }],
        runConfig: {
          clientToolInput: {
            clientToolHarnesses: capsToToolHarnesses(clientCapabilities),
            resetClientState: true,
          },
        },
      }, { signal: session.streamAbort.signal });

      finish('end_turn');
    } catch (caught) {
      const isAbort = caught instanceof Error && caught.name === 'AbortError';
      if (!session.pendingPromptRequestId) {
        cleanupPrompt();
        return;
      }
      if (isAbort) {
        writer.respondSessionPrompt(session.pendingPromptRequestId!, 'cancelled');
      } else {
        writer.respondError(
          session.pendingPromptRequestId!,
          JsonRpcErrorCode.InternalError,
          caught instanceof Error ? caught.message : String(caught),
        );
      }
      session.pendingPromptRequestId = null;
      session.streamAbort = null;
      cleanupPrompt();
    }
  }

  // ── session/cancel ──────────────────────────────────────────────────────────

  function handleSessionCancel(notification: SessionCancelNotification): void {
    sessions.cancelAll(notification.params.sessionId);
  }

  // ── session/set_mode ────────────────────────────────────────────────────────

  function handleSetMode(req: { id: unknown }): void {
    writer.respondSessionSetMode(req.id as import('./types/acp.js').RequestId);
  }
}
