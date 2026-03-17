import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { IndexedDBBackend } from '../backends/indexeddb-backend.ts';

/**
 * Note: These tests may not run in all environments (requires IndexedDB support)
 * Run in a real browser environment for accurate results
 *
 * ⚠️ SKIPPED IN NODE.JS: IndexedDB is not available in Node.js test environment
 */
describe.skip('IndexedDBBackend', () => {
	let dbName: string;

	beforeEach(() => {
		dbName = `test-db-${Date.now()}-${Math.random()}`;
	});

	afterEach(async () => {
		// Clean up database
		try {
			await IndexedDBBackend.deleteDatabase(dbName);
		} catch (e) {
			// Ignore cleanup errors
		}
	});

	it('should create database and save items', async () => {
		if (!IndexedDBBackend.isAvailable()) {
			console.log('IndexedDB not available, skipping test');
			return;
		}

		const backend = new IndexedDBBackend(dbName);

		const items = new Map([
			['key1', 'value1'],
			['key2', 'value2']
		]);

		await backend.updateItems(items);

		const loaded = await backend.getItems();
		expect(loaded.get('key1')).toBe('value1');
		expect(loaded.get('key2')).toBe('value2');

		await backend.close();
	});

	it('should handle large datasets (10000 items)', async () => {
		if (!IndexedDBBackend.isAvailable()) {
			console.log('IndexedDB not available, skipping test');
			return;
		}

		const backend = new IndexedDBBackend(dbName);

		const largeData = new Map();
		for (let i = 0; i < 10000; i++) {
			largeData.set(`key${i}`, `value${i}`);
		}

		await backend.updateItems(largeData);

		const loaded = await backend.getItems();
		expect(loaded.size).toBe(10000);
		expect(loaded.get('key5000')).toBe('value5000');

		await backend.close();
	});

	it('should support concurrent operations', async () => {
		if (!IndexedDBBackend.isAvailable()) {
			console.log('IndexedDB not available, skipping test');
			return;
		}

		const backend = new IndexedDBBackend(dbName);

		const promises = [
			backend.updateItems(new Map([['key1', 'value1']])),
			backend.updateItems(new Map([['key2', 'value2']])),
			backend.updateItems(new Map([['key3', 'value3']]))
		];

		await Promise.all(promises);

		const loaded = await backend.getItems();
		expect(loaded.size).toBe(3);

		await backend.close();
	});

	it('should delete items', async () => {
		if (!IndexedDBBackend.isAvailable()) {
			console.log('IndexedDB not available, skipping test');
			return;
		}

		const backend = new IndexedDBBackend(dbName);

		await backend.updateItems(
			new Map([
				['key1', 'value1'],
				['key2', 'value2']
			])
		);

		await backend.deleteItem('key1');

		const loaded = await backend.getItems();
		expect(loaded.has('key1')).toBe(false);
		expect(loaded.has('key2')).toBe(true);

		await backend.close();
	});

	it('should clear all items', async () => {
		if (!IndexedDBBackend.isAvailable()) {
			console.log('IndexedDB not available, skipping test');
			return;
		}

		const backend = new IndexedDBBackend(dbName);

		await backend.updateItems(
			new Map([
				['key1', 'value1'],
				['key2', 'value2']
			])
		);

		await backend.clear();

		const loaded = await backend.getItems();
		expect(loaded.size).toBe(0);

		await backend.close();
	});

	it('should persist data across instances', async () => {
		if (!IndexedDBBackend.isAvailable()) {
			console.log('IndexedDB not available, skipping test');
			return;
		}

		const backend1 = new IndexedDBBackend(dbName);
		await backend1.updateItems(new Map([['persisted', 'value']]));
		await backend1.close();

		const backend2 = new IndexedDBBackend(dbName);
		const loaded = await backend2.getItems();
		expect(loaded.get('persisted')).toBe('value');
		await backend2.close();
	});

	it('should handle large values (1MB+)', async () => {
		if (!IndexedDBBackend.isAvailable()) {
			console.log('IndexedDB not available, skipping test');
			return;
		}

		const backend = new IndexedDBBackend(dbName);

		const largeValue = 'x'.repeat(1024 * 1024); // 1MB string
		await backend.updateItems(new Map([['large', largeValue]]));

		const loaded = await backend.getItems();
		expect(loaded.get('large')).toBe(largeValue);

		await backend.close();
	});
});
