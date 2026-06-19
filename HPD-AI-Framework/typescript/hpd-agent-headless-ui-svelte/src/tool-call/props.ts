import {
  getToolCallDuration,
  isToolCallActive,
  type ToolCall,
} from '@hpd-research/hpd-agent-headless-ui';
import type {
  ToolCallActions,
  ToolCallElementProps,
  ToolCallExpandedChangeDetails,
  ToolCallInspectDetails,
  ToolCallProps,
  ToolCallState,
} from './types.js';

export interface CreateToolCallStateOptions {
  expanded?: boolean;
  inspectable?: boolean;
  inspectLabel?: string;
  label?: string;
  onInspect?: ToolCallProps['onInspect'];
  statusLabel?: string;
  tool: ToolCall;
}

export interface CreateToolCallElementPropsOptions {
  actions: ToolCallActions;
  contentId: string;
  labelId: string;
  restProps?: Record<string, unknown>;
  state: ToolCallState;
}

export function createToolCallState(options: CreateToolCallStateOptions): ToolCallState {
  const { tool } = options;
  const active = isToolCallActive(tool);
  const argsText = formatToolCallValue(tool.args);
  const resultText = tool.resultText ?? formatToolCallResult(tool.result);
  const statusLabel = options.statusLabel ?? getToolCallStatusLabel(tool.status);
  const hasArgs = argsText !== null;
  const hasResult = resultText !== null;
  const hasError = Boolean(tool.error);
  const inspectLabel = options.inspectLabel ?? `Inspect ${options.label ?? tool.name}`;

  return {
    active,
    argsText,
    durationMs: getToolCallDuration(tool),
    empty: !hasArgs && !hasResult && !hasError,
    expanded: options.expanded ?? getDefaultToolCallExpanded(tool),
    hasArgs,
    hasError,
    hasResult,
    inspectable: Boolean(options.inspectable && options.onInspect),
    inspectLabel,
    label: options.label ?? tool.name,
    resultText,
    status: tool.status,
    statusLabel,
    tool,
  };
}

export function createToolCallElementProps(
  options: CreateToolCallElementPropsOptions,
): ToolCallElementProps {
  const { actions, contentId, labelId, state } = options;
  const { tool } = state;

  return {
    args: {
      'data-empty': state.hasArgs ? undefined : '',
      'data-hpd-tool-call-args': '',
    },
    content: {
      'aria-labelledby': labelId,
      'data-expanded': state.expanded ? '' : undefined,
      'data-hpd-tool-call-content': '',
      hidden: state.expanded ? undefined : true,
      id: contentId,
    },
    error: {
      'data-hpd-tool-call-error': '',
    },
    header: {
      'data-hpd-tool-call-header': '',
    },
    inspect: {
      'aria-label': state.inspectLabel,
      'data-hpd-tool-call-inspect': '',
      onclick: (event: MouseEvent) => {
        actions.inspect({
          event,
          reason: 'inspect-press',
          trigger: event.currentTarget instanceof Element ? event.currentTarget : undefined,
        });
      },
      type: 'button',
    },
    meta: {
      'data-hpd-tool-call-meta': '',
    },
    result: {
      'data-empty': state.hasResult ? undefined : '',
      'data-hpd-tool-call-result': '',
    },
    root: {
      ...options.restProps,
      'aria-busy': state.active,
      'aria-label': `${state.label} tool call`,
      'aria-live': state.active ? 'polite' : 'off',
      'data-expanded': state.expanded ? '' : undefined,
      'data-hpd-tool-call': '',
      'data-tool-active': state.active ? '' : undefined,
      'data-tool-call-type': tool.callType,
      'data-tool-empty': state.empty ? '' : undefined,
      'data-tool-error': tool.status === 'error' || tool.error ? '' : undefined,
      'data-tool-harness': tool.toolharnessName,
      'data-tool-id': tool.callId,
      'data-tool-name': tool.name,
      'data-tool-status': tool.status,
    },
    trigger: {
      'aria-controls': contentId,
      'aria-expanded': state.expanded,
      'data-expanded': state.expanded ? '' : undefined,
      'data-hpd-tool-call-trigger': '',
      id: labelId,
      onclick: (event: MouseEvent) => {
        actions.toggle({
          event,
          reason: 'trigger-press',
          trigger: event.currentTarget instanceof Element ? event.currentTarget : undefined,
        });
      },
      onkeydown: (event: KeyboardEvent) => {
        if (event.key !== 'Enter' && event.key !== ' ') return;
        event.preventDefault();
        actions.toggle({
          event,
          reason: 'keyboard',
          trigger: event.currentTarget instanceof Element ? event.currentTarget : undefined,
        });
      },
      type: 'button',
    },
  };
}

export interface CreateToolCallActionsOptions {
  getInspectDetails?: () => Pick<ToolCallInspectDetails, 'state' | 'tool'>;
  onExpandedChange?: ToolCallProps['onExpandedChange'];
  onInspect?: ToolCallProps['onInspect'];
  setExpanded(expanded: boolean): void;
  getExpanded(): boolean;
}

export function createToolCallActions(options: CreateToolCallActionsOptions): ToolCallActions {
  const setExpanded = (
    expanded: boolean,
    details: Partial<ToolCallExpandedChangeDetails> = {},
  ): void => {
    if (expanded === options.getExpanded()) return;
    const normalizedDetails: ToolCallExpandedChangeDetails = {
      reason: details.reason ?? 'imperative-action',
      event: details.event,
      trigger: details.trigger,
    };
    options.setExpanded(expanded);
    void options.onExpandedChange?.(expanded, normalizedDetails);
  };

  return {
    collapse(details) {
      setExpanded(false, details);
    },
    expand(details) {
      setExpanded(true, details);
    },
    inspect(details = {}) {
      if (!options.onInspect || !options.getInspectDetails) return;
      const current = options.getInspectDetails();
      const normalizedDetails: ToolCallInspectDetails = {
        ...current,
        event: details.event,
        reason: details.reason ?? 'imperative-action',
        trigger: details.trigger,
      };
      void options.onInspect(normalizedDetails);
    },
    toggle(details) {
      setExpanded(!options.getExpanded(), details);
    },
  };
}

export function getDefaultToolCallExpanded(tool: ToolCall): boolean {
  return tool.status === 'pending' || tool.status === 'executing' || tool.status === 'error';
}

export function getToolCallStatusLabel(status: ToolCall['status']): string {
  if (status === 'pending') return 'pending';
  if (status === 'executing') return 'running';
  if (status === 'complete') return 'complete';
  return 'error';
}

export function formatToolCallDuration(durationMs: number | null): string | null {
  if (durationMs === null) return null;
  if (durationMs < 1000) return `${durationMs}ms`;
  return `${(durationMs / 1000).toFixed(1)}s`;
}

export function formatToolCallValue(value: unknown): string | null {
  if (value === undefined || value === null) return null;
  if (typeof value === 'string') return value;
  try {
    return JSON.stringify(value, null, 2);
  } catch {
    return String(value);
  }
}

function formatToolCallResult(result: ToolCall['result']): string | null {
  if (!result) return null;
  if (result.text) return result.text;
  if (result.json !== undefined) return formatToolCallValue(result.json);
  if (result.content && result.content.length > 0) return formatToolCallValue(result.content);
  return null;
}

export function getToolCallVisibility(options: {
  showArgs?: ToolCallProps['showArgs'];
  showResult?: ToolCallProps['showResult'];
}): {
  showArgs: boolean;
  showResult: boolean;
} {
  return {
    showArgs: options.showArgs ?? true,
    showResult: options.showResult ?? true,
  };
}
