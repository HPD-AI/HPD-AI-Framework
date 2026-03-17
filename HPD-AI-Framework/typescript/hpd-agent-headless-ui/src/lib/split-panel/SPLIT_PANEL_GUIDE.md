# Split Panel Layout System

A declarative, flexible split panel layout system for Svelte 5. Build complex resizable layouts with minimal code.

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

### Declarative Structure

The layout is defined declaratively using nested components:

```svelte
<SplitPanel.Root id="my-layout">
  <SplitPanel.Split axis="horizontal">
    <SplitPanel.Pane id="sidebar">...</SplitPanel.Pane>
    <SplitPanel.Handle />
    <SplitPanel.Pane id="main">...</SplitPanel.Pane>
  </SplitPanel.Split>
</SplitPanel.Root>
```

### Tree-Based Layout

- **Root**: Container that manages all state
- **Split**: Creates a horizontal or vertical split container
- **Pane**: A leaf node that holds content
- **Handle**: Drag handle between panes for resizing

---

## Components

### `SplitPanel.Root`

The root container. All split panels must be wrapped in a Root.

```svelte
<SplitPanel.Root
  id="unique-id"
  debug={false}
  bind:layout={layoutState}
>
  {#snippet children({ layoutState, canUndo, canRedo })}
    <!-- Your layout here -->
  {/snippet}
</SplitPanel.Root>
```

**Props:**
| Prop | Type | Default | Description |
|------|------|---------|-------------|
| `id` | `string` | required | Unique identifier for the layout |
| `storageKey` | `string` | - | Key for localStorage persistence |
| `storageBackend` | `StorageBackend` | - | Custom storage backend |
| `preset` | `LayoutPreset` | - | Preset layout configuration |
| `undoable` | `boolean` | `false` | Enable undo/redo |
| `debug` | `boolean` | `false` | Enable debug logging |
| `layout` | `SplitPanelRootState` | - | Bindable state for external access |
| `onLayoutChange` | `(event) => void` | - | Callback when layout changes |
| `onPaneClose` | `(paneId) => void` | - | Callback when pane closes |
| `onPaneFocus` | `(paneId) => void` | - | Callback when pane receives focus |

### `SplitPanel.Split`

Creates a split container with horizontal or vertical axis.

```svelte
<SplitPanel.Split axis="horizontal">
  {#snippet children()}
    <!-- Panes and Handles -->
  {/snippet}
</SplitPanel.Split>
```

**Props:**
| Prop | Type | Default | Description |
|------|------|---------|-------------|
| `axis` | `'horizontal' \| 'vertical'` | required | Split direction |

### `SplitPanel.Pane`

A resizable pane that holds content.

```svelte
<SplitPanel.Pane
  id="sidebar"
  minSize={200}
  maxSize={600}
  initialSize={30}
  initialSizeUnit="percent"
  priority="low"
  collapsed={false}
  snapPoints={[200, 400]}
  autoCollapseThreshold={100}
>
  {#snippet children({ size, toggle, isCollapsed })}
    <div>Pane content - {size}px wide</div>
  {/snippet}
</SplitPanel.Pane>
```

**Props:**
| Prop | Type | Default | Description |
|------|------|---------|-------------|
| `id` | `string` | required | Unique pane identifier |
| `minSize` | `number` | `0` | Minimum size in pixels |
| `maxSize` | `number` | `Infinity` | Maximum size in pixels |
| `initialSize` | `number` | - | Initial size value |
| `initialSizeUnit` | `'percent' \| 'pixels'` | `'pixels'` | Unit for initialSize |
| `priority` | `'low' \| 'normal' \| 'high'` | `'normal'` | Resize priority |
| `collapsed` | `boolean` | `false` | Initial collapsed state |
| `snapPoints` | `number[]` | `[]` | Sizes to snap to during drag |
| `autoCollapseThreshold` | `number` | - | Auto-collapse when below this size |

**Snippet Props (children):**
| Prop | Type | Description |
|------|------|-------------|
| `size` | `number` | Current size in pixels |
| `toggle` | `() => void` | Toggle collapsed state |
| `isCollapsed` | `boolean` | Whether pane is collapsed |

### `SplitPanel.Handle`

Draggable resize handle between panes.

```svelte
<SplitPanel.Handle />
```

**Props:**
| Prop | Type | Default | Description |
|------|------|---------|-------------|
| `disabled` | `boolean` | `false` | Disable dragging |

---

## Features

### 1. Initial Sizes (Percentage-Based)

Set initial pane sizes as percentages of the container:

```svelte
<SplitPanel.Pane id="sidebar" initialSize={30} initialSizeUnit="percent">
  <!-- Takes 30% of horizontal space -->
</SplitPanel.Pane>

<SplitPanel.Handle />

<SplitPanel.Pane id="main" initialSize={70} initialSizeUnit="percent">
  <!-- Takes 70% of horizontal space -->
</SplitPanel.Pane>
```

> **Note:** Percentages are calculated at mount time. If total exceeds 100%, they're normalized.

### 2. Collapsible Panes

Panes can be collapsed/expanded programmatically or via auto-collapse:

```svelte
<!-- Start collapsed -->
<SplitPanel.Pane id="panel" collapsed={true}>
  {#snippet children({ toggle, isCollapsed })}
    <button onclick={toggle}>
      {isCollapsed ? 'Expand' : 'Collapse'}
    </button>
  {/snippet}
</SplitPanel.Pane>
```

### 3. Auto-Collapse Threshold

Automatically collapse when dragged below a threshold:

```svelte
<SplitPanel.Pane
  id="sidebar"
  minSize={50}
  autoCollapseThreshold={100}
>
  <!-- Auto-collapses when dragged below 100px -->
</SplitPanel.Pane>
```

### 4. Snap Points

Snap to specific sizes during drag:

```svelte
<SplitPanel.Pane
  id="sidebar"
  snapPoints={[200, 350, 500]}
>
  <!-- Snaps to 200px, 350px, or 500px when dragging near -->
</SplitPanel.Pane>
```

### 5. Priority System

Control which panes shrink/grow first during resize:

```svelte
<!-- Low priority: shrinks first, grows last -->
<SplitPanel.Pane id="sidebar" priority="low" />

<!-- Normal priority: default behavior -->
<SplitPanel.Pane id="content" priority="normal" />

<!-- High priority: shrinks last, grows first -->
<SplitPanel.Pane id="main" priority="high" />
```

### 6. External Layout Control

Access layout state from outside the split panel tree:

```svelte
<script>
  let layoutState = $state(null);
</script>

<SplitPanel.Root id="layout" bind:layout={layoutState}>
  <!-- ... -->
</SplitPanel.Root>

<!-- Toggle sidebar from anywhere -->
<button onclick={() => layoutState?.togglePane('sidebar')}>
  Toggle Sidebar
</button>
```

**Available methods on `layoutState`:**
- `togglePane(paneId)` - Toggle a pane's collapsed state
- `collapsePane(paneId)` - Collapse a specific pane
- `expandPane(paneId)` - Expand a specific pane
- `setPaneSize(paneId, size, unit?)` - Set a pane's size (pixels or percent)
- `resetLayout()` - Reset to initial layout
- `undo()` / `redo()` - Undo/redo (if `undoable={true}`)

**Examples of `setPaneSize`:**
```typescript
// Set to 600 pixels (default)
layoutState?.setPaneSize('panel', 600);
layoutState?.setPaneSize('panel', 600, 'pixels');

// Set to 70% of container width
layoutState?.setPaneSize('panel', 70, 'percent');
```

### 7. Nested Splits

Create complex layouts with nested splits:

```svelte
<SplitPanel.Split axis="vertical">
  <!-- Top row -->
  <SplitPanel.Split axis="horizontal">
    <SplitPanel.Pane id="sidebar" />
    <SplitPanel.Handle />
    <SplitPanel.Pane id="main" />
  </SplitPanel.Split>

  <SplitPanel.Handle />

  <!-- Bottom panel (full width) -->
  <SplitPanel.Pane id="bottom" />
</SplitPanel.Split>
```

### 8. Persistence

Save and restore layout state:

```svelte
<SplitPanel.Root
  id="my-layout"
  storageKey="my-layout-state"
>
  <!-- Layout is saved to localStorage automatically -->
</SplitPanel.Root>
```

### 9. Debug Mode

Enable debug logging to troubleshoot layout issues:

```svelte
<SplitPanel.Root id="layout" debug={true}>
  <!-- Logs resize events, state changes, etc. -->
</SplitPanel.Root>
```

---

## Usage Patterns

###  DO: Use Snippets for Pane Content

```svelte
<SplitPanel.Pane id="sidebar">
  {#snippet children({ size, toggle, isCollapsed })}
    <!-- Access size, toggle, isCollapsed here -->
    {#if isCollapsed}
      <CollapsedView {toggle} />
    {:else}
      <ExpandedView {size} {toggle} />
    {/if}
  {/snippet}
</SplitPanel.Pane>
```

###  DO: Set Meaningful IDs

```svelte
<!-- Good: descriptive IDs -->
<SplitPanel.Pane id="sidebar" />
<SplitPanel.Pane id="chat" />
<SplitPanel.Pane id="artifact-preview" />

<!-- Bad: generic IDs -->
<SplitPanel.Pane id="pane1" />
<SplitPanel.Pane id="left" />
```

###  DO: Use Priority for Main Content

```svelte
<!-- Main content should have high priority -->
<SplitPanel.Pane id="main" priority="high" />

<!-- Auxiliary panels should have low priority -->
<SplitPanel.Pane id="sidebar" priority="low" />
```

###  DO: Use Percentages for Responsive Layouts

```svelte
<SplitPanel.Pane id="sidebar" initialSize={25} initialSizeUnit="percent" />
<SplitPanel.Pane id="main" initialSize={75} initialSizeUnit="percent" />
```

###  DO: Handle Collapsed State in Snippets

```svelte
<SplitPanel.Pane id="panel" autoCollapseThreshold={100}>
  {#snippet children({ size, toggle, isCollapsed })}
    <div data-collapsed={isCollapsed || size < 80}>
      {#if isCollapsed || size < 80}
        <button onclick={toggle}>Expand</button>
      {:else}
        <FullContent />
      {/if}
    </div>
  {/snippet}
</SplitPanel.Pane>
```

###  DO: Place Handle Between Every Pair of Panes

```svelte
<SplitPanel.Split axis="horizontal">
  <SplitPanel.Pane id="a" />
  <SplitPanel.Handle />  <!-- Between a and b -->
  <SplitPanel.Pane id="b" />
  <SplitPanel.Handle />  <!-- Between b and c -->
  <SplitPanel.Pane id="c" />
</SplitPanel.Split>
```

---

## Anti-Patterns

###  DON'T: Manually Calculate Flex/Percentages

The library handles flex calculations automatically.

```svelte
<!--  Wrong: Don't do this -->
<div style="flex: 0.3">...</div>
<div style="flex: 0.7">...</div>

<!--  Right: Use initialSize -->
<SplitPanel.Pane initialSize={30} initialSizeUnit="percent" />
<SplitPanel.Pane initialSize={70} initialSizeUnit="percent" />
```

###  DON'T: Manually Set Width/Height on Panes

The library manages all sizing. Don't override with CSS.

```svelte
<!--  Wrong: Don't manually set dimensions -->
<SplitPanel.Pane id="sidebar">
  {#snippet children()}
    <div style="width: 300px">...</div>  <!-- Don't do this! -->
  {/snippet}
</SplitPanel.Pane>

<!--  Right: Let the pane control size -->
<SplitPanel.Pane id="sidebar" minSize={200} maxSize={400}>
  {#snippet children()}
    <div style="width: 100%; height: 100%">...</div>
  {/snippet}
</SplitPanel.Pane>
```

###  DON'T: Use CSS Resize

The library provides drag handles. Don't add CSS resize.

```css
/*  Wrong: Don't use CSS resize */
.pane-content {
  resize: horizontal;
  overflow: auto;
}
```

###  DON'T: Nest Roots

Only one Root per layout tree.

```svelte
<!--  Wrong: Nested roots -->
<SplitPanel.Root id="outer">
  <SplitPanel.Root id="inner">  <!-- Don't nest! -->
    ...
  </SplitPanel.Root>
</SplitPanel.Root>

<!--  Right: Use nested Splits instead -->
<SplitPanel.Root id="layout">
  <SplitPanel.Split axis="horizontal">
    <SplitPanel.Pane id="sidebar" />
    <SplitPanel.Handle />
    <SplitPanel.Split axis="vertical">
      <SplitPanel.Pane id="main" />
      <SplitPanel.Handle />
      <SplitPanel.Pane id="bottom" />
    </SplitPanel.Split>
  </SplitPanel.Split>
</SplitPanel.Root>
```

###  DON'T: Forget Handles

Missing handles means panes can't be resized.

```svelte
<!--  Wrong: Missing handles -->
<SplitPanel.Split axis="horizontal">
  <SplitPanel.Pane id="a" />
  <SplitPanel.Pane id="b" />  <!-- Can't resize! -->
</SplitPanel.Split>

<!--  Right: Include handles -->
<SplitPanel.Split axis="horizontal">
  <SplitPanel.Pane id="a" />
  <SplitPanel.Handle />
  <SplitPanel.Pane id="b" />
</SplitPanel.Split>
```

###  DON'T: Mix Percentage and Pixel initialSize in Same Split

For predictable results, use the same unit for siblings.

```svelte
<!--  Confusing: Mixed units -->
<SplitPanel.Pane initialSize={30} initialSizeUnit="percent" />
<SplitPanel.Handle />
<SplitPanel.Pane initialSize={400} initialSizeUnit="pixels" />

<!--  Better: Consistent units -->
<SplitPanel.Pane initialSize={30} initialSizeUnit="percent" />
<SplitPanel.Handle />
<SplitPanel.Pane initialSize={70} initialSizeUnit="percent" />
```

###  DON'T: Use position: absolute/fixed in Pane Content

This breaks the layout flow.

```svelte
<!--  Wrong: Breaks layout -->
<SplitPanel.Pane id="sidebar">
  {#snippet children()}
    <div style="position: absolute; top: 0; left: 0;">
      ...
    </div>
  {/snippet}
</SplitPanel.Pane>

<!--  Right: Use normal flow -->
<SplitPanel.Pane id="sidebar">
  {#snippet children()}
    <div style="height: 100%; overflow: auto;">
      ...
    </div>
  {/snippet}
</SplitPanel.Pane>
```

###  DON'T: Manually Track Pane Sizes

Use the `size` prop from the snippet.

```svelte
<!--  Wrong: Manual tracking -->
<script>
  let sidebarSize = $state(300);
</script>

<!--  Right: Use snippet prop -->
<SplitPanel.Pane id="sidebar">
  {#snippet children({ size })}
    <div>Current size: {size}px</div>
  {/snippet}
</SplitPanel.Pane>
```

---

## API Reference

### SplitPanelRootState Methods

When using `bind:layout={layoutState}`:

| Method | Signature | Description |
|--------|-----------|-------------|
| `togglePane` | `(paneId: string) => void` | Toggle collapsed state |
| `collapsePane` | `(paneId: string) => void` | Collapse a pane |
| `expandPane` | `(paneId: string) => void` | Expand a pane |
| `setPaneSize` | `(paneId: string, size: number, unit?: 'pixels' \| 'percent') => void` | Set pane size (pixels default, or percent of container) |
| `getPaneState` | `(paneId: string) => PaneStateInfo \| null` | Get pane state (size, isCollapsed, isFocused) |
| `resetLayout` | `() => void` | Reset to initial layout |
| `undo` | `() => void` | Undo last change (if undoable) |
| `redo` | `() => void` | Redo last undone change |

### CSS Custom Properties

The library sets these CSS custom properties on panes:

| Property | Description |
|----------|-------------|
| `--pane-size` | Current size in pixels |
| `--pane-min-size` | Minimum size |
| `--pane-max-size` | Maximum size |

---

## Example: Complete Layout

```svelte
<script lang="ts">
  import { SplitPanel } from '@hpd/hpd-agent-headless-ui';

  let layoutState = $state(null);
</script>

<div class="app">
  <SplitPanel.Root id="main-layout" debug={false} bind:layout={layoutState}>
    {#snippet children()}
      <SplitPanel.Split axis="vertical">
        {#snippet children()}
          <!-- Main row -->
          <SplitPanel.Split axis="horizontal">
            {#snippet children()}
              <!-- Sidebar -->
              <SplitPanel.Pane
                id="sidebar"
                minSize={50}
                initialSize={25}
                initialSizeUnit="percent"
                priority="low"
                snapPoints={[50, 250, 350]}
                autoCollapseThreshold={80}
              >
                {#snippet children({ size, toggle, isCollapsed })}
                  {#if isCollapsed || size < 60}
                    <button onclick={toggle}>☰</button>
                  {:else}
                    <nav>
                      <h2>Sidebar</h2>
                      <button onclick={toggle}>Collapse</button>
                    </nav>
                  {/if}
                {/snippet}
              </SplitPanel.Pane>

              <SplitPanel.Handle />

              <!-- Main content -->
              <SplitPanel.Pane
                id="main"
                minSize={300}
                initialSize={75}
                initialSizeUnit="percent"
                priority="high"
              >
                {#snippet children()}
                  <main>Main Content</main>
                {/snippet}
              </SplitPanel.Pane>
            {/snippet}
          </SplitPanel.Split>

          <SplitPanel.Handle />

          <!-- Bottom panel -->
          <SplitPanel.Pane
            id="bottom"
            minSize={40}
            autoCollapseThreshold={60}
            collapsed={true}
          >
            {#snippet children({ toggle, isCollapsed })}
              {#if isCollapsed}
                <button onclick={toggle}>Show Terminal</button>
              {:else}
                <div>
                  <button onclick={toggle}>Hide</button>
                  <pre>Terminal output...</pre>
                </div>
              {/if}
            {/snippet}
          </SplitPanel.Pane>
        {/snippet}
      </SplitPanel.Split>
    {/snippet}
  </SplitPanel.Root>

  <!-- External control -->
  <footer>
    <button onclick={() => layoutState?.togglePane('sidebar')}>
      Toggle Sidebar
    </button>
  </footer>
</div>

<style>
  .app {
    display: flex;
    flex-direction: column;
    height: 100vh;
  }
</style>
```

---

## Styling with Data Attributes

This is a headless UI library - you provide all styling. Components expose data attributes for CSS targeting:

### Data Attributes

| Selector | Description |
|----------|-------------|
| `[data-split-panel-root]` | Root container |
| `[data-split-panel-split]` | Split containers (branches) |
| `[data-split-panel-pane]` | Pane containers (leaves) |
| `[data-split-panel-handle]` | Resize handles |
| `[data-orientation="horizontal"]` | Horizontal split/handle |
| `[data-orientation="vertical"]` | Vertical split/handle |
| `[data-dragging]` | Added to root during drag operations |
| `[data-state="dragging"]` | Added to handle while dragging |

### Preventing Text Selection During Drag

Add these styles to prevent text highlighting when dragging handles:

```css
[data-split-panel-root][data-dragging] {
  user-select: none;
  -webkit-user-select: none;
}

[data-split-panel-root][data-dragging] * {
  user-select: none;
  -webkit-user-select: none;
}
```

### Basic Handle Styling

```css
[data-split-panel-handle] {
  background: transparent;
  transition: background 0.15s;
}

[data-split-panel-handle]:hover,
[data-split-panel-handle][data-state="dragging"] {
  background: rgba(59, 130, 246, 0.5);
}

[data-split-panel-handle][data-orientation="horizontal"] {
  width: 4px;
  cursor: col-resize;
}

[data-split-panel-handle][data-orientation="vertical"] {
  height: 4px;
  cursor: row-resize;
}
```

---

## Troubleshooting

### Panes not resizing?
- Ensure `<SplitPanel.Handle />` is between panes
- Check that Root has a defined height

### Content overflowing?
- Add `overflow: auto` or `overflow: hidden` to your content wrapper
- Ensure content uses `height: 100%`

### Layout not persisting?
- Set `storageKey` prop on Root
- Check browser localStorage permissions

### Collapsed state not working?
- Use `isCollapsed` from snippet props, not your own state
- Ensure pane has an `id`

### Debug issues?
- Set `debug={true}` on Root
- Check browser console for layout logs
