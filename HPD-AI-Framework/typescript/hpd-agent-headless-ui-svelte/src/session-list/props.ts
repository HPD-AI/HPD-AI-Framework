import { mergeProps } from '../thread-composer/index.js';
import type {
  SessionListActions,
  SessionListDeleteElementProps,
  SessionListItemElementProps,
  SessionListNewElementProps,
  SessionListRootElementProps,
  SessionListSubtitleElementProps,
  SessionListTitleElementProps,
} from './types.js';
import type { SessionListState } from '../session-state.js';
import type {
  SessionListItem,
  SessionListSnapshot,
} from '@hpd-research/hpd-agent-headless-ui';

export function createSessionListRootElementProps(
  snapshot: SessionListSnapshot,
  restProps: Record<string, unknown> = {},
): SessionListRootElementProps {
  return mergeProps(restProps, {
    'aria-busy': snapshot.loading,
    'data-empty': snapshot.empty ? '' : undefined,
    'data-hpd-session-list': '',
    'data-loading': snapshot.loading ? '' : undefined,
  }) as unknown as SessionListRootElementProps;
}

export function createSessionListItemElementProps(
  item: SessionListItem,
  disabled = false,
  restProps: Record<string, unknown> = {},
): SessionListItemElementProps {
  return mergeProps(restProps, {
    'aria-current': item.selected ? 'true' : undefined,
    'data-hpd-session-list-item': '',
    'data-selected': item.selected ? '' : undefined,
    'data-session-id': item.id,
    disabled,
    type: 'button',
  }) as unknown as SessionListItemElementProps;
}

export function createSessionListNewElementProps(
  snapshot: SessionListSnapshot,
  restProps: Record<string, unknown> = {},
): SessionListNewElementProps {
  return mergeProps(restProps, {
    'data-hpd-session-list-new': '',
    disabled: snapshot.loading,
    type: 'button',
  }) as unknown as SessionListNewElementProps;
}

export function createSessionListDeleteElementProps(
  item: SessionListItem | null,
  snapshot: SessionListSnapshot,
  restProps: Record<string, unknown> = {},
): SessionListDeleteElementProps {
  return mergeProps(restProps, {
    'data-hpd-session-list-delete': '',
    'data-session-id': item?.id,
    disabled: snapshot.loading || !item,
    type: 'button',
  }) as unknown as SessionListDeleteElementProps;
}

export function createSessionListTitleElementProps(
  _item: SessionListItem,
  restProps: Record<string, unknown> = {},
): SessionListTitleElementProps {
  return mergeProps(restProps, {
    'data-hpd-session-list-item-label': '',
  }) as unknown as SessionListTitleElementProps;
}

export function createSessionListSubtitleElementProps(
  _item: SessionListItem,
  restProps: Record<string, unknown> = {},
): SessionListSubtitleElementProps {
  return mergeProps(restProps, {
    'data-hpd-session-list-item-subtitle': '',
  }) as unknown as SessionListSubtitleElementProps;
}

export function createSessionListActions(sessionList: SessionListState): SessionListActions {
  return {
    refresh: sessionList.refresh,
    select: sessionList.select,
    create: sessionList.create,
    update: sessionList.update,
    delete: sessionList.delete,
    clearError: sessionList.clearError,
  };
}
