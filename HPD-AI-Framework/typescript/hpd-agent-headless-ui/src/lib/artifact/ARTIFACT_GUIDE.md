# Artifact Panel System

A declarative, headless artifact system for Svelte 5. Display AI-generated content in a dedicated side panel using content "teleportation".

---

## Table of Contents

1. [Core Concepts](#core-concepts)
2. [Components](#components)
3. [Features](#features)
4. [Usage Patterns](#usage-patterns)
5. [Anti-Patterns](#anti-patterns)
6. [API Reference](#api-reference)

---

## Core Concepts

### Content Teleportation

The Artifact system uses a unique pattern where content is **defined** in one place (inline with your messages) but **rendered** in another (a side panel):

```svelte
<Artifact.Provider>
  <!-- Content is DEFINED here, inside the message -->
  <Artifact.Root id="my-artifact">
    <Artifact.Slot title={titleSnippet} content={contentSnippet} />
    <Artifact.Trigger>Open Preview</Artifact.Trigger>
  </Artifact.Root>

  <!-- Content is RENDERED here, in the panel -->
  <Artifact.Panel>
    <Artifact.Title />
    <Artifact.Content />
    <Artifact.Close>×</Artifact.Close>
  </Artifact.Panel>
</Artifact.Provider>
```

### Single Open Pattern

Only one artifact can be open at a time. Opening a new artifact automatically closes the previous one.

### Component Hierarchy

- **Provider**: Context container that manages all artifacts
- **Root**: Individual artifact instance with unique ID
- **Slot**: Registers title/content snippets (renders nothing)
- **Trigger**: Button to open/close the artifact
- **Panel**: Render target for the open artifact
- **Title**: Renders the title snippet from the open artifact
- **Content**: Renders the content snippet from the open artifact
- **Close**: Button to close the panel

---

## Components

### `Artifact.Provider`

The root container. All artifact components must be wrapped in a Provider.

```svelte
<Artifact.Provider onOpenChange={(open, id) => console.log(open, id)}>
  <!-- Your content here -->
</Artifact.Provider>
```

**Props:**
| Prop | Type | Default | Description |
|------|------|---------|-------------|
| `onOpenChange` | `(open: boolean, id: string \| null) => void` | - | Callback when any artifact opens/closes |
| `ref` | `HTMLElement` | - | Bindable reference to the container element |

### `Artifact.Root`

Container for an individual artifact. Must have a unique ID.

```svelte
<Artifact.Root
  id="artifact-1"
  defaultOpen={false}
  onOpenChange={(open) => console.log(open)}
>
  {#snippet children({ open, setOpen, toggle })}
    <p>Artifact is {open ? 'open' : 'closed'}</p>
    <button onclick={toggle}>Toggle</button>
  {/snippet}
</Artifact.Root>
```

**Props:**
| Prop | Type | Default | Description |
|------|------|---------|-------------|
| `id` | `string` | required | Unique identifier for this artifact |
| `defaultOpen` | `boolean` | `false` | Initial open state |
| `onOpenChange` | `(open: boolean) => void` | - | Callback when this artifact opens/closes |
| `ref` | `HTMLElement` | - | Bindable reference to the container element |

**Snippet Props (children):**
| Prop | Type | Description |
|------|------|-------------|
| `open` | `boolean` | Whether this artifact is open |
| `setOpen` | `(open: boolean) => void` | Set open state |
| `toggle` | `() => void` | Toggle open state |

### `Artifact.Slot`

Registers content snippets with the provider. Renders nothing - it's purely for registration.

```svelte
{#snippet titleSnippet()}
  <span>My Artifact Title</span>
{/snippet}

{#snippet contentSnippet()}
  <pre><code>{codeContent}</code></pre>
{/snippet}

<Artifact.Slot title={titleSnippet} content={contentSnippet} />
```

**Props:**
| Prop | Type | Default | Description |
|------|------|---------|-------------|
| `title` | `Snippet` | - | Snippet for the title area |
| `content` | `Snippet` | - | Snippet for the main content |

### `Artifact.Trigger`

Button that opens/closes the artifact. Must be inside an `Artifact.Root`.

```svelte
<Artifact.Trigger disabled={false}>
  {#snippet children({ open })}
    {open ? 'Close' : 'Open'} Preview
  {/snippet}
</Artifact.Trigger>
```

**Props:**
| Prop | Type | Default | Description |
|------|------|---------|-------------|
| `disabled` | `boolean` | `false` | Disable the trigger |
| `ref` | `HTMLElement` | - | Bindable reference to the button element |

**Snippet Props (children):**
| Prop | Type | Description |
|------|------|-------------|
| `open` | `boolean` | Whether the artifact is open |

### `Artifact.Panel`

Render target for the open artifact. Place this where you want the artifact content to appear.

```svelte
<Artifact.Panel>
  {#snippet children({ open, openId, title, content, close })}
    {#if open}
      <header>
        <Artifact.Title />
        <Artifact.Close>×</Artifact.Close>
      </header>
      <main>
        <Artifact.Content />
      </main>
      <footer>
        <span>ID: {openId}</span>
      </footer>
    {/if}
  {/snippet}
</Artifact.Panel>
```

**Props:**
| Prop | Type | Default | Description |
|------|------|---------|-------------|
| `ref` | `HTMLElement` | - | Bindable reference to the panel element |

**Snippet Props (children):**
| Prop | Type | Description |
|------|------|-------------|
| `open` | `boolean` | Whether any artifact is open |
| `openId` | `string \| null` | ID of the currently open artifact |
| `title` | `Snippet \| null` | Title snippet from the open artifact |
| `content` | `Snippet \| null` | Content snippet from the open artifact |
| `close` | `() => void` | Function to close the panel |

### `Artifact.Title`

Renders the title snippet from the currently open artifact.

```svelte
<Artifact.Title>
  {#snippet children()}
    <!-- Fallback if no title is provided -->
    <span>Untitled</span>
  {/snippet}
</Artifact.Title>
```

### `Artifact.Content`

Renders the content snippet from the currently open artifact.

```svelte
<Artifact.Content>
  {#snippet children()}
    <!-- Fallback if no content is provided -->
    <p>No content available</p>
  {/snippet}
</Artifact.Content>
```

### `Artifact.Close`

Button that closes the currently open artifact.

```svelte
<Artifact.Close disabled={false}>
  {#snippet children({ open })}
    <span aria-label="Close">×</span>
  {/snippet}
</Artifact.Close>
```

**Props:**
| Prop | Type | Default | Description |
|------|------|---------|-------------|
| `disabled` | `boolean` | `false` | Disable the close button |
| `ref` | `HTMLElement` | - | Bindable reference to the button element |

**Snippet Props (children):**
| Prop | Type | Description |
|------|------|-------------|
| `open` | `boolean` | Whether any artifact is open |

---

## Features

### 1. Basic Usage - Chat with Artifacts

Display artifacts inline with chat messages:

```svelte
<script>
  import { Artifact } from '@hpd/hpd-agent-headless-ui';

  let messages = [
    { id: '1', text: 'Here is the code:', code: 'console.log("Hello")' },
    { id: '2', text: 'And the documentation:', doc: 'Usage guide...' }
  ];
</script>

<div class="chat-layout">
  <Artifact.Provider>
    <!-- Chat messages area -->
    <div class="messages">
      {#each messages as msg (msg.id)}
        <div class="message">
          <p>{msg.text}</p>

          {#if msg.code}
            <Artifact.Root id={msg.id}>
              {#snippet titleSnippet()}
                <span>Code Preview</span>
              {/snippet}
              {#snippet contentSnippet()}
                <pre><code>{msg.code}</code></pre>
              {/snippet}

              <Artifact.Slot title={titleSnippet} content={contentSnippet} />
              <Artifact.Trigger>View Code</Artifact.Trigger>
            </Artifact.Root>
          {/if}
        </div>
      {/each}
    </div>

    <!-- Side panel -->
    <aside class="panel">
      <Artifact.Panel>
        {#snippet children({ open })}
          {#if open}
            <header>
              <Artifact.Title />
              <Artifact.Close>×</Artifact.Close>
            </header>
            <Artifact.Content />
          {/if}
        {/snippet}
      </Artifact.Panel>
    </aside>
  </Artifact.Provider>
</div>
```

### 2. Default Open Artifact

Open an artifact by default:

```svelte
<Artifact.Root id="intro" defaultOpen={true}>
  <!-- Opens automatically on mount -->
</Artifact.Root>
```

### 3. Callbacks

Listen to open/close events:

```svelte
<script>
  function handleProviderChange(open, id) {
    console.log(`Artifact ${id} is now ${open ? 'open' : 'closed'}`);
  }

  function handleArtifactChange(open) {
    console.log(`This artifact is now ${open ? 'open' : 'closed'}`);
  }
</script>

<Artifact.Provider onOpenChange={handleProviderChange}>
  <Artifact.Root id="my-artifact" onOpenChange={handleArtifactChange}>
    ...
  </Artifact.Root>
</Artifact.Provider>
```

### 4. Programmatic Control via Snippet Props

Control artifacts from within snippets:

```svelte
<Artifact.Root id="controlled">
  {#snippet children({ open, setOpen, toggle })}
    <div class="artifact-card" data-open={open}>
      <button onclick={toggle}>
        {open ? 'Hide' : 'Show'} Preview
      </button>
      <button onclick={() => setOpen(false)}>
        Force Close
      </button>
    </div>
  {/snippet}
</Artifact.Root>
```

### 5. Panel with Full Snippet Access

Access all panel state in your layout:

```svelte
<Artifact.Panel>
  {#snippet children({ open, openId, title, content, close })}
    <div class="panel" data-open={open}>
      {#if open}
        <div class="panel-header">
          <Artifact.Title />
          <button onclick={close}>Close</button>
        </div>
        <div class="panel-body">
          <Artifact.Content />
        </div>
        <div class="panel-footer">
          Viewing: {openId}
        </div>
      {:else}
        <div class="empty-state">
          Select an artifact to preview
        </div>
      {/if}
    </div>
  {/snippet}
</Artifact.Panel>
```

### 6. Animation Support

The Panel component has built-in presence management for enter/exit animations:

```svelte
<Artifact.Panel>
  {#snippet children({ open })}
    <div class="panel" class:panel-open={open}>
      <!-- Content renders during animation -->
    </div>
  {/snippet}
</Artifact.Panel>

<style>
  .panel {
    transform: translateX(100%);
    opacity: 0;
    transition: transform 0.2s, opacity 0.2s;
  }

  .panel-open {
    transform: translateX(0);
    opacity: 1;
  }
</style>
```

### 7. Multiple Artifact Types

Handle different content types:

```svelte
<script>
  let artifacts = [
    { id: 'code-1', type: 'code', title: 'Component', content: '...' },
    { id: 'doc-1', type: 'document', title: 'README', content: '...' },
    { id: 'chart-1', type: 'chart', title: 'Metrics', data: {...} }
  ];
</script>

{#each artifacts as artifact (artifact.id)}
  <Artifact.Root id={artifact.id}>
    {#snippet titleSnippet()}
      <span class="title" data-type={artifact.type}>
        {artifact.title}
      </span>
    {/snippet}

    {#snippet contentSnippet()}
      {#if artifact.type === 'code'}
        <pre><code>{artifact.content}</code></pre>
      {:else if artifact.type === 'document'}
        <div class="prose">{artifact.content}</div>
      {:else if artifact.type === 'chart'}
        <Chart data={artifact.data} />
      {/if}
    {/snippet}

    <Artifact.Slot title={titleSnippet} content={contentSnippet} />
    <Artifact.Trigger>Open {artifact.type}</Artifact.Trigger>
  </Artifact.Root>
{/each}
```

---

## Usage Patterns

###  DO: Use Snippets for Dynamic Content

```svelte
<Artifact.Root id="dynamic">
  {#snippet titleSnippet()}
    <!-- Snippet can access component scope -->
    <span>{artifact.title}</span>
  {/snippet}

  {#snippet contentSnippet()}
    <CodeBlock code={artifact.code} language={artifact.language} />
  {/snippet}

  <Artifact.Slot title={titleSnippet} content={contentSnippet} />
  <Artifact.Trigger>Preview</Artifact.Trigger>
</Artifact.Root>
```

###  DO: Use Unique, Meaningful IDs

```svelte
<!-- Good: descriptive IDs -->
<Artifact.Root id="code-editor-result" />
<Artifact.Root id="chart-revenue-2024" />
<Artifact.Root id={`message-${message.id}-artifact`} />

<!-- Bad: generic IDs -->
<Artifact.Root id="artifact1" />
<Artifact.Root id="a" />
```

###  DO: Place Panel in Your Layout

```svelte
<div class="app-layout">
  <Artifact.Provider>
    <main class="content">
      <!-- Messages with artifacts -->
    </main>

    <aside class="sidebar">
      <Artifact.Panel>
        <!-- Panel content -->
      </Artifact.Panel>
    </aside>
  </Artifact.Provider>
</div>
```

###  DO: Provide Fallbacks

```svelte
<Artifact.Title>
  {#snippet children()}
    <span class="fallback">Untitled Artifact</span>
  {/snippet}
</Artifact.Title>

<Artifact.Content>
  {#snippet children()}
    <p class="empty">No content available</p>
  {/snippet}
</Artifact.Content>
```

###  DO: Use Data Attributes for Styling

```svelte
<Artifact.Trigger>
  {#snippet children({ open })}
    <div class="trigger" data-open={open || undefined}>
      {open ? 'Close' : 'Open'}
    </div>
  {/snippet}
</Artifact.Trigger>

<style>
  .trigger[data-open] {
    background: var(--accent);
    color: white;
  }
</style>
```

---

## Anti-Patterns

###  DON'T: Nest Providers

```svelte
<!--  Wrong: Nested providers -->
<Artifact.Provider>
  <Artifact.Provider>
    ...
  </Artifact.Provider>
</Artifact.Provider>

<!--  Right: Single provider -->
<Artifact.Provider>
  <!-- All artifacts here -->
</Artifact.Provider>
```

###  DON'T: Duplicate IDs

```svelte
<!--  Wrong: Duplicate IDs -->
<Artifact.Root id="my-artifact">...</Artifact.Root>
<Artifact.Root id="my-artifact">...</Artifact.Root>

<!--  Right: Unique IDs -->
<Artifact.Root id="artifact-1">...</Artifact.Root>
<Artifact.Root id="artifact-2">...</Artifact.Root>
```

###  DON'T: Put Slot Outside Root

```svelte
<!--  Wrong: Slot outside Root -->
<Artifact.Slot title={titleSnippet} content={contentSnippet} />
<Artifact.Root id="orphan">
  <Artifact.Trigger>Open</Artifact.Trigger>
</Artifact.Root>

<!--  Right: Slot inside Root -->
<Artifact.Root id="correct">
  <Artifact.Slot title={titleSnippet} content={contentSnippet} />
  <Artifact.Trigger>Open</Artifact.Trigger>
</Artifact.Root>
```

###  DON'T: Put Trigger Outside Root

```svelte
<!--  Wrong: Trigger outside Root -->
<Artifact.Root id="my-artifact">
  <Artifact.Slot title={titleSnippet} content={contentSnippet} />
</Artifact.Root>
<Artifact.Trigger>Open</Artifact.Trigger>

<!--  Right: Trigger inside Root -->
<Artifact.Root id="my-artifact">
  <Artifact.Slot title={titleSnippet} content={contentSnippet} />
  <Artifact.Trigger>Open</Artifact.Trigger>
</Artifact.Root>
```

###  DON'T: Forget the Provider

```svelte
<!--  Wrong: Missing Provider -->
<Artifact.Root id="orphan">
  ...
</Artifact.Root>

<!--  Right: Wrapped in Provider -->
<Artifact.Provider>
  <Artifact.Root id="correct">
    ...
  </Artifact.Root>
</Artifact.Provider>
```

###  DON'T: Put Panel Outside Provider

```svelte
<!--  Wrong: Panel outside Provider -->
<Artifact.Provider>
  <Artifact.Root id="my-artifact">...</Artifact.Root>
</Artifact.Provider>
<Artifact.Panel>...</Artifact.Panel>

<!--  Right: Panel inside Provider -->
<Artifact.Provider>
  <Artifact.Root id="my-artifact">...</Artifact.Root>
  <Artifact.Panel>...</Artifact.Panel>
</Artifact.Provider>
```

###  DON'T: Use Title/Content/Close Outside Panel

```svelte
<!--  Wrong: Title outside Panel -->
<Artifact.Provider>
  <Artifact.Title />  <!-- Won't work! -->
  <Artifact.Panel>...</Artifact.Panel>
</Artifact.Provider>

<!--  Right: Title inside Panel -->
<Artifact.Provider>
  <Artifact.Panel>
    <Artifact.Title />
    <Artifact.Content />
    <Artifact.Close />
  </Artifact.Panel>
</Artifact.Provider>
```

---

## API Reference

### Data Attributes

Components expose these data attributes for styling:

| Selector | Description |
|----------|-------------|
| `[data-hpd-artifact-provider]` | Provider container |
| `[data-hpd-artifact-root]` | Root container |
| `[data-hpd-artifact-trigger]` | Trigger button |
| `[data-hpd-artifact-panel]` | Panel container |
| `[data-hpd-artifact-title]` | Title container |
| `[data-hpd-artifact-content]` | Content container |
| `[data-hpd-artifact-close]` | Close button |
| `[data-open]` | Present when artifact/panel is open |
| `[data-state="open"]` | Panel state (open/closed) |
| `[data-artifact-id]` | ID of the current artifact |

### Basic Styling Example

```css
/* Trigger styling */
[data-hpd-artifact-trigger] {
  padding: 0.5rem 1rem;
  background: #f0f0f0;
  border: 1px solid #ddd;
  border-radius: 4px;
  cursor: pointer;
}

[data-hpd-artifact-trigger][data-open] {
  background: #e0e7ff;
  border-color: #818cf8;
}

/* Panel styling */
[data-hpd-artifact-panel] {
  position: fixed;
  right: 0;
  top: 0;
  width: 400px;
  height: 100vh;
  background: white;
  box-shadow: -2px 0 10px rgba(0, 0, 0, 0.1);
  transform: translateX(100%);
  transition: transform 0.2s ease-out;
}

[data-hpd-artifact-panel][data-open] {
  transform: translateX(0);
}

/* Close button */
[data-hpd-artifact-close] {
  padding: 0.25rem 0.5rem;
  background: transparent;
  border: none;
  cursor: pointer;
  font-size: 1.5rem;
}
```

---

## Example: Complete Chat Layout

```svelte
<script lang="ts">
  import { Artifact } from '@hpd/hpd-agent-headless-ui';

  interface Message {
    id: string;
    role: 'user' | 'assistant';
    text: string;
    artifact?: {
      title: string;
      type: 'code' | 'document';
      content: string;
      language?: string;
    };
  }

  let messages: Message[] = [
    {
      id: 'msg-1',
      role: 'assistant',
      text: 'Here is the React component you requested:',
      artifact: {
        title: 'Button Component',
        type: 'code',
        content: `function Button({ children }) {\n  return <button>{children}</button>;\n}`,
        language: 'jsx'
      }
    }
  ];
</script>

<div class="chat-app">
  <Artifact.Provider onOpenChange={(open, id) => console.log('Changed:', open, id)}>
    <!-- Chat Messages -->
    <div class="chat-container">
      {#each messages as message (message.id)}
        <div class="message" data-role={message.role}>
          <p>{message.text}</p>

          {#if message.artifact}
            <Artifact.Root id={message.id}>
              {#snippet titleSnippet()}
                <span class="artifact-title">
                  {message.artifact.title}
                </span>
              {/snippet}

              {#snippet contentSnippet()}
                <div class="artifact-content" data-type={message.artifact.type}>
                  {#if message.artifact.type === 'code'}
                    <pre><code>{message.artifact.content}</code></pre>
                    {#if message.artifact.language}
                      <span class="lang-badge">{message.artifact.language}</span>
                    {/if}
                  {:else}
                    <div class="prose">{message.artifact.content}</div>
                  {/if}
                </div>
              {/snippet}

              <Artifact.Slot title={titleSnippet} content={contentSnippet} />

              <Artifact.Trigger>
                {#snippet children({ open })}
                  <div class="artifact-card">
                    <span class="icon">📄</span>
                    <span class="name">{message.artifact.title}</span>
                    <span class="action">{open ? 'Close' : 'Open'}</span>
                  </div>
                {/snippet}
              </Artifact.Trigger>
            </Artifact.Root>
          {/if}
        </div>
      {/each}
    </div>

    <!-- Artifact Panel -->
    <aside class="artifact-panel">
      <Artifact.Panel>
        {#snippet children({ open, openId, close })}
          {#if open}
            <div class="panel-inner">
              <header class="panel-header">
                <Artifact.Title>
                  {#snippet children()}
                    <h3>Artifact Preview</h3>
                  {/snippet}
                </Artifact.Title>
                <Artifact.Close>
                  {#snippet children()}
                    <button class="close-btn" aria-label="Close">×</button>
                  {/snippet}
                </Artifact.Close>
              </header>

              <div class="panel-body">
                <Artifact.Content>
                  {#snippet children()}
                    <p class="empty">No content</p>
                  {/snippet}
                </Artifact.Content>
              </div>

              <footer class="panel-footer">
                <span class="artifact-id">ID: {openId}</span>
              </footer>
            </div>
          {/if}
        {/snippet}
      </Artifact.Panel>
    </aside>
  </Artifact.Provider>
</div>

<style>
  .chat-app {
    display: flex;
    height: 100vh;
  }

  .chat-container {
    flex: 1;
    padding: 1rem;
    overflow-y: auto;
  }

  .artifact-panel {
    width: 400px;
    border-left: 1px solid #e0e0e0;
  }

  .panel-inner {
    display: flex;
    flex-direction: column;
    height: 100%;
  }

  .panel-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 1rem;
    border-bottom: 1px solid #e0e0e0;
  }

  .panel-body {
    flex: 1;
    padding: 1rem;
    overflow: auto;
  }

  .panel-footer {
    padding: 0.5rem 1rem;
    border-top: 1px solid #e0e0e0;
    font-size: 0.75rem;
    color: #666;
  }

  .artifact-card {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    padding: 0.75rem 1rem;
    background: #f5f5f5;
    border-radius: 8px;
    cursor: pointer;
  }

  .artifact-card:hover {
    background: #eee;
  }

  .close-btn {
    background: none;
    border: none;
    font-size: 1.5rem;
    cursor: pointer;
  }
</style>
```

---

## Troubleshooting

### Panel not rendering?
- Ensure the Panel is inside the Provider
- Check that at least one artifact is open

### Content not showing?
- Verify the Slot is inside its Root
- Ensure snippets are passed correctly to Slot
- Check that Title/Content are inside the Panel

### Trigger not working?
- Ensure the Trigger is inside its Root
- Check that the trigger isn't disabled

### Multiple artifacts opening?
- This shouldn't happen - only one can be open at a time
- If using custom state, ensure you're using the component callbacks

### Animations not working?
- The Panel has built-in presence management
- Use CSS transitions on the Panel element
- Check that you're using `data-open` or `data-state` attributes
