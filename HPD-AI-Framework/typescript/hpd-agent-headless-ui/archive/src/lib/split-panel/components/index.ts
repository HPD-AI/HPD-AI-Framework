/**
 * SplitPanel Component API
 *
 * Higher-level component-friendly wrappers around core state management.
 * Provides preset configurations, registration, and snippet props.
 */

// Root state
export { SplitPanelRootState } from './split-panel-root-state.svelte.js';
export type {
	SplitPanelRootStateOpts,
	SplitPanelLayoutState,
	PaneStateInfo,
	LayoutPreset,
	StorageBackend
} from './split-panel-root-state.svelte.js';

// Pane state
export { SplitPanelPaneState } from './split-panel-pane-state.svelte.js';
export type { SplitPanelPaneStateOpts } from './split-panel-pane-state.svelte.js';

// Split state
export { SplitPanelSplitState } from './split-panel-split-state.svelte.js';
export type { SplitPanelSplitStateOpts } from './split-panel-split-state.svelte.js';
export { SplitPanelSplitContext } from './split-panel-split-state.svelte.js';

// Handle state
export { SplitPanelHandleState } from './split-panel-handle-state.svelte.js';
export type { SplitPanelHandleStateOpts } from './split-panel-handle-state.svelte.js';

// Context and attributes
export { SplitPanelRootContext } from './split-panel-context.js';
export { splitPanelAttrs } from './split-panel-attrs.js';
