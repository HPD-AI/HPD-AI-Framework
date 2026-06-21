import type {
  ComposerTriggerBackElementProps,
  ComposerTriggerCategoryElementProps,
  ComposerTriggerItemElementProps,
  ComposerTriggerPopoverElementProps,
  ComposerTriggerRootElementProps,
} from './types.js';
import type {
  ComposerTriggerCategory,
  ComposerTriggerItem,
} from '@hpd-research/hpd-agent-headless-ui';
import { mergeProps } from '../thread-composer/index.js';

export function createComposerTriggerRootElementProps(
  restProps: Record<string, unknown>,
): ComposerTriggerRootElementProps {
  return mergeProps(restProps, {
    'data-hpd-composer-trigger-root': '',
  }) as unknown as ComposerTriggerRootElementProps;
}

export function createComposerTriggerPopoverElementProps(options: {
  ariaLabel: string;
  highlightedItemId?: string;
  open: boolean;
  restProps: Record<string, unknown>;
  trigger: string;
}): ComposerTriggerPopoverElementProps {
  return mergeProps(options.restProps, {
    'aria-activedescendant': options.highlightedItemId,
    'aria-label': options.ariaLabel,
    'data-hpd-composer-trigger-popover': '',
    'data-open': options.open ? '' : undefined,
    'data-trigger': options.trigger,
    role: 'listbox',
  }) as unknown as ComposerTriggerPopoverElementProps;
}

export function createComposerTriggerItemElementProps(options: {
  highlighted: boolean;
  item: ComposerTriggerItem;
  onClick: (event: MouseEvent) => void;
  restProps: Record<string, unknown>;
}): ComposerTriggerItemElementProps {
  return mergeProps(options.restProps, {
    'aria-selected': options.highlighted,
    'data-highlighted': options.highlighted ? '' : undefined,
    'data-hpd-composer-trigger-item': '',
    'data-item-id': options.item.id,
    'data-item-type': options.item.type,
    onclick: options.onClick,
    role: 'option',
    type: 'button',
  }) as unknown as ComposerTriggerItemElementProps;
}

export function createComposerTriggerCategoryElementProps(options: {
  category: ComposerTriggerCategory;
  onClick: (event: MouseEvent) => void;
  restProps: Record<string, unknown>;
}): ComposerTriggerCategoryElementProps {
  return mergeProps(options.restProps, {
    'data-category-id': options.category.id,
    'data-hpd-composer-trigger-category': '',
    onclick: options.onClick,
    type: 'button',
  }) as unknown as ComposerTriggerCategoryElementProps;
}

export function createComposerTriggerBackElementProps(options: {
  onClick: (event: MouseEvent) => void;
  restProps: Record<string, unknown>;
}): ComposerTriggerBackElementProps {
  return mergeProps(options.restProps, {
    'data-hpd-composer-trigger-back': '',
    onclick: options.onClick,
    type: 'button',
  }) as unknown as ComposerTriggerBackElementProps;
}
