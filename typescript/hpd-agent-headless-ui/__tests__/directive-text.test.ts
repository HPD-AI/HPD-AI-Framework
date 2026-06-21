import { describe, expect, it } from 'vitest';
import {
  createDirectiveTextParts,
  readAdditionalPropertyDirectives,
} from '../src/index.js';

describe('directive text helpers', () => {
  it('reads directives from message additional properties', () => {
    expect(readAdditionalPropertyDirectives({
      directives: [{
        id: 'workspace',
        label: 'Workspace',
        text: '@Workspace',
        trigger: '@',
        type: 'tool',
      }],
    })).toEqual([{
      id: 'workspace',
      label: 'Workspace',
      metadata: undefined,
      text: '@Workspace',
      trigger: '@',
      type: 'tool',
    }]);
  });

  it('splits text using structured directives', () => {
    expect(createDirectiveTextParts({
      text: 'Ask @Workspace to run /deep',
      directives: [
        {
          id: 'workspace',
          label: 'Workspace',
          text: '@Workspace',
          trigger: '@',
          type: 'tool',
        },
        {
          id: 'deep',
          label: 'Deep',
          text: '/deep',
          trigger: '/',
          type: 'command',
        },
      ],
    })).toEqual([
      { id: 'text:0', text: 'Ask ', type: 'text' },
      {
        directive: {
          id: 'workspace',
          label: 'Workspace',
          text: '@Workspace',
          trigger: '@',
          type: 'tool',
        },
        id: 'directive:0:workspace',
        text: '@Workspace',
        type: 'directive',
      },
      { id: 'text:1', text: ' to run ', type: 'text' },
      {
        directive: {
          id: 'deep',
          label: 'Deep',
          text: '/deep',
          trigger: '/',
          type: 'command',
        },
        id: 'directive:1:deep',
        text: '/deep',
        type: 'directive',
      },
    ]);
  });

  it('does not match directives inside longer words', () => {
    expect(createDirectiveTextParts({
      text: 'email@Workspace later',
      directives: [{
        id: 'workspace',
        label: 'Workspace',
        text: '@Workspace',
        trigger: '@',
        type: 'tool',
      }],
    })).toEqual([
      { id: 'text:0', text: 'email@Workspace later', type: 'text' },
    ]);
  });
});
