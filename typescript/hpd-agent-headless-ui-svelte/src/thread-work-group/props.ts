import type {
  ThreadWorkGroup,
  ThreadWorkPart,
} from '@hpd-research/hpd-agent-headless-ui';
import type {
  ThreadWorkGroupElementProps,
  ThreadWorkPartElementProps,
  ThreadWorkPartsElementProps,
  ThreadWorkPartsState,
} from './types.js';

export function createThreadWorkGroupElementProps(
  work: ThreadWorkGroup,
  restProps: Record<string, unknown> = {},
): ThreadWorkGroupElementProps {
  return {
    ...restProps,
    open: work.openByDefault || undefined,
    'aria-label': work.label,
    'data-hpd-thread-work-group': '',
    'data-work-id': work.id,
    'data-work-status': work.status,
    'data-open-by-default': work.openByDefault ? '' : undefined,
  } as ThreadWorkGroupElementProps;
}

export function getVisibleThreadWorkParts(
  work: ThreadWorkGroup,
  showFinalDraft = false,
): ThreadWorkPart[] {
  return work.parts.filter((part) =>
    showFinalDraft ||
    part.type !== 'assistant-draft' ||
    work.status === 'working' ||
    part.message.id !== work.finalMessageId);
}

export function createThreadWorkPartsState(
  work: ThreadWorkGroup,
  parts: ThreadWorkPart[],
): ThreadWorkPartsState {
  return {
    empty: parts.length === 0,
    parts,
    status: work.status,
    work,
  };
}

export function createThreadWorkPartsElementProps(
  parts: ThreadWorkPart[],
  restProps: Record<string, unknown> = {},
): ThreadWorkPartsElementProps {
  return {
    ...restProps,
    'data-hpd-thread-work-parts': '',
    'data-empty': parts.length === 0 ? '' : undefined,
  } as ThreadWorkPartsElementProps;
}

export function createThreadWorkPartElementProps(
  part: ThreadWorkPart,
  restProps: Record<string, unknown> = {},
): ThreadWorkPartElementProps {
  return {
    ...restProps,
    'data-hpd-thread-work-part': '',
    'data-work-part-type': part.type,
    'data-tool-id': part.type === 'tool' ? part.tool.callId : undefined,
    'data-tool-status': part.type === 'tool' ? part.tool.status : undefined,
  } as ThreadWorkPartElementProps;
}

export function formatThreadWorkPartValue(value: unknown): string {
  if (value === undefined || value === null) return '';
  if (typeof value === 'string') return value;
  try {
    return JSON.stringify(value, null, 2);
  } catch {
    return String(value);
  }
}
