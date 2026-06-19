import type { Session, SearchSessionsRequest, UpdateSessionRequest } from '@hpd-research/hpd-agent-client';
import { createSessionListItems } from './selectors.js';
import type {
  SessionListController,
  SessionListControllerOptions,
  SessionListCreateOptions,
  SessionListDeleteOptions,
  SessionListLoadOptions,
  SessionListSnapshot,
  SessionListSubscriber,
  SessionListUnsubscriber,
  SessionListUpdateOptions,
} from './types.js';

export function createSessionListController(
  options: SessionListControllerOptions,
): SessionListController {
  return new SessionListControllerImpl(options);
}

class SessionListControllerImpl implements SessionListController {
  private readonly client;
  private readonly autoSelect;
  private readonly getLabel;
  private readonly getSubtitle;
  private subscribers = new Set<SessionListSubscriber>();
  private sessions: Session[] = [];
  private search: SearchSessionsRequest;
  private _selectedSessionId: string | null;
  private _loading = false;
  private _error: string | null = null;

  constructor(options: SessionListControllerOptions) {
    this.client = options.client;
    this.autoSelect = options.autoSelect ?? true;
    this.getLabel = options.getLabel;
    this.getSubtitle = options.getSubtitle;
    this.search = options.search ?? {};
    this._selectedSessionId = options.selectedSessionId ?? null;
  }

  get loading(): boolean {
    return this._loading;
  }

  get error(): string | null {
    return this._error;
  }

  get selectedSessionId(): string | null {
    return this._selectedSessionId;
  }

  subscribe(subscriber: SessionListSubscriber): SessionListUnsubscriber {
    this.subscribers.add(subscriber);
    subscriber(this.getSnapshot());
    return () => {
      this.subscribers.delete(subscriber);
    };
  }

  getSnapshot(): SessionListSnapshot {
    const selectedSession = this._selectedSessionId
      ? this.sessions.find((session) => session.id === this._selectedSessionId) ?? null
      : null;

    return {
      sessions: this.sessions.map((session) => ({ ...session, metadata: { ...(session.metadata ?? {}) } })),
      items: createSessionListItems({
        sessions: this.sessions,
        selectedSessionId: this._selectedSessionId,
        getLabel: this.getLabel,
        getSubtitle: this.getSubtitle,
      }),
      selectedSessionId: this._selectedSessionId,
      selectedSession,
      loading: this._loading,
      error: this._error,
      search: cloneSearch(this.search),
      empty: this.sessions.length === 0,
    };
  }

  async load(options: SessionListLoadOptions = {}): Promise<SessionListSnapshot> {
    this._loading = true;
    this._error = null;
    if (options.search) this.search = options.search;
    this.emit();

    try {
      this.sessions = await this.client.searchSessions(this.search);
      if (!options.preserveSelection) {
        this.reconcileSelection();
      } else if (this._selectedSessionId && !this.sessions.some((session) => session.id === this._selectedSessionId)) {
        this._selectedSessionId = null;
      }
      return this.getSnapshot();
    } catch (error) {
      this._error = getErrorMessage(error);
      throw error;
    } finally {
      this._loading = false;
      this.emit();
    }
  }

  refresh(options: Omit<SessionListLoadOptions, 'search'> = {}): Promise<SessionListSnapshot> {
    return this.load({ ...options, search: this.search });
  }

  select(sessionId: string | null): SessionListSnapshot {
    this._selectedSessionId = sessionId;
    this.emit();
    return this.getSnapshot();
  }

  async create(options: SessionListCreateOptions = {}): Promise<Session> {
    const { select = true, signal: _signal, ...request } = options;
    this._loading = true;
    this._error = null;
    this.emit();

    try {
      const session = await this.client.createSession(request);
      this.sessions = [session, ...this.sessions.filter((existing) => existing.id !== session.id)];
      if (select) this._selectedSessionId = session.id;
      return session;
    } catch (error) {
      this._error = getErrorMessage(error);
      throw error;
    } finally {
      this._loading = false;
      this.emit();
    }
  }

  async update(
    sessionId: string,
    request: UpdateSessionRequest,
    options: SessionListUpdateOptions = {},
  ): Promise<Session> {
    void options.signal;
    this._error = null;
    try {
      const session = await this.client.updateSession(sessionId, request);
      this.sessions = this.sessions.map((existing) => existing.id === session.id ? session : existing);
      this.emit();
      return session;
    } catch (error) {
      this._error = getErrorMessage(error);
      this.emit();
      throw error;
    }
  }

  async delete(sessionId: string, options: SessionListDeleteOptions = {}): Promise<void> {
    void options.signal;
    this._error = null;
    try {
      await this.client.deleteSession(sessionId);
      this.sessions = this.sessions.filter((session) => session.id !== sessionId);
      if (this._selectedSessionId === sessionId) {
        this._selectedSessionId = options.selectFallback ?? true
          ? this.sessions[0]?.id ?? null
          : null;
      }
      this.emit();
    } catch (error) {
      this._error = getErrorMessage(error);
      this.emit();
      throw error;
    }
  }

  clearError(): void {
    this._error = null;
    this.emit();
  }

  private reconcileSelection(): void {
    if (this._selectedSessionId && this.sessions.some((session) => session.id === this._selectedSessionId)) {
      return;
    }
    this._selectedSessionId = this.autoSelect ? this.sessions[0]?.id ?? null : null;
  }

  private emit(): void {
    const snapshot = this.getSnapshot();
    for (const subscriber of this.subscribers) {
      subscriber(snapshot);
    }
  }
}

function cloneSearch(search: SearchSessionsRequest): SearchSessionsRequest {
  return {
    ...search,
    metadata: search.metadata ? { ...search.metadata } : undefined,
  };
}

function getErrorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
