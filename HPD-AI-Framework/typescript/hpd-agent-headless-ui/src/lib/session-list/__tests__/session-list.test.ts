/**
 * Unit tests for SessionList state classes
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { boxWith } from 'svelte-toolbelt';
import { box } from 'svelte-toolbelt';
import { SessionListRootState } from '../session-list.svelte.js';
import type { Session } from '@hpd/hpd-agent-client';

// Helper to create a mock session
const createMockSession = (overrides: Partial<Session> = {}): Session => ({
	id: 'session-1',
	createdAt: '2024-01-01T00:00:00Z',
	lastActivity: '2024-01-01T00:10:00Z',
	metadata: {},
	...overrides
});

// Helper to create a root state with minimal opts
function createRootState(sessions: Session[], activeId: string | null = null, opts: {
	onSelect?: (id: string) => void;
	onDelete?: (id: string) => void;
	onCreate?: () => void;
} = {}) {
	let sessionsVal = sessions;
	let activeIdVal = activeId;
	const rootNodeBox = box<HTMLElement | null>(null);

	return new SessionListRootState({
		sessions: boxWith(() => sessionsVal),
		activeSessionId: boxWith(() => activeIdVal),
		loading: boxWith(() => false),
		orientation: boxWith(() => 'vertical' as const),
		loop: boxWith(() => true),
		rootNode: rootNodeBox,
		...opts,
	});
}

describe('SessionListRootState', () => {
	describe('isEmpty', () => {
		it('should be true when no sessions', () => {
			const state = createRootState([]);
			expect(state.isEmpty).toBe(true);
		});

		it('should be false when sessions exist', () => {
			const state = createRootState([createMockSession()]);
			expect(state.isEmpty).toBe(false);
		});
	});

	describe('count', () => {
		it('should return 0 for empty list', () => {
			const state = createRootState([]);
			expect(state.count).toBe(0);
		});

		it('should return correct count', () => {
			const sessions = [
				createMockSession({ id: 's1' }),
				createMockSession({ id: 's2' }),
				createMockSession({ id: 's3' })
			];
			const state = createRootState(sessions);
			expect(state.count).toBe(3);
		});
	});

	describe('isActive', () => {
		it('should return true for active session', () => {
			const state = createRootState([], 'session-123');
			expect(state.isActive('session-123')).toBe(true);
		});

		it('should return false for inactive session', () => {
			const state = createRootState([], 'session-123');
			expect(state.isActive('session-456')).toBe(false);
		});

		it('should return false when no active session', () => {
			const state = createRootState([], null);
			expect(state.isActive('session-123')).toBe(false);
		});
	});

	describe('selectSession', () => {
		it('should call onSelect with session ID', async () => {
			const onSelect = vi.fn();
			const state = createRootState([], null, { onSelect });
			await state.selectSession('session-123');
			expect(onSelect).toHaveBeenCalledWith('session-123');
		});

		it('should not throw when onSelect is undefined', async () => {
			const state = createRootState([]);
			await expect(state.selectSession('session-123')).resolves.toBeUndefined();
		});
	});

	describe('deleteSession', () => {
		it('should call onDelete with session ID', async () => {
			const onDelete = vi.fn();
			const state = createRootState([], null, { onDelete });
			await state.deleteSession('session-123');
			expect(onDelete).toHaveBeenCalledWith('session-123');
		});

		it('should not throw when onDelete is undefined', async () => {
			const state = createRootState([]);
			await expect(state.deleteSession('session-123')).resolves.toBeUndefined();
		});
	});

	describe('createSession', () => {
		it('should call onCreate', async () => {
			const onCreate = vi.fn();
			const state = createRootState([], null, { onCreate });
			await state.createSession();
			expect(onCreate).toHaveBeenCalled();
		});

		it('should not throw when onCreate is undefined', async () => {
			const state = createRootState([]);
			await expect(state.createSession()).resolves.toBeUndefined();
		});
	});

	describe('formatDate', () => {
		const mockNow = new Date('2024-01-01T12:00:00Z');

		beforeEach(() => {
			vi.useFakeTimers();
			vi.setSystemTime(mockNow);
		});

		afterEach(() => {
			vi.useRealTimers();
		});

		it('should return "Just now" for recent dates (< 1 minute)', () => {
			const state = createRootState([]);
			const recent = new Date(mockNow.getTime() - 30_000).toISOString();
			expect(state.formatDate(recent)).toBe('Just now');
		});

		it('should return minutes for dates < 1 hour', () => {
			const state = createRootState([]);
			const minutes = new Date(mockNow.getTime() - 15 * 60_000).toISOString();
			expect(state.formatDate(minutes)).toBe('15m ago');
		});

		it('should return hours for dates < 24 hours', () => {
			const state = createRootState([]);
			const hours = new Date(mockNow.getTime() - 5 * 3_600_000).toISOString();
			expect(state.formatDate(hours)).toBe('5h ago');
		});

		it('should return days for dates < 7 days', () => {
			const state = createRootState([]);
			const days = new Date(mockNow.getTime() - 3 * 86_400_000).toISOString();
			expect(state.formatDate(days)).toBe('3d ago');
		});

		it('should return formatted date for dates >= 7 days', () => {
			const state = createRootState([]);
			const old = new Date(mockNow.getTime() - 10 * 86_400_000).toISOString();
			const result = state.formatDate(old);
			expect(result).not.toContain('ago');
		});
	});

	describe('props', () => {
		it('should include required ARIA attributes', () => {
			const state = createRootState([]);
			const props = state.props;
			expect(props.role).toBe('listbox');
			expect(props['aria-orientation']).toBe('vertical');
			expect(props['aria-busy']).toBe(false);
			expect(props['data-session-list-root']).toBe('');
		});

		it('should set data-empty when no sessions', () => {
			const state = createRootState([]);
			expect(state.props['data-empty']).toBe('');
		});

		it('should not set data-empty when sessions exist', () => {
			const state = createRootState([createMockSession()]);
			expect(state.props['data-empty']).toBeUndefined();
		});
	});
});
