<svelte:options runes={true} />

<script lang="ts">
  import type { Attachment } from 'svelte/attachments';
  import type {
    ThreadRevisionResult,
  } from '@hpd-research/hpd-agent-headless-ui';
  import {
    applyThreadComposerAutosize,
    readTextareaAutosizeMetrics,
    type ThreadComposerAutosizeMetrics,
  } from '../thread-composer/autosize.js';
  import { shouldSubmitForKeyboardEvent } from '../thread-composer/props.js';
  import {
    createMessageEditActionProps,
    createMessageEditElementProps,
  } from './props.js';
  import type {
    MessageEditApi,
    MessageEditActions,
    MessageEditProps,
  } from './types.js';

  let {
    message,
    revisions,
    runConfig,
    forkOptions,
    editing = $bindable<boolean>(false),
    draft = $bindable<string>(message.content),
    placeholder = 'Edit message...',
    saveLabel = 'Fork with replacement',
    cancelLabel = 'Cancel edit',
    submitMode = 'enter',
    minRows = 1,
    maxRows = 8,
    autosize = 'pretext',
    pretext,
    view,
    edit,
    onStartEdit,
    onSaved,
    onCancel,
    onError,
    ...restProps
  }: MessageEditProps = $props();

  let pending = $state(false);
  let error = $state<Error | null>(null);
  let textareaRef = $state<HTMLTextAreaElement | null>(null);

  const canSave = $derived<boolean>(editing && !pending && draft.trim().length > 0);

  function setDraft(nextValue: string): void {
    draft = nextValue;
  }

  function startEdit(): void {
    draft = message.content;
    error = null;
    editing = true;
    onStartEdit?.({ message });
  }

  function cancel(): void {
    if (pending) return;
    editing = false;
    draft = message.content;
    error = null;
    onCancel?.({ message });
  }

  async function save(): Promise<ThreadRevisionResult | undefined> {
    if (!canSave) return undefined;

    const text = draft.trim();
    pending = true;
    error = null;
    try {
      const revision = await revisions.forkAndEditMessage(message.id, text, {
        runConfig,
        fork: forkOptions,
      });
      editing = false;
      await onSaved?.({ message, revision, text });
      return revision;
    } catch (caught) {
      error = caught instanceof Error ? caught : new Error(String(caught));
      onError?.({ message, error });
      return undefined;
    } finally {
      pending = false;
    }
  }

  function handleInput(event: Event): void {
    const target = event.currentTarget;
    if (!(target instanceof HTMLTextAreaElement)) return;
    draft = target.value;
  }

  function handleKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      cancel();
      return;
    }

    if (!shouldSubmitForKeyboardEvent(event, submitMode)) return;
    event.preventDefault();
    void save();
  }

  function handleSaveClick(event: MouseEvent): void {
    event.preventDefault();
    void save();
  }

  function handleCancelClick(event: MouseEvent): void {
    event.preventDefault();
    cancel();
  }

  const textareaAttachment: Attachment<HTMLTextAreaElement> = (node) => {
    textareaRef = node;
    let metrics: ThreadComposerAutosizeMetrics | null = readTextareaAutosizeMetrics(node, pretext);
    const apply = (): void => {
      applyThreadComposerAutosize(node, draft, autosize, metrics, minRows, maxRows);
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

  const elementProps = $derived(createMessageEditElementProps({
    canSave,
    cancelLabel,
    draft,
    editing,
    error,
    pending,
    placeholder,
    restProps,
    saveLabel,
    onCancelClick: handleCancelClick,
    onInput: handleInput,
    onKeydown: handleKeydown,
    onSaveClick: handleSaveClick,
  }));

  const actionProps = $derived(createMessageEditActionProps({
    canSave,
    cancelLabel,
    draft,
    editing,
    error,
    pending,
    placeholder,
    restProps: {},
    saveLabel,
    onCancelClick: handleCancelClick,
    onInput: handleInput,
    onKeydown: handleKeydown,
    onSaveClick: handleSaveClick,
  }));

  const actions = $derived<MessageEditActions>({
    cancel,
    save,
    setDraft,
    startEdit,
  });

  const api = $derived<MessageEditApi>({
    actions,
    actionProps,
    canSave,
    draft,
    editing,
    error,
    pending,
    props: elementProps,
    textareaAttachment,
    textareaRef,
  });
</script>

<div {...elementProps.root}>
  {#if editing}
    {#if edit}
      {@render edit({ ...api, message })}
    {:else}
      <textarea
        {...elementProps.textarea}
        {@attach textareaAttachment}
      ></textarea>
      <button {...actionProps.cancel}>Cancel</button>
      <button {...actionProps.save}>Fork with replacement</button>
    {/if}
  {:else if view}
    {@render view({ ...api, message })}
  {:else}
    <div data-hpd-message-edit-view>{message.content}</div>
    <button type="button" onclick={actions.startEdit}>Edit</button>
  {/if}
</div>
