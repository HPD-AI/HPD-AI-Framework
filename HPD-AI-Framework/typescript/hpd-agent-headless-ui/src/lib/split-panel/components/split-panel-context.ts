/**
 * Split Panel Context
 *
 * Provides Context instance for sharing SplitPanelRootState between components.
 * Uses runed's Context for type-safe parent-child state injection.
 */

import { Context } from 'runed';
import type { SplitPanelRootState } from './split-panel-root-state.svelte.js';

/**
 * Context for SplitPanel.Root state.
 * Child components (Pane, Handle) access root state via this context.
 */
export const SplitPanelRootContext = new Context<SplitPanelRootState>('SplitPanel.Root');
