<svelte:options runes={true} />

<script lang="ts">
  import SvelteMarkdown from '@humanspeak/svelte-markdown';
  import { KatexRenderer, MermaidRenderer } from '@humanspeak/svelte-markdown/extensions';
  import 'katex/dist/katex.min.css';
  import {
    createMarkdownTextElementProps,
    createMarkdownTextExtensions,
    createMarkdownTextModel,
    createMarkdownTextRenderers,
    normalizeMermaidOptions,
  } from './props.js';
  import type {
    MarkdownKatexSnippetProps,
    MarkdownMermaidSnippetProps,
    MarkdownTextElementProps,
    MarkdownTextModel,
    MarkdownTextProps,
  } from './types.js';

  let {
    message,
    text,
    streaming,
    streamingRepair,
    preprocess,
    features,
    extensions = [],
    renderers = {},
    options,
    code,
    link,
    inlineKatex: renderInlineKatex,
    blockKatex: renderBlockKatex,
    mermaid: renderMermaid,
    child,
    ...restProps
  }: MarkdownTextProps = $props();

  const model = $derived<MarkdownTextModel>(createMarkdownTextModel({
    features,
    message,
    preprocess,
    streaming,
    streamingRepair,
    text,
  }));
  const elementProps = $derived<MarkdownTextElementProps>(
    createMarkdownTextElementProps(model, restProps),
  );
  const markdownExtensions = $derived(createMarkdownTextExtensions(
    features,
    model.streaming,
    extensions,
  ));
  const markdownRenderers = $derived(createMarkdownTextRenderers(renderers));
  const mermaidOptions = $derived(normalizeMermaidOptions(features?.mermaid));
</script>

{#if child}
  {@render child({ model, props: elementProps })}
{:else}
  <div {...elementProps}>
    <SvelteMarkdown
      source={model.source}
      streaming={model.streaming && !model.mermaidEnabled}
      extensions={markdownExtensions}
      renderers={markdownRenderers}
      {options}
    >
      {#snippet code(props)}
        {#if code}
          {@render code(props)}
        {:else}
          <pre data-hpd-markdown-code><code class={props.lang ? `language-${props.lang}` : undefined}>{props.text}</code></pre>
        {/if}
      {/snippet}

      {#snippet link(props)}
        {#if link}
          {@render link(props)}
        {:else}
          <a href={props.href} title={props.title} target="_blank" rel="noreferrer">
            {@render props.children?.()}
          </a>
        {/if}
      {/snippet}

      {#snippet inlineKatex(props: MarkdownKatexSnippetProps)}
        {#if renderInlineKatex}
          {@render renderInlineKatex(props)}
        {:else}
          <KatexRenderer text={props.text} displayMode={props.displayMode} />
        {/if}
      {/snippet}

      {#snippet blockKatex(props: MarkdownKatexSnippetProps)}
        {#if renderBlockKatex}
          {@render renderBlockKatex(props)}
        {:else}
          <KatexRenderer text={props.text} displayMode={props.displayMode} />
        {/if}
      {/snippet}

      {#snippet mermaid(props: MarkdownMermaidSnippetProps)}
        {#if renderMermaid}
          {@render renderMermaid(props)}
        {:else}
          <MermaidRenderer
            text={props.text}
            lightTheme={mermaidOptions.lightTheme}
            darkTheme={mermaidOptions.darkTheme}
          />
        {/if}
      {/snippet}
    </SvelteMarkdown>
  </div>
{/if}
