/**
 * ThreadSwitcher Types
 *
 * Type definitions for the ThreadSwitcher compound component.
 */

import type { Snippet } from 'svelte';
import type { Thread } from '@hpd-research/hpd-agent-client';

// ============================================
// Root Component Types
// ============================================

export interface ThreadSwitcherRootHTMLProps {
	'data-thread-switcher-root': '';
	'data-has-siblings'?: '';
	class?: string | undefined;
	[key: string]: unknown;
}

export interface ThreadSwitcherRootSnippetProps {
	thread: Thread | null;
	hasSiblings: boolean;
	canGoPrevious: boolean;
	canGoNext: boolean;
	position: string;
	label: string;
	isOriginal: boolean;
}

export interface ThreadSwitcherRootProps {
	thread: Thread | null;
	onNavigate?: (threadId: string) => void | Promise<void>;
	child?: Snippet<[ThreadSwitcherRootSnippetProps & { props: ThreadSwitcherRootHTMLProps }]>;
	children?: Snippet<[ThreadSwitcherRootSnippetProps]>;
	[key: string]: unknown;
}

// ============================================
// Prev Component Types
// ============================================

export interface ThreadSwitcherPrevHTMLProps {
	'data-thread-switcher-prev': '';
	'data-disabled'?: '';
	type: 'button';
	disabled: boolean;
	'aria-label': string;
	class?: string | undefined;
	[key: string]: unknown;
}

export interface ThreadSwitcherPrevProps {
	'aria-label'?: string;
	child?: Snippet<[{ props: ThreadSwitcherPrevHTMLProps }]>;
	children?: Snippet;
	[key: string]: unknown;
}

// ============================================
// Next Component Types
// ============================================

export interface ThreadSwitcherNextHTMLProps {
	'data-thread-switcher-next': '';
	'data-disabled'?: '';
	type: 'button';
	disabled: boolean;
	'aria-label': string;
	class?: string | undefined;
	[key: string]: unknown;
}

export interface ThreadSwitcherNextProps {
	'aria-label'?: string;
	child?: Snippet<[{ props: ThreadSwitcherNextHTMLProps }]>;
	children?: Snippet;
	[key: string]: unknown;
}

// ============================================
// Position Component Types
// ============================================

export interface ThreadSwitcherPositionHTMLProps {
	'data-thread-switcher-position': '';
	'aria-live': 'polite';
	'aria-atomic': 'true';
	class?: string | undefined;
	[key: string]: unknown;
}

export interface ThreadSwitcherPositionSnippetProps {
	position: string;
	label: string;
}

export interface ThreadSwitcherPositionProps {
	child?: Snippet<[ThreadSwitcherPositionSnippetProps & { props: ThreadSwitcherPositionHTMLProps }]>;
	children?: Snippet<[ThreadSwitcherPositionSnippetProps]>;
	[key: string]: unknown;
}
