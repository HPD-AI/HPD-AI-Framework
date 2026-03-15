/**
 * LayoutHistory - Undo/Redo Management for SplitPanel
 *
 * Manages undo/redo stacks with HPD-debounced snapshot capture.
 * Captures full layout snapshots including panel content states.
 *
 * Features:
 * - HPD debounce (300ms) to batch rapid layout changes
 * - Panel state serialization via PanelDescriptor registry
 * - Max history limit (50 snapshots) for memory control
 * - O(1) element registry lookups for panel state collection
 * - Svelte 5 reactive state management
 */

import { debounce } from '../../internal/index.js';
import type { LayoutSnapshot, LayoutNode, PanelDescriptor } from '../types/index.js';
import type { SplitPanelState } from './split-panel-state.svelte.js';
import type { LayoutChangeDetail } from './split-panel-state.svelte.js';

/**
 * Undo/Redo state for reactivity
 */
export interface UndoRedoState {
	canUndo: boolean;
	canRedo: boolean;
	undoCount: number;
	redoCount: number;
}

/**
 * LayoutHistory class with HPD debounce and panel state management.
 */
export class LayoutHistory {
	/** Undo stack (LIFO) */
	#undoStack: LayoutSnapshot[] = $state([]);

	/** Redo stack (LIFO) */
	#redoStack: LayoutSnapshot[] = $state([]);

	/** Max history size to control memory usage */
	#maxHistory = 50;

	/** Debounced snapshot capture (300ms) */
	#captureDebounced: ReturnType<typeof debounce>;

	/** Panel descriptor registry for content state serialization */
	#panelDescriptors = new Map<string, PanelDescriptor>();

	/** Effect cleanup reference */
	#effectCleanup: (() => void) | null = null;

	constructor(private layoutState: SplitPanelState) {
		// Create debounced snapshot function with HPD utility
		this.#captureDebounced = debounce(() => {
			this.captureSnapshot();
		}, 300);
	}

	// ===== Public API =====

	/**
	 * Get current undo/redo state for reactivity.
	 * Reactive: changes trigger Svelte updates via $state.
	 */
	get state(): UndoRedoState {
		return {
			canUndo: this.#undoStack.length > 0,
			canRedo: this.#redoStack.length > 0,
			undoCount: this.#undoStack.length,
			redoCount: this.#redoStack.length
		};
	}

	/**
	 * Register a panel descriptor for content state serialization.
	 * @param panelType - Panel type identifier
	 * @param descriptor - Serialization/deserialization functions
	 */
	registerPanelDescriptor(panelType: string, descriptor: PanelDescriptor): void {
		this.#panelDescriptors.set(panelType, descriptor);
	}

	/**
	 * Attach event listener to layout state for automatic snapshot capture.
	 * Uses $effect.root for non-tracked scope.
	 * Call this once after creating the LayoutHistory instance.
	 */
	attachEffect(): void {
		// Use $effect.root to create non-tracked scope
		this.#effectCleanup = $effect.root(() => {
			// Listen to layout change events
			const listener = (event: CustomEvent<LayoutChangeDetail>) => {
				// Debounced call batches rapid changes (300ms)
				this.#captureDebounced();
			};

			// Attach to window (CustomEventDispatcher target)
			window.addEventListener('layoutchange' as any, listener);

			// Return cleanup function
			return () => {
				// Cleanup debounced snapshot
				if (typeof this.#captureDebounced.destroy === 'function') {
					this.#captureDebounced.destroy();
				}
				window.removeEventListener('layoutchange' as any, listener);
			};
		});
	}

	/**
	 * Detach event listener and cleanup.
	 */
	detachEffect(): void {
		if (this.#effectCleanup) {
			this.#effectCleanup();
			this.#effectCleanup = null;
		}
	}

	/**
	 * Destroy the history instance and cleanup resources.
	 * Call this when the layout is unmounted.
	 */
	destroy(): void {
		this.detachEffect();
		// Cleanup debounced snapshot
		if (typeof this.#captureDebounced.destroy === 'function') {
			this.#captureDebounced.destroy();
		}
	}

	/**
	 * Clear all undo/redo history.
	 */
	clear(): void {
		this.#undoStack = [];
		this.#redoStack = [];
	}

	/**
	 * Check if undo is available.
	 */
	get canUndo(): boolean {
		return this.#undoStack.length > 0;
	}

	/**
	 * Check if redo is available.
	 */
	get canRedo(): boolean {
		return this.#redoStack.length > 0;
	}

	// ===== Snapshot Capture =====

	/**
	 * Capture current layout state as a snapshot.
	 * Includes both tree structure and panel content states.
	 *
	 * Called by debounced handler on layout changes.
	 * Pushes snapshot to undo stack and clears redo stack.
	 */
	captureSnapshot(): void {
		// Collect panel content states using PanelDescriptor registry
		const panelStates: Record<string, unknown> = {};

		const collectPanelStates = (node: LayoutNode): void => {
			if (node.type === 'leaf') {
				// O(1) registry lookup instead of O(n) querySelector
				const panelEl = this.layoutState.getPanelElement(node.id);
				const descriptor = this.#panelDescriptors.get(node.panelType ?? 'default');
				if (descriptor && panelEl) {
					panelStates[node.id] = descriptor.serialize(panelEl);
				}
			} else {
				node.children.forEach(collectPanelStates);
			}
		};

		collectPanelStates(this.layoutState.root);

		// Serialize layout tree structure
		const serialized = this.layoutState.serialize(
			this.layoutState.containerSize.width,
			this.layoutState.containerSize.height
		);

		// Assemble complete snapshot
		const snapshot: LayoutSnapshot = {
			version: serialized.version,
			root: serialized.root,
			timestamp: Date.now(),
			panelStates,
			containerWidth: this.layoutState.containerSize.width,
			containerHeight: this.layoutState.containerSize.height
		};

		// Push to undo stack
		this.#undoStack = [...this.#undoStack, snapshot];

		// Limit history size (remove oldest if exceeds max)
		if (this.#undoStack.length > this.#maxHistory) {
			this.#undoStack = this.#undoStack.slice(1);
		}

		// Clear redo stack on new action (standard undo/redo behavior)
		this.#redoStack = [];
	}

	// ===== Undo/Redo Operations =====

	/**
	 * Undo the last layout change.
	 * @returns true if undo was performed, false if undo stack is empty
	 */
	undo(): boolean {
		if (this.#undoStack.length === 0) {
			return false;
		}

		// Capture current state before undo (for redo)
		const currentPanelStates: Record<string, unknown> = {};

		const collectPanelStates = (node: LayoutNode): void => {
			if (node.type === 'leaf') {
				const panelEl = this.layoutState.getPanelElement(node.id);
				const descriptor = this.#panelDescriptors.get(node.panelType ?? 'default');
				if (descriptor && panelEl) {
					currentPanelStates[node.id] = descriptor.serialize(panelEl);
				}
			} else {
				node.children.forEach(collectPanelStates);
			}
		};

		collectPanelStates(this.layoutState.root);

		// Serialize current state for redo stack
		const currentSerialized = this.layoutState.serialize(
			this.layoutState.containerSize.width,
			this.layoutState.containerSize.height
		);

		const currentSnapshot: LayoutSnapshot = {
			version: currentSerialized.version,
			root: currentSerialized.root,
			timestamp: Date.now(),
			panelStates: currentPanelStates,
			containerWidth: this.layoutState.containerSize.width,
			containerHeight: this.layoutState.containerSize.height
		};

		// Push current state to redo stack
		this.#redoStack = [...this.#redoStack, currentSnapshot];

		// Pop from undo stack
		const previous = this.#undoStack[this.#undoStack.length - 1];
		this.#undoStack = this.#undoStack.slice(0, -1);

		// Restore previous state
		this.restoreSnapshot(previous);

		return true;
	}

	/**
	 * Redo the last undone layout change.
	 * @returns true if redo was performed, false if redo stack is empty
	 */
	redo(): boolean {
		if (this.#redoStack.length === 0) {
			return false;
		}

		// Capture current state before redo (for undo)
		const currentPanelStates: Record<string, unknown> = {};

		const collectPanelStates = (node: LayoutNode): void => {
			if (node.type === 'leaf') {
				const panelEl = this.layoutState.getPanelElement(node.id);
				const descriptor = this.#panelDescriptors.get(node.panelType ?? 'default');
				if (descriptor && panelEl) {
					currentPanelStates[node.id] = descriptor.serialize(panelEl);
				}
			} else {
				node.children.forEach(collectPanelStates);
			}
		};

		collectPanelStates(this.layoutState.root);

		// Serialize current state for undo stack
		const currentSerialized = this.layoutState.serialize(
			this.layoutState.containerSize.width,
			this.layoutState.containerSize.height
		);

		const currentSnapshot: LayoutSnapshot = {
			version: currentSerialized.version,
			root: currentSerialized.root,
			timestamp: Date.now(),
			panelStates: currentPanelStates,
			containerWidth: this.layoutState.containerSize.width,
			containerHeight: this.layoutState.containerSize.height
		};

		// Push current state to undo stack
		this.#undoStack = [...this.#undoStack, currentSnapshot];

		// Pop from redo stack
		const next = this.#redoStack[this.#redoStack.length - 1];
		this.#redoStack = this.#redoStack.slice(0, -1);

		// Restore next state
		this.restoreSnapshot(next);

		return true;
	}

	// ===== Private Helpers =====

	/**
	 * Restore a snapshot to the layout state.
	 * Used by both undo() and redo().
	 */
	private restoreSnapshot(snapshot: LayoutSnapshot): void {
		// Restore layout tree structure
		this.layoutState.root = this.layoutState.deserializeNode(snapshot.root);

		// Restore panel content states
		this.layoutState.setPanelStatesToRestore(new Map(Object.entries(snapshot.panelStates ?? {})));

		// Update container size if changed
		this.layoutState.updateContainerSize(snapshot.containerWidth, snapshot.containerHeight);

		// Invalidate path and dimension caches after restoring the tree
		this.layoutState.invalidatePathCache();
	}
}
