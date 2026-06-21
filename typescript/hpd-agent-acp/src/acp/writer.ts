import type {
  RequestId,
  JsonRpcResponse,
  JsonRpcError,
  SessionUpdateNotification,
  SessionUpdate,
  SessionRequestPermissionRequest,
  FsReadTextFileRequest,
  FsWriteTextFileRequest,
  TerminalCreateRequest,
  TerminalOutputRequest,
  TerminalWaitForExitRequest,
  TerminalKillRequest,
  TerminalReleaseRequest,
  AcpToolCall,
  AcpPermissionOption,
  InitializeResult,
  SessionNewResult,
  SessionPromptResult,
  StopReason,
} from '../types/acp.js';

/**
 * Writes newline-delimited JSON-RPC 2.0 messages to stdout.
 * Each write is a single complete JSON message followed by \n.
 */
export class AcpWriter {
  readonly #output: NodeJS.WritableStream;
  #requestCounter = 0;

  constructor(output: NodeJS.WritableStream = process.stdout) {
    this.#output = output;
  }

  // ── Helpers ──────────────────────────────────────────────────────────────

  #write(message: unknown): void {
    this.#output.write(JSON.stringify(message) + '\n');
  }

  #nextRequestId(): number {
    return ++this.#requestCounter;
  }

  // ── Responses (bridge → editor, in reply to editor requests) ─────────────

  respond<T>(id: RequestId, result: T): void {
    const msg: JsonRpcResponse<T> = { jsonrpc: '2.0', id, result };
    this.#write(msg);
  }

  respondError(id: RequestId, code: number, message: string, data?: unknown): void {
    const msg: JsonRpcError = { jsonrpc: '2.0', id, error: { code, message, data } };
    this.#write(msg);
  }

  // ── Typed response helpers ────────────────────────────────────────────────

  respondInitialize(id: RequestId, result: InitializeResult): void {
    this.respond(id, result);
  }

  respondAuthenticate(id: RequestId): void {
    this.respond(id, {});
  }

  respondSessionNew(id: RequestId, result: SessionNewResult): void {
    this.respond(id, result);
  }

  respondSessionLoad(id: RequestId): void {
    this.respond(id, null);
  }

  respondSessionPrompt(id: RequestId, stopReason: StopReason): void {
    const result: SessionPromptResult = { stopReason };
    this.respond(id, result);
  }

  respondSessionSetMode(id: RequestId): void {
    this.respond(id, null);
  }

  // ── Notifications (bridge → editor, no response expected) ─────────────────

  notifySessionUpdate(sessionId: string, update: SessionUpdate): void {
    const msg: SessionUpdateNotification = {
      jsonrpc: '2.0',
      method: 'session/update',
      params: { sessionId, update },
    };
    this.#write(msg);
  }

  // ── Agent → client requests (bridge → editor, response expected) ──────────
  // These return the request id so the caller can correlate the response.

  requestPermission(
    sessionId: string,
    toolCall: AcpToolCall,
    options?: AcpPermissionOption[],
  ): number {
    const id = this.#nextRequestId();
    const msg: SessionRequestPermissionRequest = {
      jsonrpc: '2.0',
      id,
      method: 'session/request_permission',
      params: { sessionId, toolCall, options },
    };
    this.#write(msg);
    return id;
  }

  requestFsRead(
    sessionId: string,
    path: string,
    line?: number,
    limit?: number,
  ): number {
    const id = this.#nextRequestId();
    const msg: FsReadTextFileRequest = {
      jsonrpc: '2.0',
      id,
      method: 'fs/read_text_file',
      params: { sessionId, path, line, limit },
    };
    this.#write(msg);
    return id;
  }

  requestFsWrite(sessionId: string, path: string, content: string): number {
    const id = this.#nextRequestId();
    const msg: FsWriteTextFileRequest = {
      jsonrpc: '2.0',
      id,
      method: 'fs/write_text_file',
      params: { sessionId, path, content },
    };
    this.#write(msg);
    return id;
  }

  requestTerminalCreate(
    sessionId: string,
    command: string,
    args?: string[],
    env?: Array<{ name: string; value: string }>,
    cwd?: string,
    outputByteLimit?: number,
  ): number {
    const id = this.#nextRequestId();
    const msg: TerminalCreateRequest = {
      jsonrpc: '2.0',
      id,
      method: 'terminal/create',
      params: { sessionId, command, args, env, cwd, outputByteLimit },
    };
    this.#write(msg);
    return id;
  }

  requestTerminalOutput(sessionId: string, terminalId: string): number {
    const id = this.#nextRequestId();
    const msg: TerminalOutputRequest = {
      jsonrpc: '2.0',
      id,
      method: 'terminal/output',
      params: { sessionId, terminalId },
    };
    this.#write(msg);
    return id;
  }

  requestTerminalWaitForExit(sessionId: string, terminalId: string): number {
    const id = this.#nextRequestId();
    const msg: TerminalWaitForExitRequest = {
      jsonrpc: '2.0',
      id,
      method: 'terminal/wait_for_exit',
      params: { sessionId, terminalId },
    };
    this.#write(msg);
    return id;
  }

  requestTerminalKill(sessionId: string, terminalId: string): number {
    const id = this.#nextRequestId();
    const msg: TerminalKillRequest = {
      jsonrpc: '2.0',
      id,
      method: 'terminal/kill',
      params: { sessionId, terminalId },
    };
    this.#write(msg);
    return id;
  }

  requestTerminalRelease(sessionId: string, terminalId: string): number {
    const id = this.#nextRequestId();
    const msg: TerminalReleaseRequest = {
      jsonrpc: '2.0',
      id,
      method: 'terminal/release',
      params: { sessionId, terminalId },
    };
    this.#write(msg);
    return id;
  }
}
