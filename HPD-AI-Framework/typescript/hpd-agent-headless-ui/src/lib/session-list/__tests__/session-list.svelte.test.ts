/**
 * SessionList Component Tests
 *
 * Browser-based tests for the SessionList compound component.
 * Tests: data attributes, ARIA, context wiring, keyboard nav, callbacks.
 */

import { describe, it, expect, vi } from 'vitest';
import { render } from 'vitest-browser-svelte';
import { page } from 'vitest/browser';
import type { Session } from '@hpd/hpd-agent-client';
import SessionListTest from './session-list-test.svelte';

// ============================================
// Helpers
// ============================================

const createSession = (overrides: Partial<Session> = {}): Session => ({
	id: `session-${Math.random().toString(36).slice(2, 8)}`,
	createdAt: new Date(Date.now() - 3_600_000).toISOString(),
	lastActivity: new Date(Date.now() - 60_000).toISOString(),
	metadata: {},
	...overrides,
});

function setup(props: {
	sessions?: Session[];
	activeSessionId?: string | null;
	onSelect?: (id: string) => void;
	onDelete?: (id: string) => void;
	onCreate?: () => void;
} = {}) {
	render(SessionListTest, { props } as any);

	return {
		root: page.getByTestId('root'),
		activeBiding: page.getByTestId('active-binding'),
		count: page.getByTestId('count'),
		empty: page.getByTestId('empty'),
		createBtn: page.getByTestId('create-btn'),
		item: (id: string) => page.getByTestId(`item-${id}`),
		label: (id: string) => page.getByTestId(`label-${id}`),
		activity: (id: string) => page.getByTestId(`activity-${id}`),
		activeIndicator: (id: string) => page.getByTestId(`active-indicator-${id}`),
		deleteBtn: (id: string) => page.getByTestId(`delete-${id}`),
	};
}

// ============================================
// Data Attributes
// ============================================

describe('SessionList — Data Attributes', () => {
	it('root has data-session-list-root', async () => {
		const t = setup();
		await expect.element(t.root).toHaveAttribute('data-session-list-root');
	});

	it('root has data-empty when no sessions', async () => {
		const t = setup({ sessions: [] });
		await expect.element(t.root).toHaveAttribute('data-empty');
	});

	it('root does not have data-empty when sessions exist', async () => {
		const s = createSession({ id: 'abc' });
		const t = setup({ sessions: [s] });
		await expect.element(t.root).not.toHaveAttribute('data-empty');
	});

	it('item has data-session-list-item', async () => {
		const s = createSession({ id: 's1' });
		const t = setup({ sessions: [s] });
		await expect.element(t.item('s1')).toHaveAttribute('data-session-list-item');
	});

	it('item has data-session-id', async () => {
		const s = createSession({ id: 's1' });
		const t = setup({ sessions: [s] });
		await expect.element(t.item('s1')).toHaveAttribute('data-session-id', 's1');
	});

	it('active item has data-active', async () => {
		const s = createSession({ id: 's1' });
		const t = setup({ sessions: [s], activeSessionId: 's1' });
		await expect.element(t.item('s1')).toHaveAttribute('data-active');
	});

	it('inactive item does not have data-active', async () => {
		const s = createSession({ id: 's1' });
		const t = setup({ sessions: [s], activeSessionId: null });
		await expect.element(t.item('s1')).not.toHaveAttribute('data-active');
	});

	it('empty has data-session-list-empty', async () => {
		const t = setup({ sessions: [] });
		await expect.element(t.empty).toHaveAttribute('data-session-list-empty');
	});

	it('create button has data-session-list-create', async () => {
		const t = setup({ sessions: [], onCreate: vi.fn() });
		await expect.element(t.createBtn).toHaveAttribute('data-session-list-create');
	});
});

// ============================================
// ARIA Attributes
// ============================================

describe('SessionList — ARIA', () => {
	it('root has role listbox', async () => {
		const t = setup();
		await expect.element(t.root).toHaveAttribute('role', 'listbox');
	});

	it('root has aria-orientation vertical by default', async () => {
		const t = setup();
		await expect.element(t.root).toHaveAttribute('aria-orientation', 'vertical');
	});

	it('item has role option', async () => {
		const s = createSession({ id: 's1' });
		const t = setup({ sessions: [s] });
		await expect.element(t.item('s1')).toHaveAttribute('role', 'option');
	});

	it('active item has aria-selected true', async () => {
		const s = createSession({ id: 's1' });
		const t = setup({ sessions: [s], activeSessionId: 's1' });
		await expect.element(t.item('s1')).toHaveAttribute('aria-selected', 'true');
	});

	it('inactive item has aria-selected false', async () => {
		const s = createSession({ id: 's1' });
		const t = setup({ sessions: [s], activeSessionId: null });
		await expect.element(t.item('s1')).toHaveAttribute('aria-selected', 'false');
	});

	it('create button has type button', async () => {
		const t = setup({ sessions: [], onCreate: vi.fn() });
		await expect.element(t.createBtn).toHaveAttribute('type', 'button');
	});
});

// ============================================
// Empty State
// ============================================

describe('SessionList — Empty State', () => {
	it('shows empty slot when no sessions', async () => {
		const t = setup({ sessions: [] });
		await expect.element(t.empty).toBeVisible();
	});

	it('shows empty text', async () => {
		const t = setup({ sessions: [] });
		await expect.element(t.empty).toHaveTextContent('No sessions yet');
	});

	it('count shows 0 when empty', async () => {
		const t = setup({ sessions: [] });
		await expect.element(t.count).toHaveTextContent('0');
	});
});

// ============================================
// Session Items
// ============================================

describe('SessionList — Items', () => {
	it('renders all sessions', async () => {
		const sessions = [
			createSession({ id: 's1' }),
			createSession({ id: 's2' }),
			createSession({ id: 's3' }),
		];
		const t = setup({ sessions });
		await expect.element(t.item('s1')).toBeVisible();
		await expect.element(t.item('s2')).toBeVisible();
		await expect.element(t.item('s3')).toBeVisible();
	});

	it('count reflects session count', async () => {
		const sessions = [createSession({ id: 's1' }), createSession({ id: 's2' })];
		const t = setup({ sessions });
		await expect.element(t.count).toHaveTextContent('2');
	});

	it('renders session id in label', async () => {
		const s = createSession({ id: 'my-session' });
		const t = setup({ sessions: [s] });
		await expect.element(t.label('my-session')).toHaveTextContent('my-session');
	});

	it('renders lastActivity time', async () => {
		const s = createSession({ id: 's1', lastActivity: new Date(Date.now() - 60_000).toISOString() });
		const t = setup({ sessions: [s] });
		await expect.element(t.activity('s1')).toHaveTextContent('1m ago');
	});

	it('active indicator shown for active session', async () => {
		const s = createSession({ id: 's1' });
		const t = setup({ sessions: [s], activeSessionId: 's1' });
		await expect.element(t.activeIndicator('s1')).toBeVisible();
	});

	it('active indicator not shown for inactive session', async () => {
		const s = createSession({ id: 's1' });
		const t = setup({ sessions: [s], activeSessionId: null });
		await expect.element(t.activeIndicator('s1')).not.toBeInTheDocument();
	});
});

// ============================================
// Selection
// ============================================

describe('SessionList — Selection', () => {
	it('calls onSelect when item clicked', async () => {
		const onSelect = vi.fn();
		const s = createSession({ id: 's1' });
		const t = setup({ sessions: [s], onSelect });
		await t.item('s1').click();
		expect(onSelect).toHaveBeenCalledWith('s1');
	});

	it('updates active-binding when item selected', async () => {
		const s = createSession({ id: 's1' });
		const t = setup({ sessions: [s] });
		await t.item('s1').click();
		await expect.element(t.activeBiding).toHaveTextContent('s1');
	});

	it('item gets data-active after being selected', async () => {
		const s = createSession({ id: 's1' });
		const t = setup({ sessions: [s] });
		await t.item('s1').click();
		await expect.element(t.item('s1')).toHaveAttribute('data-active');
	});

	it('selecting different item moves active state', async () => {
		const sessions = [createSession({ id: 's1' }), createSession({ id: 's2' })];
		const t = setup({ sessions, activeSessionId: 's1' });

		await t.item('s2').click();

		await expect.element(t.item('s2')).toHaveAttribute('data-active');
		await expect.element(t.item('s1')).not.toHaveAttribute('data-active');
	});
});

// ============================================
// Deletion
// ============================================

describe('SessionList — Deletion', () => {
	it('calls onDelete when delete button clicked', async () => {
		const onDelete = vi.fn();
		const s = createSession({ id: 's1' });
		const t = setup({ sessions: [s], onDelete });
		await t.deleteBtn('s1').click();
		expect(onDelete).toHaveBeenCalledWith('s1');
	});

	it('delete button does not trigger selection', async () => {
		const onSelect = vi.fn();
		const onDelete = vi.fn();
		const s = createSession({ id: 's1' });
		const t = setup({ sessions: [s], onSelect, onDelete });
		await t.deleteBtn('s1').click();
		// onDelete called, onSelect not called from delete button
		expect(onDelete).toHaveBeenCalledWith('s1');
		expect(onSelect).not.toHaveBeenCalled();
	});
});

// ============================================
// Create
// ============================================

describe('SessionList — Create', () => {
	it('shows create button when onCreate provided', async () => {
		const t = setup({ sessions: [], onCreate: vi.fn() });
		await expect.element(t.createBtn).toBeVisible();
	});

	it('calls onCreate when create button clicked', async () => {
		const onCreate = vi.fn();
		const t = setup({ sessions: [], onCreate });
		await t.createBtn.click();
		expect(onCreate).toHaveBeenCalled();
	});
});

// ============================================
// Keyboard Navigation
// ============================================

describe('SessionList — Keyboard', () => {
	it('items have tabindex 0 on first item by default', async () => {
		const sessions = [createSession({ id: 's1' }), createSession({ id: 's2' })];
		const t = setup({ sessions });
		await expect.element(t.item('s1')).toHaveAttribute('tabindex', '0');
		await expect.element(t.item('s2')).toHaveAttribute('tabindex', '-1');
	});

	it.todo('ArrowDown moves focus to next item');
	it.todo('ArrowUp moves focus to previous item');
	it.todo('Enter on focused item triggers selection');
	it.todo('Delete on focused item triggers deletion');
	it.todo('Home moves focus to first item');
	it.todo('End moves focus to last item');
});
