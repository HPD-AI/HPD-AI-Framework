<svelte:options runes={true} />

<script lang="ts">
  import {
    applyComposerTriggerDirective,
    detectComposerTrigger,
    getComposerTriggerCategories,
    getComposerTriggerItems,
    mergeComposerTriggerBehaviorResult,
    type ComposerTriggerItem,
  } from '@hpd-research/hpd-agent-headless-ui';
  import {
    createComposerTriggerPopoverElementProps,
  } from './props.js';
  import {
    getComposerTriggerRootContext,
    setComposerTriggerPopoverContext,
    type ComposerTriggerBehavior,
    type ComposerTriggerPopoverContext,
    type ComposerTriggerSelectDetails,
  } from './context.js';
  import type { ComposerTriggerPopoverProps } from './types.js';

  let {
    adapter,
    ariaLabel = 'Composer trigger suggestions',
    children,
    trigger,
    ...restProps
  }: ComposerTriggerPopoverProps = $props();

  const root = getComposerTriggerRootContext();
  let behavior = $state<ComposerTriggerBehavior | null>(null);
  let categoryId = $state<string | null>(null);
  let highlightedIndex = $state(0);

  const match = $derived(detectComposerTrigger(root.getValue(), root.getCursor(), trigger));
  const query = $derived(match?.query ?? '');
  const categories = $derived(getComposerTriggerCategories(adapter));
  const items = $derived(getComposerTriggerItems(adapter, query, categoryId));
  const open = $derived(Boolean(match && behavior && (items.length > 0 || categories.length > 0)));
  const highlightedItem = $derived(items[highlightedIndex] ?? items[0]);
  const highlightedItemId = $derived(open && highlightedItem
    ? `hpd-composer-trigger-${trigger}-${highlightedItem.id}`
    : undefined);
  const elementProps = $derived(createComposerTriggerPopoverElementProps({
    ariaLabel,
    highlightedItemId,
    open,
    restProps,
    trigger,
  }));

  async function selectItem(item: ComposerTriggerItem): Promise<void> {
    const activeMatch = match;
    const activeBehavior = behavior;
    if (!activeMatch || !activeBehavior) return;

    const selection = {
      item,
      match: activeMatch,
      trigger,
    };
    let result = applyComposerTriggerDirective({
      formatter: activeBehavior.formatter,
      removeOnExecute: activeBehavior.kind === 'action'
        ? activeBehavior.removeOnExecute
        : false,
      selection,
      text: root.getValue(),
    });
    const details: ComposerTriggerSelectDetails = {
      ...selection,
      result,
    };

    if (activeBehavior.kind === 'action') {
      const behaviorResult = await activeBehavior.onExecute?.(details);
      result = mergeComposerTriggerBehaviorResult(result, behaviorResult);
    } else {
      await activeBehavior.onInserted?.(details);
    }

    root.applyResult(result);
    categoryId = null;
    highlightedIndex = 0;
  }

  const context: ComposerTriggerPopoverContext = {
    get adapter() {
      return adapter;
    },
    get categories() {
      return categories;
    },
    getBehavior: () => behavior,
    getHighlightedIndex: () => highlightedIndex,
    getItems: () => items,
    getMatch: () => match,
    getQuery: () => query,
    isOpen: () => open,
    registerBehavior(nextBehavior) {
      behavior = nextBehavior;
      return () => {
        behavior = null;
      };
    },
    selectItem,
    setCategory(nextCategoryId) {
      categoryId = nextCategoryId;
      highlightedIndex = 0;
    },
    setHighlightedIndex(index) {
      highlightedIndex = index;
    },
    get trigger() {
      return trigger;
    },
  };

  setComposerTriggerPopoverContext(context);

</script>

<div {...elementProps} hidden={!open}>
  {@render children?.({
    actions: {
      selectItem,
      setCategory: context.setCategory,
      setHighlightedIndex: context.setHighlightedIndex,
    },
    behavior,
    categories,
    items,
    match,
    open,
    props: elementProps,
    query,
    trigger,
  })}
</div>
