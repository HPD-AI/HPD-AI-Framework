import type { Snippet } from 'svelte';
import type { SvelteHTMLElements } from 'svelte/elements';
import type {
  ToolCall,
  ToolCallStatus,
} from '@hpd-research/hpd-agent-headless-ui';

type SectionProps = Omit<SvelteHTMLElements['section'], 'children'>;
type HeaderProps = Omit<SvelteHTMLElements['header'], 'children'>;
type ButtonProps = Omit<SvelteHTMLElements['button'], 'children'>;
type DivProps = Omit<SvelteHTMLElements['div'], 'children'>;

export type ToolCallDisclosureReason =
  | 'trigger-press'
  | 'keyboard'
  | 'imperative-action';

export interface ToolCallExpandedChangeDetails {
  event?: Event;
  reason: ToolCallDisclosureReason;
  trigger?: Element;
}

export type ToolCallInspectReason =
  | 'inspect-press'
  | 'keyboard'
  | 'imperative-action';

export interface ToolCallInspectDetails {
  event?: Event;
  reason: ToolCallInspectReason;
  state: ToolCallState;
  tool: ToolCall;
  trigger?: Element;
}

export interface ToolCallState {
  active: boolean;
  argsText: string | null;
  durationMs: number | null;
  empty: boolean;
  expanded: boolean;
  hasArgs: boolean;
  hasError: boolean;
  hasResult: boolean;
  inspectable: boolean;
  inspectLabel: string;
  label: string;
  resultText: string | null;
  status: ToolCallStatus;
  statusLabel: string;
  tool: ToolCall;
}

export interface ToolCallRootElementProps extends SectionProps {
  'aria-busy': boolean;
  'aria-label': string;
  'aria-live': 'off' | 'polite';
  'data-expanded'?: '';
  'data-hpd-tool-call': '';
  'data-tool-active'?: '';
  'data-tool-call-type'?: string;
  'data-tool-empty'?: '';
  'data-tool-error'?: '';
  'data-tool-harness'?: string;
  'data-tool-id': string;
  'data-tool-name': string;
  'data-tool-status': ToolCallStatus;
}

export interface ToolCallHeaderElementProps extends HeaderProps {
  'data-hpd-tool-call-header': '';
}

export interface ToolCallTriggerElementProps extends ButtonProps {
  'aria-controls': string;
  'aria-expanded': boolean;
  'data-expanded'?: '';
  'data-hpd-tool-call-trigger': '';
  type: 'button';
}

export interface ToolCallInspectElementProps extends ButtonProps {
  'aria-label': string;
  'data-hpd-tool-call-inspect': '';
  type: 'button';
}

export interface ToolCallContentElementProps extends DivProps {
  'aria-labelledby': string;
  'data-expanded'?: '';
  'data-hpd-tool-call-content': '';
  hidden?: boolean;
  id: string;
}

export interface ToolCallMetaElementProps extends DivProps {
  'data-hpd-tool-call-meta': '';
}

export interface ToolCallErrorElementProps extends DivProps {
  'data-hpd-tool-call-error': '';
}

export interface ToolCallArgsElementProps extends DivProps {
  'data-empty'?: '';
  'data-hpd-tool-call-args': '';
}

export interface ToolCallResultElementProps extends DivProps {
  'data-empty'?: '';
  'data-hpd-tool-call-result': '';
}

export interface ToolCallElementProps {
  args: ToolCallArgsElementProps;
  content: ToolCallContentElementProps;
  error: ToolCallErrorElementProps;
  header: ToolCallHeaderElementProps;
  inspect: ToolCallInspectElementProps;
  meta: ToolCallMetaElementProps;
  result: ToolCallResultElementProps;
  root: ToolCallRootElementProps;
  trigger: ToolCallTriggerElementProps;
}

export interface ToolCallActions {
  collapse(details?: Partial<ToolCallExpandedChangeDetails>): void;
  expand(details?: Partial<ToolCallExpandedChangeDetails>): void;
  inspect(details?: Partial<ToolCallInspectDetails>): void;
  toggle(details?: Partial<ToolCallExpandedChangeDetails>): void;
}

export interface ToolCallChildProps {
  actions: ToolCallActions;
  elementProps: ToolCallElementProps;
  state: ToolCallState;
  tool: ToolCall;
}

export interface ToolCallProps extends SectionProps {
  children?: Snippet<[ToolCallChildProps]>;
  defaultExpanded?: boolean;
  expanded?: boolean;
  inspectable?: boolean;
  inspectLabel?: string;
  label?: string;
  onExpandedChange?: (
    expanded: boolean,
    details: ToolCallExpandedChangeDetails,
  ) => void | Promise<void>;
  onInspect?: (details: ToolCallInspectDetails) => void | Promise<void>;
  showArgs?: boolean;
  showResult?: boolean;
  statusLabel?: string;
  tool: ToolCall;
}
