import type { Snippet } from 'svelte';
import type { SvelteHTMLElements } from 'svelte/elements';
import type {
  Session,
} from '@hpd-research/hpd-agent-client';
import type {
  SessionListItem,
  SessionListSnapshot,
} from '@hpd-research/hpd-agent-headless-ui';
import type { SessionListState } from '../session-state.js';

type DivProps = Omit<SvelteHTMLElements['div'], 'children'>;
type ButtonProps = Omit<SvelteHTMLElements['button'], 'children'>;
type SpanProps = Omit<SvelteHTMLElements['span'], 'children'>;

export interface SessionListRootElementProps extends DivProps {
  'aria-busy': boolean;
  'data-empty'?: '';
  'data-hpd-session-list': '';
  'data-loading'?: '';
}

export interface SessionListItemElementProps extends ButtonProps {
  'aria-current'?: 'true';
  'data-hpd-session-list-item': '';
  'data-selected'?: '';
  'data-session-id': string;
  disabled: boolean;
  type: 'button';
}

export interface SessionListNewElementProps extends ButtonProps {
  'data-hpd-session-list-new': '';
  disabled: boolean;
  type: 'button';
}

export interface SessionListDeleteElementProps extends ButtonProps {
  'data-hpd-session-list-delete': '';
  'data-session-id'?: string;
  disabled: boolean;
  type: 'button';
}

export interface SessionListTitleElementProps extends SpanProps {
  'data-hpd-session-list-item-label': '';
}

export interface SessionListSubtitleElementProps extends SpanProps {
  'data-hpd-session-list-item-subtitle': '';
}

export interface SessionListActions {
  refresh(): Promise<SessionListSnapshot>;
  select(sessionId: string | null): SessionListSnapshot;
  create: SessionListState['create'];
  update: SessionListState['update'];
  delete: SessionListState['delete'];
  clearError(): void;
}

export interface SessionListRootContext {
  actions: SessionListActions;
  props: SessionListRootElementProps;
  sessionList: SessionListState;
  snapshot: SessionListSnapshot;
}

export interface SessionListRootChildProps {
  actions: SessionListActions;
  props: SessionListRootElementProps;
  snapshot: SessionListSnapshot;
}

export interface SessionListItemsChildProps {
  actions: SessionListActions;
  snapshot: SessionListSnapshot;
}

export interface SessionListItemSnippetProps {
  actions: SessionListActions;
  item: SessionListItem;
  index: number;
  snapshot: SessionListSnapshot;
}

export interface SessionListItemChildProps extends SessionListItemSnippetProps {
  props: SessionListItemElementProps;
}

export interface SessionListEmptySnippetProps {
  actions: SessionListActions;
  snapshot: SessionListSnapshot;
}

export interface SessionListErrorSnippetProps {
  actions: SessionListActions;
  error: string;
  snapshot: SessionListSnapshot;
}

export interface SessionListNewChildProps {
  actions: SessionListActions;
  props: SessionListNewElementProps;
  snapshot: SessionListSnapshot;
}

export interface SessionListDeleteChildProps {
  actions: SessionListActions;
  item: SessionListItem | null;
  props: SessionListDeleteElementProps;
  snapshot: SessionListSnapshot;
}

export interface SessionListTitleChildProps {
  item: SessionListItem;
  props: SessionListTitleElementProps;
}

export interface SessionListSubtitleChildProps {
  item: SessionListItem;
  props: SessionListSubtitleElementProps;
}

export interface SessionListRootProps extends DivProps {
  children?: Snippet<[SessionListRootChildProps]>;
  sessionList: SessionListState;
}

export interface SessionListItemsProps {
  children?: Snippet<[SessionListItemsChildProps]>;
  empty?: Snippet<[SessionListEmptySnippetProps]>;
  error?: Snippet<[SessionListErrorSnippetProps]>;
  item?: Snippet<[SessionListItemSnippetProps]>;
}

export interface SessionListItemProps extends ButtonProps {
  children?: Snippet<[SessionListItemChildProps]>;
  index: number;
  item: SessionListItem;
  onSelect?: (item: SessionListItem) => void | Promise<void>;
}

export interface SessionListNewProps extends ButtonProps {
  children?: Snippet<[SessionListNewChildProps]>;
  metadata?: Record<string, unknown>;
  name?: string;
  onCreate?: (session: Session) => void | Promise<void>;
  select?: boolean;
  sessionId?: string;
}

export interface SessionListDeleteProps extends ButtonProps {
  children?: Snippet<[SessionListDeleteChildProps]>;
  item?: SessionListItem;
  onDelete?: (item: SessionListItem) => void | Promise<void>;
  selectFallback?: boolean;
}

export interface SessionListTitleProps extends SpanProps {
  children?: Snippet<[SessionListTitleChildProps]>;
}

export interface SessionListSubtitleProps extends SpanProps {
  children?: Snippet<[SessionListSubtitleChildProps]>;
}
