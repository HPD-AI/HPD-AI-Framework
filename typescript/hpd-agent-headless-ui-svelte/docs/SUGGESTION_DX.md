# Suggestion DX

`Suggestion` renders a suggested prompt as a clickable button. `SuggestionList`
renders a collection of structured suggestions. They are Svelte adapter
primitives over existing thread/composer infrastructure.

Use it for prompt chips such as "Summarize this thread", "Find bugs", or
"Explain this file".

## Populate A Composer

The default mode is `populate`. Bind `targetValue` to the same value used by
`ThreadComposer`.

```svelte
<script lang="ts">
  import {
    Suggestion,
    ThreadComposer,
  } from '@hpd-research/hpd-agent-headless-ui-svelte';

  let draft = $state('');
</script>

<Suggestion prompt="Explain this code" bind:targetValue={draft} />
<ThreadComposer {thread} bind:value={draft} />
```

`Suggestion` does not reach into `ThreadComposer`. The shared draft is plain
Svelte state.

Use `populateMode="append"` when a suggestion should add text to the current
draft instead of replacing it:

```svelte
<Suggestion
  prompt="using the selected code"
  populateMode="append"
  bind:targetValue={draft}
/>
```

## Send Immediately

Set `mode="send"` to submit the suggestion through `ThreadState.sendMessage`.

```svelte
<Suggestion
  {thread}
  prompt="Summarize this thread"
  title="Summarize"
  description="Create a short recap"
  mode="send"
/>
```

Send mode uses the thread's current submission state. It disables when the
thread is busy, blocked by a runtime request, disconnected from sendability, or
has an error.

Send mode stores suggestion context as message metadata by default:

```ts
additionalProperties: {
  suggestion: {
    prompt,
    title,
    description
  }
}
```

Pass `persistSuggestionMetadata={false}` to send only the text content.

## Suggestion Lists

```svelte
<SuggestionList
  {thread}
  bind:targetValue={draft}
  suggestions={[
    {
      prompt: 'Find likely bugs and missing tests',
      title: 'Find bugs',
      description: 'Review the current file'
    }
  ]}
/>
```

Customize each item with the `suggestion` snippet:

```svelte
<SuggestionList {thread} {suggestions}>
  {#snippet suggestion({ suggestion, props, actions, blockedReason })}
    <button {...props} onclick={() => actions.select()}>
      <strong>{suggestion.title}</strong>
      <small>{blockedReason ?? suggestion.description}</small>
    </button>
  {/snippet}
</SuggestionList>
```

## Callback Usage

Use `onSelect` when the app wants to decide how population behaves.

```svelte
<Suggestion
  prompt="Find bugs"
  onSelect={({ prompt }) => {
    draft = prompt;
  }}
/>
```

`onSelect` also runs after a successful direct send.

## Custom Rendering

Use `children` when you want custom contents inside the default button.

```svelte
<Suggestion prompt="Write tests" title="Write tests" description="Add coverage">
  {#snippet children({ title, description, blockedReason })}
    <span>{title}</span>
    <small>{blockedReason ?? description}</small>
  {/snippet}
</Suggestion>
```

Use `child` for full DOM control.

```svelte
<Suggestion prompt="Review this file" title="Review">
  {#snippet child({ actions, props, title })}
    <button {...props} class="prompt" onclick={() => actions.select()}>
      {title}
    </button>
  {/snippet}
</Suggestion>
```

## Styling Hooks

Suggestion primitives expose stable HPD-owned attributes:

```css
[data-hpd-suggestion-list] {
}

[data-hpd-suggestion] {
}

[data-hpd-suggestion][data-mode="populate"] {
}

[data-hpd-suggestion][data-mode="send"] {
}

[data-hpd-suggestion][data-populate-mode="append"] {
}

[data-hpd-suggestion]:not([data-can-select]) {
}

[data-hpd-suggestion][data-blocked-reason] {
}

[data-hpd-suggestion][data-submitting] {
}
```

Use the `children`, `child`, and `suggestion` snippets when prompt chips need
icons, categories, descriptions, or custom disabled/submitting treatments.
