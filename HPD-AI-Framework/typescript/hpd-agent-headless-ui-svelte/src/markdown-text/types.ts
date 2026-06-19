import type { Snippet } from 'svelte';
import type { SvelteHTMLElements } from 'svelte/elements';
import type {
  CodeSnippetProps,
  LinkSnippetProps,
  MarkedExtension,
  Renderers,
  SvelteMarkdownOptions,
} from '@humanspeak/svelte-markdown';
import type { MarkedKatexOptions } from '@humanspeak/svelte-markdown/extensions';
import type { Message } from '@hpd-research/hpd-agent-headless-ui';

type DivProps = Omit<SvelteHTMLElements['div'], 'children'>;

export interface MarkdownMermaidOptions {
  enabled?: boolean;
  renderWhileStreaming?: boolean;
  lightTheme?: string;
  darkTheme?: string;
}

export interface MarkdownRepairOptions {
  enabled?: boolean;
}

export interface MarkdownTextFeatures {
  katex?: boolean | MarkedKatexOptions;
  mermaid?: boolean | MarkdownMermaidOptions;
}

export interface MarkdownTextModel {
  message?: Message;
  source: string;
  streaming: boolean;
  mermaidEnabled: boolean;
}

export type MarkdownCodeSnippetProps = CodeSnippetProps;
export type MarkdownLinkSnippetProps = LinkSnippetProps;

export interface MarkdownKatexSnippetProps {
  text: string;
  displayMode?: boolean;
}

export interface MarkdownMermaidSnippetProps {
  text: string;
}

export interface MarkdownTextChildProps {
  model: MarkdownTextModel;
  props: MarkdownTextElementProps;
}

export type MarkdownTextElementProps = DivProps & {
  'data-hpd-markdown-text': '';
  'data-message-id'?: string;
  'data-streaming'?: '';
  'data-mermaid-enabled'?: '';
};

export interface MarkdownTextProps extends DivProps {
  message?: Message;
  text?: string;
  streaming?: boolean;
  streamingRepair?: boolean | MarkdownRepairOptions;
  preprocess?: (text: string) => string;
  features?: MarkdownTextFeatures;
  extensions?: MarkedExtension[];
  renderers?: Partial<Renderers>;
  options?: Partial<SvelteMarkdownOptions>;
  code?: Snippet<[MarkdownCodeSnippetProps]>;
  link?: Snippet<[MarkdownLinkSnippetProps]>;
  inlineKatex?: Snippet<[MarkdownKatexSnippetProps]>;
  blockKatex?: Snippet<[MarkdownKatexSnippetProps]>;
  mermaid?: Snippet<[MarkdownMermaidSnippetProps]>;
  child?: Snippet<[MarkdownTextChildProps]>;
}
