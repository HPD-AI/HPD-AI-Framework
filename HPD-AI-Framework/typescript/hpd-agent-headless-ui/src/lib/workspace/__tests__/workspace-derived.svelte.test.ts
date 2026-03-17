/**
 * Tests for workspace derived state and helper logic.
 *
 * These are pure-logic tests that don't require a real backend:
 *   - activeSiblings grouping and sort order
 *   - currentSiblingPosition 1-based display
 *   - canGoNext / canGoPrevious navigation flags
 *   - session/branch state isolation (compound cache key)
 *   - AgentState.loadHistory() is used (not fake streaming)
 */

import { describe, it, expect } from 'vitest';
import { createMockWorkspace } from '../../testing/mock-agent.svelte.ts';

// ============================================
// Helpers
// ============================================

async function tick(ms = 200): Promise<void> {
	await new Promise((r) => setTimeout(r, ms));
}

// ============================================
// activeSiblings grouping
// ============================================

describe('workspace — activeSiblings', () => {
	it('contains the active branch itself when no siblings', () => {
		const ws = createMockWorkspace();
		// main has forkedFrom: null — it is its own sibling group
		const main = ws.branches.get('main')!;
		const siblings = ws.activeSiblings;
		expect(siblings.some((s) => s.id === main.id)).toBe(true);
	});

	it('all fork siblings share the same forkedFrom and forkedAtMessageIndex (source is slot 0)', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.send('msg');
		await tick(300);

		// Create two forks from index 0
		await ws.editMessage(0, 'fork-a');
		await tick(300);
		const forkAId = ws.activeBranchId!;

		// Switch back to main, fork again
		await ws.switchBranch('main');
		await ws.editMessage(0, 'fork-b');
		await tick(300);

		const siblings = ws.activeSiblings;
		// activeSiblings now includes source (slot 0) plus all forks.
		// Source (main) has forkedFrom=null; forks share forkedFrom='main'.
		const forks = siblings.filter((s) => !s.isOriginal);
		const forkedFrom = forks[0]?.forkedFrom;
		const forkedAt = forks[0]?.forkedAtMessageIndex;
		expect(forks.every((s) => s.forkedFrom === forkedFrom)).toBe(true);
		expect(forks.every((s) => s.forkedAtMessageIndex === forkedAt)).toBe(true);
		// Source branch is present as slot 0
		const source = siblings.find((s) => s.isOriginal);
		expect(source).toBeDefined();
		expect(source!.siblingIndex).toBe(0);
		expect(forkAId).toBeTruthy(); // just confirms the fork was created
	});

	it('is sorted by siblingIndex ascending', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.send('msg');
		await tick(300);

		await ws.editMessage(0, 'first fork');
		await tick(300);
		await ws.switchBranch('main');
		await ws.editMessage(0, 'second fork');
		await tick(300);

		const siblings = ws.activeSiblings;
		for (let i = 1; i < siblings.length; i++) {
			expect(siblings[i].siblingIndex).toBeGreaterThanOrEqual(siblings[i - 1].siblingIndex);
		}
	});
});

// ============================================
// currentSiblingPosition
// ============================================

describe('workspace — currentSiblingPosition', () => {
	it('is { current: 1, total: 1 } for a lone branch', () => {
		const ws = createMockWorkspace();
		expect(ws.currentSiblingPosition).toEqual({ current: 1, total: 1 });
	});

	it('current is 1-based (siblingIndex + 1)', () => {
		const ws = createMockWorkspace();
		const main = ws.branches.get('main')!;
		expect(ws.currentSiblingPosition.current).toBe(main.siblingIndex + 1);
	});

	it('is { 0, 0 } when no active branch', async () => {
		const ws = createMockWorkspace();
		await ws.deleteSession('mock-session-1');
		await ws.deleteSession('mock-session-2');
		expect(ws.currentSiblingPosition).toEqual({ current: 0, total: 0 });
	});
});

// ============================================
// canGoNext / canGoPrevious
// ============================================

describe('workspace — canGoNext / canGoPrevious', () => {
	it('both false for a branch with no siblings', () => {
		const ws = createMockWorkspace();
		expect(ws.canGoNext).toBe(false);
		expect(ws.canGoPrevious).toBe(false);
	});

	it('both false when no active branch', async () => {
		const ws = createMockWorkspace();
		await ws.deleteSession('mock-session-1');
		await ws.deleteSession('mock-session-2');
		expect(ws.canGoNext).toBe(false);
		expect(ws.canGoPrevious).toBe(false);
	});
});

// ============================================
// Session isolation (compound cache key)
// ============================================

describe('workspace — session isolation', () => {
	it('session-1:main and session-2:main are independent states', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });

		// Send to session-1
		await ws.send('session-1 hello');
		await tick(300);
		const s1Messages = ws.state?.messages.length ?? 0;
		expect(s1Messages).toBeGreaterThan(0);

		// Switch to session-2 — should be empty
		await ws.selectSession('mock-session-2');
		expect(ws.state?.messages).toHaveLength(0);

		// Session-1 messages still exist when switching back
		await ws.selectSession('mock-session-1');
		expect(ws.state?.messages.length).toBe(s1Messages);
	});

	it('creating branches in session-1 does not appear in session-2', async () => {
		const ws = createMockWorkspace();
		await ws.createBranch({ branchId: 'session-1-feature' });
		await ws.selectSession('mock-session-2');
		expect(ws.branches.has('session-1-feature')).toBe(false);
	});
});

// ============================================
// loadHistory vs fake streaming
// ============================================

describe('workspace — loadHistory is used for branch switches', () => {
	it('switching to a branch does not leave streaming === true', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });

		// Send a message (streaming completes)
		await ws.send('hello');
		await tick(300);

		// Create another branch and switch to it
		await ws.createBranch({ branchId: 'other' });
		await ws.switchBranch('other');

		// The other branch's state should not have streaming = true
		expect(ws.state?.streaming).toBe(false);
	});

	it('switching back to main shows settled messages (streaming: false on all)', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.send('hello');
		await tick(300);

		await ws.createBranch({ branchId: 'other' });
		await ws.switchBranch('other');
		await ws.switchBranch('main');

		const messages = ws.state?.messages ?? [];
		expect(messages.every((m) => m.streaming === false)).toBe(true);
	});
});

// ============================================
// branches map reactivity
// ============================================

describe('workspace — branches map', () => {
	it('branches map reflects active session only', async () => {
		const ws = createMockWorkspace();
		await ws.createBranch({ branchId: 'session-1-only' });
		const session1BranchCount = ws.branches.size;

		await ws.selectSession('mock-session-2');
		expect(ws.branches.size).toBeLessThan(session1BranchCount);
		expect(ws.branches.has('session-1-only')).toBe(false);
	});

	it('branches map updates after createBranch', async () => {
		const ws = createMockWorkspace();
		const before = ws.branches.size;
		await ws.createBranch({ branchId: 'extra' });
		expect(ws.branches.size).toBe(before + 1);
	});

	it('branches map updates after deleteBranch', async () => {
		const ws = createMockWorkspace();
		await ws.createBranch({ branchId: 'extra' });
		const before = ws.branches.size;
		await ws.deleteBranch('extra');
		expect(ws.branches.size).toBe(before - 1);
	});
});

// ============================================
// canGoNext / canGoPrevious — with real siblings
// ============================================

describe('workspace — canGoNext / canGoPrevious with siblings', () => {
	it('canGoNext is true when activeBranch.nextSiblingId is set', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.send('msg');
		await tick(300);

		// editMessage creates a fork which sets nextSiblingId on the original
		await ws.editMessage(0, 'fork');
		await tick(300);

		// Switch back to main — main now has a next sibling (the fork)
		await ws.switchBranch('main');
		// The fork has previousSiblingId pointing to main, main has nextSiblingId
		// In the mock, canGoNext reflects activeBranch.nextSiblingId != null
		// main's nextSiblingId is set after fork
		expect(ws.canGoNext).toBe(ws.activeBranch?.nextSiblingId != null);
	});

	it('canGoPrevious is true when activeBranch.previousSiblingId is set', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.send('msg');
		await tick(300);

		await ws.editMessage(0, 'fork');
		await tick(300);

		// The fork is currently active — check its previousSiblingId
		expect(ws.canGoPrevious).toBe(ws.activeBranch?.previousSiblingId != null);
	});
});

// ============================================
// mapToUIMessages — loaded messages field correctness
// ============================================

describe('workspace — loadHistory produces settled messages', () => {
	it('all messages have streaming: false after branch switch', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.send('hello');
		await tick(300);

		// Switch away and back — history reloaded via loadHistory
		await ws.createBranch({ branchId: 'side' });
		await ws.switchBranch('side');
		await ws.switchBranch('main');

		for (const msg of ws.state!.messages) {
			expect(msg.streaming).toBe(false);
		}
	});

	it('all messages have thinking: false after branch switch', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.send('hello');
		await tick(300);

		await ws.createBranch({ branchId: 'side' });
		await ws.switchBranch('side');
		await ws.switchBranch('main');

		for (const msg of ws.state!.messages) {
			expect(msg.thinking).toBe(false);
		}
	});

	it('all messages have toolCalls: [] after branch switch', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.send('hello');
		await tick(300);

		await ws.createBranch({ branchId: 'side' });
		await ws.switchBranch('side');
		await ws.switchBranch('main');

		for (const msg of ws.state!.messages) {
			expect(msg.toolCalls).toEqual([]);
		}
	});

	it('all messages have a valid timestamp Date after branch switch', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.send('hello');
		await tick(300);

		await ws.createBranch({ branchId: 'side' });
		await ws.switchBranch('side');
		await ws.switchBranch('main');

		for (const msg of ws.state!.messages) {
			expect(msg.timestamp).toBeInstanceOf(Date);
		}
	});
});

// ============================================
// state derived value — switching
// ============================================

describe('workspace — state $derived updates on switch', () => {
	it('state points to a different AgentState after selectSession', async () => {
		const ws = createMockWorkspace();
		const before = ws.state;
		await ws.selectSession('mock-session-2');
		expect(ws.state).not.toBe(before);
	});

	it('state is null when activeSessionId is null', async () => {
		const ws = createMockWorkspace();
		await ws.deleteSession('mock-session-1');
		await ws.deleteSession('mock-session-2');
		expect(ws.state).toBeNull();
	});

	it('state points to a different AgentState after switchBranch', async () => {
		const ws = createMockWorkspace();
		const before = ws.state;
		await ws.createBranch({ branchId: 'other' });
		await ws.switchBranch('other');
		expect(ws.state).not.toBe(before);
	});

	it('state returns to the same object when switching back to a cached branch', async () => {
		const ws = createMockWorkspace();
		const mainState = ws.state;
		await ws.createBranch({ branchId: 'other' });
		await ws.switchBranch('other');
		await ws.switchBranch('main');
		// Should be the exact same AgentState instance (cache hit)
		expect(ws.state).toBe(mainState);
	});
});

// ============================================
// activeSiblings includes source as slot 0 (W1-W7)
// ============================================

describe('workspace — sibling redesign: source is slot 0 (W1-W7)', () => {
	async function tick(ms = 300): Promise<void> {
		await new Promise((r) => setTimeout(r, ms));
	}

	// W1: activeSiblings includes source (main) at slot 0 after a fork
	it('W1: activeSiblings includes source branch as slot 0 after editMessage', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.send('hello');
		await tick();

		// editMessage creates a fork — switch to it
		await ws.editMessage(0, 'fork content');
		await tick();

		const siblings = ws.activeSiblings;
		expect(siblings.length).toBeGreaterThanOrEqual(2);

		// source branch (main) should be slot 0
		const source = siblings.find((s) => s.id === 'main');
		expect(source).toBeDefined();
		expect(source!.siblingIndex).toBe(0);
	});

	// W2: currentSiblingPosition is { current: 2, total: 2 } when on fork (slot 1)
	it('W2: currentSiblingPosition is { 2, 2 } when on fork branch', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.send('hello');
		await tick();

		await ws.editMessage(0, 'fork');
		await tick();

		// Active branch is the fork (slot 1 out of 2)
		expect(ws.activeBranch?.siblingIndex).toBe(1);
		expect(ws.currentSiblingPosition.current).toBe(2); // 1-based
		expect(ws.currentSiblingPosition.total).toBe(2);
	});

	// W3: currentSiblingPosition is { 1, 2 } when back on main (slot 0)
	it('W3: currentSiblingPosition is { 1, 2 } when back on main after fork', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.send('hello');
		await tick();

		await ws.editMessage(0, 'fork');
		await tick();
		await ws.switchBranch('main');

		expect(ws.activeBranch?.id).toBe('main');
		expect(ws.activeBranch?.siblingIndex).toBe(0);
		expect(ws.currentSiblingPosition.current).toBe(1);
		expect(ws.currentSiblingPosition.total).toBe(2);
	});

	// W4: canGoPrevious is true when on fork (source is prev)
	it('W4: canGoPrevious is true when on fork branch (source is previousSiblingId)', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.send('hello');
		await tick();

		await ws.editMessage(0, 'fork');
		await tick();

		// Active is the fork — it has previousSiblingId=main
		expect(ws.activeBranch?.previousSiblingId).toBe('main');
		expect(ws.canGoPrevious).toBe(true);
	});

	// W5: canGoNext is true when on main (fork is next)
	it('W5: canGoNext is true when on main after fork (fork is nextSiblingId)', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.send('hello');
		await tick();

		await ws.editMessage(0, 'fork');
		await tick();
		await ws.switchBranch('main');

		// main now has nextSiblingId pointing to the fork
		expect(ws.activeBranch?.nextSiblingId).toBeTruthy();
		expect(ws.canGoNext).toBe(true);
	});

	// W6: goToPreviousSibling from fork lands on main
	it('W6: goToPreviousSibling from fork navigates back to main', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.send('hello');
		await tick();

		await ws.editMessage(0, 'fork');
		await tick();

		// Confirm we are on the fork
		const forkId = ws.activeBranchId;
		expect(forkId).not.toBe('main');

		// Navigate back to source
		await ws.goToPreviousSibling();

		expect(ws.activeBranch?.id).toBe('main');
	});

	// W7: goToNextSibling from main lands on fork
	it('W7: goToNextSibling from main navigates to the fork', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.send('hello');
		await tick();

		await ws.editMessage(0, 'fork');
		await tick();
		const forkId = ws.activeBranchId!;
		expect(forkId).not.toBe('main');

		await ws.switchBranch('main');
		await ws.goToNextSibling();

		expect(ws.activeBranch?.id).toBe(forkId);
	});
});
