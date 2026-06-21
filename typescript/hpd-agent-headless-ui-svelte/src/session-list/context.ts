import { createContext } from 'svelte';
import type { SessionListItem } from '@hpd-research/hpd-agent-headless-ui';
import type {
  SessionListActions,
  SessionListRootContext,
} from './types.js';

export const [getSessionListRootContext, setSessionListRootContext] =
  createContext<SessionListRootContext>();

export const [getSessionListItemContext, setSessionListItemContext] =
  createContext<SessionListItemContext>();

export interface SessionListItemContext {
  actions: SessionListActions;
  item: SessionListItem;
  index: number;
}
