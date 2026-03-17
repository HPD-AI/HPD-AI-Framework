/**
 * SessionList Types
 *
 * Type definitions for the SessionList compound component.
 */

import type { Snippet } from 'svelte';
import type { Session } from '@hpd/hpd-agent-client';

// ============================================
// Root Component Types
// ============================================

export interface SessionListRootHTMLProps {
	'data-session-list-root': '';
	'data-empty'?: '';
	'data-loading'?: '';
	role: 'listbox';
	'aria-label': string;
	'aria-orientation': 'vertical' | 'horizontal';
	'aria-busy': boolean;
	class?: string | undefined;
	[key: string]: unknown;
}

export interface SessionListRootSnippetProps {
	sessions: Session[];
	isEmpty: boolean;
	count: number;
	loading: boolean;
}

export interface SessionListRootProps {
	sessions: Session[];
	activeSessionId?: string | null;
	loading?: boolean;
	orientation?: 'vertical' | 'horizontal';
	loop?: boolean;
	'aria-label'?: string;
	onSelect?: (sessionId: string) => void | Promise<void>;
	onDelete?: (sessionId: string) => void | Promise<void>;
	onCreate?: () => void | Promise<void>;
	child?: Snippet<[SessionListRootSnippetProps & { props: SessionListRootHTMLProps }]>;
	children?: Snippet<[SessionListRootSnippetProps]>;
	[key: string]: unknown;
}

// ============================================
// Item Component Types
// ============================================

export interface SessionListItemHTMLProps {
	'data-session-list-item': '';
	'data-active'?: '';
	'data-session-id': string;
	role: 'option';
	'aria-selected': boolean;
	tabindex: 0 | -1;
	class?: string | undefined;
	[key: string]: unknown;
}

export interface SessionListItemSnippetProps {
	session: Session;
	isActive: boolean;
	lastActivity: string;
	createdAt: string;
}

export interface SessionListItemProps {
	session: Session;
	child?: Snippet<[SessionListItemSnippetProps & { props: SessionListItemHTMLProps }]>;
	children?: Snippet<[SessionListItemSnippetProps]>;
	[key: string]: unknown;
}

// ============================================
// Empty Component Types
// ============================================

export interface SessionListEmptyHTMLProps {
	'data-session-list-empty': '';
	class?: string | undefined;
	[key: string]: unknown;
}

export interface SessionListEmptyProps {
	child?: Snippet<[{ props: SessionListEmptyHTMLProps }]>;
	children?: Snippet;
	[key: string]: unknown;
}

// ============================================
// CreateButton Component Types
// ============================================

export interface SessionListCreateButtonHTMLProps {
	'data-session-list-create': '';
	type: 'button';
	class?: string | undefined;
	[key: string]: unknown;
}

export interface SessionListCreateButtonProps {
	child?: Snippet<[{ props: SessionListCreateButtonHTMLProps }]>;
	children?: Snippet;
	[key: string]: unknown;
}
