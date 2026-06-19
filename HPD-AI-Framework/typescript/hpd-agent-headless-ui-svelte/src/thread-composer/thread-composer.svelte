<svelte:options runes={true} />

<script lang="ts">
  import type { Attachment } from 'svelte/attachments';
  import { createTextContent } from '@hpd-research/hpd-agent-client';
  import type { ThreadStateSnapshot } from '../thread-state.js';
  import {
    applyThreadComposerAutosize,
    readTextareaAutosizeMetrics,
    type ThreadComposerAutosizeMetrics,
  } from './autosize.js';
  import {
    createThreadComposerActions,
    createThreadComposerElementProps,
    createThreadComposerState,
    shouldSubmitForKeyboardEvent,
  } from './props.js';
  import type {
    ThreadComposerApi,
    ThreadComposerBlockedReason,
    ThreadComposerActions,
    ThreadComposerElementProps,
    ThreadComposerProps,
    ThreadComposerState,
  } from './types.js';

  let {
    attachments,
    thread,
    value = $bindable(''),
    textareaRef = $bindable(null),
    additionalProperties,
    quote = $bindable(null),
    runConfig,
    placeholder = 'Type a message...',
    disabled = false,
    clear: clearMode = 'on-submit',
    submitMode = 'enter',
    minRows = 1,
    maxRows = 8,
    autosize = 'pretext',
    pretext,
    child,
    children,
    ...restProps
  }: ThreadComposerProps = $props();

  let current = $state<ThreadStateSnapshot | null>(null);
  let focused = $state(false);
  let isSubmitting = $state(false);

  $effect(() => {
    current = thread.getSnapshot();
    return thread.subscribe((snapshot: ThreadStateSnapshot) => {
      current = snapshot;
    });
  });

  const snapshot = $derived(current ?? thread.getSnapshot());
  const composerAttachments = $derived(attachments?.attachments ?? []);
  const readyContents = $derived(attachments?.readyContents ?? []);
  const hasAttachments = $derived(composerAttachments.length > 0);
  const isUploadingAttachments = $derived(attachments?.isUploading ?? false);
  const hasAttachmentError = $derived(attachments?.hasError ?? false);
  const isEmpty = $derived(value.trim().length === 0 && !hasAttachments);
  const isBusy = $derived(snapshot.textSubmissionState.reason === 'busy' || isSubmitting || isUploadingAttachments);
  const canInterrupt = $derived(
    snapshot.activity.streaming ||
    snapshot.activity.reasoning ||
    snapshot.textSubmissionState.reason === 'busy',
  );
  const blockedReason = $derived.by<ThreadComposerBlockedReason>(() : ThreadComposerBlockedReason => {
    if (disabled) return 'disabled';
    if (isUploadingAttachments) return 'attachments-uploading';
    if (hasAttachmentError) return 'attachment-error';
    if (isEmpty) return 'empty';
    return snapshot.textSubmissionState.reason;
  });
  const canSubmit = $derived(blockedReason === null && !isSubmitting);

  function setValue(nextValue: string): void {
    value = nextValue;
  }

  function clear(): void {
    value = '';
  }

  async function submit(): Promise<void> {
    if (!canSubmit) return;

    const submittedValue = value.trim();
    isSubmitting = true;
    try {
      const submittedAttachments = dedupeAttachmentContents(readyContents);
      const contents = submittedValue.length > 0
        ? [createTextContent(submittedValue), ...submittedAttachments]
        : submittedAttachments;
      const messageAdditionalProperties = createMessageAdditionalProperties(additionalProperties, quote);
      await thread.sendMessage({
        contents,
        ...(messageAdditionalProperties ? { additionalProperties: messageAdditionalProperties } : {}),
      }, { runConfig });
      if (clearMode === 'on-submit') {
        clear();
        quote = null;
        attachments?.clear();
      }
    } finally {
      isSubmitting = false;
    }
  }

  function dedupeAttachmentContents(contents: typeof readyContents): typeof readyContents {
    const seen = new Set<string>();
    return contents.filter((content) => {
      const additionalProperties = content.additionalProperties as { contentId?: unknown } | undefined;
      const key = additionalProperties?.contentId
        ?? (content.$type === 'uri' ? content.uri : undefined);
      if (!key) return true;
      if (seen.has(String(key))) return false;
      seen.add(String(key));
      return true;
    });
  }

  function createMessageAdditionalProperties(
    base: Record<string, unknown> | undefined,
    selectedQuote: typeof quote,
  ): Record<string, unknown> | undefined {
    if (!base && !selectedQuote) return undefined;
    return {
      ...(base ?? {}),
      ...(selectedQuote ? { quote: selectedQuote } : {}),
    };
  }

  async function interrupt(): Promise<void> {
    if (!canInterrupt) return;
    await thread.interrupt();
  }

  function focus(options?: FocusOptions): void {
    textareaRef?.focus(options);
  }

  function handleInput(event: InputEvent): void {
    const target = event.currentTarget;
    if (!(target instanceof HTMLTextAreaElement)) return;
    value = target.value;
  }

  function handleFocus(): void {
    focused = true;
  }

  function handleBlur(): void {
    focused = false;
  }

  function handleKeydown(event: KeyboardEvent): void {
    if (!shouldSubmitForKeyboardEvent(event, submitMode)) return;
    event.preventDefault();
    void submit();
  }

  function handleSubmit(event: SubmitEvent): void {
    event.preventDefault();
    void submit();
  }

  function handleInterruptClick(event: MouseEvent): void {
    event.preventDefault();
    void interrupt();
  }

  const textareaAttachment: Attachment<HTMLTextAreaElement> = (node) => {
    textareaRef = node;
    let metrics: ThreadComposerAutosizeMetrics | null = readTextareaAutosizeMetrics(node, pretext);
    const apply = (): void => {
      applyThreadComposerAutosize(node, value, autosize, metrics, minRows, maxRows);
    };

    let observer: ResizeObserver | null = null;
    if (typeof ResizeObserver !== 'undefined') {
      observer = new ResizeObserver(() => {
        metrics = readTextareaAutosizeMetrics(node, pretext);
        apply();
      });
      observer.observe(node);
    }

    $effect(() => {
      metrics = readTextareaAutosizeMetrics(node, pretext);
      apply();
    });

    return () => {
      observer?.disconnect();
      if (textareaRef === node) textareaRef = null;
    };
  };

  const composerState = $derived<ThreadComposerState>(createThreadComposerState({
    attachments: composerAttachments,
    blockedReason,
    canInterrupt,
    canSubmit,
    disabled,
    focused,
    isBusy,
    isEmpty,
    isSubmitting,
    readyContents,
    textSubmissionState: snapshot.textSubmissionState,
    value,
  }));

  const composerActions = $derived<ThreadComposerActions>(createThreadComposerActions({
    clear,
    focus,
    interrupt,
    setValue,
    submit,
  }));

  const elementProps = $derived<ThreadComposerElementProps>(createThreadComposerElementProps({
    autosize,
    blockedReason,
    canInterrupt,
    canSubmit,
    disabled,
    focused,
    formProps: restProps,
    inputAttachment: textareaAttachment,
    isBusy,
    isEmpty,
    isSubmitting,
    onBlur: handleBlur,
    onFocus: handleFocus,
    onInput: handleInput,
    onInterruptClick: handleInterruptClick,
    onKeydown: handleKeydown,
    onSubmit: handleSubmit,
    placeholder,
    value,
  }));

  const api = $derived<ThreadComposerApi>({
    actions: composerActions,
    props: elementProps,
    state: composerState,
    textareaRef,
  });
</script>

{#if child}
  {@render child(api)}
{:else}
  <form {...elementProps.root}>
    <textarea
      {...elementProps.input}
      {@attach elementProps.inputAttachment}
    ></textarea>
    {#if children}
      {@render children(api)}
    {:else}
      <button {...elementProps.submit}>Send</button>
    {/if}
  </form>
{/if}
