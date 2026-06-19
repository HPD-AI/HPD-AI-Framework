import { getContext, setContext } from 'svelte';
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

const rootKey = Symbol('hpd-composer-trigger-root');
const popoverKey = Symbol('hpd-composer-trigger-popover');

export interface ComposerTriggerBehavior {
  formatter?: ComposerTriggerDirectiveFormatter;
  kind: 'directive' | 'action';
  onExecute?: (details: ComposerTriggerSelectDetails) =>
    ComposerTriggerBehaviorResult | void | Promise<ComposerTriggerBehaviorResult | void>;
  onInserted?: (details: ComposerTriggerSelectDetails) => void | Promise<void>;
  removeOnExecute?: boolean;
}

export interface ComposerTriggerSelectDetails {
  item: ComposerTriggerItem;
  match: ComposerTriggerMatch;
  result: ComposerTriggerApplyResult;
  trigger: string;
}

export interface ComposerTriggerRootContext {
  applyResult(result: ComposerTriggerApplyResult): void;
  getAdditionalProperties(): Record<string, unknown> | undefined;
  getCursor(): number;
  getInput(): HTMLTextAreaElement | null;
  getRunConfig(): ThreadComposerRunConfig | undefined;
  getValue(): string;
  mergeAdditionalProperties(patch: Record<string, unknown> | undefined): void;
  mergeRunConfig(patch: ThreadComposerRunConfig | undefined): void;
  setCursor(cursor: number): void;
  setValue(value: string): void;
}

export interface ComposerTriggerPopoverContext {
  readonly adapter: ComposerTriggerAdapter | undefined;
  categories: readonly ComposerTriggerCategory[];
  getBehavior(): ComposerTriggerBehavior | null;
  getHighlightedIndex(): number;
  getItems(): readonly ComposerTriggerItem[];
  getMatch(): ComposerTriggerMatch | null;
  getQuery(): string;
  isOpen(): boolean;
  registerBehavior(behavior: ComposerTriggerBehavior): () => void;
  selectItem(item: ComposerTriggerItem): Promise<void>;
  setCategory(categoryId: string | null): void;
  setHighlightedIndex(index: number): void;
  readonly trigger: string;
}

export function setComposerTriggerRootContext(context: ComposerTriggerRootContext): void {
  setContext(rootKey, context);
}

export function getComposerTriggerRootContext(): ComposerTriggerRootContext {
  const context = getContext<ComposerTriggerRootContext | undefined>(rootKey);
  if (!context) {
    throw new Error('Composer trigger primitives must be used inside ComposerTriggerRoot.');
  }
  return context;
}

export function setComposerTriggerPopoverContext(context: ComposerTriggerPopoverContext): void {
  setContext(popoverKey, context);
}

export function getComposerTriggerPopoverContext(): ComposerTriggerPopoverContext {
  const context = getContext<ComposerTriggerPopoverContext | undefined>(popoverKey);
  if (!context) {
    throw new Error('Composer trigger popover primitives must be used inside ComposerTriggerPopover.');
  }
  return context;
}
