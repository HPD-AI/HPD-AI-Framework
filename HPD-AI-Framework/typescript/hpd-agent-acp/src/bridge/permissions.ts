import type { PermissionRequestEvent, PermissionResponse } from '@hpd/hpd-agent-client';
import type { AcpSessionState } from './session.js';
import type { AcpWriter } from '../acp/writer.js';
import { toolNameToKind } from './tools.js';

/**
 * Handles the PERMISSION_REQUEST → session/request_permission round-trip.
 *
 * Flow:
 *   HPD emits PERMISSION_REQUEST
 *   → bridge sends session/request_permission to editor (JSON-RPC request)
 *   → editor responds with outcome
 *   → bridge resolves the HPD permission promise
 *   → HPD stream resumes
 */
export function handlePermissionRequest(
  event: PermissionRequestEvent,
  session: AcpSessionState,
  writer: AcpWriter,
): Promise<PermissionResponse> {
  return new Promise((resolve, reject) => {
    const toolCall = {
      toolCallId: event.callId ?? event.permissionId,
      title: event.functionName,
      kind: toolNameToKind(event.functionName),
      status: 'pending' as const,
      rawInput: event.arguments,
    };

    const options = [
      { optionId: 'allow_once',   name: 'Allow once',   kind: 'allow_once'   as const },
      { optionId: 'allow_always', name: 'Allow always', kind: 'allow_always' as const },
      { optionId: 'reject_once',  name: 'Deny',         kind: 'reject_once'  as const },
    ];

    const outboundId = writer.requestPermission(session.acpSessionId, toolCall, options);
    const key = String(outboundId);

    session.pendingPermissions.set(key, {
      resolve: ({ approved, optionId }) => {
        const choice = optionId === 'allow_always'
          ? 'allow_always'
          : optionId === 'reject_once'
            ? 'deny_always'    // closest HPD mapping
            : 'ask';
        resolve({ approved, choice });
      },
      reject,
    });
  });
}
