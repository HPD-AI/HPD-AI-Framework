import type { Snippet } from 'svelte';
import type { SvelteHTMLElements } from 'svelte/elements';
import type { RuntimeRequest } from '@hpd-research/hpd-agent-headless-ui';
import type {
  RuntimeRequestActions,
  RuntimeRequestElementProps,
} from '../runtime-request/index.js';
import type { ThreadState } from '../thread-state.js';

type DivProps = Omit<SvelteHTMLElements['div'], 'children'>;

export interface ThreadRuntimeRequestSnippetProps {
  actions: RuntimeRequestActions;
  index: number;
  item: RuntimeRequest;
  props: RuntimeRequestElementProps;
}

export interface ThreadRuntimeRequestsProps extends DivProps {
  empty?: Snippet<[]>;
  request?: Snippet<[ThreadRuntimeRequestSnippetProps]>;
  requests?: RuntimeRequest[];
  thread?: ThreadState;
}
