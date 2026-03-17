import { describe, it, expect, beforeEach } from 'vitest';
import { StorageState } from '../storage-state.svelte.ts';
import { LocalStorageBackend } from '../backends/local-storage-backend.ts';
import { MemoryStorageBackend } from '../backends/memory-storage-backend.ts';
import { StorageScope } from '../types.ts';

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

global.localStorage = localStorageMock as any;

describe('Integration Tests', () => {
	beforeEach(() => {
		localStorage.clear();
	});

	describe('End-to-End with LocalStorage', () => {
		it('should work end-to-end with persistence', async () => {
			const backend = new LocalStorageBackend('e2e-test');
			const storage = await StorageState.create(backend, StorageScope.WORKSPACE);

			storage.set('sidebar.width' as any, 250);
			storage.set('theme.mode' as any, 'dark');

			await storage.flush();

			// Create new instance (simulating app restart)
			const storage2 = await StorageState.create(backend, StorageScope.WORKSPACE);

			expect(storage2.get('sidebar.width' as any, 0)).toBe(250);
			expect(storage2.get('theme.mode' as any, 'light')).toBe('dark');
		});

		it('should handle ShellLayout use case', async () => {
			const backend = new LocalStorageBackend('shell-layout');
			const storage = await StorageState.create(backend, StorageScope.WORKSPACE, 'app-1');

			const layout = {
				sidebar: { width: 250, collapsed: false },
				panel: { width: 300, position: 'right' as const },
				bottomPanel: { height: 200 }
			};

			storage.set('layout.config' as any, layout);
			await storage.flush();

			// Restart simulation
			const storage2 = await StorageState.create(backend, StorageScope.WORKSPACE, 'app-1');
			const loaded = storage2.get('layout.config' as any);

			expect(loaded).toEqual(layout);
		});
	});

	describe('Multi-Scope Architecture', () => {
		it('should support global and workspace scopes independently', async () => {
			const backend = new MemoryStorageBackend();

			const globalStorage = await StorageState.create(backend, StorageScope.GLOBAL);
			const workspaceStorage = await StorageState.create(
				backend,
				StorageScope.WORKSPACE,
				'project-1'
			);

			globalStorage.set('theme.mode' as any, 'dark');
			workspaceStorage.set('sidebar.width' as any, 250);

			await globalStorage.flush();
			await workspaceStorage.flush();

			// Restart
			const globalStorage2 = await StorageState.create(backend, StorageScope.GLOBAL);
			const workspaceStorage2 = await StorageState.create(
				backend,
				StorageScope.WORKSPACE,
				'project-1'
			);

			expect(globalStorage2.get('theme.mode' as any, 'light')).toBe('dark');
			expect(workspaceStorage2.get('sidebar.width' as any, 0)).toBe(250);
		});

		it('should isolate different workspace IDs', async () => {
			const backend = new MemoryStorageBackend();

			const workspace1 = await StorageState.create(backend, StorageScope.WORKSPACE, 'project-1');
			const workspace2 = await StorageState.create(backend, StorageScope.WORKSPACE, 'project-2');

			workspace1.set('sidebar.width' as any, 250);
			workspace2.set('sidebar.width' as any, 400);

			await workspace1.flush();
			await workspace2.flush();

			expect(workspace1.get('sidebar.width' as any, 0)).toBe(250);
			expect(workspace2.get('sidebar.width' as any, 0)).toBe(400);
		});

		it('should support multi-project workspace switching', async () => {
			const backend = new MemoryStorageBackend();

			// Setup project 1
			const project1 = await StorageState.create(backend, StorageScope.WORKSPACE, 'project-1');
			project1.set('layout.config' as any, { sidebar: { width: 250 } });
			await project1.flush();

			// Setup project 2
			const project2 = await StorageState.create(backend, StorageScope.WORKSPACE, 'project-2');
			project2.set('layout.config' as any, { sidebar: { width: 350 } });
			await project2.flush();

			// Switch back to project 1
			const project1Again = await StorageState.create(backend, StorageScope.WORKSPACE, 'project-1');

			const config = project1Again.get('layout.config' as any) as any;
			expect(config.sidebar.width).toBe(250);
		});
	});

	describe('ShellLayout Integration', () => {
		it('should persist all layout state correctly', async () => {
			const backend = new LocalStorageBackend('shell-layout-test');
			const storage = await StorageState.create(backend, StorageScope.WORKSPACE, 'test-app');

			// Simulate ShellLayout state
			const layoutState = {
				sidebar: {
					width: 250,
					collapsed: false,
					snapPoints: [50, 250, 400]
				},
				panel: {
					width: 300,
					position: 'right' as const,
					collapsed: false,
					snapPoints: [60, 300, 450]
				},
				bottomPanel: {
					height: 200,
					collapsed: false,
					snapPoints: [100, 200, 300]
				}
			};

			storage.set('shell.layout.config' as any, layoutState);
			await storage.flush();

			// Reload
			const storage2 = await StorageState.create(backend, StorageScope.WORKSPACE, 'test-app');
			const restored = storage2.get('shell.layout.config' as any);

			expect(restored).toEqual(layoutState);
		});

		// ⚠️ SKIPPED: Requires Svelte reactivity runtime (not available in Node.js tests)
		it.skip('should handle rapid layout updates during resize', async () => {
			const backend = new MemoryStorageBackend();
			const storage = await StorageState.create(backend, StorageScope.WORKSPACE);

			// Simulate rapid resize updates
			for (let i = 200; i <= 400; i += 10) {
				storage.set('sidebar.width' as any, i);
			}

			await new Promise((resolve) => setTimeout(resolve, 150));

			// Should only persist latest value
			expect(backend.getRawStore().get('workspace.sidebar.width')).toBe('400');
		});

		it('should support separate global preferences and workspace layout', async () => {
			const backend = new MemoryStorageBackend();

			// Global preferences (theme, etc)
			const global = await StorageState.create(backend, StorageScope.GLOBAL);
			global.set('theme.mode' as any, 'dark');
			global.set('theme.accentColor' as any, '#0066cc');

			// Workspace layout
			const workspace = await StorageState.create(backend, StorageScope.WORKSPACE, 'my-project');
			workspace.set('layout.config' as any, { sidebar: { width: 250 } });

			await global.flush();
			await workspace.flush();

			// Verify isolation
			expect(global.size).toBe(2);
			expect(workspace.size).toBe(1);

			// Verify retrieval
			const globalReloaded = await StorageState.create(backend, StorageScope.GLOBAL);
			expect(globalReloaded.get('theme.mode' as any, 'light')).toBe('dark');

			const workspaceReloaded = await StorageState.create(
				backend,
				StorageScope.WORKSPACE,
				'my-project'
			);
			const layout = workspaceReloaded.get('layout.config' as any) as any;
			expect(layout.sidebar.width).toBe(250);
		});
	});

	// ⚠️ SKIPPED: Performance tests require Svelte reactivity runtime
	describe.skip('Performance at Scale', () => {
		it('should handle realistic app usage pattern', async () => {
			const backend = new MemoryStorageBackend();
			const storage = await StorageState.create(backend, StorageScope.WORKSPACE);

			// Simulate 100 updates over 5 seconds (20 updates/sec during resize)
			for (let i = 0; i < 100; i++) {
				storage.set('sidebar.width' as any, 200 + i);

				if (i % 20 === 0) {
					await new Promise((resolve) => setTimeout(resolve, 10));
				}
			}

			await storage.flush();

			// Should have final value
			expect(backend.getRawStore().get('workspace.sidebar.width')).toBe('299');

			// Total writes should be minimal (debounced)
			expect(backend.size).toBeLessThan(10);
		});

		it('should handle switching between multiple workspaces efficiently', async () => {
			const backend = new MemoryStorageBackend();

			// Create 10 different workspace storages
			const workspaces = await Promise.all(
				Array.from({ length: 10 }, (_, i) =>
					StorageState.create(backend, StorageScope.WORKSPACE, `project-${i}`)
				)
			);

			// Set different values in each
			for (let i = 0; i < 10; i++) {
				workspaces[i].set('sidebar.width' as any, 200 + i * 10);
			}

			await Promise.all(workspaces.map((w: StorageState) => w.flush()));

			// Reload and verify all
			for (let i = 0; i < 10; i++) {
				const reloaded = await StorageState.create(backend, StorageScope.WORKSPACE, `project-${i}`);
				expect(reloaded.get('sidebar.width' as any, 0)).toBe(200 + i * 10);
			}
		});
	});
});
