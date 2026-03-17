import type { ClientToolInvokeRequestEvent, ClientToolInvokeResponse, clientToolKitDefinition } from '@hpd/hpd-agent-client';
import { createTextResult } from '@hpd/hpd-agent-client';
import type { AcpSessionState, ClientToolResult } from './session.js';
import type { AcpWriter } from '../acp/writer.js';
import type { AcpClientCapabilities } from '../types/acp.js';

/**
 * Builds the clientToolKits array to pass into each stream() call,
 * based on what the editor declared during ACP initialize.
 * This is what tells the agent which client-side tools it can call.
 */
export function capsToToolKits(caps: AcpClientCapabilities): clientToolKitDefinition[] {
  const tools: clientToolKitDefinition['tools'] = [];

  if (caps.fs?.readTextFile) {
    tools.push({
      name: 'editor_read_file',
      description: 'Read the contents of a file in the editor workspace.',
      parametersSchema: {
        type: 'object',
        properties: {
          path:  { type: 'string', description: 'File path (absolute or relative to the workspace root)' },
          line:  { type: 'number', description: 'First line to read (1-based, optional)' },
          limit: { type: 'number', description: 'Maximum number of lines to return (optional)' },
        },
        required: ['path'],
      },
    });
  }

  if (caps.fs?.writeTextFile) {
    tools.push({
      name: 'editor_write_file',
      description: 'Write or overwrite a file in the editor workspace.',
      parametersSchema: {
        type: 'object',
        properties: {
          path:    { type: 'string', description: 'File path (absolute or relative to the workspace root)' },
          content: { type: 'string', description: 'Full file content to write' },
        },
        required: ['path', 'content'],
      },
    });
  }

  if (caps.terminal) {
    tools.push({
      name: 'editor_run_command',
      description: 'Run a shell command in the editor terminal and return its output.',
      parametersSchema: {
        type: 'object',
        properties: {
          command: { type: 'string', description: 'Shell command to execute' },
          cwd:     { type: 'string', description: 'Working directory (defaults to session cwd)' },
        },
        required: ['command'],
      },
    });
  }

  if (tools.length === 0) return [];

  return [{ name: 'editor', tools, startCollapsed: false }];
}

function ok(requestId: string, text: string): ClientToolInvokeResponse {
  return { requestId, content: createTextResult(text), success: true };
}

function err(requestId: string, message: string): ClientToolInvokeResponse {
  return { requestId, content: createTextResult(message), success: false, errorMessage: message };
}

/**
 * Handles CLIENT_TOOL_INVOKE_REQUEST events by forwarding the operation
 * to the editor via the appropriate ACP method (fs/* or terminal/*),
 * then relaying the result back to HPD.
 */
export function handleClientToolInvoke(
  request: ClientToolInvokeRequestEvent,
  session: AcpSessionState,
  writer: AcpWriter,
  clientCapabilities: AcpClientCapabilities,
): Promise<ClientToolInvokeResponse> {
  const { toolName } = request;

  if (isFileRead(toolName))  return handleFileRead(request, session, writer, clientCapabilities);
  if (isFileWrite(toolName)) return handleFileWrite(request, session, writer, clientCapabilities);
  if (isTerminalOp(toolName)) return handleTerminal(request, session, writer, clientCapabilities);

  return Promise.resolve(err(request.requestId, `Unknown client tool: ${toolName}`));
}

// ── File read ────────────────────────────────────────────────────────────────

function handleFileRead(
  request: ClientToolInvokeRequestEvent,
  session: AcpSessionState,
  writer: AcpWriter,
  caps: AcpClientCapabilities,
): Promise<ClientToolInvokeResponse> {
  if (!caps.fs?.readTextFile) {
    return Promise.resolve(err(request.requestId, 'Editor does not support fs/read_text_file'));
  }

  const path  = resolvePath(String(request.arguments['path'] ?? ''), session.cwd);
  const line  = request.arguments['line']  != null ? Number(request.arguments['line'])  : undefined;
  const limit = request.arguments['limit'] != null ? Number(request.arguments['limit']) : undefined;

  return waitForOutbound(session, () => String(writer.requestFsRead(session.acpSessionId, path, line, limit)))
    .then(
      (result) => ok(request.requestId, result.content),
      (e: Error) => err(request.requestId, e.message),
    );
}

// ── File write ───────────────────────────────────────────────────────────────

function handleFileWrite(
  request: ClientToolInvokeRequestEvent,
  session: AcpSessionState,
  writer: AcpWriter,
  caps: AcpClientCapabilities,
): Promise<ClientToolInvokeResponse> {
  if (!caps.fs?.writeTextFile) {
    return Promise.resolve(err(request.requestId, 'Editor does not support fs/write_text_file'));
  }

  const path    = resolvePath(String(request.arguments['path']    ?? ''), session.cwd);
  const content = String(request.arguments['content'] ?? '');

  return waitForOutbound(session, () => String(writer.requestFsWrite(session.acpSessionId, path, content)))
    .then(
      (_result) => ok(request.requestId, 'File written successfully'),
      (e: Error) => err(request.requestId, e.message),
    );
}

// ── Terminal ─────────────────────────────────────────────────────────────────

function handleTerminal(
  request: ClientToolInvokeRequestEvent,
  session: AcpSessionState,
  writer: AcpWriter,
  caps: AcpClientCapabilities,
): Promise<ClientToolInvokeResponse> {
  if (!caps.terminal) {
    return Promise.resolve(err(request.requestId, 'Editor does not support terminal operations'));
  }

  const command = String(request.arguments['command'] ?? '');
  const cwd     = request.arguments['cwd'] != null ? String(request.arguments['cwd']) : session.cwd;

  // Step 1: create terminal → get terminalId
  // Step 2: wait for exit
  // Step 3: get output → return it, release terminal
  return waitForOutbound(session, () =>
    String(writer.requestTerminalCreate(session.acpSessionId, command, undefined, undefined, cwd)),
  ).then((createResult) => {
    const terminalId = createResult.content;

    return waitForOutbound(session, () =>
      String(writer.requestTerminalWaitForExit(session.acpSessionId, terminalId)),
    ).then(() =>
      waitForOutbound(session, () =>
        String(writer.requestTerminalOutput(session.acpSessionId, terminalId)),
      ).then(
        (outputResult) => {
          writer.requestTerminalRelease(session.acpSessionId, terminalId);
          return ok(request.requestId, outputResult.content);
        },
        (e: Error) => err(request.requestId, e.message),
      ),
    );
  }).catch((e: Error) => err(request.requestId, e.message));
}

// ── Helpers ──────────────────────────────────────────────────────────────────

/** Resolve a path against cwd if it is not already absolute. */
function resolvePath(path: string, cwd: string): string {
  return path.startsWith('/') ? path : `${cwd}/${path}`;
}

function isFileRead(name: string): boolean {
  return name === 'editor_read_file';
}

function isFileWrite(name: string): boolean {
  return name === 'editor_write_file';
}

function isTerminalOp(name: string): boolean {
  return name === 'editor_run_command';
}

/**
 * Registers a pending outbound request on the session's `pendingClientTools` map.
 * `sender` must synchronously send the request and return its string key.
 */
function waitForOutbound(
  session: AcpSessionState,
  sender: () => string,
): Promise<ClientToolResult> {
  return new Promise<ClientToolResult>((resolve, reject) => {
    const key = sender();
    session.pendingClientTools.set(key, { resolve, reject });
  });
}
