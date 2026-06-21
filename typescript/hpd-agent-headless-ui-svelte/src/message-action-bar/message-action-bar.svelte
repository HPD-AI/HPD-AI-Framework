<svelte:options runes={true} />

<script lang="ts">
  import type { Attachment } from 'svelte/attachments';
  import {
    createMessageActionBarActions,
    createMessageActionBarElementProps,
    createMessageActionBarState,
    getDefaultMessageCopyText,
  } from './props.js';
  import type {
    MessageActionBarActions,
    MessageActionBarElementProps,
    MessageActionBarProps,
    MessageActionBarState,
  } from './types.js';

  let {
    autohide = 'never',
    children,
    copiedDuration = 1600,
    copyLabel = 'Copy',
    copyText,
    editLabel = 'Edit',
    float = 'never',
    hideWhenBusy = false,
    isLast = true,
    branchCount = 1,
    message,
    onCopy,
    onEditRequest,
    onRetryRequest,
    onRevisionCreated,
    retryLabel = 'Retry',
    revisions,
    status: providedStatus,
    ...restProps
  }: MessageActionBarProps = $props();

  let copied = $state(false);
  let copiedTimer = $state<ReturnType<typeof setTimeout> | null>(null);
  let focused = $state(false);
  let hovered = $state(false);
  let interactionCount = $state(0);
  let pending = $state(false);

  const resolvedCopyText = $derived<string>((copyText ?? getDefaultMessageCopyText)(message));
  const actionBarState = $derived<MessageActionBarState>(createMessageActionBarState({
    autohide,
    branchCount,
    copied,
    copyText: resolvedCopyText,
    float,
    focused,
    hideWhenBusy,
    hovered,
    interactionCount,
    isLast,
    message,
    onEditRequest,
    onRetryRequest,
    pending,
    revisions,
    status: providedStatus,
  }));

  function clearCopiedTimer(): void {
    if (!copiedTimer) return;
    clearTimeout(copiedTimer);
    copiedTimer = null;
  }

  function handleCopyClick(event: MouseEvent): void {
    event.preventDefault();
    void actions.copy();
  }

  function handleEditClick(event: MouseEvent): void {
    event.preventDefault();
    actions.requestEdit();
  }

  function handleRetryClick(event: MouseEvent): void {
    event.preventDefault();
    void actions.retry();
  }

  const actions = $derived<MessageActionBarActions>(createMessageActionBarActions({
    clearCopiedTimer,
    copiedDuration,
    copyText,
    message,
    onCopy,
    onEditRequest,
    onRetryRequest,
    onRevisionCreated,
    revisions,
    setCopied: (value) => {
      copied = value;
    },
    setCopiedTimer: (timer) => {
      copiedTimer = timer;
    },
    setInteractionCount: (update) => {
      interactionCount = update(interactionCount);
    },
    setPending: (value) => {
      pending = value;
    },
    state: actionBarState,
  }));

  const elementProps = $derived<MessageActionBarElementProps>(createMessageActionBarElementProps({
    copyLabel,
    editLabel,
    onCopyClick: handleCopyClick,
    onEditClick: handleEditClick,
    onRetryClick: handleRetryClick,
    restProps,
    retryLabel,
    state: actionBarState,
  }));

  const rootAttachment: Attachment<HTMLElement> = (node) => {
    const onPointerEnter = (): void => {
      hovered = true;
    };
    const onPointerLeave = (): void => {
      hovered = false;
    };
    const onFocusIn = (): void => {
      focused = true;
    };
    const onFocusOut = (): void => {
      focused = node.matches(':focus-within');
    };

    node.addEventListener('pointerenter', onPointerEnter);
    node.addEventListener('pointerleave', onPointerLeave);
    node.addEventListener('focusin', onFocusIn);
    node.addEventListener('focusout', onFocusOut);

    return () => {
      node.removeEventListener('pointerenter', onPointerEnter);
      node.removeEventListener('pointerleave', onPointerLeave);
      node.removeEventListener('focusin', onFocusIn);
      node.removeEventListener('focusout', onFocusOut);
      clearCopiedTimer();
    };
  };
</script>

{#if children && actionBarState.visible}
  {@render children({ actions, message, props: elementProps, rootAttachment, state: actionBarState })}
{:else if actionBarState.visible}
  <div {...elementProps.root} {@attach rootAttachment}>
    {#if actionBarState.canCopy}
      <button {...elementProps.copy}>{actionBarState.copied ? 'Copied' : copyLabel}</button>
    {/if}

    {#if actionBarState.canEdit}
      <button {...elementProps.edit}>{editLabel}</button>
    {/if}

    {#if actionBarState.canRetry}
      <button {...elementProps.retry}>{retryLabel}</button>
    {/if}
  </div>
{/if}
