<svelte:options runes={true} />

<script lang="ts">
  import type { Attachment } from 'svelte/attachments';
  import { setSelectionToolbarContext } from './context.js';
  import {
    createSelectionToolbarRootElementProps,
    createSelectionToolbarState,
    createThreadQuoteFromSelection,
    getSelectionToolbarPosition,
    readSelectionWithinRoot,
  } from './props.js';
  import type {
    SelectionToolbarActions,
    SelectionToolbarRootContext,
    SelectionToolbarRootProps,
    SelectionToolbarSelection,
    ThreadQuote,
  } from './types.js';

  let {
    child,
    children,
    clearSelectionOnQuote = true,
    closeOnQuote = true,
    disabled = false,
    minLength = 1,
    offset = 8,
    onQuote,
    placement = 'above',
    quote = $bindable<ThreadQuote | null>(null),
    toolbarLabel = 'Selected text actions',
    ...restProps
  }: SelectionToolbarRootProps = $props();

  let rootRef = $state<HTMLElement | null>(null);
  let selection = $state<SelectionToolbarSelection | null>(null);
  let position = $state<{ left: number; top: number } | null>(null);

  function refresh(): void {
    if (disabled || typeof document === 'undefined') {
      close();
      return;
    }

    const nextSelection = readSelectionWithinRoot(rootRef, document.getSelection());
    selection = nextSelection;
    position = getSelectionToolbarPosition(nextSelection, {
      offset,
      placement,
    });
  }

  function close(): void {
    selection = null;
    position = null;
  }

  function clearSelection(): void {
    if (typeof document === 'undefined') return;
    document.getSelection()?.removeAllRanges();
  }

  function setQuote(nextQuote: ThreadQuote | null): void {
    quote = nextQuote;
  }

  function handleKeyup(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      close();
      return;
    }
    refresh();
  }

  function handlePointerDown(event: PointerEvent): void {
    const target = event.target;
    if (target instanceof Node && rootRef?.contains(target)) return;
    close();
  }

  const rootAttachment: Attachment<HTMLElement> = (node) => {
    rootRef = node;

    if (typeof document === 'undefined' || typeof window === 'undefined') {
      return () => {
        if (rootRef === node) rootRef = null;
      };
    }

    const onSelectionChange = (): void => refresh();
    const onPointerUp = (): void => refresh();
    const onKeyup = (event: KeyboardEvent): void => handleKeyup(event);
    const onScroll = (): void => refresh();
    const onResize = (): void => refresh();
    const onPointerDown = (event: PointerEvent): void => handlePointerDown(event);

    document.addEventListener('selectionchange', onSelectionChange);
    document.addEventListener('pointerup', onPointerUp);
    document.addEventListener('keyup', onKeyup);
    document.addEventListener('pointerdown', onPointerDown, true);
    window.addEventListener('scroll', onScroll, true);
    window.addEventListener('resize', onResize);

    refresh();

    return () => {
      document.removeEventListener('selectionchange', onSelectionChange);
      document.removeEventListener('pointerup', onPointerUp);
      document.removeEventListener('keyup', onKeyup);
      document.removeEventListener('pointerdown', onPointerDown, true);
      window.removeEventListener('scroll', onScroll, true);
      window.removeEventListener('resize', onResize);
      if (rootRef === node) rootRef = null;
    };
  };

  const model = $derived(createSelectionToolbarState({
    disabled,
    minLength,
    placement,
    position,
    quote,
    selection,
  }));

  const actions = $derived<SelectionToolbarActions>({
    clearSelection,
    close,
    quote() {
      const nextQuote = createThreadQuoteFromSelection(model.selection);
      if (!model.open || !model.selection || !nextQuote) return null;

      quote = nextQuote;
      void onQuote?.(nextQuote, model.selection);

      if (clearSelectionOnQuote) {
        clearSelection();
      }
      if (closeOnQuote) {
        close();
      }

      return nextQuote;
    },
    refresh,
    setQuote,
  });

  const elementProps = $derived(createSelectionToolbarRootElementProps({
    restProps,
    state: model,
    toolbarLabel,
  }));

  const context = $derived<SelectionToolbarRootContext>({
    actions,
    props: elementProps,
    rootAttachment,
    rootRef,
    state: model,
  });

  setSelectionToolbarContext({
    get actions() {
      return actions;
    },
    get props() {
      return elementProps;
    },
    get rootAttachment() {
      return rootAttachment;
    },
    get rootRef() {
      return rootRef;
    },
    get state() {
      return model;
    },
  });
</script>

{#if child}
  {@render child(context)}
{:else}
  <div {...elementProps.root} {@attach rootAttachment}>
    {@render children?.(context)}
  </div>
{/if}
