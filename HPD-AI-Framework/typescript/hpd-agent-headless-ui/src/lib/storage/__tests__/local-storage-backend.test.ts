import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { LocalStorageBackend } from '../backends/local-storage-backend.ts';

// Mock localStorage for testing
const localStorageMock = (() => {
	let store: Map<string, string> = new Map();

	return {
		getItem: (key: string) => store.get(key) ?? null,
		setItem: (key: string, value: string) => store.set(key, value),
		removeItem: (key: string) => store.delete(key),
		clear: () => store.clear(),
		get length() {
			return store.size;
		},
		key: (index: number) => {
			const keys = Array.from(store.keys());
			return keys[index] ?? null;
		}
	};
})();

// Setup global mock
global.localStorage = localStorageMock as any;

describe('LocalStorageBackend', () => {
	let backend: LocalStorageBackend;
	const prefix = 'test-app';

	beforeEach(() => {
		localStorage.clear();
		backend = new LocalStorageBackend(prefix);
	});

	afterEach(() => {
		localStorage.clear();
	});

	describe('constructor', () => {
		it('should throw if prefix is empty', () => {
			expect(() => new LocalStorageBackend('')).toThrow();
		});

		it('should accept valid prefix', () => {
			expect(() => new LocalStorageBackend('my-app')).not.toThrow();
		});
	});

	describe('getItems', () => {
		it('should return empty map initially', async () => {
			const items = await backend.getItems();
			expect(items.size).toBe(0);
		});

		it('should only return items with correct prefix', async () => {
			// Add items with different prefixes
			localStorage.setItem('test-app:key1', 'value1');
			localStorage.setItem('test-app:key2', 'value2');
			localStorage.setItem('other-app:key3', 'value3');
			localStorage.setItem('no-prefix', 'value4');

			const items = await backend.getItems();
			expect(items.size).toBe(2);
			// Backend strips prefix when returning
			expect(items.get('key1')).toBe('value1');
			expect(items.get('key2')).toBe('value2');
		});
	});

	describe('updateItems', () => {
		it('should add new items', async () => {
			// Pass keys without prefix - backend will add it
			await backend.updateItems(
				new Map([
					['key1', 'value1'],
					['key2', 'value2']
				])
			);

			// Verify backend added prefix to localStorage
			expect(localStorage.getItem('test-app:key1')).toBe('value1');
			expect(localStorage.getItem('test-app:key2')).toBe('value2');
		});

		it('should update existing items', async () => {
			await backend.updateItems(new Map([['key1', 'value1']]));
			await backend.updateItems(new Map([['key1', 'value2']]));

			expect(localStorage.getItem('test-app:key1')).toBe('value2');
		});

		it('should throw on quota exceeded error', async () => {
			const largeData = new Map([['key1', 'x'.repeat(1000000)]]);

			// Create error backend with mocked setItem
			const errorBackend = new LocalStorageBackend('test-app');
			const originalSetItem = localStorage.setItem.bind(localStorage);

			localStorage.setItem = () => {
				const error = new DOMException('QuotaExceededError', 'QuotaExceededError');
				throw error;
			};

			await expect(errorBackend.updateItems(largeData)).rejects.toThrow('Storage quota exceeded');

			localStorage.setItem = originalSetItem;
		});

		it('should handle empty map', async () => {
			await backend.updateItems(new Map());
			const items = await backend.getItems();
			expect(items.size).toBe(0);
		});
	});

	describe('deleteItem', () => {
		it('should delete existing item', async () => {
			localStorage.setItem('test-app:key1', 'value1');
			localStorage.setItem('test-app:key2', 'value2');

			// Pass key without prefix - backend will add it
			await backend.deleteItem('key1');

			expect(localStorage.getItem('test-app:key1')).toBeNull();
			expect(localStorage.getItem('test-app:key2')).toBe('value2');
		});

		it('should handle deleting non-existent item', async () => {
			await backend.deleteItem('nonexistent');
			expect(true).toBe(true); // Should not throw
		});
	});

	describe('clear', () => {
		it('should clear only items with prefix', async () => {
			localStorage.setItem('test-app:key1', 'value1');
			localStorage.setItem('test-app:key2', 'value2');
			localStorage.setItem('other-app:key3', 'value3');

			await backend.clear();

			expect(localStorage.getItem('test-app:key1')).toBeNull();
			expect(localStorage.getItem('test-app:key2')).toBeNull();
			expect(localStorage.getItem('other-app:key3')).toBe('value3');
		});

		it('should handle clearing empty store', async () => {
			await backend.clear();
			expect(true).toBe(true); // Should not throw
		});
	});

	describe('getPrefix', () => {
		it('should return the prefix', () => {
			expect(backend.getPrefix()).toBe(prefix);
		});
	});

	describe('isAvailable', () => {
		it('should return true when localStorage is available', () => {
			expect(LocalStorageBackend.isAvailable()).toBe(true);
		});

		it('should return false when localStorage throws', () => {
			const originalSetItem = localStorage.setItem;
			localStorage.setItem = () => {
				throw new Error('localStorage not available');
			};

			expect(LocalStorageBackend.isAvailable()).toBe(false);

			localStorage.setItem = originalSetItem;
		});
	});

	describe('edge cases', () => {
		it('should handle special characters in keys', async () => {
			const specialKeys = new Map([
				['key:with:colons', 'value1'],
				['key.with.dots', 'value2']
			]);

			await backend.updateItems(specialKeys);
			const items = await backend.getItems();

			expect(items.get('key:with:colons')).toBe('value1');
			expect(items.get('key.with.dots')).toBe('value2');
		});

		it('should handle empty string values', async () => {
			await backend.updateItems(new Map([['key1', '']]));
			const items = await backend.getItems();
			expect(items.get('key1')).toBe('');
		});

		it('should handle unicode values', async () => {
			const unicodeValue = '你好世界 🌍';
			await backend.updateItems(new Map([['key1', unicodeValue]]));

			const items = await backend.getItems();
			expect(items.get('key1')).toBe(unicodeValue);
		});

		it('should handle batch operations', async () => {
			const batch = new Map<string, string>();
			for (let i = 0; i < 100; i++) {
				batch.set(`key${i}`, `value${i}`);
			}

			await backend.updateItems(batch);
			const items = await backend.getItems();

			expect(items.size).toBe(100);
			expect(items.get('key50')).toBe('value50');
		});
	});

	describe('real-world scenarios', () => {
		it('should handle layout persistence scenario', async () => {
			const layoutData = new Map([
				['workspace.sidebar.width', '250'],
				['workspace.sidebar.collapsed', 'false'],
				['workspace.panel.width', '300'],
				['workspace.panel.position', 'right']
			]);

			await backend.updateItems(layoutData);

			const items = await backend.getItems();
			expect(items.get('workspace.sidebar.width')).toBe('250');
			expect(items.get('workspace.sidebar.collapsed')).toBe('false');
		});

		it('should handle JSON stringified objects', async () => {
			const config = { theme: 'dark', fontSize: 14 };
			const jsonString = JSON.stringify(config);

			await backend.updateItems(new Map([['config', jsonString]]));

			const items = await backend.getItems();
			const retrieved = JSON.parse(items.get('config')!);

			expect(retrieved).toEqual(config);
		});
	});
});
