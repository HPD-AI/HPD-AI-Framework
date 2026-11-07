import { describe, expect, it, vi } from 'vitest';
import { PassThrough } from 'node:stream';
import type { PermissionRequestEvent } from '@hpd-research/hpd-agent-client';
import { handlePermissionRequest } from '../../src/bridge/permissions.js';
import { AcpWriter } from '../../src/acp/writer.js';
import { SessionRegistry } from '../../src/bridge/session.js';

function setup() {
  const writer = new AcpWriter(new PassThrough());
  const outbound = vi.spyOn(writer, 'requestPermission');
  const session = new SessionRegistry().create('session', 'main', '/workspace');
  const event: PermissionRequestEvent = {
    type: 'PERMISSION_REQUEST', permissionId: 'permission', callId: 'call', sourceName: 'gate',
    permissionDomain: 'ToolExecution', authority: 'commands/run', reason: 'Review this command',
    functionName: 'run_command', action: 'run', arguments: { command: 'npm test' },
    evidence: { kind: 'command', schemaVersion: '1', payload: { command: 'git status' } },
    choices: [
      { id: 'custom-allow-pattern', label: 'Allow this pattern', kind: 'Allow' },
      { id: 'custom-decline', label: 'Decline', kind: 'Deny' },
      { id: 'custom-feedback', label: 'Provide feedback', kind: 'Feedback' },
    ],
  };
  const answer = (approved: boolean, optionId?: string) => {
    const pending = [...session.pendingPermissions.values()][0];
    if (!pending) throw new Error('No pending ACP request');
    pending.resolve({ approved, optionId });
  };
  return { writer, outbound, session, event, answer };
}

describe('permission bridge', () => {
  it('renders declared options and preserves custom choice identity', async () => {
    const f = setup();
    const result = handlePermissionRequest(f.event, f.session, f.writer);
    expect(f.outbound).toHaveBeenCalledWith(f.session.acpSessionId,
      expect.objectContaining({
        toolCallId: 'call',
        title: f.event.reason,
        rawInput: expect.objectContaining({
          functionName: 'run_command',
          action: 'run',
          arguments: { command: 'npm test' },
        }),
      }),
      f.event.choices.map(choice => ({ optionId: choice.id, name: choice.label,
        kind: choice.kind === 'Allow' ? 'allow_once' : 'reject_once' })));
    f.answer(true, 'custom-allow-pattern');
    await expect(result).resolves.toEqual({ choiceId: 'custom-allow-pattern' });
  });
  it('uses the first declared Deny for cancellation', async () => {
    const f = setup();
    const result = handlePermissionRequest(f.event, f.session, f.writer);
    f.answer(false);
    await expect(result).resolves.toEqual({ choiceId: 'custom-decline' });
  });
  it('rejects a choice outside the active request', async () => {
    const f = setup();
    const result = handlePermissionRequest(f.event, f.session, f.writer);
    f.answer(true, 'undeclared');
    await expect(result).rejects.toThrow('undeclared permission choice');
  });
  it('preserves feedback without translating it to a persistent denial', async () => {
    const f = setup();
    const result = handlePermissionRequest(f.event, f.session, f.writer);
    f.answer(true, 'custom-feedback');
    await expect(result).resolves.toEqual({ choiceId: 'custom-feedback' });
  });
  it('rejects a malformed request without a Deny choice', async () => {
    const f = setup();
    await expect(handlePermissionRequest({ ...f.event, choices: [f.event.choices[0]] }, f.session, f.writer))
      .rejects.toThrow('no denial choice');
  });
});
