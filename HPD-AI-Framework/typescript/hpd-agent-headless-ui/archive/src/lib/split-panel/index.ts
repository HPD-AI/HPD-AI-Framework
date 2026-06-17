/**
 * SplitPanel - Advanced Split Panel Layout System
 *
 * A production-ready split panel component system with:
 * - Arbitrary nesting depth
 * - Smooth animations and RAF-batched resize
 * - Undo/redo with full state restoration
 * - Persistence with ShellOS Storage
 * - Keyboard navigation and accessibility
 * - Property-based testing
 */

// Core types
export * from './types/index.js';

// Utilities
export * from './utils/index.js';

// State management
export { SplitPanelState } from './state/index.js';
export type { LayoutChangeDetail } from './state/index.js';

export { LayoutHistory } from './state/index.js';
export type { UndoRedoState } from './state/index.js';

export { LayoutPersistence } from './state/index.js';
export type { StorageState } from './state/index.js';

// Component API (wrappers and higher-level interfaces)
export * from './components/index.js';

// Svelte components (Bits UI namespace pattern)
export { default as Root } from './components/split-panel-root.svelte';
export { default as Split } from './components/split-panel-split.svelte';
export { default as Pane } from './components/split-panel-pane.svelte';
export { default as Handle } from './components/split-panel-handle.svelte';

// Actions
export * from './actions/index.js';
