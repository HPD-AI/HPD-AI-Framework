/**
 * Storage backend interface - inspired by VSCode's IStorageDatabase
 * but simplified for async-first modern JavaScript
 */
export interface IStorageBackend {
	/**
	 * Load all items from storage.
	 * @returns Promise resolving to Map of key-value pairs
	 */
	getItems(): Promise<Map<string, string>>;

	/**
	 * Update items in storage (insert/delete batch).
	 * @param items - Map of items to save
	 */
	updateItems(items: Map<string, string>): Promise<void>;

	/**
	 * Delete a specific item.
	 * @param key - Key to delete
	 */
	deleteItem(key: string): Promise<void>;

	/**
	 * Clear all items in this storage scope.
	 */
	clear(): Promise<void>;
}

/**
 * Storage scope classification (inspired by VSCode)
 */
export enum StorageScope {
	/** Global application settings (cross-workspace) */
	GLOBAL = 'global',

	/** Workspace-specific settings (per-project) */
	WORKSPACE = 'workspace'
}

/**
 * Type-safe storage schema definition
 * Extend this interface in your application to define your storage structure
 *
 * @example
 * ```typescript
 * // In your app:
 * declare module '@shellos/headless-ui' {
 *   interface StorageSchema {
 *     'sidebar.width': number;
 *     'sidebar.collapsed': boolean;
 *     'theme.mode': 'light' | 'dark';
 *   }
 * }
 * ```
 */
export interface StorageSchema {
	// Layout state
	'sidebar.width': number;
	'sidebar.collapsed': boolean;
	'panel.width': number;
	'panel.position': 'left' | 'right';
	'bottomPanel.height': number;

	// UI preferences
	'theme.mode': 'light' | 'dark';
	'theme.accentColor': string;

	// Complex objects (examples)
	'layout.config': Record<string, any>;
	'workspace.recentFiles': string[];
}
