import type { Snippet } from 'svelte';
import type { SvelteHTMLElements } from 'svelte/elements';
import type {
  RuntimeRequest,
  TextSubmissionBlockedReason,
  TextSubmissionState,
  ThreadActivity,
  ThreadRunView,
  ToolCall,
} from '@hpd-research/hpd-agent-headless-ui';
import type {
  ThreadState,
  ThreadStateSnapshot,
} from '../thread-state.js';

type DivProps = Omit<SvelteHTMLElements['div'], 'children'>;
type SpanProps = Omit<SvelteHTMLElements['span'], 'children'>;

export type ThreadStatusState =
  | 'loading'
  | 'error'
  | 'disconnected'
  | 'requesting'
  | 'working'
  | 'ready';

export interface ThreadStatusModel {
  activeToolCount: number;
  activity: ThreadActivity;
  activeTools: ToolCall[];
  blockedReason: TextSubmissionBlockedReason | null;
  busy: boolean;
  connected: boolean;
  error: string | null;
  label: string;
  loading: boolean;
  pendingRequestCount: number;
  pendingRuntimeRequests: RuntimeRequest[];
  snapshot: ThreadStateSnapshot;
  state: ThreadStatusState;
  textSubmissionState: TextSubmissionState;
  threadRun: ThreadRunView | null;
}

export interface ThreadStatusElementProps extends DivProps {
  'aria-busy': boolean;
  'aria-label': string;
  'aria-live': 'polite' | 'off';
  'data-busy'?: '';
  'data-connected'?: '';
  'data-hpd-thread-status': '';
  'data-loading'?: '';
  'data-status-state': ThreadStatusState;
}

export interface ThreadStatusIndicatorElementProps extends SpanProps {
  'aria-live': 'polite' | 'off';
  'data-hpd-thread-status-indicator': '';
  'data-status-state': ThreadStatusState;
}

export interface ThreadStatusMetricsElementProps extends SpanProps {
  'data-blocked-reason'?: TextSubmissionBlockedReason;
  'data-hpd-thread-status-metrics': '';
}

export interface ThreadStatusProps extends DivProps {
  child?: Snippet<[ThreadStatusModel & { props: ThreadStatusElementProps }]>;
  children?: Snippet<[ThreadStatusModel]>;
  thread: ThreadState;
}

export interface ThreadStatusIndicatorProps extends SpanProps {
  child?: Snippet<[ThreadStatusIndicatorChildProps]>;
  children?: Snippet<[ThreadStatusIndicatorSnippetProps]>;
  status: ThreadStatusModel;
}

export interface ThreadStatusIndicatorSnippetProps {
  status: ThreadStatusModel;
}

export interface ThreadStatusIndicatorChildProps extends ThreadStatusIndicatorSnippetProps {
  props: ThreadStatusIndicatorElementProps;
}

export interface ThreadStatusMetricsProps extends SpanProps {
  child?: Snippet<[ThreadStatusMetricsChildProps]>;
  children?: Snippet<[ThreadStatusMetricsSnippetProps]>;
  status: ThreadStatusModel;
}

export interface ThreadStatusMetricsSnippetProps {
  status: ThreadStatusModel;
}

export interface ThreadStatusMetricsChildProps extends ThreadStatusMetricsSnippetProps {
  props: ThreadStatusMetricsElementProps;
}
