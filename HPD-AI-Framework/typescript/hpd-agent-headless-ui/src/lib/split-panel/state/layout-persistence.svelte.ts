/**
 * LayoutPersistence - Storage Integration for SplitPanel
 *
 * Manages automatic persistence of layout state to ShellOS Storage.
 * Provides load/save operations with debounced auto-save on layout changes.
 *
 * Features:
 * - HPD debounce (1000ms) for layout change batching
 * - Event-based auto-save (not $effect) to capture deep mutations
 * - Version validation (v3 only)
 * - Type-safe storage integration via StorageSchema extension
 * - Error handling with logging (no exceptions thrown)
 * - Cleanup lifecycle for proper resource disposal
 */

import { debounce } from '../../internal/index.js';
import { SplitPanelState } from './split-panel-state.svelte.js';
import type { LayoutChangeDetail } from './split-panel-state.svelte.js';
import type { SerializedLayout } from '../types/index.js';

/**
 * StorageState interface for ShellOS Storage integration.
 * This matches the ShellOS storage API pattern.
 */
export interface StorageState {
	get<T>(key: string, fallback?: T): T | undefined;
	set<T>(key: string, value: T): void;
}

/**
 * Storage schema extension for type-safe layout persistence.
 * Applications should declare this module augmentation in their storage setup:
 *
 * @example
 * ```typescript
 * // In your app's types file:
 * import type { SerializedLayout } from 'shellos-headless-ui/split-panel';
 *
 * declare module '$lib/storage/types' {
 *   interface StorageSchema {
 *     this.storageKey: SerializedLayout;
 *   }
 * }
 * ```
 */
// Note: Module augmentation is application-specific, not library-specific
// Uncomment and adjust module path in your application as needed

/**
 * LayoutPersistence class with event-based auto-save and ShellOS Storage.
 */
export class LayoutPersistence {
	/** Debounced save function (1000ms) */
	#saveDebounced: ReturnType<typeof debounce>;

	/** Auto-save cleanup function */
	#autoSaveCleanup: (() => void) | null = null;

	/**
	 * Create a new LayoutPersistence instance.
	 *
	 * @param layoutState - SplitPanelState instance to persist
	 * @param storage - ShellOS StorageState instance
	 * @param containerWidth - Function returning current container width in pixels
	 * @param containerHeight - Function returning current container height in pixels
	 */
	constructor(
		private layoutState: SplitPanelState,
		private storage: StorageState,
		private containerWidth: () => number,
		private containerHeight: () => number,
		private storageKey: string = this.storageKey
	) {
		// Create debounced save function with HPD utility (1000ms)
		// Uses 1000ms (not 300ms like LayoutHistory) because persistence is I/O-heavy
		this.#saveDebounced = debounce(() => {
			this.save();
		}, 1000);
	}

	// ===== Public API =====

	/**
	 * Load layout from storage.
	 * Validates version and deserializes layout state.
	 *
	 * @returns true if loaded successfully, false if not found or incompatible
	 */
	load(): boolean {
		try {
			const stored = this.storage.get<SerializedLayout>(this.storageKey);
			if (!stored) {
				return false;
			}

			// Validate version (reject incompatible layouts)
			if (stored.version !== 3) {
				console.warn(
					`Incompatible layout version: ${stored.version}. Expected version 3. Skipping restore.`
				);
				return false;
			}

			// Deserialize and restore layout state
			const deserialized = SplitPanelState.deserialize(stored);
			this.layoutState.root = deserialized.root;

			// Update container size if different from saved
			this.layoutState.updateContainerSize(this.containerWidth(), this.containerHeight());

			return true;
		} catch (error) {
			console.error('Failed to load layout from storage:', error);
			return false;
		}
	}

	/**
	 * Save current layout to storage.
	 * Serializes layout with current container dimensions.
	 */
	save(): void {
		try {
			const serialized = this.layoutState.serialize(this.containerWidth(), this.containerHeight());
			this.storage.set(this.storageKey, serialized);
		} catch (error) {
			console.error('Failed to save layout to storage:', error);
		}
	}

	/**
	 * Enable automatic saving on layout changes.
	 * Listens to layoutchange events and debounces save calls (1000ms).
	 *
	 * Uses event-based listening instead of $effect to properly capture deep mutations
	 * during resize operations. Svelte 5 effects only re-run on object reference changes,
	 * not on leaf.size/flex mutations.
	 *
	 * @returns Cleanup function to disable auto-save
	 */
	enableAutoSave(): () => void {
		// Prevent double-enabling
		if (this.#autoSaveCleanup) {
			console.warn('Auto-save already enabled. Call returned cleanup function before re-enabling.');
			return this.#autoSaveCleanup;
		}

		// Create event listener
		const listener = (event: CustomEvent<LayoutChangeDetail>) => {
			// Debounced save on any layout change event (1000ms)
			this.#saveDebounced();
		};

		// Attach to window (CustomEventDispatcher target)
		window.addEventListener('layoutchange' as any, listener);

		// Create cleanup function
		this.#autoSaveCleanup = () => {
			// Cleanup debounced save
			if (typeof this.#saveDebounced.destroy === 'function') {
				this.#saveDebounced.destroy();
			}
			window.removeEventListener('layoutchange' as any, listener);
			this.#autoSaveCleanup = null;
		};

		return this.#autoSaveCleanup;
	}

	/**
	 * Disable auto-save if enabled.
	 * Safe to call multiple times.
	 */
	disableAutoSave(): void {
		if (this.#autoSaveCleanup) {
			this.#autoSaveCleanup();
		}
	}

	/**
	 * Destroy the persistence instance and cleanup resources.
	 * Call this when the layout is unmounted or persistence is no longer needed.
	 */
	destroy(): void {
		// Disable auto-save and cleanup event listeners
		this.disableAutoSave();

		// Cleanup debounced save
		if (typeof this.#saveDebounced.destroy === 'function') {
			this.#saveDebounced.destroy();
		}
	}
}
