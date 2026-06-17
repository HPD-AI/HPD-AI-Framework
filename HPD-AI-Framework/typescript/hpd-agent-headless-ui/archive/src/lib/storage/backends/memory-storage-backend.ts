import type { IStorageBackend } from '../types.ts';

/**
 * Memory storage backend - for testing and development
 *
 * This backend stores data in memory and does not persist across page reloads.
 * Useful for:
 * - Unit testing (no side effects)
 * - Development (fast, no quota limits)
 * - Temporary state (session-only data)
 *
 * @example
 * ```typescript
 * const backend = new MemoryStorageBackend();
 * const storage = await StorageState.create(backend, StorageScope.WORKSPACE);
 * ```
 */
export class MemoryStorageBackend implements IStorageBackend {
	private store = new Map<string, string>();

	async getItems(): Promise<Map<string, string>> {
		// Return a copy to prevent external mutations
		return new Map(this.store);
	}

	async updateItems(items: Map<string, string>): Promise<void> {
		for (const [key, value] of items) {
			this.store.set(key, value);
		}
	}

	async deleteItem(key: string): Promise<void> {
		this.store.delete(key);
	}

	async clear(): Promise<void> {
		this.store.clear();
	}

	/**
	 * Get the raw internal store (for testing/debugging only)
	 * @internal
	 */
	getRawStore(): Map<string, string> {
		return this.store;
	}

	/**
	 * Get current size of storage
	 */
	get size(): number {
		return this.store.size;
	}
}
