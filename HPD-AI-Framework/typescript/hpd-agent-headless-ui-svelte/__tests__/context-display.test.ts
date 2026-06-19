import { mount, unmount } from 'svelte';
import { describe, expect, it } from 'vitest';
import {
  createContextDisplayModel,
  formatContextDisplayPercent,
  formatContextDisplayTokens,
} from '../src/context-display/index.js';
import ContextDisplayHarness from './fixtures/context-display-harness.svelte';

function mountTarget(): HTMLElement {
  const target = document.createElement('div');
  document.body.append(target);
  return target;
}

describe('ContextDisplay', () => {
  it('computes context usage model from token usage', () => {
    const model = createContextDisplayModel({
      modelContextWindow: 2000,
      usage: {
        inputTokenCount: 700,
        outputTokenCount: 200,
        totalTokenCount: 1000,
      },
    });

    expect(model.totalTokens).toBe(1000);
    expect(model.percent).toBe(50);
    expect(model.severity).toBe('normal');
  });

  it('formats compact token and percent labels', () => {
    expect(formatContextDisplayTokens(1250)).toBe('1.3k');
    expect(formatContextDisplayPercent(49.6)).toBe('50%');
  });

  it('renders bar, text, and breakdown primitives', async () => {
    const target = mountTarget();
    const component = mount(ContextDisplayHarness, { target });

    expect(target.querySelector('[data-hpd-context-display-root]')).not.toBeNull();
    expect(target.querySelector('[data-hpd-context-display-bar]')).not.toBeNull();
    expect(target.querySelector('[data-hpd-context-display-text]')?.textContent).toContain('1.0k');
    expect(target.querySelector('[data-hpd-context-display-text]')?.textContent).toContain('50%');
    expect(target.querySelector('[data-row-key="cached"]')?.textContent).toContain('Cached');
    expect(target.querySelector('[data-row-key="reasoning"]')?.textContent).toContain('Reasoning');

    await unmount(component);
    target.remove();
  });
});

