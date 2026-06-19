import { flushSync, mount, unmount } from 'svelte';
import { describe, expect, it, vi } from 'vitest';
import {
  createSelectionToolbarState,
  createThreadQuoteFromSelection,
  getSelectionToolbarPosition,
  type SelectionToolbarSelection,
  type ThreadQuote,
} from '../src/index.js';
import SelectionToolbarHarness from './fixtures/selection-toolbar-harness.svelte';
import SelectionToolbarCustomHarness from './fixtures/selection-toolbar-custom-harness.svelte';

function mountTarget(): HTMLElement {
  const target = document.createElement('div');
  document.body.append(target);
  return target;
}

function rect(options: {
  height?: number;
  left?: number;
  top?: number;
  width?: number;
} = {}): DOMRect {
  const left = options.left ?? 20;
  const top = options.top ?? 30;
  const width = options.width ?? 80;
  const height = options.height ?? 20;
  return DOMRect.fromRect({
    height,
    width,
    x: left,
    y: top,
  });
}

function installSelection(options: {
  anchorNode: Node;
  focusNode?: Node;
  text: string;
  rect?: DOMRect;
}): () => void {
  const range = {
    getBoundingClientRect: () => options.rect ?? rect(),
    getClientRects: () => [options.rect ?? rect()],
  } as unknown as Range;
  const selection = {
    anchorNode: options.anchorNode,
    focusNode: options.focusNode ?? options.anchorNode,
    isCollapsed: false,
    rangeCount: 1,
    getRangeAt: () => range,
    removeAllRanges: vi.fn(),
    toString: () => options.text,
  } as unknown as Selection;
  const spy = vi.spyOn(document, 'getSelection').mockReturnValue(selection);
  return () => spy.mockRestore();
}

describe('SelectionToolbar', () => {
  it('derives open state and structured quotes from valid selection', () => {
    const selection: SelectionToolbarSelection = {
      anchorNode: null,
      focusNode: null,
      messageId: 'message-1',
      rect: rect(),
      text: 'Hello',
    };

    expect(createSelectionToolbarState({ selection })).toMatchObject({
      open: true,
      placement: 'above',
      quote: null,
    });

    expect(createThreadQuoteFromSelection(selection)).toEqual({
      messageId: 'message-1',
      source: 'selection',
      text: 'Hello',
    });

    expect(createSelectionToolbarState({
      disabled: true,
      selection,
    })).toMatchObject({
      open: false,
      selection: null,
    });
  });

  it('positions above or below the selection rectangle', () => {
    const selection = {
      anchorNode: null,
      focusNode: null,
      messageId: null,
      rect: rect({ left: 40, top: 60, width: 100, height: 20 }),
      text: 'Hello',
    };

    expect(getSelectionToolbarPosition(selection, {
      offset: 10,
      placement: 'above',
      viewportHeight: 500,
      viewportWidth: 500,
    })).toEqual({
      left: 90,
      top: 50,
    });

    expect(getSelectionToolbarPosition(selection, {
      offset: 10,
      placement: 'below',
      viewportHeight: 500,
      viewportWidth: 500,
    })).toEqual({
      left: 90,
      top: 90,
    });
  });

  it('captures selected text as structured quote state', async () => {
    const target = mountTarget();
    const onQuote = vi.fn<(quote: ThreadQuote, selection: SelectionToolbarSelection) => void>();
    const component = mount(SelectionToolbarHarness, {
      target,
      props: { onQuote },
    });
    const selectable = target.querySelector('[data-testid="selectable"]')?.firstChild;
    expect(selectable).toBeTruthy();

    const restoreSelection = installSelection({
      anchorNode: selectable as Node,
      text: 'Alpha selected text',
    });
    document.dispatchEvent(new Event('selectionchange'));
    flushSync();

    const quote = target.querySelector<HTMLButtonElement>('[data-hpd-selection-toolbar-quote]');
    expect(quote?.disabled).toBe(false);
    quote?.click();
    await Promise.resolve();
    flushSync();

    expect(target.querySelector('[data-testid="quote-text"]')?.textContent)
      .toBe('Alpha selected text');
    expect(target.querySelector('[data-testid="quote-message-id"]')?.textContent)
      .toBe('message-1');
    expect(target.querySelector('[data-testid="composer-quote-text"]')?.textContent)
      .toBe('Alpha selected text');
    expect(onQuote).toHaveBeenCalledWith(
      expect.objectContaining({
        messageId: 'message-1',
        source: 'selection',
        text: 'Alpha selected text',
      }),
      expect.objectContaining({
        text: 'Alpha selected text',
      }),
    );

    target.querySelector<HTMLButtonElement>('[data-testid="composer-quote-dismiss"]')?.click();
    await Promise.resolve();
    flushSync();

    expect(target.querySelector('[data-testid="composer-quote-text"]')).toBeNull();

    restoreSelection();
    await unmount(component);
    target.remove();
  });

  it('does not open when disabled or below minLength', async () => {
    const target = mountTarget();
    const component = mount(SelectionToolbarHarness, {
      target,
      props: {
        minLength: 10,
      },
    });
    const selectable = target.querySelector('[data-testid="selectable"]')?.firstChild;
    const restoreSelection = installSelection({
      anchorNode: selectable as Node,
      text: 'short',
    });

    document.dispatchEvent(new Event('selectionchange'));
    flushSync();

    expect(target.querySelector('[data-hpd-selection-toolbar-root]')?.getAttribute('data-open'))
      .toBeNull();
    expect(target.querySelector<HTMLButtonElement>('[data-hpd-selection-toolbar-quote]')?.disabled)
      .toBe(true);

    restoreSelection();
    await unmount(component);
    target.remove();
  });

  it('supports custom toolbar composition', async () => {
    const target = mountTarget();
    const component = mount(SelectionToolbarCustomHarness, { target });
    const selectable = target.querySelector('[data-testid="selectable"]')?.firstChild;
    const restoreSelection = installSelection({
      anchorNode: selectable as Node,
      text: 'Custom selected text',
    });

    document.dispatchEvent(new Event('selectionchange'));
    flushSync();

    expect(target.querySelector('[data-testid="custom-toolbar"]')).toBeTruthy();
    expect(target.querySelector('[data-testid="custom-quote"]')?.textContent)
      .toContain('Quote 20');

    target.querySelector<HTMLButtonElement>('[data-testid="custom-quote"]')?.click();
    await Promise.resolve();
    flushSync();

    expect(target.querySelector('[data-testid="quote"]')?.textContent)
      .toBe('Custom selected text');

    restoreSelection();
    await unmount(component);
    target.remove();
  });
});
