import { flushSync, mount, unmount } from 'svelte';
import { describe, expect, it } from 'vitest';
import {
  createComposerTriggerItemElementProps,
} from '../src/composer-trigger/index.js';
import ComposerTriggerDirectiveHarness from './fixtures/composer-trigger-directive-harness.svelte';
import ComposerTriggerActionHarness from './fixtures/composer-trigger-action-harness.svelte';

function mountTarget(): HTMLElement {
  const target = document.createElement('div');
  document.body.append(target);
  return target;
}

async function tick(): Promise<void> {
  await Promise.resolve();
  await Promise.resolve();
  flushSync();
}

describe('ComposerTrigger', () => {
  it('inserts mention directives and structured metadata', async () => {
    const target = mountTarget();
    const component = mount(ComposerTriggerDirectiveHarness, { target });
    await tick();

    const item = target.querySelector<HTMLButtonElement>('[data-hpd-composer-trigger-item]');
    expect(item?.textContent).toContain('Workspace');
    item?.click();
    await tick();

    expect(target.querySelector('[data-testid="value"]')?.textContent).toBe('ask @Workspace');
    expect(target.querySelector('[data-testid="metadata"]')?.textContent).toContain('"directives"');
    expect(target.querySelector('[data-testid="metadata"]')?.textContent).toContain('"workspace"');

    await unmount(component);
    target.remove();
  });

  it('executes slash commands and patches run config', async () => {
    const target = mountTarget();
    const component = mount(ComposerTriggerActionHarness, { target });
    await tick();

    target.querySelector<HTMLButtonElement>('[data-hpd-composer-trigger-item]')?.click();
    await tick();

    expect(target.querySelector('[data-testid="value"]')?.textContent).toBe('');
    expect(target.querySelector('[data-testid="executed"]')?.textContent).toBe('deep');
    expect(target.querySelector('[data-testid="run-config"]')?.textContent).toContain('"modelId":"deep-model"');
    expect(target.querySelector('[data-testid="run-config"]')?.textContent).toContain('"command":"deep"');

    await unmount(component);
    target.remove();
  });

  it('creates stable item props for custom renderers', () => {
    const props = createComposerTriggerItemElementProps({
      highlighted: true,
      item: {
        id: 'workspace',
        label: 'Workspace',
        type: 'tool',
      },
      onClick: () => {},
      restProps: { class: 'item' },
    });

    expect(props['data-hpd-composer-trigger-item']).toBe('');
    expect(props['data-item-id']).toBe('workspace');
    expect(props['data-item-type']).toBe('tool');
    expect(props['data-highlighted']).toBe('');
    expect(props.class).toContain('item');
  });
});
