/**
 * SplitPanel State Management
 *
 * Core state management classes for the split panel system.
 */

export { SplitPanelState } from './split-panel-state.svelte.js';
export type { LayoutChangeDetail } from './split-panel-state.svelte.js';

export { LayoutHistory } from './layout-history.svelte.js';
export type { UndoRedoState } from './layout-history.svelte.js';

export { LayoutPersistence } from './layout-persistence.svelte.js';
export type { StorageState } from './layout-persistence.svelte.js';

// Helpers (internal use)
export * from './split-panel-state-helpers.js';
