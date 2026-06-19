import type { Session } from '@hpd-research/hpd-agent-client';
import type {
  SessionLabelSelector,
  SessionListItem,
  SessionSortDirection,
  SessionSortField,
  SessionSubtitleSelector,
} from './types.js';

export function createSessionListItems(options: {
  sessions: Session[];
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
    metadata: session.metadata ?? {},
  }));
}

export function getSessionLabel(session: Session, selector?: SessionLabelSelector): string {
  const selected = selector?.(session);
  if (selected && selected.trim().length > 0) return selected;

  const metadataName = readSessionMetadataString(session, 'name')
    ?? readSessionMetadataString(session, 'title')
    ?? readSessionMetadataString(session, 'hpdos.name')
    ?? readSessionMetadataString(session, 'hpdos.title');

  return metadataName ?? session.id.slice(0, 16);
}

export function getSessionSubtitle(
  session: Session,
  selector?: SessionSubtitleSelector,
): string | null {
  const selected = selector?.(session);
  if (selected !== undefined) return selected;

  return readSessionMetadataString(session, 'description')
    ?? readSessionMetadataString(session, 'hpdos.description')
    ?? session.id;
}

export function readSessionMetadataString(session: Session, key: string): string | null {
  const value = session.metadata?.[key];
  return typeof value === 'string' && value.trim().length > 0 ? value : null;
}

export function sortSessions(
  sessions: Session[],
  field: SessionSortField = 'lastActivity',
  direction: SessionSortDirection = 'desc',
): Session[] {
  const multiplier = direction === 'asc' ? 1 : -1;
  return [...sessions].sort((a, b) => {
    const left = Date.parse(a[field] ?? '') || 0;
    const right = Date.parse(b[field] ?? '') || 0;
    return (left - right) * multiplier;
  });
}
