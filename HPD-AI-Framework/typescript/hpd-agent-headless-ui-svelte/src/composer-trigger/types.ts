import type { Snippet } from 'svelte';
import type { SvelteHTMLElements } from 'svelte/elements';
import type {
  ComposerTriggerAdapter,
  ComposerTriggerApplyResult,
  ComposerTriggerBehaviorResult,
  ComposerTriggerCategory,
  ComposerTriggerDirectiveFormatter,
  ComposerTriggerItem,
  ComposerTriggerMatch,
} from '@hpd-research/hpd-agent-headless-ui';
import type { ThreadComposerRunConfig } from '../thread-composer/index.js';
import type {
  ComposerTriggerBehavior,
  ComposerTriggerSelectDetails,
} from './context.js';

type DivProps = Omit<SvelteHTMLElements['div'], 'children'>;
type ButtonProps = Omit<SvelteHTMLElements['button'], 'children'>;

export interface ComposerTriggerRootElementProps extends DivProps {
  'data-hpd-composer-trigger-root': '';
}

export interface ComposerTriggerPopoverElementProps extends DivProps {
  'aria-activedescendant'?: string;
  'aria-label': string;
  'data-hpd-composer-trigger-popover': '';
  'data-open'?: '';
  'data-trigger': string;
  role: 'listbox';
}

export interface ComposerTriggerItemElementProps extends ButtonProps {
  'aria-selected': boolean;
  'data-highlighted'?: '';
  'data-hpd-composer-trigger-item': '';
  'data-item-id': string;
  'data-item-type': string;
  role: 'option';
  type: 'button';
}

export interface ComposerTriggerCategoryElementProps extends ButtonProps {
  'data-category-id': string;
  'data-hpd-composer-trigger-category': '';
  type: 'button';
}

export interface ComposerTriggerBackElementProps extends ButtonProps {
  'data-hpd-composer-trigger-back': '';
  type: 'button';
}

export interface ComposerTriggerRootChildProps {
  additionalProperties?: Record<string, unknown>;
  cursor: number;
  inputRef: HTMLTextAreaElement | null;
  props: ComposerTriggerRootElementProps;
  runConfig?: ThreadComposerRunConfig;
  value: string;
}

export interface ComposerTriggerRootProps extends DivProps {
  additionalProperties?: Record<string, unknown>;
  children?: Snippet<[ComposerTriggerRootChildProps]>;
  cursor?: number;
  inputRef?: HTMLTextAreaElement | null;
  runConfig?: ThreadComposerRunConfig;
  value?: string;
}

export interface ComposerTriggerPopoverChildProps {
  actions: {
    selectItem(item: ComposerTriggerItem): Promise<void>;
    setCategory(categoryId: string | null): void;
    setHighlightedIndex(index: number): void;
  };
  behavior: ComposerTriggerBehavior | null;
  categories: readonly ComposerTriggerCategory[];
  items: readonly ComposerTriggerItem[];
  match: ComposerTriggerMatch | null;
  open: boolean;
  props: ComposerTriggerPopoverElementProps;
  query: string;
  trigger: string;
}

export interface ComposerTriggerPopoverProps extends DivProps {
  adapter?: ComposerTriggerAdapter;
  ariaLabel?: string;
  children?: Snippet<[ComposerTriggerPopoverChildProps]>;
  isLoading?: boolean;
  trigger: string;
}

export interface ComposerTriggerItemsChildProps {
  items: readonly ComposerTriggerItem[];
}

export interface ComposerTriggerItemsProps {
  children: Snippet<[ComposerTriggerItemsChildProps]>;
}

export interface ComposerTriggerCategoriesChildProps {
  categories: readonly ComposerTriggerCategory[];
}

export interface ComposerTriggerCategoriesProps {
  children: Snippet<[ComposerTriggerCategoriesChildProps]>;
}

export interface ComposerTriggerItemChildProps {
  highlighted: boolean;
  item: ComposerTriggerItem;
  props: ComposerTriggerItemElementProps;
  select(): Promise<void>;
}

export interface ComposerTriggerItemProps extends ButtonProps {
  children?: Snippet<[ComposerTriggerItemChildProps]>;
  index?: number;
  item: ComposerTriggerItem;
}

export interface ComposerTriggerCategoryChildProps {
  category: ComposerTriggerCategory;
  props: ComposerTriggerCategoryElementProps;
  select(): void;
}

export interface ComposerTriggerCategoryProps extends ButtonProps {
  category: ComposerTriggerCategory;
  children?: Snippet<[ComposerTriggerCategoryChildProps]>;
}

export interface ComposerTriggerBackChildProps {
  props: ComposerTriggerBackElementProps;
  select(): void;
}

export interface ComposerTriggerBackProps extends ButtonProps {
  children?: Snippet<[ComposerTriggerBackChildProps]>;
}

export interface ComposerTriggerDirectiveProps {
  additionalProperties?: (details: ComposerTriggerSelectDetails) => Record<string, unknown> | undefined;
  formatter?: ComposerTriggerDirectiveFormatter;
  onInserted?: (details: ComposerTriggerSelectDetails) => void | Promise<void>;
}

export interface ComposerTriggerActionProps {
  formatter?: ComposerTriggerDirectiveFormatter;
  onExecute: (details: ComposerTriggerSelectDetails) =>
    ComposerTriggerBehaviorResult | void | Promise<ComposerTriggerBehaviorResult | void>;
  removeOnExecute?: boolean;
}

export type {
  ComposerTriggerAdapter,
  ComposerTriggerApplyResult,
  ComposerTriggerBehaviorResult,
  ComposerTriggerCategory as ComposerTriggerCategoryData,
  ComposerTriggerDirectiveFormatter,
  ComposerTriggerItem as ComposerTriggerItemData,
  ComposerTriggerMatch,
  ComposerTriggerSelectDetails,
};
