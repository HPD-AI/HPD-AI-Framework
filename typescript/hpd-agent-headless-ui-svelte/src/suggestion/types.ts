import type { Snippet } from 'svelte';
import type { SvelteHTMLElements } from 'svelte/elements';
import type { TextSubmissionBlockedReason } from '@hpd-research/hpd-agent-headless-ui';
import type { ThreadState } from '../thread-state.js';
import type { ThreadComposerRunConfig } from '../thread-composer/index.js';

type ButtonProps = Omit<SvelteHTMLElements['button'], 'children' | 'value'>;
type DivProps = Omit<SvelteHTMLElements['div'], 'children'>;

export type SuggestionMode = 'populate' | 'send';
export type SuggestionPopulateMode = 'replace' | 'append';

export interface SuggestionItem {
  additionalProperties?: Record<string, unknown>;
  description?: string;
  prompt: string;
  title?: string;
}

export type SuggestionBlockedReason =
  | 'disabled'
  | 'empty'
  | 'missing-thread'
  | 'submitting'
  | TextSubmissionBlockedReason
  | null;

export interface SuggestionSelectDetails {
  additionalProperties?: Record<string, unknown>;
  description: string;
  mode: SuggestionMode;
  populateMode: SuggestionPopulateMode;
  prompt: string;
  thread: ThreadState | null;
  title: string;
}

export interface SuggestionElementProps extends ButtonProps {
  'aria-disabled': boolean;
  'data-blocked-reason'?: Exclude<SuggestionBlockedReason, null>;
  'data-can-select'?: '';
  'data-hpd-suggestion': '';
  'data-mode': SuggestionMode;
  'data-populate-mode': SuggestionPopulateMode;
  'data-submitting'?: '';
  disabled: boolean;
  type: 'button';
}

export interface SuggestionListElementProps extends DivProps {
  'data-hpd-suggestion-list': '';
}

export interface SuggestionActions {
  select(): Promise<void>;
}

export interface SuggestionModel {
  additionalProperties?: Record<string, unknown>;
  blockedReason: SuggestionBlockedReason;
  canSelect: boolean;
  description: string;
  mode: SuggestionMode;
  persistSuggestionMetadata: boolean;
  populateMode: SuggestionPopulateMode;
  prompt: string;
  submitting: boolean;
  thread: ThreadState | null;
  title: string;
}

export interface SuggestionChildProps extends SuggestionModel {
  actions: SuggestionActions;
  props: SuggestionElementProps;
}

export type SuggestionChildrenProps = SuggestionChildProps;

export interface SuggestionProps extends ButtonProps {
  additionalProperties?: Record<string, unknown>;
  child?: Snippet<[SuggestionChildProps]>;
  children?: Snippet<[SuggestionChildrenProps]>;
  description?: string;
  disabled?: boolean;
  mode?: SuggestionMode;
  onSelect?: (details: SuggestionSelectDetails) => void | Promise<void>;
  persistSuggestionMetadata?: boolean;
  populateMode?: SuggestionPopulateMode;
  prompt: string;
  runConfig?: ThreadComposerRunConfig;
  targetValue?: string;
  title?: string;
  thread?: ThreadState;
}

export interface SuggestionListSuggestionProps extends SuggestionChildProps {
  suggestion: SuggestionItem;
}

export interface SuggestionListProps extends DivProps {
  additionalProperties?: Record<string, unknown>;
  child?: Snippet<[SuggestionListChildProps]>;
  children?: Snippet<[SuggestionListChildProps]>;
  disabled?: boolean;
  mode?: SuggestionMode;
  onSelect?: (details: SuggestionSelectDetails) => void | Promise<void>;
  persistSuggestionMetadata?: boolean;
  populateMode?: SuggestionPopulateMode;
  runConfig?: ThreadComposerRunConfig;
  suggestion?: Snippet<[SuggestionListSuggestionProps]>;
  suggestions: readonly SuggestionItem[];
  targetValue?: string;
  thread?: ThreadState;
}

export interface SuggestionListChildProps {
  props: SuggestionListElementProps;
  suggestions: readonly SuggestionItem[];
}
