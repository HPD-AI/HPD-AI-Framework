import type { IStorageBackend, StorageScope, StorageSchema } from './types.ts';

/**
 * Reactive storage state using plain JavaScript reactivity
 * Replaces VSCode's 4-layer architecture with 1 reactive class
 *
 *  DESIGN:
 * 1. Uses Map/Set for storage (compatible with components and tests)
 * 2. Factory pattern for safe async initialization
 * 3. Callbacks for reactivity integration (to be used with Svelte $effect in components)
 * 4. Added scope ID support for multi-project isolation
 *
 * @example
 * ```typescript
 * // Create storage asynchronously
 * const backend = new LocalStorageBackend('my-app');
 * const storage = await StorageState.create(backend, StorageScope.WORKSPACE);
 *
 * // Type-safe get/set
 * storage.set('sidebar.width', 250);
 * const width = storage.get('sidebar.width', 200); // number
 *
 * // Use in Svelte components:
 * let width = $derived(storage.get('sidebar.width', 200));
 * ```
 */
export class StorageState<TSchema extends Record<string, any> = StorageSchema> {
	//  Private constructor - synchronous only
	private constructor(
		private backend: IStorageBackend,
		private scope: StorageScope,
		private scopeId: string = '',
		private debounce = 100
	) {}

	//  Static async factory (handles async initialization safely)
	static async create<T extends Record<string, any> = StorageSchema>(
		backend: IStorageBackend,
		scope: StorageScope,
		scopeId?: string,
		debounce?: number
	): Promise<StorageState<T>> {
		const state = new StorageState<T>(backend, scope, scopeId ?? '', debounce);
		await state.#init();
		return state;
	}

	// ========================================
	// Reactive State (replaces VSCode Layer 2 cache)
	// ========================================

	/** Cache - uses Map for storage */
	cache = new Map<string, string>();

	/** Dirty keys pending flush */
	#dirtyKeys = new Set<string>();

	/** Deleted keys pending flush */
	#deletedKeys = new Set<string>();

	/** Initialization state */
	#initialized = false;

	/** Debounce timer for batched writes */
	#persistTimer: ReturnType<typeof setTimeout> | null = null;

	// ========================================
	// Derived State (replaces manual getters)
	// ========================================

	/** Number of items in cache */
	get size(): number {
		return this.cache.size;
	}

	/** Whether there are pending changes */
	get isDirty(): boolean {
		return this.#dirtyKeys.size > 0 || this.#deletedKeys.size > 0;
	}

	/** Whether storage is ready for use */
	get ready(): boolean {
		return this.#initialized;
	}

	/** Scope-filtered keys (for debugging/inspection) */
	get scopedKeys(): string[] {
		const keys: string[] = [];
		const prefix = this.#getScopePrefix();
		for (const key of this.cache.keys()) {
			if (key.startsWith(prefix)) {
				keys.push(key.slice(prefix.length + 1));
			}
		}
		return keys;
	}

	// ========================================
	// Initialization
	// ========================================

	async #init() {
		try {
			const items = await this.backend.getItems();

			//  Populate cache
			for (const [key, value] of items) {
				this.cache.set(key, value);
			}

			this.#initialized = true;

			//  Setup persistence AFTER data loaded
			this.#setupAutoPersist();
		} catch (error) {
			console.error('[StorageState] Init failed:', error);
			this.#initialized = true; // Mark ready despite error
		}
	}

	/**
	 * Auto-persist on changes
	 * Called on each set/delete to debounce writes
	 */
	#setupAutoPersist() {
		// Initial setup is complete - future modifications trigger debouncing via #scheduleFlush
	}

	/**
	 * Schedule a debounced flush of dirty keys
	 * This is called after each set/delete operation
	 */
	#scheduleFlush() {
		if (this.#persistTimer) clearTimeout(this.#persistTimer);
		this.#persistTimer = setTimeout(() => {
			const dirtyArray = Array.from(this.#dirtyKeys);
			this.#flush(dirtyArray);
			this.#persistTimer = null;
		}, this.debounce);
	}

	async #flush(dirtyKeys: string[]) {
		try {
			const itemsToSave = new Map<string, string>();
			for (const key of dirtyKeys) {
				const value = this.cache.get(key);
				if (value !== undefined) {
					itemsToSave.set(key, value);
				}
			}

			// Handle updates
			if (itemsToSave.size > 0) {
				await this.backend.updateItems(itemsToSave);
			}

			// Handle deletions
			const deletedArray = Array.from(this.#deletedKeys);
			for (const key of deletedArray) {
				await this.backend.deleteItem(key);
			}

			// Clear dirty flags after successful flush
			for (const key of dirtyKeys) {
				this.#dirtyKeys.delete(key);
			}
			this.#deletedKeys.clear();
		} catch (error) {
			console.error('[StorageState] Flush failed:', error);
		}
	}

	// ========================================
	// Type-Safe API (replaces VSCode's 4 getter methods)
	// ========================================

	/**
	 * Get value with automatic type inference and parsing
	 * Replaces: getBoolean, getNumber, getObject, get
	 */
	get<K extends keyof TSchema>(key: K, fallback?: TSchema[K]): TSchema[K] | undefined {
		const scopedKey = this.#scopeKey(key);
		const raw = this.cache.get(scopedKey);

		if (raw === undefined) return fallback;

		// Auto-parse based on fallback type
		if (fallback !== undefined) {
			if (typeof fallback === 'boolean') return (raw === 'true') as TSchema[K];
			if (typeof fallback === 'number') return parseFloat(raw) as TSchema[K];
			if (typeof fallback === 'object') {
				try {
					return JSON.parse(raw) as TSchema[K];
				} catch {
					return fallback;
				}
			}
			// String fallback - return raw value
			return raw as TSchema[K];
		}

		// No fallback - try to parse as JSON for objects/arrays
		// If it looks like JSON (starts with { or [), parse it
		if (raw.startsWith('{') || raw.startsWith('[')) {
			try {
				return JSON.parse(raw) as TSchema[K];
			} catch {
				// If parse fails, return as-is
				return raw as TSchema[K];
			}
		}

		// Return raw value for primitives
		return raw as TSchema[K];
	}

	/**
	 * Set value with automatic stringification
	 */
	set<K extends keyof TSchema>(key: K, value: TSchema[K]): void {
		if (!this.#initialized) {
			console.warn('[StorageState] Not initialized yet');
			return;
		}

		const scopedKey = this.#scopeKey(key);

		// Handle serialization based on type
		let valueStr: string;
		if (value === null) {
			valueStr = 'null';
		} else if (value === undefined) {
			valueStr = 'undefined';
		} else if (typeof value === 'object' && value && 'toISOString' in value) {
			// Handle Date objects and objects with toISOString method
			valueStr = (value as { toISOString(): string }).toISOString();
		} else if (typeof value === 'object') {
			try {
				valueStr = JSON.stringify(value);
			} catch (error) {
				// Handle circular references
				if (error instanceof TypeError && error.message.includes('circular')) {
					throw new Error('[StorageState] Cannot store circular references');
				}
				throw error;
			}
		} else {
			valueStr = String(value);
		}

		//  Map mutation, then schedule flush
		this.cache.set(scopedKey, valueStr);
		this.#dirtyKeys.add(scopedKey);
		this.#scheduleFlush();
	}

	/**
	 * Delete a key
	 */
	delete<K extends keyof TSchema>(key: K): void {
		const scopedKey = this.#scopeKey(key);
		this.cache.delete(scopedKey);
		this.#dirtyKeys.delete(scopedKey); // Remove from dirty if it was pending save
		this.#deletedKeys.add(scopedKey); // Mark for deletion in backend
		this.#scheduleFlush();
	}

	/**
	 * Check if key exists
	 */
	has<K extends keyof TSchema>(key: K): boolean {
		const scopedKey = this.#scopeKey(key);
		return this.cache.has(scopedKey);
	}

	/**
	 * Clear all items in this scope
	 */
	async clear(): Promise<void> {
		const keysToRemove: string[] = [];
		const prefix = this.#getScopePrefix();

		for (const key of this.cache.keys()) {
			if (key.startsWith(prefix)) {
				keysToRemove.push(key);
			}
		}

		for (const key of keysToRemove) {
			this.cache.delete(key);
		}

		await this.backend.clear();
		this.#dirtyKeys.clear();
		this.#deletedKeys.clear();
	}

	/**
	 * Force immediate flush (for shutdown scenarios)
	 */
	async flush(): Promise<void> {
		if (this.#persistTimer) {
			clearTimeout(this.#persistTimer);
			this.#persistTimer = null;
		}
		const dirtyArray = Array.from(this.#dirtyKeys);
		await this.#flush(dirtyArray);
	}

	// ========================================
	// Private Helpers
	// ========================================

	/**
	 *  Scope key with ID support
	 */
	#scopeKey<K extends keyof TSchema>(key: K): string {
		const prefix = this.#getScopePrefix();
		return `${prefix}.${String(key)}`;
	}

	#getScopePrefix(): string {
		return this.scopeId
			? `${this.scope}:${this.scopeId}` // e.g., "workspace:project-123"
			: this.scope; // e.g., "workspace"
	}
}
