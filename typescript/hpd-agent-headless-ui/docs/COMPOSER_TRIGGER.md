# Composer Trigger Core

Composer trigger core utilities provide framework-neutral behavior for
`@mentions`, `/commands`, and other inline composer directives.

The core owns pure logic only:

- detect an active trigger near a cursor
- filter trigger items from an adapter
- format selected items back into text
- produce structured metadata and run-config patches

Adapters own DOM, keyboard navigation, popover placement, and visual rendering.

## Detection

```ts
import {
  detectComposerTrigger,
} from '@hpd-research/hpd-agent-headless-ui';

const match = detectComposerTrigger('Ask @wor', 8, '@');
```

A match is active when the trigger starts at the beginning of the text or after
whitespace, and the cursor is still inside the contiguous query.

## Static Adapter

```ts
import {
  createStaticComposerTriggerAdapter,
} from '@hpd-research/hpd-agent-headless-ui';

const adapter = createStaticComposerTriggerAdapter({
  items: [
    {
      id: 'workspace',
      type: 'tool',
      label: 'Workspace',
    },
  ],
});
```

The Svelte adapter can render `getComposerTriggerItems(adapter, query)`.

## Applying A Selection

```ts
import {
  applyComposerTriggerDirective,
} from '@hpd-research/hpd-agent-headless-ui';

const result = applyComposerTriggerDirective({
  text: 'Ask @wor',
  selection: {
    trigger: '@',
    match,
    item,
  },
});
```

The result contains the next text, cursor, selected item, and optional metadata
or run-config patches.

## Metadata

```ts
import {
  createComposerDirectiveAdditionalProperties,
} from '@hpd-research/hpd-agent-headless-ui';

const additionalProperties = createComposerDirectiveAdditionalProperties({
  item,
  trigger: '@',
});
```

The core does not decide how directives become model-visible context.
HPD backends and apps can interpret `additionalProperties` and `runConfig`
according to their own policy.
