import type { Snippet } from 'svelte';
import type { SvelteHTMLElements } from 'svelte/elements';
import type {
  ThreadContextUsage,
} from '@hpd-research/hpd-agent-headless-ui';
import type {
  UsageDetails,
} from '@hpd-research/hpd-agent-client';
import type {
  ThreadState,
} from '../thread-state.js';

type DivProps = Omit<SvelteHTMLElements['div'], 'children'>;
type SpanProps = Omit<SvelteHTMLElements['span'], 'children'>;
type SvgProps = Omit<SvelteHTMLElements['svg'], 'children'>;

export type ContextDisplaySeverity = 'normal' | 'warning' | 'critical';

export interface ContextDisplayModel {
  contextUsage: ThreadContextUsage | null;
  usage: UsageDetails | null;
  modelContextWindow: number | null;
  totalTokens: number;
  percent: number | null;
  severity: ContextDisplaySeverity;
  hasUsage: boolean;
  inputTokens?: number;
  outputTokens?: number;
  cachedInputTokens?: number;
  reasoningTokens?: number;
  inputAudioTokens?: number;
  inputTextTokens?: number;
  outputAudioTokens?: number;
  outputTextTokens?: number;
  additionalCounts?: Record<string, number>;
}

export interface ContextDisplayRootElementProps extends DivProps {
  'aria-label': string;
  'data-hpd-context-display-root': '';
  'data-has-usage'?: '';
  'data-severity': ContextDisplaySeverity;
}

export interface ContextDisplayBarElementProps extends DivProps {
  'aria-label': string;
  'data-hpd-context-display-bar': '';
  'data-severity': ContextDisplaySeverity;
  role: 'meter';
  'aria-valuemin': 0;
  'aria-valuemax': number;
  'aria-valuenow': number;
}

export interface ContextDisplayBarFillElementProps extends DivProps {
  'data-hpd-context-display-bar-fill': '';
  'data-severity': ContextDisplaySeverity;
  style: string;
}

export interface ContextDisplayRingElementProps extends SvgProps {
  'aria-label': string;
  'data-hpd-context-display-ring': '';
  'data-severity': ContextDisplaySeverity;
  role: 'meter';
  'aria-valuemin': 0;
  'aria-valuemax': number;
  'aria-valuenow': number;
}

export interface ContextDisplayTextElementProps extends SpanProps {
  'data-hpd-context-display-text': '';
  'data-severity': ContextDisplaySeverity;
}

export interface ContextDisplayBreakdownElementProps extends DivProps {
  'data-hpd-context-display-breakdown': '';
  'data-severity': ContextDisplaySeverity;
}

export interface ContextDisplayBreakdownRow {
  label: string;
  value: number;
  key: string;
}

export interface ContextDisplayRootProps extends DivProps {
  child?: Snippet<[ContextDisplayRootChildProps]>;
  children?: Snippet<[ContextDisplayModel]>;
  modelContextWindow?: number | null;
  thread?: ThreadState;
  usage?: ThreadContextUsage | UsageDetails | null;
}

export interface ContextDisplayRootChildProps extends ContextDisplayModel {
  props: ContextDisplayRootElementProps;
}

export interface ContextDisplayBarProps extends DivProps {
  child?: Snippet<[ContextDisplayBarChildProps]>;
  children?: Snippet<[ContextDisplayBarSnippetProps]>;
}

export interface ContextDisplayBarSnippetProps {
  fillProps: ContextDisplayBarFillElementProps;
  model: ContextDisplayModel;
  props: ContextDisplayBarElementProps;
}

export interface ContextDisplayBarChildProps extends ContextDisplayBarSnippetProps {}

export interface ContextDisplayRingProps extends SvgProps {
  child?: Snippet<[ContextDisplayRingChildProps]>;
  children?: Snippet<[ContextDisplayRingSnippetProps]>;
  size?: number;
  strokeWidth?: number;
}

export interface ContextDisplayRingSnippetProps {
  circumference: number;
  model: ContextDisplayModel;
  progressOffset: number;
  radius: number;
  props: ContextDisplayRingElementProps;
  size: number;
  strokeWidth: number;
}

export interface ContextDisplayRingChildProps extends ContextDisplayRingSnippetProps {}

export interface ContextDisplayTextProps extends SpanProps {
  child?: Snippet<[ContextDisplayTextChildProps]>;
  children?: Snippet<[ContextDisplayTextSnippetProps]>;
}

export interface ContextDisplayTextSnippetProps {
  model: ContextDisplayModel;
  props: ContextDisplayTextElementProps;
}

export interface ContextDisplayTextChildProps extends ContextDisplayTextSnippetProps {}

export interface ContextDisplayBreakdownProps extends DivProps {
  child?: Snippet<[ContextDisplayBreakdownChildProps]>;
  children?: Snippet<[ContextDisplayBreakdownSnippetProps]>;
}

export interface ContextDisplayBreakdownSnippetProps {
  model: ContextDisplayModel;
  props: ContextDisplayBreakdownElementProps;
  rows: ContextDisplayBreakdownRow[];
}

export interface ContextDisplayBreakdownChildProps extends ContextDisplayBreakdownSnippetProps {}

