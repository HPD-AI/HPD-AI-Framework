/**
 * registerPanel Action
 *
 * Svelte action that registers a panel element with the layout state.
 * Features:
 * - Automatic registration/unregistration on mount/destroy
 * - ResizeObserver for tracking panel size changes
 * - RAF-batched updates to prevent forced reflows
 * - Element registry for O(1) lookups during snapshot operations
 */

import type { Action } from 'svelte/action';
import type { SplitPanelState } from '../state/split-panel-state.svelte.js';

/**
 * Parameters for registerPanel action.
 */
export interface RegisterPanelParams {
	/** Layout state instance */
	layoutState: SplitPanelState;

	/** Panel ID to register */
	panelId: string;

	/** Optional callback when panel is resized */
	onResize?: (panelId: string, rect: DOMRectReadOnly) => void;
}

/**
 * Panel rectangle cache for geometry-based operations.
 * Stores cached DOMRectReadOnly for each panel.
 */
const panelRectCache = new Map<string, DOMRectReadOnly>();

/**
 * Panel element registry for O(1) lookups.
 * Replaces O(n) document.querySelector calls.
 */
const panelElementRegistry = new Map<string, HTMLElement>();

/**
 * Register a panel element for size tracking and geometry operations.
 *
 * @example
 * ```svelte
 * <div use:registerPanel={{ layoutState, panelId: 'main' }}>
 *   Panel content
 * </div>
 * ```
 */
export const registerPanel: Action<HTMLElement, RegisterPanelParams> = (node, params) => {
	const { layoutState, panelId, onResize } = params;

	// Register element in O(1) registry
	panelElementRegistry.set(panelId, node);

	// Cache initial rect
	updatePanelRect(panelId, node);

	// Set up ResizeObserver for this panel
	// Batches updates via RAF to coalesce multiple resize events
	let rafPending = false;
	const observer = new ResizeObserver((entries) => {
		if (rafPending) return; // Already scheduled for this frame

		rafPending = true;
		requestAnimationFrame(() => {
			rafPending = false;

			// Update cached rect
			const rect = updatePanelRect(panelId, node);

			// Trigger callback if provided
			if (rect && onResize) {
				onResize(panelId, rect);
			}
		});
	});

	observer.observe(node);

	// Cleanup function
	return {
		destroy() {
			// Disconnect observer
			observer.disconnect();

			// Unregister element
			panelElementRegistry.delete(panelId);
			panelRectCache.delete(panelId);
		}
	};
};

/**
 * Update cached rect for a panel.
 * Returns the rect for immediate use.
 */
function updatePanelRect(panelId: string, node: HTMLElement): DOMRectReadOnly | null {
	const rect = node.getBoundingClientRect();
	panelRectCache.set(panelId, rect);
	return rect;
}

/**
 * Get cached rect for a panel (O(1) lookup).
 * Used by keyboard navigation for geometry-based operations.
 */
export function getPanelRect(panelId: string): DOMRectReadOnly | undefined {
	return panelRectCache.get(panelId);
}

/**
 * Get panel element from registry (O(1) lookup).
 * Used by focus management and snapshot operations.
 */
export function getPanelElement(panelId: string): HTMLElement | undefined {
	return panelElementRegistry.get(panelId);
}

/**
 * Get all registered panel IDs.
 */
export function getRegisteredPanelIds(): string[] {
	return Array.from(panelElementRegistry.keys());
}

/**
 * Clear all panel registrations (for testing/cleanup).
 */
export function clearPanelRegistry(): void {
	panelRectCache.clear();
	panelElementRegistry.clear();
}
