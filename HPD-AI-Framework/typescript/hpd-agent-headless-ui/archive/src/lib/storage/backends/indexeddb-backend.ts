import type { IStorageBackend } from '../types.ts';

/**
 * IndexedDB backend - for large web applications
 *
 * Persists data to browser's IndexedDB (survives page reloads).
 * Better for large datasets compared to localStorage.
 *
 * Advantages over localStorage:
 * - Much larger quota (~50MB+, varies by browser)
 * - Async API (non-blocking)
 * - Better performance for large datasets
 * - Can store structured data (though we use strings for interface consistency)
 *
 * @example
 * ```typescript
 * const backend = new IndexedDBBackend('my-app-db');
 * const storage = await StorageState.create(backend, StorageScope.WORKSPACE);
 * ```
 */
export class IndexedDBBackend implements IStorageBackend {
	private dbPromise: Promise<IDBDatabase> | null = null;

	constructor(
		private dbName: string,
		private storeName = 'storage',
		private version = 1
	) {
		if (!dbName) {
			throw new Error('[IndexedDBBackend] Database name is required');
		}
	}

	async getItems(): Promise<Map<string, string>> {
		const db = await this.#openDB();
		const items = new Map<string, string>();

		return new Promise((resolve, reject) => {
			try {
				const tx = db.transaction(this.storeName, 'readonly');
				const store = tx.objectStore(this.storeName);
				const request = store.openCursor();

				request.onsuccess = (e) => {
					const cursor = (e.target as IDBRequest).result;
					if (cursor) {
						items.set(cursor.key as string, cursor.value);
						cursor.continue();
					} else {
						resolve(items);
					}
				};

				request.onerror = () => reject(request.error);
			} catch (error) {
				reject(error);
			}
		});
	}

	async updateItems(items: Map<string, string>): Promise<void> {
		const db = await this.#openDB();

		return new Promise((resolve, reject) => {
			try {
				const tx = db.transaction(this.storeName, 'readwrite');
				const store = tx.objectStore(this.storeName);

				for (const [key, value] of items) {
					store.put(value, key);
				}

				tx.oncomplete = () => resolve();
				tx.onerror = () => reject(tx.error);
			} catch (error) {
				reject(error);
			}
		});
	}

	async deleteItem(key: string): Promise<void> {
		const db = await this.#openDB();

		return new Promise((resolve, reject) => {
			try {
				const tx = db.transaction(this.storeName, 'readwrite');
				const request = tx.objectStore(this.storeName).delete(key);

				request.onsuccess = () => resolve();
				request.onerror = () => reject(request.error);
			} catch (error) {
				reject(error);
			}
		});
	}

	async clear(): Promise<void> {
		const db = await this.#openDB();

		return new Promise((resolve, reject) => {
			try {
				const tx = db.transaction(this.storeName, 'readwrite');
				const request = tx.objectStore(this.storeName).clear();

				request.onsuccess = () => resolve();
				request.onerror = () => reject(request.error);
			} catch (error) {
				reject(error);
			}
		});
	}

	/**
	 * Open database connection (cached)
	 */
	async #openDB(): Promise<IDBDatabase> {
		if (!this.dbPromise) {
			this.dbPromise = new Promise((resolve, reject) => {
				const request = indexedDB.open(this.dbName, this.version);

				request.onerror = () => reject(request.error);
				request.onsuccess = () => resolve(request.result);

				request.onupgradeneeded = (e) => {
					const db = (e.target as IDBOpenDBRequest).result;
					if (!db.objectStoreNames.contains(this.storeName)) {
						db.createObjectStore(this.storeName);
					}
				};
			});
		}

		return this.dbPromise;
	}

	/**
	 * Close database connection
	 * Call this when storage is no longer needed
	 */
	async close(): Promise<void> {
		if (this.dbPromise) {
			const db = await this.dbPromise;
			db.close();
			this.dbPromise = null;
		}
	}

	/**
	 * Delete the entire database
	 * WARNING: This removes all data permanently
	 */
	static async deleteDatabase(dbName: string): Promise<void> {
		return new Promise((resolve, reject) => {
			const request = indexedDB.deleteDatabase(dbName);
			request.onsuccess = () => resolve();
			request.onerror = () => reject(request.error);
		});
	}

	/**
	 * Check if IndexedDB is available
	 * (may not be in SSR, old browsers, etc.)
	 */
	static isAvailable(): boolean {
		try {
			return typeof indexedDB !== 'undefined';
		} catch {
			return false;
		}
	}
}
