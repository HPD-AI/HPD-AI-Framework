import { describe, it, expect, beforeEach } from 'vitest';
import { MemoryStorageBackend } from '../backends/memory-storage-backend.ts';

describe('MemoryStorageBackend', () => {
	let backend: MemoryStorageBackend;

	beforeEach(() => {
		backend = new MemoryStorageBackend();
	});

	describe('getItems', () => {
		it('should return empty map initially', async () => {
			const items = await backend.getItems();
			expect(items.size).toBe(0);
		});

		it('should return a copy of the store', async () => {
			await backend.updateItems(
				new Map([
					['key1', 'value1'],
					['key2', 'value2']
				])
			);

			const items1 = await backend.getItems();
			const items2 = await backend.getItems();

			// Should be different instances (copies)
			expect(items1).not.toBe(items2);
			expect(items1.size).toBe(2);
			expect(items2.size).toBe(2);
		});

		it('should not allow external mutations to affect internal store', async () => {
			await backend.updateItems(new Map([['key1', 'value1']]));

			const items = await backend.getItems();
			items.set('key2', 'value2'); // Mutate the copy

			const itemsAgain = await backend.getItems();
			expect(itemsAgain.size).toBe(1); // Should still be 1
			expect(itemsAgain.has('key2')).toBe(false);
		});
	});

	describe('updateItems', () => {
		it('should add new items', async () => {
			await backend.updateItems(
				new Map([
					['key1', 'value1'],
					['key2', 'value2']
				])
			);

			const items = await backend.getItems();
			expect(items.get('key1')).toBe('value1');
			expect(items.get('key2')).toBe('value2');
		});

		it('should update existing items', async () => {
			await backend.updateItems(new Map([['key1', 'value1']]));
			await backend.updateItems(new Map([['key1', 'value2']]));

			const items = await backend.getItems();
			expect(items.get('key1')).toBe('value2');
		});

		it('should handle empty map', async () => {
			await backend.updateItems(new Map());
			const items = await backend.getItems();
			expect(items.size).toBe(0);
		});

		it('should handle batch updates', async () => {
			const batch = new Map([
				['key1', 'value1'],
				['key2', 'value2'],
				['key3', 'value3']
			]);

			await backend.updateItems(batch);
			const items = await backend.getItems();

			expect(items.size).toBe(3);
			expect(items.get('key1')).toBe('value1');
			expect(items.get('key2')).toBe('value2');
			expect(items.get('key3')).toBe('value3');
		});
	});

	describe('deleteItem', () => {
		it('should delete existing item', async () => {
			await backend.updateItems(
				new Map([
					['key1', 'value1'],
					['key2', 'value2']
				])
			);

			await backend.deleteItem('key1');

			const items = await backend.getItems();
			expect(items.has('key1')).toBe(false);
			expect(items.has('key2')).toBe(true);
			expect(items.size).toBe(1);
		});

		it('should handle deleting non-existent item', async () => {
			await backend.deleteItem('nonexistent');
			const items = await backend.getItems();
			expect(items.size).toBe(0);
		});

		it('should handle deleting from empty store', async () => {
			await backend.deleteItem('key1');
			const items = await backend.getItems();
			expect(items.size).toBe(0);
		});
	});

	describe('clear', () => {
		it('should clear all items', async () => {
			await backend.updateItems(
				new Map([
					['key1', 'value1'],
					['key2', 'value2'],
					['key3', 'value3']
				])
			);

			await backend.clear();

			const items = await backend.getItems();
			expect(items.size).toBe(0);
		});

		it('should handle clearing empty store', async () => {
			await backend.clear();
			const items = await backend.getItems();
			expect(items.size).toBe(0);
		});

		it('should allow re-adding items after clear', async () => {
			await backend.updateItems(new Map([['key1', 'value1']]));
			await backend.clear();
			await backend.updateItems(new Map([['key2', 'value2']]));

			const items = await backend.getItems();
			expect(items.size).toBe(1);
			expect(items.get('key2')).toBe('value2');
		});
	});

	describe('size', () => {
		it('should return 0 for empty store', () => {
			expect(backend.size).toBe(0);
		});

		it('should return correct size after updates', async () => {
			await backend.updateItems(
				new Map([
					['key1', 'value1'],
					['key2', 'value2']
				])
			);

			expect(backend.size).toBe(2);
		});

		it('should update after deletions', async () => {
			await backend.updateItems(new Map([['key1', 'value1']]));
			expect(backend.size).toBe(1);

			await backend.deleteItem('key1');
			expect(backend.size).toBe(0);
		});
	});

	describe('getRawStore', () => {
		it('should return internal store reference', async () => {
			await backend.updateItems(new Map([['key1', 'value1']]));

			const rawStore = backend.getRawStore();
			expect(rawStore.get('key1')).toBe('value1');
		});

		it('should allow direct mutations (for testing)', async () => {
			const rawStore = backend.getRawStore();
			rawStore.set('key1', 'value1');

			const items = await backend.getItems();
			expect(items.get('key1')).toBe('value1');
		});
	});

	describe('edge cases', () => {
		it('should handle special characters in keys', async () => {
			const specialKeys = new Map([
				['key:with:colons', 'value1'],
				['key.with.dots', 'value2'],
				['key-with-dashes', 'value3'],
				['key_with_underscores', 'value4']
			]);

			await backend.updateItems(specialKeys);
			const items = await backend.getItems();

			expect(items.get('key:with:colons')).toBe('value1');
			expect(items.get('key.with.dots')).toBe('value2');
			expect(items.get('key-with-dashes')).toBe('value3');
			expect(items.get('key_with_underscores')).toBe('value4');
		});

		it('should handle empty string values', async () => {
			await backend.updateItems(new Map([['key1', '']]));
			const items = await backend.getItems();
			expect(items.get('key1')).toBe('');
		});

		it('should handle large strings', async () => {
			const largeValue = 'x'.repeat(10000);
			await backend.updateItems(new Map([['key1', largeValue]]));

			const items = await backend.getItems();
			expect(items.get('key1')).toBe(largeValue);
		});

		it('should handle many items', async () => {
			const manyItems = new Map<string, string>();
			for (let i = 0; i < 1000; i++) {
				manyItems.set(`key${i}`, `value${i}`);
			}

			await backend.updateItems(manyItems);
			const items = await backend.getItems();

			expect(items.size).toBe(1000);
			expect(items.get('key500')).toBe('value500');
		});
	});
});
