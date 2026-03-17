/**
 * Panel Descriptor Registry
 *
 * Manages panel state serialization/deserialization for undo/redo and persistence.
 * Applications register descriptors by panel type to define how to serialize/restore content state.
 *
 * Design: Descriptor-based pattern allows each panel type to define its own serialization logic
 * without coupling the layout system to specific panel implementations.
 */

import type { PanelDescriptor } from '../types/index.js';

/**
 * Global panel descriptor registry.
 * Maps panel type to its serializer/deserializer.
 */
const panelDescriptors = new Map<string, PanelDescriptor>();

/**
 * Register a panel descriptor for a specific panel type.
 *
 * @param panelType - Unique identifier for the panel type (e.g., 'editor', 'terminal', 'preview')
 * @param descriptor - Serialization/deserialization logic for this panel type
 *
 * @example
 * ```typescript
 * registerPanelDescriptor('editor', {
 *   serialize: (el) => ({
 *     scrollTop: el?.querySelector('.editor-content')?.scrollTop ?? 0,
 *     scrollLeft: el?.querySelector('.editor-content')?.scrollLeft ?? 0,
 *     cursorPosition: getCursorPosition(el),
 *   }),
 *   deserialize: (state, el) => {
 *     const content = el.querySelector('.editor-content');
 *     if (content) {
 *       content.scrollTop = state.scrollTop;
 *       content.scrollLeft = state.scrollLeft;
 *       setCursorPosition(el, state.cursorPosition);
 *     }
 *   }
 * });
 * ```
 */
export function registerPanelDescriptor<T = unknown>(
	panelType: string,
	descriptor: PanelDescriptor<T>
): void {
	panelDescriptors.set(panelType, descriptor as PanelDescriptor<unknown>);
}

/**
 * Unregister a panel descriptor.
 * Useful for cleanup or testing.
 */
export function unregisterPanelDescriptor(panelType: string): boolean {
	return panelDescriptors.delete(panelType);
}

/**
 * Get a panel descriptor by panel type.
 * Returns undefined if no descriptor is registered for this type.
 */
export function getPanelDescriptor<T = unknown>(panelType: string): PanelDescriptor<T> | undefined {
	return panelDescriptors.get(panelType) as PanelDescriptor<T> | undefined;
}

/**
 * Check if a panel descriptor is registered for a specific panel type.
 */
export function hasPanelDescriptor(panelType: string): boolean {
	return panelDescriptors.has(panelType);
}

/**
 * Clear all panel descriptors.
 * Useful for testing.
 */
export function clearPanelDescriptors(): void {
	panelDescriptors.clear();
}

/**
 * Get all registered panel types.
 */
export function getRegisteredPanelTypes(): string[] {
	return Array.from(panelDescriptors.keys());
}

/**
 * Default panel descriptor that preserves no state.
 * Used as fallback when no descriptor is registered for a panel type.
 */
export const defaultPanelDescriptor: PanelDescriptor<Record<string, never>> = {
	serialize: () => ({}),
	deserialize: () => {}
};
