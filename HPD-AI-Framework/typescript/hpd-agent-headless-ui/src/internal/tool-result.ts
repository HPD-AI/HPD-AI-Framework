import type { ToolResultPayload } from '@hpd-research/hpd-agent-client';

export function formatToolResultPayload(result: ToolResultPayload): string {
  if (result.text) return result.text;
  if (result.json !== undefined) return JSON.stringify(result.json);
  if (result.content && result.content.length > 0) return JSON.stringify(result.content);
  return '';
}
