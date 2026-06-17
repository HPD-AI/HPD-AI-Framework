import type { IStorageBackend } from '../types.ts';

/**
 * LocalStorage backend - for web applications
 *
 * Persists data to browser's localStorage (survives page reloads).
 * Scopes all keys with a prefix to avoid collisions.
 *
 * Limitations:
 * - ~5-10MB quota (varies by browser)
 * - Synchronous API (but wrapped in async for interface compatibility)
 * - String values only
 * - Same-origin only
 *
 * @example
 * ```typescript
 * const backend = new LocalStorageBackend('my-app');
 * const storage = await StorageState.create(backend, StorageScope.WORKSPACE);
 * ```
 */
export class LocalStorageBackend implements IStorageBackend {
	constructor(private prefix: string) {
		if (!prefix) {
			throw new Error('[LocalStorageBackend] Prefix is required to avoid key collisions');
		}
	}

	async getItems(): Promise<Map<string, string>> {
		const items = new Map<string, string>();

		try {
			const prefixWithColon = this.prefix + ':';
			for (let i = 0; i < localStorage.length; i++) {
				const storageKey = localStorage.key(i);
				if (storageKey && storageKey.startsWith(prefixWithColon)) {
					const value = localStorage.getItem(storageKey);
					if (value !== null) {
						// Strip the prefix when returning keys to StorageState
						const key = storageKey.slice(prefixWithColon.length);
						items.set(key, value);
					}
				}
			}
		} catch (error) {
			console.error('[LocalStorageBackend] getItems failed:', error);
			// Return empty map on error (e.g., localStorage not available)
		}

		return items;
	}

	async updateItems(items: Map<string, string>): Promise<void> {
		try {
			for (const [key, value] of items) {
				// Prepend prefix when storing to localStorage
				const storageKey = `${this.prefix}:${key}`;
				localStorage.setItem(storageKey, value);
			}
		} catch (error) {
			if (error instanceof DOMException && error.name === 'QuotaExceededError') {
				console.error('[LocalStorageBackend] Storage quota exceeded');
				throw new Error('Storage quota exceeded. Consider clearing old data.');
			}
			throw error;
		}
	}

	async deleteItem(key: string): Promise<void> {
		try {
			// Prepend prefix when deleting from localStorage
			const storageKey = `${this.prefix}:${key}`;
			localStorage.removeItem(storageKey);
		} catch (error) {
			console.error('[LocalStorageBackend] deleteItem failed:', error);
		}
	}

	async clear(): Promise<void> {
		try {
			const keysToRemove: string[] = [];

			for (let i = 0; i < localStorage.length; i++) {
				const key = localStorage.key(i);
				if (key?.startsWith(this.prefix + ':')) {
					keysToRemove.push(key);
				}
			}

			for (const key of keysToRemove) {
				localStorage.removeItem(key);
			}
		} catch (error) {
			console.error('[LocalStorageBackend] clear failed:', error);
		}
	}

	/**
	 * Get the prefix used for scoping keys
	 */
	getPrefix(): string {
		return this.prefix;
	}

	/**
	 * Check if localStorage is available
	 * (may not be in SSR, private browsing, etc.)
	 */
	static isAvailable(): boolean {
		try {
			const test = '__storage_test__';
			localStorage.setItem(test, test);
			localStorage.removeItem(test);
			return true;
		} catch {
			return false;
		}
	}
}
