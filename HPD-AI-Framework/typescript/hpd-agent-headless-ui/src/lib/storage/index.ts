/**
 * ShellOS Storage System
 *
 * Reactive storage and persistence architecture for ShellLayout and other components.
 * Inspired by VSCode's storage architecture but simplified using Svelte 5 runes.
 *
 * @module storage
 *
 * @example
 * ```typescript
 * import { StorageState, LocalStorageBackend, StorageScope } from '@shellos/headless-ui/storage';
 *
 * // Create storage asynchronously
 * const backend = new LocalStorageBackend('my-app');
 * const storage = await StorageState.create(backend, StorageScope.WORKSPACE);
 *
 * // Type-safe get/set
 * storage.set('sidebar.width', 250);
 * const width = storage.get('sidebar.width', 200);
 * ```
 */

// Core types and interfaces
export type { IStorageBackend, StorageSchema } from './types.ts';
export { StorageScope } from './types.ts';

// Storage state class
export { StorageState } from './storage-state.svelte.ts';

// Backend implementations
export { MemoryStorageBackend, LocalStorageBackend, IndexedDBBackend } from './backends/index.ts';
