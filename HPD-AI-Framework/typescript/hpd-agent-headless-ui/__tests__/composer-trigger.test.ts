import { describe, expect, it } from 'vitest';
import {
  applyComposerTriggerDirective,
  createComposerDirectiveAdditionalProperties,
  createStaticComposerTriggerAdapter,
  detectComposerTrigger,
  getActiveComposerTrigger,
  getComposerTriggerCategories,
  getComposerTriggerItems,
} from '../src/index.js';

describe('composer trigger helpers', () => {
  it('detects triggers contiguous with the cursor', () => {
    expect(detectComposerTrigger('ask @wor', 8, '@')).toEqual({
      cursor: 8,
      offset: 4,
      query: 'wor',
      trigger: '@',
    });
    expect(detectComposerTrigger('email me@work', 13, '@')).toBeNull();
    expect(detectComposerTrigger('ask @wor later', 14, '@')).toBeNull();
  });

  it('selects the first active trigger from a list', () => {
    expect(getActiveComposerTrigger('/sum', 4, ['@', '/'])?.trigger).toBe('/');
    expect(getActiveComposerTrigger('@agent', 6, ['@', '/'])?.query).toBe('agent');
  });

  it('creates static adapters with categories and search', () => {
    const adapter = createStaticComposerTriggerAdapter({
      categories: [{ id: 'tools', label: 'Tools' }],
      items: [
        { id: 'workspace', type: 'tool', label: 'Workspace', categoryId: 'tools' },
        { id: 'docs', type: 'tool', label: 'Docs', categoryId: 'tools' },
      ],
    });

    expect(getComposerTriggerCategories(adapter)).toEqual([{ id: 'tools', label: 'Tools' }]);
    expect(getComposerTriggerItems(adapter, '', 'tools').map((item) => item.id)).toEqual(['workspace', 'docs']);
    expect(getComposerTriggerItems(adapter, 'doc').map((item) => item.id)).toEqual(['docs']);
  });

  it('applies directive selections to composer text', () => {
    const result = applyComposerTriggerDirective({
      selection: {
        trigger: '@',
        item: { id: 'workspace', type: 'tool', label: 'Workspace' },
        match: {
          trigger: '@',
          query: 'wor',
          offset: 4,
          cursor: 8,
        },
      },
      text: 'ask @wor please',
    });

    expect(result.text).toBe('ask @Workspace please');
    expect(result.nextCursor).toBe('ask @Workspace'.length);
  });

  it('creates structured directive metadata', () => {
    expect(createComposerDirectiveAdditionalProperties({
      trigger: '@',
      item: {
        id: 'workspace',
        type: 'tool',
        label: 'Workspace',
      },
    })).toEqual({
      directives: [{
        id: 'workspace',
        label: 'Workspace',
        metadata: undefined,
        text: '@Workspace',
        trigger: '@',
        type: 'tool',
      }],
    });
  });
});
