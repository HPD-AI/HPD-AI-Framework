/**
 * Artifact State Tests - State Management Unit Tests
 *
 * Tests the core reactive state managers for the Artifact component.
 * Note: These tests verify the state logic, but don't test Svelte reactivity
 * since that requires a Svelte runtime context (tested in browser tests).
 */

import { describe, it, expect, vi } from 'vitest';
import { ArtifactProviderState } from '../artifact.svelte.ts';
import { boxWith } from 'svelte-toolbelt';

describe('ArtifactProviderState', () => {
	describe('Initialization', () => {
		it('should initialize with closed state', () => {
			const state = new ArtifactProviderState({
				onOpenChange: boxWith(() => undefined)
			});

			expect(state.open).toBe(false);
			expect(state.openId).toBeNull();
			expect(state.mountedId).toBeNull();
		});

		it('should initialize with no current slot', () => {
			const state = new ArtifactProviderState({
				onOpenChange: boxWith(() => undefined)
			});

			expect(state.currentSlot).toBeNull();
			expect(state.title).toBeNull();
			expect(state.content).toBeNull();
		});
	});

	describe('openArtifact', () => {
		it('should set openId and mountedId when opening', () => {
			const state = new ArtifactProviderState({
				onOpenChange: boxWith(() => undefined)
			});

			state.openArtifact('artifact-1');

			expect(state.open).toBe(true);
			expect(state.openId).toBe('artifact-1');
			expect(state.mountedId).toBe('artifact-1');
		});

		it('should call onOpenChange with true and id when opening', () => {
			const onOpenChange = vi.fn();
			const state = new ArtifactProviderState({
				onOpenChange: boxWith(() => onOpenChange)
			});

			state.openArtifact('artifact-1');

			expect(onOpenChange).toHaveBeenCalledWith(true, 'artifact-1');
			expect(onOpenChange).toHaveBeenCalledTimes(1);
		});

		it('should switch artifacts when opening a new one', () => {
			const onOpenChange = vi.fn();
			const state = new ArtifactProviderState({
				onOpenChange: boxWith(() => onOpenChange)
			});

			state.openArtifact('artifact-1');
			state.openArtifact('artifact-2');

			expect(state.openId).toBe('artifact-2');
			expect(state.mountedId).toBe('artifact-2');
			// Called twice - once for each different artifact
			expect(onOpenChange).toHaveBeenCalledTimes(2);
			expect(onOpenChange).toHaveBeenNthCalledWith(1, true, 'artifact-1');
			expect(onOpenChange).toHaveBeenNthCalledWith(2, true, 'artifact-2');
		});

		it('should NOT call onOpenChange when opening the same artifact again', () => {
			const onOpenChange = vi.fn();
			const state = new ArtifactProviderState({
				onOpenChange: boxWith(() => onOpenChange)
			});

			state.openArtifact('artifact-1');
			state.openArtifact('artifact-1'); // Same artifact

			expect(onOpenChange).toHaveBeenCalledTimes(1);
		});
	});

	describe('closeArtifact', () => {
		it('should set openId to null when closing', () => {
			const state = new ArtifactProviderState({
				onOpenChange: boxWith(() => undefined)
			});

			state.openArtifact('artifact-1');
			state.closeArtifact();

			expect(state.open).toBe(false);
			expect(state.openId).toBeNull();
		});

		it('should keep mountedId for animation timing', () => {
			const state = new ArtifactProviderState({
				onOpenChange: boxWith(() => undefined)
			});

			state.openArtifact('artifact-1');
			state.closeArtifact();

			// mountedId stays for animation - cleared by PresenceManager
			expect(state.mountedId).toBe('artifact-1');
		});

		it('should call onOpenChange with false and id when closing', () => {
			const onOpenChange = vi.fn();
			const state = new ArtifactProviderState({
				onOpenChange: boxWith(() => onOpenChange)
			});

			state.openArtifact('artifact-1');
			onOpenChange.mockClear();
			state.closeArtifact();

			expect(onOpenChange).toHaveBeenCalledWith(false, 'artifact-1');
		});

		it('should not call onOpenChange if already closed', () => {
			const onOpenChange = vi.fn();
			const state = new ArtifactProviderState({
				onOpenChange: boxWith(() => onOpenChange)
			});

			state.closeArtifact();

			expect(onOpenChange).not.toHaveBeenCalled();
		});
	});

	describe('clearMounted', () => {
		it('should clear mountedId when openId is null', () => {
			const state = new ArtifactProviderState({
				onOpenChange: boxWith(() => undefined)
			});

			state.openArtifact('artifact-1');
			state.closeArtifact();
			state.clearMounted();

			expect(state.mountedId).toBeNull();
		});

		it('should NOT clear mountedId when artifact is still open', () => {
			const state = new ArtifactProviderState({
				onOpenChange: boxWith(() => undefined)
			});

			state.openArtifact('artifact-1');
			state.clearMounted();

			// Should still be mounted because it's open
			expect(state.mountedId).toBe('artifact-1');
		});
	});

	describe('Slot Registry', () => {
		it('should register a slot', () => {
			const state = new ArtifactProviderState({
				onOpenChange: boxWith(() => undefined)
			});

			state.registerSlot('artifact-1', {
				id: 'artifact-1',
				title: null,
				content: null
			});

			expect(state.hasSlot('artifact-1')).toBe(true);
		});

		it('should unregister a slot', () => {
			const state = new ArtifactProviderState({
				onOpenChange: boxWith(() => undefined)
			});

			state.registerSlot('artifact-1', {
				id: 'artifact-1',
				title: null,
				content: null
			});
			state.unregisterSlot('artifact-1');

			expect(state.hasSlot('artifact-1')).toBe(false);
		});

		it('should return currentSlot when artifact is open', () => {
			const state = new ArtifactProviderState({
				onOpenChange: boxWith(() => undefined)
			});

			const mockTitle = (() => {}) as any;
			const mockContent = (() => {}) as any;

			state.registerSlot('artifact-1', {
				id: 'artifact-1',
				title: mockTitle,
				content: mockContent
			});
			state.openArtifact('artifact-1');

			expect(state.currentSlot).toEqual({
				id: 'artifact-1',
				title: mockTitle,
				content: mockContent
			});
			expect(state.title).toBe(mockTitle);
			expect(state.content).toBe(mockContent);
		});

		it('should auto-close when open slot is unregistered', () => {
			const onOpenChange = vi.fn();
			const state = new ArtifactProviderState({
				onOpenChange: boxWith(() => onOpenChange)
			});

			state.registerSlot('artifact-1', {
				id: 'artifact-1',
				title: null,
				content: null
			});
			state.openArtifact('artifact-1');
			onOpenChange.mockClear();

			state.unregisterSlot('artifact-1');

			expect(state.open).toBe(false);
			expect(onOpenChange).toHaveBeenCalledWith(false, 'artifact-1');
		});

		it('should clear mountedId when slot is unregistered', () => {
			const state = new ArtifactProviderState({
				onOpenChange: boxWith(() => undefined)
			});

			state.registerSlot('artifact-1', {
				id: 'artifact-1',
				title: null,
				content: null
			});
			state.openArtifact('artifact-1');
			state.closeArtifact();
			// mountedId is still artifact-1 here

			state.unregisterSlot('artifact-1');

			expect(state.mountedId).toBeNull();
		});
	});

	describe('hasSlot', () => {
		it('should return true for registered slots', () => {
			const state = new ArtifactProviderState({
				onOpenChange: boxWith(() => undefined)
			});

			state.registerSlot('artifact-1', {
				id: 'artifact-1',
				title: null,
				content: null
			});

			expect(state.hasSlot('artifact-1')).toBe(true);
		});

		it('should return false for unregistered slots', () => {
			const state = new ArtifactProviderState({
				onOpenChange: boxWith(() => undefined)
			});

			expect(state.hasSlot('nonexistent')).toBe(false);
		});
	});

	describe('sharedProps', () => {
		it('should include data-open when open', () => {
			const state = new ArtifactProviderState({
				onOpenChange: boxWith(() => undefined)
			});

			state.openArtifact('artifact-1');

			expect(state.sharedProps['data-open']).toBe('');
			expect(state.sharedProps['data-artifact-id']).toBe('artifact-1');
		});

		it('should NOT include data-open when closed', () => {
			const state = new ArtifactProviderState({
				onOpenChange: boxWith(() => undefined)
			});

			expect(state.sharedProps['data-open']).toBeUndefined();
			expect(state.sharedProps['data-artifact-id']).toBeUndefined();
		});
	});

	describe('Edge Cases', () => {
		it('should handle opening non-existent artifact gracefully', () => {
			const state = new ArtifactProviderState({
				onOpenChange: boxWith(() => undefined)
			});

			// Opening an artifact that hasn't been registered
			state.openArtifact('nonexistent');

			expect(state.open).toBe(true);
			expect(state.openId).toBe('nonexistent');
			// currentSlot should be null since it's not registered
			expect(state.currentSlot).toBeNull();
		});

		it('should handle rapid open/close cycles', () => {
			const state = new ArtifactProviderState({
				onOpenChange: boxWith(() => undefined)
			});

			for (let i = 0; i < 100; i++) {
				state.openArtifact(`artifact-${i}`);
				if (i % 2 === 0) {
					state.closeArtifact();
				}
			}

			// After 100 iterations, last one (99) opened but not closed
			expect(state.openId).toBe('artifact-99');
		});

		it('should handle multiple slots with same ID (last one wins)', () => {
			const state = new ArtifactProviderState({
				onOpenChange: boxWith(() => undefined)
			});

			const firstContent = (() => {}) as any;
			const secondContent = (() => {}) as any;

			state.registerSlot('artifact-1', {
				id: 'artifact-1',
				title: null,
				content: firstContent
			});

			state.registerSlot('artifact-1', {
				id: 'artifact-1',
				title: null,
				content: secondContent
			});

			state.openArtifact('artifact-1');

			// Last registration wins
			expect(state.content).toBe(secondContent);
		});
	});
});
