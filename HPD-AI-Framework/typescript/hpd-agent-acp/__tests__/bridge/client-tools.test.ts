/**
 * Unit tests for bridge/client-tools.ts — CLIENT_TOOL_INVOKE_REQUEST handling.
 *
 * What these tests cover:
 *   capsToToolKits builds the correct clientToolKits array from ACP clientCapabilities.
 *   handleClientToolInvoke routes editor_read_file / editor_write_file /
 *   editor_run_command to the correct ACP fs/* or terminal/* requests, relays
 *   results back to HPD, handles capability-not-declared errors, and resolves
 *   relative paths against session.cwd.
 *
 * Test type: unit — AcpWriter replaced by a mock; outbound responses are
 * simulated by resolving pendingClientTools directly.
 */

import { describe, it, expect, vi } from 'vitest';
import { PassThrough } from 'node:stream';
import { capsToToolKits, handleClientToolInvoke } from '../../src/bridge/client-tools.js';
import { AcpWriter } from '../../src/acp/writer.js';
import { SessionRegistry } from '../../src/bridge/session.js';
import type { AcpClientCapabilities } from '../../src/types/acp.js';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeWriter() {
  const out = new PassThrough();
  return new AcpWriter(out);
}

function makeSession(cwd = '/project') {
  const registry = new SessionRegistry();
  return registry.create('hpd-1', 'branch-1', cwd);
}

function makeInvokeRequest(toolName: string, args: Record<string, unknown> = {}) {
  return {
    type: 'CLIENT_TOOL_INVOKE_REQUEST' as const,
    requestId: 'req-1',
    toolName,
    arguments: args,
    version: '1.0',
  };
}

/** Simulate the editor returning a successful result for the most recent outbound request. */
function resolveLastOutbound(
  session: ReturnType<typeof makeSession>,
  result: { success: boolean; content: string },
) {
  const entries = [...session.pendingClientTools.entries()];
  if (entries.length === 0) throw new Error('No pending client tools');
  const [key, pending] = entries[entries.length - 1]!;
  pending.resolve(result);
  session.pendingClientTools.delete(key);
}

// ---------------------------------------------------------------------------
// capsToToolKits
// ---------------------------------------------------------------------------

describe('capsToToolKits', () => {
  it('empty caps → empty array', () => {
    expect(capsToToolKits({})).toEqual([]);
  });

  it('fs.readTextFile only → toolkit with editor_read_file', () => {
    const kits = capsToToolKits({ fs: { readTextFile: true } });

    expect(kits).toHaveLength(1);
    expect(kits[0]!.tools.some((t) => t.name === 'editor_read_file')).toBe(true);
  });

  it('fs.writeTextFile only → toolkit with editor_write_file', () => {
    const kits = capsToToolKits({ fs: { writeTextFile: true } });

    expect(kits[0]!.tools.some((t) => t.name === 'editor_write_file')).toBe(true);
  });

  it('terminal only → toolkit with editor_run_command', () => {
    const kits = capsToToolKits({ terminal: true });

    expect(kits[0]!.tools.some((t) => t.name === 'editor_run_command')).toBe(true);
  });

  it('all caps → toolkit with three tools', () => {
    const kits = capsToToolKits({ fs: { readTextFile: true, writeTextFile: true }, terminal: true });

    expect(kits[0]!.tools).toHaveLength(3);
  });

  it('startCollapsed is always false', () => {
    const kits = capsToToolKits({ fs: { readTextFile: true } });

    expect(kits[0]!.startCollapsed).toBe(false);
  });

  it('toolkit name is "editor"', () => {
    const kits = capsToToolKits({ terminal: true });

    expect(kits[0]!.name).toBe('editor');
  });
});

// ---------------------------------------------------------------------------
// handleClientToolInvoke — unknown tool
// ---------------------------------------------------------------------------

describe('handleClientToolInvoke — unknown tool', () => {
  it('returns error response for unrecognised tool name', async () => {
    const writer = makeWriter();
    const session = makeSession();

    const result = await handleClientToolInvoke(
      makeInvokeRequest('mystery_tool'),
      session,
      writer,
      {},
    );

    expect(result.success).toBe(false);
    expect(result.errorMessage).toContain('mystery_tool');
  });
});

// ---------------------------------------------------------------------------
// handleClientToolInvoke — file read
// ---------------------------------------------------------------------------

describe('handleClientToolInvoke — editor_read_file', () => {
  it('returns error when fs.readTextFile capability is not declared', async () => {
    const writer = makeWriter();
    const session = makeSession();

    const result = await handleClientToolInvoke(
      makeInvokeRequest('editor_read_file', { path: '/foo.ts' }),
      session,
      writer,
      {},
    );

    expect(result.success).toBe(false);
    expect(result.errorMessage).toContain('fs/read_text_file');
  });

  it('sends fs/read_text_file and returns file content on success', async () => {
    const writer = makeWriter();
    const session = makeSession();
    const spy = vi.spyOn(writer, 'requestFsRead');
    const caps: AcpClientCapabilities = { fs: { readTextFile: true } };

    const promise = handleClientToolInvoke(
      makeInvokeRequest('editor_read_file', { path: '/foo.ts' }),
      session,
      writer,
      caps,
    );

    expect(spy).toHaveBeenCalledOnce();
    resolveLastOutbound(session, { success: true, content: 'file data' });

    const result = await promise;
    expect(result.success).toBe(true);
  });

  it('resolves relative path against session.cwd', async () => {
    const writer = makeWriter();
    const session = makeSession('/project');
    const spy = vi.spyOn(writer, 'requestFsRead');
    const caps: AcpClientCapabilities = { fs: { readTextFile: true } };

    const promise = handleClientToolInvoke(
      makeInvokeRequest('editor_read_file', { path: 'src/foo.ts' }),
      session,
      writer,
      caps,
    );

    resolveLastOutbound(session, { success: true, content: '' });
    await promise;

    expect(spy.mock.calls[0]![1]).toBe('/project/src/foo.ts');
  });

  it('does not modify absolute paths', async () => {
    const writer = makeWriter();
    const session = makeSession('/project');
    const spy = vi.spyOn(writer, 'requestFsRead');
    const caps: AcpClientCapabilities = { fs: { readTextFile: true } };

    const promise = handleClientToolInvoke(
      makeInvokeRequest('editor_read_file', { path: '/abs/path.ts' }),
      session,
      writer,
      caps,
    );

    resolveLastOutbound(session, { success: true, content: '' });
    await promise;

    expect(spy.mock.calls[0]![1]).toBe('/abs/path.ts');
  });
});

// ---------------------------------------------------------------------------
// handleClientToolInvoke — file write
// ---------------------------------------------------------------------------

describe('handleClientToolInvoke — editor_write_file', () => {
  it('returns error when fs.writeTextFile capability is not declared', async () => {
    const writer = makeWriter();
    const session = makeSession();

    const result = await handleClientToolInvoke(
      makeInvokeRequest('editor_write_file', { path: '/out.ts', content: 'x' }),
      session,
      writer,
      {},
    );

    expect(result.success).toBe(false);
    expect(result.errorMessage).toContain('fs/write_text_file');
  });

  it('sends fs/write_text_file and returns success', async () => {
    const writer = makeWriter();
    const session = makeSession();
    const spy = vi.spyOn(writer, 'requestFsWrite');
    const caps: AcpClientCapabilities = { fs: { writeTextFile: true } };

    const promise = handleClientToolInvoke(
      makeInvokeRequest('editor_write_file', { path: '/out.ts', content: 'hello' }),
      session,
      writer,
      caps,
    );

    expect(spy).toHaveBeenCalledOnce();
    expect(spy.mock.calls[0]![2]).toBe('hello');
    resolveLastOutbound(session, { success: true, content: '' });

    const result = await promise;
    expect(result.success).toBe(true);
  });
});

// ---------------------------------------------------------------------------
// handleClientToolInvoke — terminal
// ---------------------------------------------------------------------------

describe('handleClientToolInvoke — editor_run_command', () => {
  it('returns error when terminal capability is not declared', async () => {
    const writer = makeWriter();
    const session = makeSession();

    const result = await handleClientToolInvoke(
      makeInvokeRequest('editor_run_command', { command: 'ls' }),
      session,
      writer,
      {},
    );

    expect(result.success).toBe(false);
    expect(result.errorMessage).toContain('terminal');
  });

  it('sends terminal/create, wait_for_exit, output, release in sequence', async () => {
    const writer = makeWriter();
    const session = makeSession();
    const createSpy  = vi.spyOn(writer, 'requestTerminalCreate');
    const waitSpy    = vi.spyOn(writer, 'requestTerminalWaitForExit');
    const outputSpy  = vi.spyOn(writer, 'requestTerminalOutput');
    const releaseSpy = vi.spyOn(writer, 'requestTerminalRelease');
    const caps: AcpClientCapabilities = { terminal: true };

    const promise = handleClientToolInvoke(
      makeInvokeRequest('editor_run_command', { command: 'echo hi' }),
      session,
      writer,
      caps,
    );

    // Step 1: create → respond with terminalId
    expect(createSpy).toHaveBeenCalledOnce();
    resolveLastOutbound(session, { success: true, content: 'term-1' });

    await new Promise((r) => setTimeout(r, 5));

    // Step 2: wait_for_exit
    expect(waitSpy).toHaveBeenCalledOnce();
    resolveLastOutbound(session, { success: true, content: '' });

    await new Promise((r) => setTimeout(r, 5));

    // Step 3: output
    expect(outputSpy).toHaveBeenCalledOnce();
    resolveLastOutbound(session, { success: true, content: 'echo hi\n' });

    const result = await promise;

    expect(result.success).toBe(true);
    // Step 4: release (fire-and-forget, just verify it was called)
    expect(releaseSpy).toHaveBeenCalledOnce();
  });

  it('uses session.cwd as default working directory', async () => {
    const writer = makeWriter();
    const session = makeSession('/my/project');
    const createSpy = vi.spyOn(writer, 'requestTerminalCreate');
    const caps: AcpClientCapabilities = { terminal: true };

    const promise = handleClientToolInvoke(
      makeInvokeRequest('editor_run_command', { command: 'ls' }),
      session,
      writer,
      caps,
    );

    // resolve create, wait, output
    resolveLastOutbound(session, { success: true, content: 'term-1' });
    await new Promise((r) => setTimeout(r, 5));
    resolveLastOutbound(session, { success: true, content: '' });
    await new Promise((r) => setTimeout(r, 5));
    resolveLastOutbound(session, { success: true, content: 'output' });

    await promise;

    // cwd param is the 5th arg of requestTerminalCreate (0-indexed: sessionId, command, args, env, cwd)
    expect(createSpy.mock.calls[0]![4]).toBe('/my/project');
  });

  it('uses explicit cwd from arguments when provided', async () => {
    const writer = makeWriter();
    const session = makeSession('/default');
    const createSpy = vi.spyOn(writer, 'requestTerminalCreate');
    const caps: AcpClientCapabilities = { terminal: true };

    const promise = handleClientToolInvoke(
      makeInvokeRequest('editor_run_command', { command: 'ls', cwd: '/explicit' }),
      session,
      writer,
      caps,
    );

    resolveLastOutbound(session, { success: true, content: 'term-1' });
    await new Promise((r) => setTimeout(r, 5));
    resolveLastOutbound(session, { success: true, content: '' });
    await new Promise((r) => setTimeout(r, 5));
    resolveLastOutbound(session, { success: true, content: 'output' });

    await promise;

    expect(createSpy.mock.calls[0]![4]).toBe('/explicit');
  });
});
