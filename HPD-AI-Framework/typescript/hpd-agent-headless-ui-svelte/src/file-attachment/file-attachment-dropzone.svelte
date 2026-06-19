<svelte:options runes={true} />

<script lang="ts">
  import {
    createFileAttachmentDropzoneActions,
    createFileAttachmentDropzoneElementProps,
    createFileAttachmentDropzoneState,
  } from './props.js';
  import type {
    FileAttachmentDropzoneActions,
    FileAttachmentDropzoneApi,
    FileAttachmentDropzoneElementProps,
    FileAttachmentDropzoneProps,
    FileAttachmentDropzoneState,
  } from './types.js';

  let {
    child,
    children,
    disabled = false,
    state: attachments,
    ...restProps
  }: FileAttachmentDropzoneProps = $props();

  let dragging = $state(false);

  const isDisabled = $derived(disabled || attachments.disabled);
  const dropzoneState = $derived<FileAttachmentDropzoneState>(createFileAttachmentDropzoneState({
    disabled: isDisabled,
    dragging,
  }));

  const actions = $derived<FileAttachmentDropzoneActions>(createFileAttachmentDropzoneActions({
    add: attachments.add,
    setDragging(value) {
      dragging = value;
    },
  }));

  function handleDragEnter(event: DragEvent): void {
    if (isDisabled) return;
    event.preventDefault();
    dragging = true;
  }

  function handleDragOver(event: DragEvent): void {
    if (isDisabled) return;
    event.preventDefault();
    if (!dragging) dragging = true;
  }

  function handleDragLeave(event: DragEvent): void {
    if (isDisabled) return;
    event.preventDefault();
    const current = event.currentTarget;
    const next = event.relatedTarget;
    if (current instanceof Node && next instanceof Node && current.contains(next)) return;
    dragging = false;
  }

  function handleDrop(event: DragEvent): void {
    if (isDisabled) return;
    event.preventDefault();
    void actions.drop(event);
  }

  const elementProps = $derived<FileAttachmentDropzoneElementProps>(
    createFileAttachmentDropzoneElementProps({
      disabled: isDisabled,
      dragging,
      onDragEnter: handleDragEnter,
      onDragLeave: handleDragLeave,
      onDragOver: handleDragOver,
      onDrop: handleDrop,
      rootProps: restProps,
    }),
  );

  const api = $derived<FileAttachmentDropzoneApi>({
    actions,
    props: elementProps,
    state: dropzoneState,
  });
</script>

{#if child}
  {@render child(api)}
{:else}
  <div {...elementProps.root}>
    {#if children}
      {@render children(api)}
    {/if}
  </div>
{/if}
