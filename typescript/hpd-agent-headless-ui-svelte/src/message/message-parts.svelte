<svelte:options runes={true} />

<script lang="ts">
  import Reasoning from '../reasoning/reasoning.svelte';
  import ToolCall from '../tool-call/tool-call.svelte';
  import DirectiveText from '../directive-text/directive-text.svelte';
  import MarkdownText from '../markdown-text/markdown-text.svelte';
  import {
    createMessagePartElementProps,
    createMessageParts,
    createMessagePartsState,
  } from './props.js';
  import type {
    MessagePartElementProps,
    MessagePartsProps,
    MessagePartsState,
    MessageRenderPart,
  } from './types.js';

  let {
    message,
    part: renderPart,
    children,
    ...restProps
  }: MessagePartsProps = $props();

  const parts = $derived<MessageRenderPart[]>(createMessageParts(message));
  const state = $derived<MessagePartsState>(createMessagePartsState(message, parts));

  function propsFor(part: MessageRenderPart): MessagePartElementProps {
    return createMessagePartElementProps(part);
  }
</script>

{#if children}
  {@render children({ message, parts, state })}
{:else}
  <div {...restProps} data-hpd-message-parts data-empty={state.empty ? '' : undefined}>
    {#each parts as item (item.id)}
      {@const props = propsFor(item)}
      {#if renderPart}
        {@render renderPart({ message, part: item, props })}
      {:else if item.type === 'thinking'}
        <div {...props}>Thinking...</div>
      {:else if item.type === 'reasoning'}
        <Reasoning
          text={item.text}
          status={item.status}
          data-hpd-message-reasoning
          {...props}
        />
      {:else if item.type === 'text'}
        {#if item.message.role === 'assistant'}
          <MarkdownText
            {...props}
            message={item.message}
            text={item.text}
            streaming={item.streaming}
            features={{ katex: true, mermaid: true }}
            data-hpd-message-content
          />
        {:else}
          <span {...props} data-hpd-message-content>
            <DirectiveText
              message={item.message}
              text={item.text}
            />
          </span>
        {/if}
      {:else if item.type === 'content'}
        <div {...props}>
          {#if item.content.$type === 'uri'}
            {item.content.uri}
          {:else if item.content.$type === 'data'}
            {item.content.mediaType}
          {:else if item.content.$type === 'error'}
            {item.content.message}
          {:else if item.content.$type === 'functionCall'}
            {item.content.name}
          {:else if item.content.$type === 'functionResult'}
            {item.content.callId}
          {:else}
            {item.content.$type}
          {/if}
        </div>
      {:else if item.type === 'tool'}
        <div {...props} data-hpd-message-tool>
          <ToolCall tool={item.tool} />
        </div>
      {:else if item.type === 'cursor'}
        <span {...props} data-hpd-message-cursor aria-hidden="true">|</span>
      {/if}
    {/each}
  </div>
{/if}
