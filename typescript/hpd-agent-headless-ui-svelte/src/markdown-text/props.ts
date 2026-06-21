import {
  markedKatex,
  markedMermaid,
} from '@humanspeak/svelte-markdown/extensions';
import type {
  MarkedExtension,
  Renderers,
} from '@humanspeak/svelte-markdown';
import type {
  MarkdownMermaidOptions,
  MarkdownTextElementProps,
  MarkdownTextFeatures,
  MarkdownTextModel,
  MarkdownRepairOptions,
} from './types.js';
import type { Message } from '@hpd-research/hpd-agent-headless-ui';

export function createMarkdownTextModel(input: {
  message?: Message;
  text?: string;
  streaming?: boolean;
  preprocess?: (text: string) => string;
  streamingRepair?: boolean | MarkdownRepairOptions;
  features?: MarkdownTextFeatures;
}): MarkdownTextModel {
  const rawSource = input.text ?? input.message?.content ?? '';
  const preprocessed = input.preprocess ? input.preprocess(rawSource) : rawSource;
  const source = shouldRepairMarkdown(input.streamingRepair)
    ? repairStreamingMarkdown(preprocessed)
    : preprocessed;
  const streaming = input.streaming ?? input.message?.streaming ?? false;
  const mermaid = normalizeMermaidOptions(input.features?.mermaid);
  const mermaidEnabled = mermaid.enabled && (!streaming || mermaid.renderWhileStreaming);

  return {
    message: input.message,
    source,
    streaming,
    mermaidEnabled,
  };
}

export function createMarkdownTextElementProps(
  model: MarkdownTextModel,
  restProps: Record<string, unknown> = {},
): MarkdownTextElementProps {
  return {
    ...restProps,
    'data-hpd-markdown-text': '',
    'data-message-id': model.message?.id,
    'data-streaming': model.streaming ? '' : undefined,
    'data-mermaid-enabled': model.mermaidEnabled ? '' : undefined,
  } as MarkdownTextElementProps;
}

export function createMarkdownTextExtensions(
  features: MarkdownTextFeatures | undefined,
  streaming: boolean,
  extensions: MarkedExtension[] = [],
): MarkedExtension[] {
  const builtIns: MarkedExtension[] = [];

  const katex = normalizeKatexOptions(features?.katex);
  if (katex.enabled) {
    builtIns.push(markedKatex(katex.options));
  }

  const mermaid = normalizeMermaidOptions(features?.mermaid);
  if (mermaid.enabled && (!streaming || mermaid.renderWhileStreaming)) {
    builtIns.push(markedMermaid());
  }

  return [...builtIns, ...extensions];
}

export function createMarkdownTextRenderers(
  renderers: Partial<Renderers> = {},
): Partial<Renderers> {
  return renderers;
}

export function normalizeMermaidOptions(value: MarkdownTextFeatures['mermaid']): Required<MarkdownMermaidOptions> {
  if (value === true) {
    return {
      enabled: true,
      renderWhileStreaming: false,
      lightTheme: 'default',
      darkTheme: 'dark',
    };
  }

  if (!value) {
    return {
      enabled: false,
      renderWhileStreaming: false,
      lightTheme: 'default',
      darkTheme: 'dark',
    };
  }

  return {
    enabled: value.enabled ?? true,
    renderWhileStreaming: value.renderWhileStreaming ?? false,
    lightTheme: value.lightTheme ?? 'default',
    darkTheme: value.darkTheme ?? 'dark',
  };
}

function normalizeKatexOptions(value: MarkdownTextFeatures['katex']): {
  enabled: boolean;
  options: Exclude<MarkdownTextFeatures['katex'], boolean | undefined>;
} {
  if (value === true) {
    return { enabled: true, options: {} };
  }

  if (!value) {
    return { enabled: false, options: {} };
  }

  return { enabled: true, options: value };
}

function shouldRepairMarkdown(value: boolean | MarkdownRepairOptions | undefined): boolean {
  return value === true || (typeof value === 'object' && value.enabled === true);
}

function repairStreamingMarkdown(source: string): string {
  return source;
}
