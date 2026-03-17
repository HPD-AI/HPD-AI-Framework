/**
 * SessionList State Management
 *
 * Compound headless component for listing and managing agent sessions.
 *
 * Parts: Root, Item, Empty, CreateButton
 *
 * @example
 * ```svelte
 * <SessionList.Root {sessions} {activeSessionId} onSelect={...} onDelete={...} onCreate={...}>
 *   {#snippet children({ isEmpty })}
 *     <SessionList.CreateButton>New Session</SessionList.CreateButton>
 *     {#if isEmpty}
 *       <SessionList.Empty>No sessions yet</SessionList.Empty>
 *     {:else}
 *       {#each sessions as session (session.id)}
 *         <SessionList.Item {session}>
 *           {#snippet children({ isActive, lastActivity })}
 *             <span>{session.id.substring(0, 8)}</span>
 *             <span>{lastActivity}</span>
 *           {/snippet}
 *         </SessionList.Item>
 *       {/each}
 *     {/if}
 *   {/snippet}
 * </SessionList.Root>
 * ```
 */

import { Context } from 'runed';
import { boxWith, type Box, type ReadableBox } from 'svelte-toolbelt';
import { RovingFocusGroup } from '$lib/internal/roving-focus-group.js';
import { kbd } from '$lib/internal/kbd.js';
import { createHPDAttrs } from '$lib/internal/attrs.js';
import type { Orientation } from '$lib/internal/get-directional-keys.js';
import type { Session } from '@hpd/hpd-agent-client';
import type {
	SessionListRootHTMLProps,
	SessionListRootSnippetProps,
	SessionListItemHTMLProps,
	SessionListItemSnippetProps,
	SessionListEmptyHTMLProps,
	SessionListCreateButtonHTMLProps,
} from './types.js';

// ============================================
// Data Attributes
// ============================================

export const sessionListAttrs = createHPDAttrs({
	component: 'session-list',
	parts: ['root', 'item', 'empty', 'create'] as const,
});

// ============================================
// Root Context
// ============================================

const SessionListRootContext = new Context<SessionListRootState>('SessionList.Root');

// ============================================
// Root State
// ============================================

interface SessionListRootStateOpts {
	sessions: ReadableBox<Session[]>;
	activeSessionId: ReadableBox<string | null>;
	loading: ReadableBox<boolean>;
	orientation: ReadableBox<Orientation>;
	loop: ReadableBox<boolean>;
	rootNode: Box<HTMLElement | null>;
	onSelect?: (sessionId: string) => void | Promise<void>;
	onDelete?: (sessionId: string) => void | Promise<void>;
	onCreate?: () => void | Promise<void>;
}

export class SessionListRootState {
	readonly #opts: SessionListRootStateOpts;
	readonly rovingFocusGroup: RovingFocusGroup;

	constructor(opts: SessionListRootStateOpts) {
		this.#opts = opts;

		this.rovingFocusGroup = new RovingFocusGroup({
			rootNode: opts.rootNode,
			candidateAttr: sessionListAttrs.getAttr('item'),
			loop: opts.loop,
			orientation: opts.orientation,
		});
	}

	static create(opts: SessionListRootStateOpts): SessionListRootState {
		return SessionListRootContext.set(new SessionListRootState(opts));
	}

	// ============================================
	// Derived State
	// ============================================

	readonly sessions = $derived.by(() => this.#opts.sessions.current);
	readonly activeSessionId = $derived.by(() => this.#opts.activeSessionId.current);
	readonly loading = $derived.by(() => this.#opts.loading.current);
	readonly isEmpty = $derived.by(() => this.sessions.length === 0 && !this.loading);
	readonly count = $derived.by(() => this.sessions.length);

	// ============================================
	// Props
	// ============================================

	get props(): SessionListRootHTMLProps {
		return {
			'data-session-list-root': '',
			'data-empty': this.isEmpty ? '' : undefined,
			'data-loading': this.loading ? '' : undefined,
			role: 'listbox',
			'aria-label': 'Sessions',
			'aria-orientation': this.#opts.orientation.current,
			'aria-busy': this.loading,
		};
	}

	get snippetProps(): SessionListRootSnippetProps {
		return {
			sessions: this.sessions,
			isEmpty: this.isEmpty,
			count: this.count,
			loading: this.loading,
		};
	}

	// ============================================
	// Methods
	// ============================================

	isActive(sessionId: string): boolean {
		return this.activeSessionId === sessionId;
	}

	async selectSession(sessionId: string): Promise<void> {
		await this.#opts.onSelect?.(sessionId);
	}

	async deleteSession(sessionId: string): Promise<void> {
		await this.#opts.onDelete?.(sessionId);
	}

	async createSession(): Promise<void> {
		await this.#opts.onCreate?.();
	}

	formatDate(dateString: string): string {
		const date = new Date(dateString);
		const now = new Date();
		const diff = now.getTime() - date.getTime();

		if (diff < 60_000) return 'Just now';
		if (diff < 3_600_000) return `${Math.floor(diff / 60_000)}m ago`;
		if (diff < 86_400_000) return `${Math.floor(diff / 3_600_000)}h ago`;
		if (diff < 604_800_000) return `${Math.floor(diff / 86_400_000)}d ago`;
		return date.toLocaleDateString();
	}
}

// ============================================
// Item State
// ============================================

interface SessionListItemStateOpts {
	session: ReadableBox<Session>;
	itemNode: Box<HTMLElement | null>;
}

export class SessionListItemState {
	readonly #opts: SessionListItemStateOpts;
	readonly #root: SessionListRootState;

	constructor(opts: SessionListItemStateOpts, root: SessionListRootState) {
		this.#opts = opts;
		this.#root = root;
	}

	static create(opts: SessionListItemStateOpts): SessionListItemState {
		const root = SessionListRootContext.get();
		return new SessionListItemState(opts, root);
	}

	// ============================================
	// Derived State
	// ============================================

	readonly session = $derived.by(() => this.#opts.session.current);
	readonly isActive = $derived.by(() => this.#root.isActive(this.session.id));

	// ============================================
	// Props
	// ============================================

	get props(): SessionListItemHTMLProps {
		return {
			'data-session-list-item': '',
			'data-active': this.isActive ? '' : undefined,
			'data-session-id': this.session.id,
			role: 'option',
			'aria-selected': this.isActive,
			tabindex: this.#root.rovingFocusGroup.getTabIndex(this.#opts.itemNode.current),
			onclick: (e: MouseEvent) => {
				if (e.button === 0) this.#root.selectSession(this.session.id);
			},
			onkeydown: (e: KeyboardEvent) => {
				this.#root.rovingFocusGroup.handleKeydown(this.#opts.itemNode.current, e);

				if (e.key === kbd.ENTER || e.key === kbd.SPACE) {
					e.preventDefault();
					this.#root.selectSession(this.session.id);
				}

				if (e.key === kbd.DELETE || e.key === kbd.BACKSPACE) {
					e.preventDefault();
					this.#root.deleteSession(this.session.id);
				}
			},
		};
	}

	get snippetProps(): SessionListItemSnippetProps {
		return {
			session: this.session,
			isActive: this.isActive,
			lastActivity: this.#root.formatDate(this.session.lastActivity),
			createdAt: this.#root.formatDate(this.session.createdAt),
		};
	}
}

// ============================================
// Empty State
// ============================================

export class SessionListEmptyState {
	static create(): SessionListEmptyState {
		SessionListRootContext.get(); // validates context exists
		return new SessionListEmptyState();
	}

	get props(): SessionListEmptyHTMLProps {
		return {
			'data-session-list-empty': '',
		};
	}
}

// ============================================
// CreateButton State
// ============================================

export class SessionListCreateButtonState {
	readonly #root: SessionListRootState;

	constructor(root: SessionListRootState) {
		this.#root = root;
	}

	static create(): SessionListCreateButtonState {
		const root = SessionListRootContext.get();
		return new SessionListCreateButtonState(root);
	}

	get props(): SessionListCreateButtonHTMLProps {
		return {
			'data-session-list-create': '',
			type: 'button',
			onclick: () => this.#root.createSession(),
		};
	}
}
