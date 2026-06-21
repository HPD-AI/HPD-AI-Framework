<svelte:options runes={true} />

<script lang="ts">
  import {
    canEditThreadMessage,
    canRetryThreadMessage,
    getMessageStatus,
  } from '@hpd-research/hpd-agent-headless-ui';
  import MessageActionBar from '../message-action-bar/message-action-bar.svelte';
  import {
    createMessageActionBarActions,
    getDefaultMessageCopyText,
    createMessageActionBarElementProps,
  } from '../message-action-bar/index.js';
  import MessageParts from './message-parts.svelte';
  import {
    createMessageElementProps,
    createMessageParts,
  } from './props.js';
  import type { MessageProps } from './types.js';

  let {
    message,
    showActions = false,
    copyText,
    onCopy,
    onEditRequest,
    onRetryRequest,
    child,
    children,
    actionBar: renderActionBar,
    ...restProps
  }: MessageProps = $props();

  const status = $derived(getMessageStatus(message));
  const parts = $derived(createMessageParts(message));
  const elementProps = $derived(createMessageElementProps(message, restProps, status));
  const canRequestEdit = $derived(Boolean(onEditRequest) && canEditThreadMessage(message));
  const canRequestRetry = $derived(Boolean(onRetryRequest) && canRetryThreadMessage(message));
  const resolvedCopyText = $derived((copyText ?? getDefaultMessageCopyText)(message));
  const actionState = $derived({
    canCopy: resolvedCopyText.length > 0,
    canEdit: canRequestEdit,
    canRetry: canRequestRetry,
    copied: false,
    floating: false,
    focused: false,
    hovered: false,
    pending: false,
    status,
    visible: true,
  });
  const messageActions = $derived(createMessageActionBarActions({
    message,
    copyText,
    onCopy,
    onEditRequest,
    onRetryRequest,
    state: actionState,
  }));
  const actionProps = $derived(createMessageActionBarElementProps({
    state: actionState,
    onCopyClick: () => void messageActions.copy(),
    onEditClick: () => messageActions.requestEdit(),
    onRetryClick: () => void messageActions.retry(),
  }));
  const shouldRenderActions = $derived(Boolean(renderActionBar) || showActions);
</script>

{#if child}
  {@render child({ props: elementProps, message, parts, status, actions: messageActions, actionProps })}
{:else}
  <div {...elementProps}>
    {#if children}
      {@render children({ message, parts, status })}
    {:else}
      <MessageParts {message} />
    {/if}

    {#if shouldRenderActions}
      {#if renderActionBar}
        <MessageActionBar
          {copyText}
          {message}
          {onCopy}
          {onEditRequest}
          {onRetryRequest}
          {status}
        >
          {#snippet children(action)}
            {@render renderActionBar(action)}
          {/snippet}
        </MessageActionBar>
      {:else}
        <MessageActionBar
          {copyText}
          {message}
          {onCopy}
          {onEditRequest}
          {onRetryRequest}
          {status}
        />
      {/if}
    {/if}
  </div>
{/if}
