import type { SessionDto } from '@hpd-research/hpd-agent-client';
import type {
  SessionLabelSelector,
  SessionListItem,
  SessionSortDirection,
  SessionSortField,
  SessionSubtitleSelector,
} from './types.js';

export function createSessionListItems(options: {
  sessions: SessionDto[];
  selectedSessionId: string | null;
  getLabel?: SessionLabelSelector;
  getSubtitle?: SessionSubtitleSelector;
}): SessionListItem[] {
  return options.sessions.map((session) => ({
    session,
    id: session.id,
    label: getSessionLabel(session, options.getLabel),
    subtitle: getSessionSubtitle(session, options.getSubtitle),
    selected: session.id === options.selectedSessionId,
    metadata: sessionMetadata(session),
  }));
}

export function getSessionLabel(session: SessionDto, selector?: SessionLabelSelector): string {
  const selected = selector?.(session);
  if (selected && selected.trim().length > 0) return selected;

  const metadataName = readSessionMetadataString(session, 'name')
    ?? readSessionMetadataString(session, 'title')
    ?? readSessionMetadataString(session, 'hpdos.name')
    ?? readSessionMetadataString(session, 'hpdos.title');

  return metadataName ?? session.id.slice(0, 16);
}

export function getSessionSubtitle(
  session: SessionDto,
  selector?: SessionSubtitleSelector,
): string | null {
  const selected = selector?.(session);
  if (selected !== undefined) return selected;

  return readSessionMetadataString(session, 'description')
    ?? readSessionMetadataString(session, 'hpdos.description')
    ?? session.id;
}

export function readSessionMetadataString(session: SessionDto, key: string): string | null {
  const value = sessionMetadata(session)[key];
  return typeof value === 'string' && value.trim().length > 0 ? value : null;
}

export function sortSessions(
  sessions: SessionDto[],
  field: SessionSortField = 'lastActivity',
  direction: SessionSortDirection = 'desc',
): SessionDto[] {
  const multiplier = direction === 'asc' ? 1 : -1;
  return [...sessions].sort((a, b) => {
    const left = Date.parse(a[field] ?? '') || 0;
    const right = Date.parse(b[field] ?? '') || 0;
    return (left - right) * multiplier;
  });
}

function sessionMetadata(session: SessionDto): Record<string, unknown> {
  const metadata = session.metadata;
  return typeof metadata === 'object' && metadata !== null && !Array.isArray(metadata)
    ? { ...metadata }
    : {};
}
