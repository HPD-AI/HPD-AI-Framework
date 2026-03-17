import type { RequestId } from '../types/acp.js';

export type PendingResolver<T> = (value: T) => void;
export type PendingRejecter = (reason: Error) => void;

export interface PendingOutboundRequest<T> {
  resolve: PendingResolver<T>;
  reject: PendingRejecter;
}

/**
 * Per-ACP-session state held by the bridge for the lifetime of the session.
 */
export interface AcpSessionState {
  /** ACP session ID assigned by this bridge on session/new */
  acpSessionId: string;
  /** HPD server session ID */
  hpdSessionId: string;
  /** HPD server branch ID (always 'main' for new sessions) */
  hpdBranchId: string;
  /** Working directory declared by the editor on session/new */
  cwd: string;

  /**
   * The request id of the currently in-flight session/prompt.
   * Set when the prompt starts; cleared when MESSAGE_TURN_FINISHED or error.
   */
  pendingPromptRequestId: RequestId | null;

  /**
   * AbortController for the active HPD stream.
   * Aborted when session/cancel arrives.
   */
  streamAbort: AbortController | null;

  /**
   * Pending permission resolvers keyed by HPD permissionId.
   * The bridge sends session/request_permission to the editor and waits.
   */
  pendingPermissions: Map<string, PendingOutboundRequest<{ approved: boolean; optionId?: string }>>;

  /**
   * Pending client-tool resolvers keyed by HPD requestId.
   * Keyed by the outbound JSON-RPC request id (number) as a string so
   * we can look up by the id in the editor's response.
   */
  pendingClientTools: Map<string, PendingOutboundRequest<ClientToolResult>>;

  /**
   * Active clarification, if any.
   * There can be at most one outstanding clarification at a time.
   */
  pendingClarification: PendingClarification | null;
}

export interface ClientToolResult {
  success: boolean;
  content: string;
  errorMessage?: string;
}

export interface PendingClarification {
  hpdRequestId: string;
  resolve: PendingResolver<string>;
  reject: PendingRejecter;
}

/**
 * Registry of all active ACP sessions for one connection.
 * One ACP connection (stdio) can carry multiple concurrent sessions.
 */
export class SessionRegistry {
  readonly #sessions = new Map<string, AcpSessionState>();

  create(hpdSessionId: string, hpdBranchId: string, cwd: string): AcpSessionState {
    // Use the HPD session ID as the ACP session ID so it survives bridge restarts.
    // acpx stores and re-sends the session ID on reconnect — if we made up an opaque
    // ID it would be lost when the process exits. HPD session IDs are stable GUIDs.
    const acpSessionId = hpdSessionId;
    const state: AcpSessionState = {
      acpSessionId,
      hpdSessionId,
      hpdBranchId,
      cwd,
      pendingPromptRequestId: null,
      streamAbort: null,
      pendingPermissions: new Map(),
      pendingClientTools: new Map(),
      pendingClarification: null,
    };
    this.#sessions.set(acpSessionId, state);
    return state;
  }

  get(acpSessionId: string): AcpSessionState | undefined {
    return this.#sessions.get(acpSessionId);
  }

  delete(acpSessionId: string): void {
    this.#sessions.delete(acpSessionId);
  }

  /** Called when the editor's response to an agent→client request arrives. */
  resolveOutboundRequest(outboundRequestId: RequestId, result: unknown, error?: { code: number; message: string }): boolean {
    const key = String(outboundRequestId);
    for (const session of this.#sessions.values()) {
      // Check pending permissions (keyed by HPD permissionId, stored under outbound request id)
      const permission = session.pendingPermissions.get(key);
      if (permission) {
        if (error) {
          permission.reject(new Error(`${error.code}: ${error.message}`));
        } else {
          const r = result as { outcome: { outcome: string; optionId?: string } };
          permission.resolve({
            approved: r.outcome.outcome === 'selected',
            optionId: r.outcome.optionId,
          });
        }
        session.pendingPermissions.delete(key);
        return true;
      }

      // Check pending client tools
      const tool = session.pendingClientTools.get(key);
      if (tool) {
        if (error) {
          tool.reject(new Error(`${error.code}: ${error.message}`));
        } else {
          tool.resolve(result as ClientToolResult);
        }
        session.pendingClientTools.delete(key);
        return true;
      }
    }
    return false;
  }

  cancelAll(acpSessionId: string): void {
    const session = this.#sessions.get(acpSessionId);
    if (!session) return;

    session.streamAbort?.abort();

    const cancelError = new Error('Session cancelled');
    for (const p of session.pendingPermissions.values()) p.reject(cancelError);
    for (const t of session.pendingClientTools.values()) t.reject(cancelError);
    session.pendingClarification?.reject(cancelError);

    session.pendingPermissions.clear();
    session.pendingClientTools.clear();
    session.pendingClarification = null;
    session.streamAbort = null;
  }
}
