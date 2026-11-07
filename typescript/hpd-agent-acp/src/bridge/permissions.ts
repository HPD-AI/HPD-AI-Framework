import type { PermissionRequestEvent } from '@hpd-research/hpd-agent-client';
import type { AcpSessionState } from './session.js';
import type { AcpWriter } from '../acp/writer.js';

export function handlePermissionRequest(event: PermissionRequestEvent, session: AcpSessionState,
  writer: AcpWriter): Promise<{ choiceId: string }> {
  return new Promise((resolve, reject) => {
    const deny = event.choices.find(choice => choice.kind === 'Deny');
    if (!deny) { reject(new Error('Permission request has no denial choice')); return; }
    const toolCall = {
      toolCallId: event.callId,
      title: event.reason ?? event.functionName ?? event.authority ?? 'Approval required',
      kind: 'other' as const,
      status: 'pending' as const,
      rawInput: {
        functionName: event.functionName,
        action: event.action,
        arguments: event.arguments,
        evidence: event.evidence?.payload,
      },
    };
    const options = event.choices.map(choice => ({
      optionId: choice.id, name: choice.label,
      kind: choice.kind === 'Allow' ? 'allow_once' as const : 'reject_once' as const,
    }));
    const outboundId = writer.requestPermission(session.acpSessionId, toolCall, options);
    session.pendingPermissions.set(String(outboundId), {
      resolve: ({ approved, optionId }) => {
        const choice = approved ? event.choices.find(candidate => candidate.id === optionId) : deny;
        if (!choice) { reject(new Error('Editor selected an undeclared permission choice')); return; }
        resolve({ choiceId: choice.id });
      }, reject,
    });
  });
}
