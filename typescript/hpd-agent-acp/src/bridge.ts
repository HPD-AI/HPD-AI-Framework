import { type AgentClient, type HpdAgentResponseInput, type ThreadObservation } from '@hpd-research/hpd-agent-client';
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
    const { session: hpdSession, thread: hpdThread } = await client.sessions.create({ agentId: config.agentName });

    const state = sessions.create(hpdSession.id, hpdThread.id, req.params.cwd);
    writer.respondSessionNew(req.id, { sessionId: state.acpSessionId });
  }

  // ── session/load ────────────────────────────────────────────────────────────

  async function handleSessionLoad(req: SessionLoadRequest): Promise<void> {
    const hpdSessionId = req.params.sessionId;

    let session = sessions.get(hpdSessionId);

    if (!session) {
      try {
        const threads = await (await client.sessions.get(hpdSessionId)).threads.list();
        const thread = threads[0];
        if (!thread) throw new Error('No threads found');
        session = sessions.create(hpdSessionId, thread.id, req.params.cwd);
      } catch {
        writer.respondError(req.id, JsonRpcErrorCode.ResourceNotFound, `Session not found: ${hpdSessionId}`);
        return;
      }
    }

    const thread = await (await client.sessions.get(session.hpdSessionId)).threads.get(session.hpdThreadId);
    const state = await thread.state();
    const { generation, sequenceNumber } = state.observedCursor;
    if (generation != null && sequenceNumber != null && sequenceNumber > 0) {
      const controller = new AbortController();
      const roles = new Map<string, string>();
      const observation = await thread.observe({
        after: { generation, sequence: 0 },
        signal: controller.signal,
        onEvent({ event, cursor }) {
          if (event.type === 'TEXT_MESSAGE_START' && (event.visibility == null || event.visibility === 0)) {
            roles.set(event.messageId, event.role);
          } else if (event.type === 'TEXT_DELTA') {
            const role = roles.get(event.messageId);
            if (role === 'user' || role === 'assistant') {
              writer.notifySessionUpdate(session.acpSessionId, {
                sessionUpdate: role === 'user' ? 'user_message_chunk' : 'agent_message_chunk',
                content: { type: 'text', text: event.text },
              });
            }
          }
          if (cursor && cursor.sequence >= sequenceNumber) controller.abort();
        },
      });
      await observation.done;
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
    const controller = new AbortController();
    session.streamAbort = controller;
    let observation: ThreadObservation | undefined;

    try {
      const thread = await (await client.sessions.get(session.hpdSessionId)).threads.get(session.hpdThreadId);
      const state = await thread.state({ agentId: config.agentName });
      const { generation, sequenceNumber } = state.observedCursor;
      if (generation == null || sequenceNumber == null) throw new Error('Missing thread cursor');
      let finishExecution!: (value: { id: string; outcome: string }) => void;
      const finished = new Promise<{ id: string; outcome: string }>(resolve => { finishExecution = resolve; });
      let rejectInteraction!: (error: unknown) => void;
      const interactionFailed = new Promise<never>((_, reject) => { rejectInteraction = reject; });
      void interactionFailed.catch(() => {});
      const respond = (input: HpdAgentResponseInput) =>
        thread.respond(input, { agentId: config.agentName });

      observation = await thread.observe({
        agentId: config.agentName,
        after: { generation, sequence: sequenceNumber },
        signal: controller.signal,
        onEvent: ({ event }) => {
          const update = hpdEventToAcpUpdate(event);
          if (update) writer.notifySessionUpdate(session.acpSessionId, update);
        },
        handlers: {
          PERMISSION_REQUEST: ({ event }) => {
            void handlePermissionRequest(event, session, writer)
              .then(({ choiceId }) => respond({
                type: 'PERMISSION_RESPONSE', permissionId: event.permissionId, sourceName: event.sourceName, choiceId,
              })).catch(rejectInteraction);
          },
          CLIENT_TOOL_INVOKE_REQUEST: ({ event }) => {
            void handleClientToolInvoke({ ...event, arguments: { ...event.arguments } }, session, writer, clientCapabilities)
              .then(response => respond({ ...response, type: 'CLIENT_TOOL_INVOKE_OUTCOME' }))
              .catch(rejectInteraction);
          },
          USER_QUESTION_REQUEST: ({ event }) => {
            void (async () => {
              const answers: Extract<HpdAgentResponseInput, { type: 'QUESTION_RESPONSE' }>['answers'] = [];
              for (const question of event.questions) {
                const answer = await handleClarificationRequest(event.requestId, question, session, writer);
                const selected = question.options?.find(option => option.id === answer || option.label === answer);
                answers.push({
                  questionId: question.id,
                  selectedOptionIds: selected ? [selected.id] : [],
                  customText: selected ? undefined : answer,
                });
              }
              await respond({ type: 'QUESTION_RESPONSE', requestId: event.requestId, sourceName: event.sourceName,
                outcome: 'Answered', answers });
            })().catch(rejectInteraction);
          },
          CONTINUATION_REQUEST: ({ event }) => {
            void respond({ type: 'CONTINUATION_RESPONSE', continuationId: event.continuationId,
              sourceName: event.sourceName, approved: true }).catch(rejectInteraction);
          },
          THREAD_EXECUTION_FINISHED: ({ event }) => {
            finishExecution({ id: event.threadExecutionId, outcome: event.outcome });
          },
        },
      });

      controller.signal.throwIfAborted();
      const result = await thread.send(promptText, {
        agentId: config.agentName,
        runConfig: { tools: { clientInput: {
          clientToolHarnesses: capsToToolHarnesses(clientCapabilities),
          resetClientState: true,
        } } },
      });
      if (result.status === 'busy') throw new Error('Thread already has an active execution');
      session.execution = result.execution;
      if (controller.signal.aborted) {
        await result.execution.cancel();
        controller.signal.throwIfAborted();
      }
      const end = await Promise.race([finished, interactionFailed, observation.done.then(() => {
        controller.signal.throwIfAborted();
        throw new Error('Observation closed before the execution finished');
      })]);
      const outcome = end.id === result.execution.id ? end.outcome.toLowerCase()
        : (await result.execution.wait({ signal: controller.signal })).status;
      if (outcome !== 'succeeded' && outcome !== 'cancelled') throw new Error(`Execution ${outcome}`);
      writer.respondSessionPrompt(req.id, outcome === 'cancelled' ? 'cancelled' : 'end_turn');
    } catch (caught) {
      if (controller.signal.aborted || (caught instanceof Error && caught.name === 'AbortError')) {
        writer.respondSessionPrompt(req.id, 'cancelled');
      }
      else writer.respondError(req.id, JsonRpcErrorCode.InternalError,
        caught instanceof Error ? caught.message : String(caught));
    } finally {
      await observation?.close().catch(() => {});
      session.pendingPromptRequestId = null;
      session.streamAbort = null;
      session.execution = undefined;
    }
  }

  // ── session/cancel ──────────────────────────────────────────────────────────

  function handleSessionCancel(notification: SessionCancelNotification): void {
    const id = notification.params.sessionId;
    const execution = sessions.get(id)?.execution;
    sessions.cancelAll(id);
    void execution?.cancel().catch(error => process.stderr.write(`HPD execution cancellation failed: ${error}\n`));
  }

  // ── session/set_mode ────────────────────────────────────────────────────────

  function handleSetMode(req: { id: unknown }): void {
    writer.respondSessionSetMode(req.id as import('./types/acp.js').RequestId);
  }
}
