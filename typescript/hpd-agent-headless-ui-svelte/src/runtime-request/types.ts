import type { Snippet } from 'svelte';
import type { SvelteHTMLElements } from 'svelte/elements';
import type {
  RuntimeRequest,
  RuntimeRequestKind,
} from '@hpd-research/hpd-agent-headless-ui';
import type { ThreadState } from '../thread-state.js';

type DivProps = Omit<SvelteHTMLElements['div'], 'children'>;
type ButtonProps = Omit<SvelteHTMLElements['button'], 'children'>;

type ApproveChoice = Parameters<ThreadState['approve']>[1];
type ClientToolOutcome = Parameters<ThreadState['answerClientToolRequest']>[1];
type ClientToolOutcomeOptions = Parameters<ThreadState['answerClientToolRequest']>[2];
type ResponseInput = Parameters<ThreadState['respond']>[0];

export interface RuntimeRequestActions {
  approve(choice?: ApproveChoice): Promise<unknown>;
  clarify(answer: string): Promise<unknown>;
  deny(reason?: string): Promise<unknown>;
  respond(input: ResponseInput): Promise<unknown>;
  answerClientToolRequest(outcome: ClientToolOutcome, options?: ClientToolOutcomeOptions): Promise<unknown>;
}

export interface RuntimeRequestElementProps extends DivProps {
  'aria-label': string;
  'data-hpd-runtime-request': '';
  'data-request-id': string;
  'data-request-kind': RuntimeRequestKind;
  'data-request-source': string;
  'data-request-event-type': string;
  'data-response-event-type'?: string;
  'data-response-policy'?: string;
  'data-visibility'?: string;
}

export interface RuntimeRequestActionProps {
  approve: ButtonProps & {
    'data-hpd-runtime-request-approve': '';
  };
  deny: ButtonProps & {
    'data-hpd-runtime-request-deny': '';
  };
  submit: ButtonProps & {
    'data-hpd-runtime-request-submit': '';
  };
}

export interface RuntimeRequestSnippetProps {
  actions: RuntimeRequestActions;
  actionProps: RuntimeRequestActionProps;
  item: RuntimeRequest;
}

export interface RuntimeRequestKindSnippetProps extends RuntimeRequestSnippetProps {
  props: RuntimeRequestKindElementProps;
}

export interface RuntimeRequestChildProps extends RuntimeRequestSnippetProps {
  props: RuntimeRequestElementProps;
}

export interface RuntimeRequestKindElementProps extends DivProps {
  'data-hpd-runtime-request-kind': RuntimeRequestKind;
}

export interface RuntimeRequestActionDetails {
  item: RuntimeRequest;
}

export interface RuntimeRequestApproveDetails extends RuntimeRequestActionDetails {
  choice?: ApproveChoice;
}

export interface RuntimeRequestDenyDetails extends RuntimeRequestActionDetails {
  reason?: string;
}

export interface RuntimeRequestClarifyDetails extends RuntimeRequestActionDetails {
  answer: string;
}

export interface RuntimeRequestRespondDetails extends RuntimeRequestActionDetails {
  input: ResponseInput;
}

export interface RuntimeRequestClientToolRespondDetails extends RuntimeRequestActionDetails {
  options?: ClientToolOutcomeOptions;
  outcome: ClientToolOutcome;
}

export interface RuntimeRequestProps extends DivProps {
  child?: Snippet<[RuntimeRequestChildProps]>;
  children?: Snippet<[RuntimeRequestSnippetProps]>;
  clientTool?: Snippet<[RuntimeRequestKindSnippetProps]>;
  clarification?: Snippet<[RuntimeRequestKindSnippetProps]>;
  custom?: Snippet<[RuntimeRequestKindSnippetProps]>;
  item: RuntimeRequest;
  onApprove?: (details: RuntimeRequestApproveDetails) => void | Promise<void>;
  onClarify?: (details: RuntimeRequestClarifyDetails) => void | Promise<void>;
  onClientToolRespond?: (details: RuntimeRequestClientToolRespondDetails) => void | Promise<void>;
  onDeny?: (details: RuntimeRequestDenyDetails) => void | Promise<void>;
  onRespond?: (details: RuntimeRequestRespondDetails) => void | Promise<void>;
  permission?: Snippet<[RuntimeRequestKindSnippetProps]>;
  thread?: ThreadState;
}

export interface RuntimeRequestLeafProps extends DivProps {
  actions?: RuntimeRequestActions;
  actionProps?: RuntimeRequestActionProps;
  children?: Snippet<[RuntimeRequestKindSnippetProps]>;
  item: RuntimeRequest;
  onApprove?: (details: RuntimeRequestApproveDetails) => void | Promise<void>;
  onClarify?: (details: RuntimeRequestClarifyDetails) => void | Promise<void>;
  onClientToolRespond?: (details: RuntimeRequestClientToolRespondDetails) => void | Promise<void>;
  onDeny?: (details: RuntimeRequestDenyDetails) => void | Promise<void>;
  onRespond?: (details: RuntimeRequestRespondDetails) => void | Promise<void>;
  thread?: ThreadState;
}
