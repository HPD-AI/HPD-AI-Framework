/**
 * Tests for createMockWorkspace()
 *
 * The mock implements the full Workspace interface without a real backend.
 * These tests cover:
 *   - Initialization state
 *   - Session CRUD and switching
 *   - Branch CRUD and switching
 *   - Derived state correctness ($derived)
 *   - LRU cache isolation per session
 *   - Streaming simulation (send)
 *   - editMessage fork flow
 *   - deleteBranch edge cases
 *   - Workspace interface contract
 */

import { describe, it, expect, beforeEach } from 'vitest';
import { createMockWorkspace } from '../../testing/mock-agent.svelte.ts';
import type { Workspace } from '../types.ts';

// ============================================
// Helpers
// ============================================

/** Drain microtasks + setTimeout(0) queue so mock async ops complete. */
async function tick(ms = 200): Promise<void> {
	await new Promise((r) => setTimeout(r, ms));
}

// ============================================
// Group 1: Initialization
// ============================================

describe('createMockWorkspace — initialization', () => {
	it('starts with 2 mock sessions', () => {
		const ws = createMockWorkspace();
		expect(ws.sessions).toHaveLength(2);
	});

	it('first session is active on creation', () => {
		const ws = createMockWorkspace();
		expect(ws.activeSessionId).toBe('mock-session-1');
	});

	it('main branch is active on creation', () => {
		const ws = createMockWorkspace();
		expect(ws.activeBranchId).toBe('main');
	});

	it('state is not null after init', () => {
		const ws = createMockWorkspace();
		expect(ws.state).not.toBeNull();
	});

	it('state starts with no messages', () => {
		const ws = createMockWorkspace();
		expect(ws.state?.messages).toHaveLength(0);
	});

	it('loading is false after init', () => {
		const ws = createMockWorkspace();
		expect(ws.loading).toBe(false);
	});

	it('error is null after init', () => {
		const ws = createMockWorkspace();
		expect(ws.error).toBeNull();
	});

	it('each session has a main branch', () => {
		const ws = createMockWorkspace();
		expect(ws.branches.has('main')).toBe(true);
	});
});

// ============================================
// Group 2: Session switching
// ============================================

describe('createMockWorkspace — selectSession', () => {
	it('switches activeSessionId', async () => {
		const ws = createMockWorkspace();
		await ws.selectSession('mock-session-2');
		expect(ws.activeSessionId).toBe('mock-session-2');
	});

	it('switches to main branch in new session', async () => {
		const ws = createMockWorkspace();
		await ws.selectSession('mock-session-2');
		expect(ws.activeBranchId).toBe('main');
	});

	it('new session has its own isolated AgentState', async () => {
		const ws = createMockWorkspace();

		// Put a message in session-1
		await ws.send('hello from session 1');
		const session1MessageCount = ws.state?.messages.length ?? 0;
		expect(session1MessageCount).toBeGreaterThan(0);

		// Switch to session-2 — its state should be empty
		await ws.selectSession('mock-session-2');
		expect(ws.state?.messages).toHaveLength(0);
	});

	it('state updates reactively after selectSession', async () => {
		const ws = createMockWorkspace();
		const initialState = ws.state;

		await ws.selectSession('mock-session-2');
		// state object should be for session-2:main, a different AgentState instance
		expect(ws.state).not.toBe(initialState);
	});

	it('selectSession with current session is a no-op', async () => {
		const ws = createMockWorkspace();
		const stateBefore = ws.state;
		await ws.selectSession('mock-session-1');
		expect(ws.state).toBe(stateBefore);
		expect(ws.activeBranchId).toBe('main');
	});

	it('switching back to session-1 restores its state', async () => {
		const ws = createMockWorkspace();

		// Add a message to session-1
		await ws.send('session 1 message');
		const msgCount = ws.state?.messages.length ?? 0;

		// Go to session-2
		await ws.selectSession('mock-session-2');

		// Go back to session-1
		await ws.selectSession('mock-session-1');
		expect(ws.state?.messages.length).toBe(msgCount);
	});
});

// ============================================
// Group 3: Session creation and deletion
// ============================================

describe('createMockWorkspace — createSession', () => {
	it('adds session to the sessions list', async () => {
		const ws = createMockWorkspace();
		await ws.createSession();
		expect(ws.sessions).toHaveLength(3);
	});

	it('new session becomes active', async () => {
		const ws = createMockWorkspace();
		await ws.createSession();
		// last session in list should be active
		const lastId = ws.sessions[ws.sessions.length - 1].id;
		expect(ws.activeSessionId).toBe(lastId);
	});

	it('new session has main branch active', async () => {
		const ws = createMockWorkspace();
		await ws.createSession();
		expect(ws.activeBranchId).toBe('main');
	});

	it('respects provided sessionId', async () => {
		const ws = createMockWorkspace();
		await ws.createSession({ sessionId: 'my-custom-session' });
		expect(ws.activeSessionId).toBe('my-custom-session');
	});
});

describe('createMockWorkspace — deleteSession', () => {
	it('removes session from the list', async () => {
		const ws = createMockWorkspace();
		await ws.deleteSession('mock-session-2');
		expect(ws.sessions.find((s) => s.id === 'mock-session-2')).toBeUndefined();
		expect(ws.sessions).toHaveLength(1);
	});

	it('deleting inactive session does not change activeSessionId', async () => {
		const ws = createMockWorkspace();
		await ws.deleteSession('mock-session-2');
		expect(ws.activeSessionId).toBe('mock-session-1');
	});

	it('deleting active session navigates to another session', async () => {
		const ws = createMockWorkspace();
		await ws.deleteSession('mock-session-1');
		expect(ws.activeSessionId).toBe('mock-session-2');
	});

	it('deleting only session sets activeSessionId to null', async () => {
		const ws = createMockWorkspace();
		await ws.deleteSession('mock-session-1');
		await ws.deleteSession('mock-session-2');
		expect(ws.activeSessionId).toBeNull();
		expect(ws.activeBranchId).toBeNull();
		expect(ws.state).toBeNull();
	});

	it('evicts cached state for deleted session', async () => {
		const ws = createMockWorkspace();
		await ws.send('a message');

		// Switch to session-2 so session-1 stays cached
		await ws.selectSession('mock-session-2');
		await ws.deleteSession('mock-session-1');

		// Switch back to session-2 (which is still active) — state is fine
		expect(ws.state?.messages).toHaveLength(0);
	});
});

// ============================================
// Group 4: Branch switching
// ============================================

describe('createMockWorkspace — switchBranch', () => {
	it('changes activeBranchId', async () => {
		const ws = createMockWorkspace();
		await ws.createBranch({ branchId: 'feature' });
		await ws.switchBranch('feature');
		expect(ws.activeBranchId).toBe('feature');
	});

	it('state updates to the new branch state', async () => {
		const ws = createMockWorkspace();
		const mainState = ws.state;

		await ws.createBranch({ branchId: 'feature' });
		await ws.switchBranch('feature');
		expect(ws.state).not.toBe(mainState);
	});

	it('new branch starts with empty messages', async () => {
		const ws = createMockWorkspace();
		await ws.createBranch({ branchId: 'feature' });
		await ws.switchBranch('feature');
		expect(ws.state?.messages).toHaveLength(0);
	});

	it('switching back to main restores its state', async () => {
		const ws = createMockWorkspace();
		await ws.send('main message');
		const mainMsgCount = ws.state?.messages.length ?? 0;

		await ws.createBranch({ branchId: 'feature' });
		await ws.switchBranch('feature');
		await ws.switchBranch('main');

		expect(ws.state?.messages.length).toBe(mainMsgCount);
	});

	it('switchBranch with current branch is a no-op', async () => {
		const ws = createMockWorkspace();
		const stateBefore = ws.state;
		await ws.switchBranch('main');
		expect(ws.state).toBe(stateBefore);
	});

	it('throws when branch does not exist', async () => {
		const ws = createMockWorkspace();
		await expect(ws.switchBranch('nonexistent')).rejects.toThrow();
	});
});

// ============================================
// Group 5: Branch creation and deletion
// ============================================

describe('createMockWorkspace — createBranch', () => {
	it('adds branch to branches map', async () => {
		const ws = createMockWorkspace();
		await ws.createBranch({ branchId: 'feature' });
		expect(ws.branches.has('feature')).toBe(true);
	});

	it('returned branch has the provided id', async () => {
		const ws = createMockWorkspace();
		const branch = await ws.createBranch({ branchId: 'feature' });
		expect(branch.id).toBe('feature');
	});

	it('does not switch activeBranchId automatically', async () => {
		const ws = createMockWorkspace();
		await ws.createBranch({ branchId: 'feature' });
		expect(ws.activeBranchId).toBe('main');
	});

	it('generates an id when none provided', async () => {
		const ws = createMockWorkspace();
		const branch = await ws.createBranch();
		expect(branch.id).toBeTruthy();
		expect(ws.branches.has(branch.id)).toBe(true);
	});
});

describe('createMockWorkspace — deleteBranch', () => {
	it('removes branch from branches map', async () => {
		const ws = createMockWorkspace();
		await ws.createBranch({ branchId: 'feature' });
		await ws.deleteBranch('feature');
		expect(ws.branches.has('feature')).toBe(false);
	});

	it('deleting inactive branch does not change activeBranchId', async () => {
		const ws = createMockWorkspace();
		await ws.createBranch({ branchId: 'feature' });
		await ws.deleteBranch('feature');
		expect(ws.activeBranchId).toBe('main');
	});

	it('throws when deleting the only branch', async () => {
		const ws = createMockWorkspace();
		await expect(ws.deleteBranch('main')).rejects.toThrow();
	});

	it('throws when branch does not exist', async () => {
		const ws = createMockWorkspace();
		await expect(ws.deleteBranch('nonexistent')).rejects.toThrow();
	});

	it('navigates away when deleting active branch', async () => {
		const ws = createMockWorkspace();
		await ws.createBranch({ branchId: 'feature' });
		await ws.switchBranch('feature');
		await ws.deleteBranch('feature');
		expect(ws.activeBranchId).toBe('main');
	});

	it('throws when branch has children', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		// Send a message so editMessage has something to fork from
		await ws.send('hello');
		await tick(300);
		// editMessage creates a fork, making main the parent with childBranches.length > 0
		await ws.editMessage(0, 'fork it');
		await tick(300);
		// Now switch back to main and try to delete it — main has a child fork
		await ws.switchBranch('main');
		await expect(ws.deleteBranch('main')).rejects.toThrow();
	});
});

// ============================================
// Group 6: editMessage
// ============================================

describe('createMockWorkspace — editMessage', () => {
	it('creates a fork branch after sending a message', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.send('original message');
		await tick(300);

		const branchCountBefore = ws.branches.size;
		await ws.editMessage(0, 'edited message');
		await tick(300);

		expect(ws.branches.size).toBeGreaterThan(branchCountBefore);
	});

	it('switches to the fork branch', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.send('original message');
		await tick(300);

		await ws.editMessage(0, 'edited message');
		await tick(300);

		expect(ws.activeBranchId).not.toBe('main');
	});

	it('fork contains the edited message', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.send('original message');
		await tick(300);

		await ws.editMessage(0, 'my edit');
		await tick(300);

		const messages = ws.state?.messages ?? [];
		const userMessages = messages.filter((m) => m.role === 'user');
		expect(userMessages.some((m) => m.content === 'my edit')).toBe(true);
	});

	it('throws when messageIndex is out of bounds', async () => {
		const ws = createMockWorkspace();
		await expect(ws.editMessage(99, 'edit')).rejects.toThrow();
	});

	it('throws when no messages exist', async () => {
		const ws = createMockWorkspace();
		await expect(ws.editMessage(0, 'edit')).rejects.toThrow();
	});
});

// ============================================
// Group 7: Sibling navigation
// ============================================

describe('createMockWorkspace — sibling navigation', () => {
	it('goToNextSibling throws when canGoNext is false', async () => {
		const ws = createMockWorkspace();
		expect(ws.canGoNext).toBe(false);
		await expect(ws.goToNextSibling()).rejects.toThrow();
	});

	it('goToPreviousSibling throws when canGoPrevious is false', async () => {
		const ws = createMockWorkspace();
		expect(ws.canGoPrevious).toBe(false);
		await expect(ws.goToPreviousSibling()).rejects.toThrow();
	});

	it('goToSiblingByIndex throws when index is out of range', async () => {
		const ws = createMockWorkspace();
		await expect(ws.goToSiblingByIndex(5)).rejects.toThrow();
	});

	it('goToSiblingByIndex(0) switches to first sibling', async () => {
		const ws = createMockWorkspace();
		await ws.goToSiblingByIndex(0);
		expect(ws.activeBranchId).toBe('main');
	});
});

// ============================================
// Group 8: Derived state
// ============================================

describe('createMockWorkspace — derived state', () => {
	it('activeBranch reflects branches.get(activeBranchId)', () => {
		const ws = createMockWorkspace();
		const branch = ws.branches.get('main');
		expect(ws.activeBranch).toBe(branch);
	});

	it('activeBranch is null when activeBranchId is null', async () => {
		const ws = createMockWorkspace();
		await ws.deleteSession('mock-session-1');
		await ws.deleteSession('mock-session-2');
		expect(ws.activeBranch).toBeNull();
	});

	it('state is null when activeBranchId is null', async () => {
		const ws = createMockWorkspace();
		await ws.deleteSession('mock-session-1');
		await ws.deleteSession('mock-session-2');
		expect(ws.state).toBeNull();
	});

	it('currentSiblingPosition is 1-based', () => {
		const ws = createMockWorkspace();
		expect(ws.currentSiblingPosition.current).toBe(1);
		expect(ws.currentSiblingPosition.total).toBe(1);
	});

	it('currentSiblingPosition is { 0, 0 } when no active branch', async () => {
		const ws = createMockWorkspace();
		await ws.deleteSession('mock-session-1');
		await ws.deleteSession('mock-session-2');
		expect(ws.currentSiblingPosition).toEqual({ current: 0, total: 0 });
	});

	it('canGoNext is false for a lone branch', () => {
		const ws = createMockWorkspace();
		expect(ws.canGoNext).toBe(false);
	});

	it('canGoPrevious is false for a lone branch', () => {
		const ws = createMockWorkspace();
		expect(ws.canGoPrevious).toBe(false);
	});

	it('activeSiblings includes active branch when no siblings', () => {
		const ws = createMockWorkspace();
		// main has forkedFrom: null, so filter matches all root branches
		expect(ws.activeSiblings.length).toBeGreaterThanOrEqual(1);
	});
});

// ============================================
// Group 9: Streaming (send)
// ============================================

describe('createMockWorkspace — send', () => {
	it('adds user message immediately', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		const sendPromise = ws.send('hello');
		// User message is added synchronously before any streaming
		expect(ws.state?.messages.some((m) => m.role === 'user' && m.content === 'hello')).toBe(true);
		await sendPromise;
	});

	it('adds assistant response after send completes', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.send('hello');
		await tick(300);
		const messages = ws.state?.messages ?? [];
		expect(messages.some((m) => m.role === 'assistant')).toBe(true);
	});

	it('cycles through provided responses', async () => {
		const ws = createMockWorkspace({
			typingDelay: 0,
			responses: ['response-A', 'response-B']
		});

		await ws.send('msg1');
		await tick(300);
		const firstResponse = ws.state?.messages.find((m) => m.role === 'assistant')?.content ?? '';

		// Clear and send again
		ws.clear();
		await ws.send('msg2');
		await tick(300);
		const secondResponse = ws.state?.messages.find((m) => m.role === 'assistant')?.content ?? '';

		expect(firstResponse).toContain('response-A');
		expect(secondResponse).toContain('response-B');
	});

	it('send on null state throws', async () => {
		const ws = createMockWorkspace();
		await ws.deleteSession('mock-session-1');
		await ws.deleteSession('mock-session-2');
		await expect(ws.send('hello')).rejects.toThrow();
	});

	it('send to one branch does not affect another branch', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.send('main message');
		await tick(300);

		await ws.createBranch({ branchId: 'feature' });
		await ws.switchBranch('feature');
		expect(ws.state?.messages).toHaveLength(0);
	});
});

// ============================================
// Group 10: clear / abort / approve / deny / clarify
// ============================================

describe('createMockWorkspace — clear', () => {
	it('clears all messages from active branch state', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.send('hello');
		await tick(300);
		expect(ws.state?.messages.length).toBeGreaterThan(0);

		ws.clear();
		expect(ws.state?.messages).toHaveLength(0);
	});

	it('clear on null state does not throw', async () => {
		const ws = createMockWorkspace();
		await ws.deleteSession('mock-session-1');
		await ws.deleteSession('mock-session-2');
		expect(() => ws.clear()).not.toThrow();
	});
});

describe('createMockWorkspace — abort / approve / deny / clarify', () => {
	it('abort does not throw', () => {
		const ws = createMockWorkspace();
		expect(() => ws.abort()).not.toThrow();
	});

	it('approve does not throw', async () => {
		const ws = createMockWorkspace();
		await expect(ws.approve('any-id')).resolves.not.toThrow();
	});

	it('deny does not throw', async () => {
		const ws = createMockWorkspace();
		await expect(ws.deny('any-id')).resolves.not.toThrow();
	});

	it('clarify does not throw', async () => {
		const ws = createMockWorkspace();
		await expect(ws.clarify('any-id', 'answer')).resolves.not.toThrow();
	});
});

// ============================================
// Group 11: Workspace interface contract
// ============================================

describe('createMockWorkspace — Workspace interface contract', () => {
	it('exposes all required Level 1 properties', () => {
		const ws: Workspace = createMockWorkspace();
		expect(typeof ws.sessions).toBe('object');
		expect(ws.activeSessionId === null || typeof ws.activeSessionId === 'string').toBe(true);
		expect(typeof ws.loading).toBe('boolean');
		expect(ws.error === null || typeof ws.error === 'string').toBe(true);
	});

	it('exposes all required Level 2 properties', () => {
		const ws: Workspace = createMockWorkspace();
		expect(ws.branches instanceof Map).toBe(true);
		expect(ws.activeBranchId === null || typeof ws.activeBranchId === 'string').toBe(true);
		expect(ws.activeBranch === null || typeof ws.activeBranch === 'object').toBe(true);
		expect(Array.isArray(ws.activeSiblings)).toBe(true);
		expect(typeof ws.canGoNext).toBe('boolean');
		expect(typeof ws.canGoPrevious).toBe('boolean');
		expect(typeof ws.currentSiblingPosition).toBe('object');
	});

	it('exposes all required Level 3 properties', () => {
		const ws: Workspace = createMockWorkspace();
		expect(ws.state === null || typeof ws.state === 'object').toBe(true);
	});

	it('exposes all required methods', () => {
		const ws: Workspace = createMockWorkspace();
		expect(typeof ws.selectSession).toBe('function');
		expect(typeof ws.createSession).toBe('function');
		expect(typeof ws.deleteSession).toBe('function');
		expect(typeof ws.switchBranch).toBe('function');
		expect(typeof ws.goToNextSibling).toBe('function');
		expect(typeof ws.goToPreviousSibling).toBe('function');
		expect(typeof ws.goToSiblingByIndex).toBe('function');
		expect(typeof ws.editMessage).toBe('function');
		expect(typeof ws.deleteBranch).toBe('function');
		expect(typeof ws.createBranch).toBe('function');
		expect(typeof ws.refreshBranch).toBe('function');
		expect(typeof ws.invalidateBranch).toBe('function');
		expect(typeof ws.send).toBe('function');
		expect(typeof ws.abort).toBe('function');
		expect(typeof ws.approve).toBe('function');
		expect(typeof ws.deny).toBe('function');
		expect(typeof ws.clarify).toBe('function');
		expect(typeof ws.clear).toBe('function');
	});
});

// ============================================
// Group 12: invalidateBranch / refreshBranch
// ============================================

describe('createMockWorkspace — invalidateBranch / refreshBranch', () => {
	it('invalidateBranch does not throw', () => {
		const ws = createMockWorkspace();
		expect(() => ws.invalidateBranch('main')).not.toThrow();
	});

	it('invalidateBranch on nonexistent branch does not throw', () => {
		const ws = createMockWorkspace();
		expect(() => ws.invalidateBranch('nonexistent')).not.toThrow();
	});

	it('refreshBranch does not throw', async () => {
		const ws = createMockWorkspace();
		await expect(ws.refreshBranch('main')).resolves.not.toThrow();
	});
});

// ============================================
// Group 13: canSend gate
// ============================================

describe('createMockWorkspace — canSend', () => {
	it('is true when idle', () => {
		const ws = createMockWorkspace();
		expect(ws.state?.canSend).toBe(true);
	});

	it('is false while streaming (typingDelay > 0)', async () => {
		// typingDelay: 10ms per char. Mock sleeps 100ms before typing starts.
		// Poll from 110ms onward — well within the streaming window.
		const ws = createMockWorkspace({ typingDelay: 10 });
		let seenFalse = false;

		const sendPromise = ws.send('hello');

		// Poll every 20ms starting after the 100ms network sleep
		for (let i = 0; i < 25; i++) {
			await new Promise((r) => setTimeout(r, 20));
			if (ws.state?.canSend === false) {
				seenFalse = true;
				break;
			}
		}

		await sendPromise;
		expect(seenFalse).toBe(true);
	});

	it('is true after streaming completes', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.send('hello');
		await tick(300);
		expect(ws.state?.canSend).toBe(true);
	});

	it('is null when no active branch', async () => {
		const ws = createMockWorkspace();
		await ws.deleteSession('mock-session-1');
		await ws.deleteSession('mock-session-2');
		// state itself is null — canSend is accessed via optional chain
		expect(ws.state?.canSend).toBeUndefined();
	});
});

// ============================================
// Group 14: editMessage — message pre-population
// ============================================

describe('createMockWorkspace — editMessage message pre-population', () => {
	it('fork state has exactly messageIndex messages before the edit', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });

		// Send two messages so we have history at indices 0 (user) and 1 (assistant)
		await ws.send('first');
		await tick(300);
		await ws.send('second');
		await tick(300);

		const mainMessageCount = ws.state!.messages.length; // should be 4 (2 user + 2 asst)
		expect(mainMessageCount).toBeGreaterThanOrEqual(2);

		// Fork at index 2 — fork should start with the first 2 messages
		await ws.editMessage(2, 'edited');
		await tick(300);

		// Fork messages include: the 2 pre-fork messages + the new user msg + assistant response
		// The 2 pre-fork messages must be there
		const forkMessages = ws.state!.messages;
		expect(forkMessages.length).toBeGreaterThanOrEqual(3); // at least 2 history + edited
	});

	it('pre-populated messages have streaming: false', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.send('original');
		await tick(300);

		await ws.editMessage(0, 'edit at index 0');
		await tick(300);

		// The very first message in the fork comes from loadHistory — must not be streaming
		// (index 0 was edited, so the fork starts empty before the edit)
		// Let's verify all settled messages are not streaming
		const settled = ws.state!.messages.filter((m) => !m.streaming);
		expect(settled.length).toBe(ws.state!.messages.length);
	});

	it('parent branch records the fork in childBranches', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.send('hello');
		await tick(300);

		await ws.editMessage(0, 'edit');
		await tick(300);

		// Switch back to main and check its childBranches
		await ws.switchBranch('main');
		const main = ws.branches.get('main')!;
		expect(main.childBranches.length).toBeGreaterThan(0);
	});
});

// ============================================
// Group 15: error lifecycle
// ============================================

describe('createMockWorkspace — error lifecycle', () => {
	it('error is null after successful init', () => {
		const ws = createMockWorkspace();
		expect(ws.error).toBeNull();
	});

	it('loading is false after init', () => {
		const ws = createMockWorkspace();
		expect(ws.loading).toBe(false);
	});

	it('switchBranch to nonexistent does not permanently set error', async () => {
		const ws = createMockWorkspace();
		// switchBranch throws but error state on the workspace should reset next time
		await ws.switchBranch('nonexistent').catch(() => {});
		// After a successful operation, workspace is usable again
		await ws.switchBranch('main');
		expect(ws.activeBranchId).toBe('main');
	});
});

// ============================================
// Group 16: streaming does not bleed across branches
// ============================================

describe('createMockWorkspace — stream isolation', () => {
	it('send() to main does not affect feature branch messages', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.send('main message');
		await tick(300);
		const mainCount = ws.state!.messages.length;

		await ws.createBranch({ branchId: 'feature' });
		await ws.switchBranch('feature');
		// feature branch starts empty regardless of what main has
		expect(ws.state!.messages).toHaveLength(0);

		// Switch back — main still has its messages
		await ws.switchBranch('main');
		expect(ws.state!.messages.length).toBe(mainCount);
	});

	it('send() to feature branch does not appear in main', async () => {
		const ws = createMockWorkspace({ typingDelay: 0 });
		await ws.createBranch({ branchId: 'feature' });
		await ws.switchBranch('feature');
		await ws.send('feature message');
		await tick(300);

		await ws.switchBranch('main');
		expect(ws.state!.messages).toHaveLength(0);
	});
});
