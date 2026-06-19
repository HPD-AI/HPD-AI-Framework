import { mergeProps } from '../thread-composer/index.js';
import type {
  SelectionToolbarPlacement,
  SelectionToolbarPosition,
  SelectionToolbarQuoteElementProps,
  SelectionToolbarRootElementProps,
  SelectionToolbarSelection,
  SelectionToolbarState,
  SelectionToolbarToolbarElementProps,
  ThreadQuote,
} from './types.js';

type EventHandler<TEvent extends Event> = (event: TEvent) => void;

export interface CreateSelectionToolbarStateOptions {
  disabled?: boolean;
  minLength?: number;
  placement?: SelectionToolbarPlacement;
  position?: SelectionToolbarPosition | null;
  quote?: ThreadQuote | null;
  selection?: SelectionToolbarSelection | null;
}

export interface CreateSelectionToolbarRootElementPropsOptions {
  state: SelectionToolbarState;
  restProps?: Record<string, unknown>;
  toolbarLabel?: string;
}

export interface CreateSelectionToolbarQuoteElementPropsOptions {
  label?: string;
  onClick: EventHandler<MouseEvent>;
  restProps?: Record<string, unknown>;
  state: SelectionToolbarState;
}

export function createSelectionToolbarState(
  options: CreateSelectionToolbarStateOptions,
): SelectionToolbarState {
  const disabled = options.disabled ?? false;
  const minLength = Math.max(1, Math.floor(options.minLength ?? 1));
  const selection = options.selection ?? null;
  const open = !disabled && selection !== null && selection.text.trim().length >= minLength;

  return {
    disabled,
    minLength,
    open,
    placement: options.placement ?? 'above',
    position: open ? options.position ?? null : null,
    quote: options.quote ?? null,
    selection: open ? selection : null,
  };
}

export function createSelectionToolbarRootElementProps(
  options: CreateSelectionToolbarRootElementPropsOptions,
): {
  root: SelectionToolbarRootElementProps;
  toolbar: SelectionToolbarToolbarElementProps;
} {
  const { state, restProps = {} } = options;

  return {
    root: mergeProps(restProps, {
      'data-hpd-selection-toolbar-root': '',
      'data-disabled': state.disabled ? '' : undefined,
      'data-open': state.open ? '' : undefined,
    }) as unknown as SelectionToolbarRootElementProps,
    toolbar: {
      'aria-label': options.toolbarLabel ?? 'Selected text actions',
      'data-hpd-selection-toolbar': '',
      'data-open': state.open ? '' : undefined,
      'data-placement': state.placement,
      role: 'toolbar',
      style: createSelectionToolbarStyle(state),
    },
  };
}

export function createSelectionToolbarQuoteElementProps(
  options: CreateSelectionToolbarQuoteElementPropsOptions,
): SelectionToolbarQuoteElementProps {
  const disabled = !options.state.open || !options.state.selection;

  return mergeProps(options.restProps ?? {}, {
    'aria-disabled': disabled,
    'aria-label': options.label ?? 'Quote selected text',
    'data-hpd-selection-toolbar-quote': '',
    disabled,
    onclick: options.onClick,
    type: 'button',
  }) as unknown as SelectionToolbarQuoteElementProps;
}

export function createThreadQuoteFromSelection(
  selection: SelectionToolbarSelection | null,
): ThreadQuote | null {
  if (!selection) return null;
  const text = selection.text.trim();
  if (!text) return null;

  return {
    messageId: selection.messageId ?? undefined,
    source: 'selection',
    text,
  };
}

export function readSelectionWithinRoot(
  root: HTMLElement | null,
  selection: Selection | null,
): SelectionToolbarSelection | null {
  if (!root || !selection || selection.rangeCount === 0 || selection.isCollapsed) return null;
  const anchorNode = selection.anchorNode;
  const focusNode = selection.focusNode;
  if (!isNodeInside(root, anchorNode) || !isNodeInside(root, focusNode)) return null;

  const anchorMessageId = findMessageId(anchorNode);
  const focusMessageId = findMessageId(focusNode);
  const messageId = anchorMessageId && anchorMessageId === focusMessageId
    ? anchorMessageId
    : null;
  const text = selection.toString();
  if (!text.trim()) return null;

  const range = selection.getRangeAt(0);
  const rect = readRangeRect(range);
  if (!rect) return null;

  return {
    anchorNode,
    focusNode,
    messageId,
    rect,
    text,
  };
}

export function getSelectionToolbarPosition(
  selection: SelectionToolbarSelection | null,
  options: {
    offset?: number;
    placement?: SelectionToolbarPlacement;
    viewportHeight?: number;
    viewportWidth?: number;
  } = {},
): SelectionToolbarPosition | null {
  if (!selection) return null;

  const offset = options.offset ?? 8;
  const viewportWidth = options.viewportWidth ?? globalThis.window?.innerWidth ?? 0;
  const viewportHeight = options.viewportHeight ?? globalThis.window?.innerHeight ?? 0;
  const x = selection.rect.left + selection.rect.width / 2;
  const y = options.placement === 'below'
    ? selection.rect.bottom + offset
    : selection.rect.top - offset;

  return {
    left: clamp(x, 8, Math.max(8, viewportWidth - 8)),
    top: clamp(y, 8, Math.max(8, viewportHeight - 8)),
  };
}

function createSelectionToolbarStyle(state: SelectionToolbarState): string {
  if (!state.open || !state.position) {
    return 'display: none;';
  }

  const translateY = state.placement === 'above' ? '-100%' : '0';
  return [
    'position: fixed',
    `left: ${state.position.left}px`,
    `top: ${state.position.top}px`,
    `transform: translate(-50%, ${translateY})`,
  ].join('; ');
}

function findMessageId(node: Node | null): string | null {
  let element = node instanceof HTMLElement ? node : node?.parentElement ?? null;
  while (element) {
    const id = element.getAttribute('data-message-id');
    if (id) return id;
    element = element.parentElement;
  }
  return null;
}

function readRangeRect(range: Range): DOMRectReadOnly | null {
  const rect = range.getBoundingClientRect();
  if (hasUsableRect(rect)) return rect;

  for (const candidate of Array.from(range.getClientRects())) {
    if (hasUsableRect(candidate)) return candidate;
  }

  return rect.width || rect.height ? rect : null;
}

function hasUsableRect(rect: DOMRectReadOnly): boolean {
  return rect.width > 0 || rect.height > 0;
}

function isNodeInside(root: HTMLElement, node: Node | null): boolean {
  return Boolean(node && (node === root || root.contains(node)));
}

function clamp(value: number, min: number, max: number): number {
  if (max < min) return min;
  return Math.min(Math.max(value, min), max);
}
