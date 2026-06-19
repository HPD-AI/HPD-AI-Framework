import { isThreadBusy } from '@hpd-research/hpd-agent-headless-ui';
import { mergeProps } from '../thread-composer/index.js';
import type { ThreadStateSnapshot } from '../thread-state.js';
import type {
  ThreadStatusElementProps,
  ThreadStatusIndicatorElementProps,
  ThreadStatusMetricsElementProps,
  ThreadStatusModel,
  ThreadStatusState,
} from './types.js';

export function createThreadStatusModel(snapshot: ThreadStateSnapshot): ThreadStatusModel {
  const busy = isThreadBusy(snapshot.projection);
  const state = getThreadStatusState(snapshot, busy);

  return {
    activeToolCount: snapshot.activity.activeToolCount,
    activity: snapshot.activity,
    activeTools: snapshot.activeTools,
    blockedReason: snapshot.textSubmissionState.reason,
    busy,
    connected: snapshot.connected,
    error: snapshot.error,
    label: getThreadStatusLabel(snapshot, state),
    loading: snapshot.loading,
    pendingRequestCount: snapshot.activity.pendingRequestCount,
    pendingRuntimeRequests: snapshot.pendingRuntimeRequests,
    snapshot,
    state,
    textSubmissionState: snapshot.textSubmissionState,
    threadRun: snapshot.projection.threadRun,
  };
}

export function createThreadStatusElementProps(
  status: ThreadStatusModel,
  restProps: Record<string, unknown> = {},
): ThreadStatusElementProps {
  return mergeProps(restProps, {
    'aria-busy': status.busy || status.loading,
    'aria-label': status.label,
    'aria-live': status.state === 'error' || status.state === 'requesting' ? 'polite' : 'off',
    'data-busy': status.busy ? '' : undefined,
    'data-connected': status.connected ? '' : undefined,
    'data-hpd-thread-status': '',
    'data-loading': status.loading ? '' : undefined,
    'data-status-state': status.state,
  }) as unknown as ThreadStatusElementProps;
}

export function createThreadStatusIndicatorElementProps(
  status: ThreadStatusModel,
  restProps: Record<string, unknown> = {},
): ThreadStatusIndicatorElementProps {
  return mergeProps(restProps, {
    'aria-live': status.state === 'error' || status.state === 'requesting' ? 'polite' : 'off',
    'data-hpd-thread-status-indicator': '',
    'data-status-state': status.state,
  }) as unknown as ThreadStatusIndicatorElementProps;
}

export function createThreadStatusMetricsElementProps(
  status: ThreadStatusModel,
  restProps: Record<string, unknown> = {},
): ThreadStatusMetricsElementProps {
  return mergeProps(restProps, {
    'data-blocked-reason': status.blockedReason ?? undefined,
    'data-hpd-thread-status-metrics': '',
  }) as unknown as ThreadStatusMetricsElementProps;
}

function getThreadStatusState(
  snapshot: ThreadStateSnapshot,
  busy: boolean,
): ThreadStatusState {
  if (snapshot.loading) return 'loading';
  if (snapshot.error) return 'error';
  if (!snapshot.connected) return 'disconnected';
  if (snapshot.pendingRuntimeRequests.length > 0) return 'requesting';
  if (busy) return 'working';
  return 'ready';
}

function getThreadStatusLabel(
  snapshot: ThreadStateSnapshot,
  state: ThreadStatusState,
): string {
  if (state === 'loading') return 'Loading thread';
  if (state === 'error') return snapshot.error ?? 'Thread error';
  if (state === 'disconnected') return 'Disconnected';

  if (state === 'requesting') {
    const count = snapshot.pendingRuntimeRequests.length;
    return count === 1 ? '1 request pending' : `${count} requests pending`;
  }

  if (state === 'working') {
    const [tool] = snapshot.activeTools;
    if (tool) return `${tool.name} running`;
    if (snapshot.activity.reasoning) return 'Reasoning';
    if (snapshot.activity.streaming) return 'Working';
    if (snapshot.projection.threadRun?.status === 'active') return 'Run active';
    return 'Working';
  }

  return 'Ready';
}
