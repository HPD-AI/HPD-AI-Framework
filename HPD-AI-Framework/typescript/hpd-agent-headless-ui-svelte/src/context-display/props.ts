import type {
  ThreadContextUsage,
} from '@hpd-research/hpd-agent-headless-ui';
import type {
  UsageDetails,
} from '@hpd-research/hpd-agent-client';
import { mergeProps } from '../thread-composer/index.js';
import type {
  ContextDisplayBarElementProps,
  ContextDisplayBarFillElementProps,
  ContextDisplayBreakdownElementProps,
  ContextDisplayBreakdownRow,
  ContextDisplayModel,
  ContextDisplayRingElementProps,
  ContextDisplayRootElementProps,
  ContextDisplaySeverity,
  ContextDisplayTextElementProps,
} from './types.js';

export function createContextDisplayModel(options: {
  modelContextWindow?: number | null;
  usage?: ThreadContextUsage | UsageDetails | null;
}): ContextDisplayModel {
  const contextUsage = normalizeContextUsage(options.usage);
  const usage = contextUsage?.usage ?? normalizeUsage(options.usage);
  const totalTokens = getTotalTokens(usage);
  const modelContextWindow = normalizeTokenCount(options.modelContextWindow);
  const percent = modelContextWindow && modelContextWindow > 0
    ? Math.min((totalTokens / modelContextWindow) * 100, 100)
    : null;

  return {
    contextUsage,
    usage,
    modelContextWindow,
    totalTokens,
    percent,
    severity: getContextDisplaySeverity(percent),
    hasUsage: usage !== null,
    inputTokens: normalizeTokenCount(usage?.inputTokenCount) ?? undefined,
    outputTokens: normalizeTokenCount(usage?.outputTokenCount) ?? undefined,
    cachedInputTokens: normalizeTokenCount(usage?.cachedInputTokenCount) ?? undefined,
    reasoningTokens: normalizeTokenCount(usage?.reasoningTokenCount) ?? undefined,
    inputAudioTokens: normalizeTokenCount(usage?.inputAudioTokenCount) ?? undefined,
    inputTextTokens: normalizeTokenCount(usage?.inputTextTokenCount) ?? undefined,
    outputAudioTokens: normalizeTokenCount(usage?.outputAudioTokenCount) ?? undefined,
    outputTextTokens: normalizeTokenCount(usage?.outputTextTokenCount) ?? undefined,
    additionalCounts: normalizeAdditionalCounts(usage?.additionalCounts),
  };
}

export function createContextDisplayRootElementProps(
  model: ContextDisplayModel,
  restProps: Record<string, unknown> = {},
): ContextDisplayRootElementProps {
  return mergeProps(restProps, {
    'aria-label': createContextDisplayLabel(model),
    'data-has-usage': model.hasUsage ? '' : undefined,
    'data-hpd-context-display-root': '',
    'data-severity': model.severity,
  }) as unknown as ContextDisplayRootElementProps;
}

export function createContextDisplayBarElementProps(
  model: ContextDisplayModel,
  restProps: Record<string, unknown> = {},
): ContextDisplayBarElementProps {
  return mergeProps(restProps, {
    'aria-label': createContextDisplayLabel(model),
    'aria-valuemax': model.modelContextWindow ?? Math.max(model.totalTokens, 1),
    'aria-valuemin': 0,
    'aria-valuenow': model.totalTokens,
    'data-hpd-context-display-bar': '',
    'data-severity': model.severity,
    role: 'meter',
  }) as unknown as ContextDisplayBarElementProps;
}

export function createContextDisplayBarFillElementProps(
  model: ContextDisplayModel,
  restProps: Record<string, unknown> = {},
): ContextDisplayBarFillElementProps {
  return mergeProps(restProps, {
    'data-hpd-context-display-bar-fill': '',
    'data-severity': model.severity,
    style: `width: ${model.percent ?? 0}%`,
  }) as unknown as ContextDisplayBarFillElementProps;
}

export function createContextDisplayRingElementProps(
  model: ContextDisplayModel,
  restProps: Record<string, unknown> = {},
): ContextDisplayRingElementProps {
  return mergeProps(restProps, {
    'aria-label': createContextDisplayLabel(model),
    'aria-valuemax': model.modelContextWindow ?? Math.max(model.totalTokens, 1),
    'aria-valuemin': 0,
    'aria-valuenow': model.totalTokens,
    'data-hpd-context-display-ring': '',
    'data-severity': model.severity,
    role: 'meter',
  }) as unknown as ContextDisplayRingElementProps;
}

export function createContextDisplayTextElementProps(
  model: ContextDisplayModel,
  restProps: Record<string, unknown> = {},
): ContextDisplayTextElementProps {
  return mergeProps(restProps, {
    'data-hpd-context-display-text': '',
    'data-severity': model.severity,
  }) as unknown as ContextDisplayTextElementProps;
}

export function createContextDisplayBreakdownElementProps(
  model: ContextDisplayModel,
  restProps: Record<string, unknown> = {},
): ContextDisplayBreakdownElementProps {
  return mergeProps(restProps, {
    'data-hpd-context-display-breakdown': '',
    'data-severity': model.severity,
  }) as unknown as ContextDisplayBreakdownElementProps;
}

export function getContextDisplayBreakdownRows(model: ContextDisplayModel): ContextDisplayBreakdownRow[] {
  const rows: ContextDisplayBreakdownRow[] = [];
  addRow(rows, 'Input', 'input', model.inputTokens);
  addRow(rows, 'Cached', 'cached', model.cachedInputTokens);
  addRow(rows, 'Output', 'output', model.outputTokens);
  addRow(rows, 'Reasoning', 'reasoning', model.reasoningTokens);
  addRow(rows, 'Input audio', 'input-audio', model.inputAudioTokens);
  addRow(rows, 'Input text', 'input-text', model.inputTextTokens);
  addRow(rows, 'Output audio', 'output-audio', model.outputAudioTokens);
  addRow(rows, 'Output text', 'output-text', model.outputTextTokens);

  for (const [key, value] of Object.entries(model.additionalCounts ?? {})) {
    addRow(rows, key, `additional:${key}`, value);
  }

  addRow(rows, 'Total', 'total', model.totalTokens);
  return rows;
}

export function formatContextDisplayTokens(value: number | null | undefined): string {
  const tokens = normalizeTokenCount(value) ?? 0;
  if (tokens >= 1_000_000) return `${(tokens / 1_000_000).toFixed(1)}M`;
  if (tokens >= 1_000) return `${(tokens / 1_000).toFixed(1)}k`;
  return `${tokens}`;
}

export function formatContextDisplayPercent(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return '0%';
  return `${Math.round(value)}%`;
}

function getContextDisplaySeverity(percent: number | null): ContextDisplaySeverity {
  if (percent === null) return 'normal';
  if (percent > 85) return 'critical';
  if (percent >= 65) return 'warning';
  return 'normal';
}

function getTotalTokens(usage: UsageDetails | null): number {
  const total = normalizeTokenCount(usage?.totalTokenCount);
  if (total !== null) return total;
  return (normalizeTokenCount(usage?.inputTokenCount) ?? 0) +
    (normalizeTokenCount(usage?.outputTokenCount) ?? 0);
}

function normalizeContextUsage(usage: ThreadContextUsage | UsageDetails | null | undefined): ThreadContextUsage | null {
  if (!usage || !('usage' in usage)) return null;
  return {
    ...usage,
    usage: normalizeUsage(usage.usage) ?? {},
  };
}

function normalizeUsage(usage: ThreadContextUsage | UsageDetails | null | undefined): UsageDetails | null {
  if (!usage) return null;
  if ('usage' in usage) return normalizeUsage(usage.usage);
  return {
    ...usage,
    additionalCounts: normalizeAdditionalCounts(usage.additionalCounts),
  };
}

function normalizeAdditionalCounts(counts: Record<string, number> | null | undefined): Record<string, number> | undefined {
  if (!counts) return undefined;
  const normalized: Record<string, number> = {};
  for (const [key, value] of Object.entries(counts)) {
    const tokenCount = normalizeTokenCount(value);
    if (tokenCount !== null) normalized[key] = tokenCount;
  }
  return Object.keys(normalized).length > 0 ? normalized : undefined;
}

function normalizeTokenCount(value: number | null | undefined): number | null {
  if (value === null || value === undefined || Number.isNaN(value)) return null;
  return Math.max(0, Math.trunc(value));
}

function createContextDisplayLabel(model: ContextDisplayModel): string {
  const total = formatContextDisplayTokens(model.totalTokens);
  if (!model.modelContextWindow) return `${total} tokens used`;
  return `${total} of ${formatContextDisplayTokens(model.modelContextWindow)} context tokens used`;
}

function addRow(
  rows: ContextDisplayBreakdownRow[],
  label: string,
  key: string,
  value: number | null | undefined,
): void {
  const normalized = normalizeTokenCount(value);
  if (normalized === null) return;
  rows.push({ label, key, value: normalized });
}

