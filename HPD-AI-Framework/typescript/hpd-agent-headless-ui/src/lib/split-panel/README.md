# SplitPanel

Production-ready split panel layout system for Svelte 5 with advanced features.

## Features

- **Arbitrary nesting depth** - Recursive branch/leaf structure
- **RAF-batched resize** - 60fps performance even with 1000Hz mice
- **Undo/redo** - Full state restoration with history
- **Persistence** - ShellOS Storage integration
- **Keyboard navigation** - 2D spatial navigation with arrow keys
- **Accessibility** - ARIA roles, focus management, keyboard support
- **Property-based testing** - 100+ test runs with fast-check
- **Type-safe** - Full TypeScript support with discriminated unions
- **CSS variables** - Themeable via data attributes

## Installation

```bash
bun add @hpd/hpd-agent-headless-ui
```

This is a headless UI library - you provide all styling via CSS targeting the data attributes:
- `[data-split-panel-root]` - Root container
- `[data-split-panel-split]` - Split containers
- `[data-split-panel-pane]` - Pane containers
- `[data-split-panel-handle]` - Resize handles
- `[data-dragging]` - Added to root during drag operations

## Quick Start

### Basic Example

```svelte
<script lang="ts">
	import { SplitPanelRootState } from 'shellos-headless-ui/split-panel';

	const rootState = new SplitPanelRootState({
		getContainerWidth: () => 1000,
		getContainerHeight: () => 800
	});

	// Add panels
	rootState.layoutState.addPanel('panel-1', [], { size: 300 });
	rootState.layoutState.addPanel('panel-2', [], { size: 500 });
</script>

<div class="container">
	<!-- Render your layout here -->
</div>
```

### Complete Example with Components

See [examples/LayoutTreeExample.svelte](./examples/LayoutTreeExample.svelte) for a full working example with:

- Polymorphic rendering
- Keyboard navigation
- Resize handles
- Panel collapse/expand

## Core Concepts

### Layout Tree Structure

The layout is a tree of `BranchNode` (containers) and `LeafNode` (panels):

```typescript
type LayoutNode = BranchNode | LeafNode;

interface BranchNode {
	type: 'branch';
	axis: 'row' | 'column';
	children: LayoutNode[];
	flexes: Float32Array; // Flex values for each child
}

interface LeafNode {
	type: 'leaf';
	id: string;
	size: number;
	minSize?: number;
	maxSize?: number;
}
```

### State Management

The system uses three state classes:

1. **`SplitPanelState`** - Core layout engine
   - Add/remove panels
   - Resize dividers
   - Toggle collapse
   - Serialize/deserialize

2. **`LayoutHistory`** - Undo/redo management
   - Automatic snapshots
   - Debounced saving
   - State restoration

3. **`LayoutPersistence`** - Storage integration
   - Save to localStorage/ShellOS Storage
   - Auto-save on changes
   - Load on mount

## API Reference

### SplitPanelState

Core layout engine class.

#### Constructor

```typescript
new SplitPanelState(options: {
  getContainerWidth: () => number;
  getContainerHeight: () => number;
})
```

#### Methods

##### `addPanel(panelId, path, config)`

Add a new panel to the layout.

```typescript
addPanel(
  panelId: string | undefined,
  path: number[],
  config: Partial<LeafNode>
): Result<boolean>
```

**Parameters:**

- `panelId` - Unique panel ID (auto-generated if undefined)
- `path` - Path to parent branch ([] for root)
- `config` - Panel configuration (size, minSize, maxSize)

**Example:**

```typescript
state.addPanel('my-panel', [], { size: 300, minSize: 100 });
```

##### `removePanel(panelId)`

Remove a panel from the layout.

```typescript
removePanel(panelId: string): Result<boolean>
```

##### `togglePanel(panelId)`

Toggle panel collapsed state.

```typescript
togglePanel(panelId: string): Result<void>
```

##### `resizeDivider(parentPath, dividerIndex, delta)`

Resize a divider between panels.

```typescript
resizeDivider(
  parentPath: number[],
  dividerIndex: number,
  delta: number
): Result<void>
```

**Parameters:**

- `parentPath` - Path to parent branch
- `dividerIndex` - Index of divider (0-based, between active children)
- `delta` - Pixels to move (positive = right/down, negative = left/up)

##### `setContainerSize(width, height)`

Update container dimensions and recompute layout.

```typescript
setContainerSize(width: number, height: number): void
```

##### `serialize(width, height)`

Serialize layout to JSON.

```typescript
serialize(width: number, height: number): SerializedLayout
```

##### `deserialize(data)`

Restore layout from serialized data.

```typescript
deserialize(data: SerializedLayout): void
```

### LayoutHistory

Undo/redo state management.

#### Constructor

```typescript
new LayoutHistory(layoutState: SplitPanelState, options?: {
  maxHistorySize?: number; // Default: 50
  debounceMs?: number;     // Default: 300
})
```

#### Methods

##### `undo()`

Undo last operation.

```typescript
undo(): boolean // Returns true if undo was performed
```

##### `redo()`

Redo last undone operation.

```typescript
redo(): boolean // Returns true if redo was performed
```

#### Properties

- `canUndo: boolean` - Whether undo is available
- `canRedo: boolean` - Whether redo is available
- `historySize: number` - Current history stack size

### LayoutPersistence

Storage persistence management.

#### Constructor

```typescript
new LayoutPersistence(layoutState: SplitPanelState, options: {
  storageKey: string;
  storage?: Storage; // Default: localStorage
  autoSave?: boolean; // Default: true
  debounceMs?: number; // Default: 1000
})
```

#### Methods

##### `save()`

Manually save current layout.

```typescript
save(): void
```

##### `load()`

Load layout from storage.

```typescript
load(): boolean // Returns true if loaded successfully
```

##### `clear()`

Clear saved layout from storage.

```typescript
clear(): void
```

## Keyboard Navigation

### Panel Navigation

- **Arrow Keys** - Navigate between panels spatially (2D navigation)
- **Space/Enter** - Toggle panel collapse
- **Tab** - Standard focus navigation

### Handle Navigation

- **Arrow Keys** - Resize by 10px increments
- **Shift + Arrow** - Resize by 50px increments
- **Home/End** - Collapse to min/max size

## CSS Styling

The system uses data attributes for styling:

### Data Attributes

- `[data-split-panel-root]` - Root container
- `[data-split-panel-split]` - Branch node container
- `[data-split-panel-pane]` - Leaf panel
- `[data-split-panel-handle]` - Resize handle
- `[data-orientation="horizontal|vertical"]` - Orientation
- `[data-focused]` - Focused element
- `[data-collapsed]` - Collapsed panel
- `[data-maximized]` - Maximized panel
- `[data-dragging]` - Dragging state
- `[data-disabled]` - Disabled state

### CSS Variables

Customize via CSS variables:

```css
:root {
	--focus-ring-color: #2196f3;
	--handle-color: #e0e0e0;
	--handle-hover-color: #2196f3;
	--handle-active-color: #1976d2;
}
```

### Custom Styling

```css
/* Custom panel style */
[data-split-panel-pane] {
	background: white;
	border: 1px solid #ddd;
}

/* Custom handle style */
[data-split-panel-handle] {
	background: linear-gradient(to right, #eee, #ccc, #eee);
}
```

## Actions

### `registerPanel`

Svelte action for automatic panel registration.

```typescript
use:registerPanel={{ layoutState, panelId }}
```

**Features:**

- Automatic ResizeObserver setup
- RAF-batched resize updates
- O(1) element registry
- Rect caching for navigation

**Example:**

```svelte
<div
	use:registerPanel={{ layoutState: rootState.layoutState, panelId: node.id }}
	data-panel-id={node.id}
>
	Panel content
</div>
```

## Utilities

### Layout Rendering

```typescript
import {
	getActiveChildren,
	computeGridTemplate,
	encodeResizeHandle,
	decodeResizeHandle
} from 'shellos-headless-ui/split-panel';
```

#### `getActiveChildren(node)`

Get active (non-collapsed) children with metadata.

```typescript
getActiveChildren(node: BranchNode): ActiveChild[]
```

#### `computeGridTemplate(node, axis, layoutState)`

Generate CSS Grid template string.

```typescript
computeGridTemplate(
  node: BranchNode,
  axis: 'row' | 'column',
  layoutState: SplitPanelState
): string
```

### Keyboard Navigation

```typescript
import { findPanelInDirection, collectPanelIds } from 'shellos-headless-ui/split-panel';
```

#### `findPanelInDirection(fromPanelId, direction, panelIds)`

Find next panel in a direction using geometry-based navigation.

```typescript
findPanelInDirection(
  fromPanelId: string,
  direction: 'up' | 'down' | 'left' | 'right',
  panelIds: string[]
): string | null
```

**Features:**

- O(n) for < 100 panels
- O(log n) for >= 100 panels (spatial index)
- Overlap detection
- Distance-based sorting

## Error Handling

All operations return `Result<T>` for type-safe error handling:

```typescript
const result = state.addPanel('panel-1', [], { size: 300 });

if (result.ok) {
	console.log('Panel added successfully');
} else {
	console.error('Error:', result.error);
}
```

### Error Types

```typescript
type LayoutError =
	| { type: 'not-found'; id: string }
	| { type: 'invalid-path'; path: number[] }
	| { type: 'invalid-config'; reason: string }
	| { type: 'layout-in-progress' }
	| { type: 'serialization-error'; reason: string };
```

## Performance

### Benchmarks

Based on stress tests:

- **Queue 2000 resize events**: < 100ms
- **Add 50 panels**: < 1 second
- **100 toggle operations**: < 500ms
- **Serialize 30 panels**: < 100ms
- **Deserialize 30 panels**: < 100ms
- **1000 mixed operations**: Completes without errors

### Optimizations

- **RAF Batching**: Coalesces high-frequency resize events
- **Path Caching**: O(1) panel lookups
- **Element Registry**: O(1) DOM access
- **Spatial Index**: O(log n) navigation for large layouts
- **Dimension Caching**: Avoids repeated layout calculations

## Testing

Comprehensive test suite with property-based testing:

```bash
# Run all tests
bun test

# Run property tests
bun test layout.property

# Run stress tests
bun test layout.stress

# Run with coverage
bun test --coverage
```

See [\_\_tests\_\_/README.md](./__tests__/README.md) for details.

## Examples

See the [examples](./examples/) directory:

- [LayoutTreeExample.svelte](./examples/LayoutTreeExample.svelte) - Complete working example
- [BasicExample.svelte](./examples/BasicExample.svelte) - Simple usage

## Architecture

Based on the ShellOSUI V3 Final Architecture proposal:

- **BEAR Constraints**: Boundary/Epsilon/Active-set/Remainder validation
- **Discriminated Unions**: Type-safe node operations
- **Result Pattern**: Explicit error handling
- **State Machine**: Idle/busy guards for consistency
- **Svelte 5 Runes**: Reactive state management

## License

MIT

## Contributing

See contribution guidelines in the main repository.
