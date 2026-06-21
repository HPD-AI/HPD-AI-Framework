# Suggestion Proposal

`Suggestion` and `SuggestionList` are the Svelte adapter primitives for
suggested prompts. A suggestion is structured display metadata plus the prompt
that will be populated or sent.

The primitive belongs in `hpd-agent-headless-ui-svelte`, not the framework
neutral core. The core already owns thread submission state and message sending.
The adapter owns Svelte ergonomics: bindable draft values, snippets, button
props, and DOM attributes.

## Goals

- Let apps render structured suggested prompts as clickable pills.
- Let apps render lists of suggestions without a global provider.
- Support composer population without reaching into `ThreadComposer` internals.
- Support direct send through `ThreadState.sendMessage()`.
- Preserve suggestion context as message `additionalProperties` when sending.
- Use existing thread submission state for disabled/busy behavior.
- Keep the component unstyled and snippet-friendly.

## Non-Goals

- Do not add protocol events.
- Do not add client transport behavior.
- Do not create a global suggestion registry.
- Do not couple directly to `ThreadComposer`.
- Do not require a thread when the suggestion only populates draft text.
- Do not preserve the legacy `value`/`label` API.

## API

```svelte
<script lang="ts">
  import {
    Suggestion,
    ThreadComposer,
  } from '@hpd-research/hpd-agent-headless-ui-svelte';

  let draft = $state('');
</script>

<Suggestion
  prompt="Explain this code"
  title="Explain"
  description="Describe the selected code"
  bind:targetValue={draft}
/>
<ThreadComposer {thread} bind:value={draft} />
```

Direct send:

```svelte
<Suggestion
  {thread}
  prompt="Summarize this thread"
  title="Summarize"
  description="Create a short recap"
  mode="send"
/>
```

Callback-only population:

```svelte
<Suggestion
  prompt="Find bugs"
  onSelect={({ prompt }) => {
    draft = prompt;
  }}
/>
```

List rendering:

```svelte
<SuggestionList
  {thread}
  bind:targetValue={draft}
  suggestions={[
    {
      prompt: 'Review this file for likely bugs',
      title: 'Find bugs',
      description: 'Check correctness and tests'
    }
  ]}
/>
```

## Behavior

- `mode="populate"` is the default.
- `title ?? prompt` is displayed by default.
- `description` is optional secondary text.
- Populate mode writes `targetValue = prompt` when the prop is bound and calls
  `onSelect`.
- `populateMode="append"` appends the prompt to the current target value.
- Send mode calls `thread.sendMessage()` with `createTextContent(prompt)`.
- Send mode persists suggestion metadata through message `additionalProperties`
  by default.
- Send mode is disabled when the thread is not sendable.
- Empty suggestions are disabled.

## Stable Attributes

- `data-hpd-suggestion`
- `data-hpd-suggestion-list`
- `data-mode`
- `data-populate-mode`
- `data-can-select`
- `data-blocked-reason`
- `data-submitting`
