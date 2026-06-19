import type {
  AgentClient,
  CreateSessionRequest,
  SearchSessionsRequest,
  Session,
  UpdateSessionRequest,
} from '@hpd-research/hpd-agent-client';

export type SessionSortField = 'createdAt' | 'lastActivity';
export type SessionSortDirection = 'asc' | 'desc';

export interface SessionListScope {
  agentId?: string;
}

export interface SessionListControllerOptions extends SessionListScope {
  client: AgentClient;
  search?: SearchSessionsRequest;
  selectedSessionId?: string | null;
  autoSelect?: boolean;
  getLabel?: SessionLabelSelector;
  getSubtitle?: SessionSubtitleSelector;
}

export interface SessionListLoadOptions {
  search?: SearchSessionsRequest;
  preserveSelection?: boolean;
  signal?: AbortSignal;
}

export interface SessionListCreateOptions extends CreateSessionRequest {
  select?: boolean;
  signal?: AbortSignal;
}

export interface SessionListUpdateOptions {
  signal?: AbortSignal;
}

export interface SessionListDeleteOptions {
  selectFallback?: boolean;
  signal?: AbortSignal;
}

export interface SessionListSnapshot {
  sessions: Session[];
  items: SessionListItem[];
  selectedSessionId: string | null;
  selectedSession: Session | null;
  loading: boolean;
  error: string | null;
  search: SearchSessionsRequest;
  empty: boolean;
}

export interface SessionListItem {
  session: Session;
  id: string;
  label: string;
  subtitle: string | null;
  selected: boolean;
  metadata: Record<string, unknown>;
}

export type SessionListSubscriber = (snapshot: SessionListSnapshot) => void;
export type SessionListUnsubscriber = () => void;
export type SessionLabelSelector = (session: Session) => string;
export type SessionSubtitleSelector = (session: Session) => string | null | undefined;

export interface SessionListController {
  readonly loading: boolean;
  readonly error: string | null;
  readonly selectedSessionId: string | null;
  subscribe(subscriber: SessionListSubscriber): SessionListUnsubscriber;
  getSnapshot(): SessionListSnapshot;
  load(options?: SessionListLoadOptions): Promise<SessionListSnapshot>;
  refresh(options?: Omit<SessionListLoadOptions, 'search'>): Promise<SessionListSnapshot>;
  select(sessionId: string | null): SessionListSnapshot;
  create(options?: SessionListCreateOptions): Promise<Session>;
  update(sessionId: string, request: UpdateSessionRequest, options?: SessionListUpdateOptions): Promise<Session>;
  delete(sessionId: string, options?: SessionListDeleteOptions): Promise<void>;
  clearError(): void;
}
