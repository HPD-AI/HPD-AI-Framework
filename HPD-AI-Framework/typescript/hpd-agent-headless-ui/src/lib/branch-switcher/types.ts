/**
 * BranchSwitcher Types
 *
 * Type definitions for the BranchSwitcher compound component.
 */

import type { Snippet } from 'svelte';
import type { Branch } from '@hpd/hpd-agent-client';

// ============================================
// Root Component Types
// ============================================

export interface BranchSwitcherRootHTMLProps {
	'data-branch-switcher-root': '';
	'data-has-siblings'?: '';
	class?: string | undefined;
	[key: string]: unknown;
}

export interface BranchSwitcherRootSnippetProps {
	branch: Branch | null;
	hasSiblings: boolean;
	canGoPrevious: boolean;
	canGoNext: boolean;
	position: string;
	label: string;
	isOriginal: boolean;
}

export interface BranchSwitcherRootProps {
	branch: Branch | null;
	onNavigate?: (branchId: string) => void | Promise<void>;
	child?: Snippet<[BranchSwitcherRootSnippetProps & { props: BranchSwitcherRootHTMLProps }]>;
	children?: Snippet<[BranchSwitcherRootSnippetProps]>;
	[key: string]: unknown;
}

// ============================================
// Prev Component Types
// ============================================

export interface BranchSwitcherPrevHTMLProps {
	'data-branch-switcher-prev': '';
	'data-disabled'?: '';
	type: 'button';
	disabled: boolean;
	'aria-label': string;
	class?: string | undefined;
	[key: string]: unknown;
}

export interface BranchSwitcherPrevProps {
	'aria-label'?: string;
	child?: Snippet<[{ props: BranchSwitcherPrevHTMLProps }]>;
	children?: Snippet;
	[key: string]: unknown;
}

// ============================================
// Next Component Types
// ============================================

export interface BranchSwitcherNextHTMLProps {
	'data-branch-switcher-next': '';
	'data-disabled'?: '';
	type: 'button';
	disabled: boolean;
	'aria-label': string;
	class?: string | undefined;
	[key: string]: unknown;
}

export interface BranchSwitcherNextProps {
	'aria-label'?: string;
	child?: Snippet<[{ props: BranchSwitcherNextHTMLProps }]>;
	children?: Snippet;
	[key: string]: unknown;
}

// ============================================
// Position Component Types
// ============================================

export interface BranchSwitcherPositionHTMLProps {
	'data-branch-switcher-position': '';
	'aria-live': 'polite';
	'aria-atomic': 'true';
	class?: string | undefined;
	[key: string]: unknown;
}

export interface BranchSwitcherPositionSnippetProps {
	position: string;
	label: string;
}

export interface BranchSwitcherPositionProps {
	child?: Snippet<[BranchSwitcherPositionSnippetProps & { props: BranchSwitcherPositionHTMLProps }]>;
	children?: Snippet<[BranchSwitcherPositionSnippetProps]>;
	[key: string]: unknown;
}
