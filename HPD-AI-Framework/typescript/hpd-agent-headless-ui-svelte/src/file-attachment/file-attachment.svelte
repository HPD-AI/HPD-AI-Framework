<svelte:options runes={true} />

<script lang="ts">
  import { FileAttachmentState } from './file-attachment-state.svelte.js';
  import type { FileAttachmentProps } from './types.js';

  let {
    accept,
    child,
    children,
    client,
    disabled = false,
    multiple = true,
    sessionId,
    state,
    threadId,
    triggerLabel = 'Attach files',
    upload,
    ...restProps
  }: FileAttachmentProps = $props();

  const ownedState = $derived.by(() => {
    if (state) return state;
    if (!sessionId || !threadId) return null;
    return new FileAttachmentState({
      client,
      disabled,
      sessionId,
      threadId,
      upload,
    });
  });

  $effect(() => {
    if (ownedState) ownedState.disabled = disabled;
  });

  function handleInputChange(event: Event): void {
    const target = event.currentTarget;
    if (!(target instanceof HTMLInputElement) || !target.files) return;
    void ownedState?.add(target.files);
  }

  const elementProps = $derived(ownedState?.createElementProps({
    accept,
    multiple,
    onInputChange: handleInputChange,
    rootProps: restProps,
    triggerLabel,
  }));
  const api = $derived(ownedState && elementProps ? ownedState.createApi(elementProps) : null);
</script>

{#if api}
  {#if child}
    {@render child(api)}
  {:else}
    <div {...api.props.root}>
      <input
        {...api.props.input}
        {@attach api.props.inputAttachment}
      />
      {#if children}
        {@render children(api)}
      {:else}
        <button {...api.props.trigger}>{triggerLabel}</button>
      {/if}
    </div>
  {/if}
{/if}
