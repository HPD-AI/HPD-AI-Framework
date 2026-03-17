/**
 * Artifact Type Definitions
 *
 * Type definitions for the Artifact compositional component.
 * Enables AI-generated content to be rendered in a dedicated side panel.
 */

import type { Snippet } from 'svelte';
import type { HPDPrimitiveDivAttributes, HPDPrimitiveButtonAttributes } from '$lib/shared/types.js';
import type { OnChangeFn, WithChild } from '$lib/internal/types.js';
import type { Without } from 'svelte-toolbelt';

// ============================================
// Slot Data (stored in registry)
// ============================================

export interface ArtifactSlotData {
	id: string;
	title: Snippet | null;
	content: Snippet | null;
}

// ============================================
// Provider Component Props
// ============================================

export type ArtifactProviderPropsWithoutHTML = WithChild<{
	/**
	 * Callback when any artifact opens/closes
	 */
	onOpenChange?: (open: boolean, id: string | null) => void;

	/**
	 * Rest props support
	 */
	[key: string]: unknown;
}>;

export type ArtifactProviderProps = ArtifactProviderPropsWithoutHTML &
	Without<HPDPrimitiveDivAttributes, ArtifactProviderPropsWithoutHTML>;

// ============================================
// Root Component Props
// ============================================

export type ArtifactRootSnippetProps = {
	/**
	 * Whether this artifact is currently open
	 */
	open: boolean;

	/**
	 * Function to open/close this artifact
	 */
	setOpen: (open: boolean) => void;

	/**
	 * Toggle open state
	 */
	toggle: () => void;
};

export type ArtifactRootPropsWithoutHTML = WithChild<
	{
		/**
		 * Unique identifier for this artifact
		 */
		id: string;

		/**
		 * Initial open state
		 * @default false
		 */
		defaultOpen?: boolean;

		/**
		 * Callback when this artifact opens/closes
		 */
		onOpenChange?: OnChangeFn<boolean>;

		/**
		 * Rest props support
		 */
		[key: string]: unknown;
	},
	ArtifactRootSnippetProps
>;

export type ArtifactRootProps = ArtifactRootPropsWithoutHTML &
	Without<HPDPrimitiveDivAttributes, ArtifactRootPropsWithoutHTML>;

// ============================================
// Slot Component Props
// ============================================

export type ArtifactSlotPropsWithoutHTML = {
	/**
	 * Snippet for the title area
	 */
	title?: Snippet;

	/**
	 * Snippet for the main content area
	 */
	content?: Snippet;
};

export type ArtifactSlotProps = ArtifactSlotPropsWithoutHTML;

// ============================================
// Trigger Component Props
// ============================================

export type ArtifactTriggerSnippetProps = {
	/**
	 * Whether the artifact is open
	 */
	open: boolean;
};

export type ArtifactTriggerPropsWithoutHTML = WithChild<
	{
		/**
		 * Override disabled state
		 */
		disabled?: boolean;

		/**
		 * Rest props support
		 */
		[key: string]: unknown;
	},
	ArtifactTriggerSnippetProps
>;

export type ArtifactTriggerProps = ArtifactTriggerPropsWithoutHTML &
	Without<HPDPrimitiveButtonAttributes, ArtifactTriggerPropsWithoutHTML>;

// ============================================
// Panel Component Props
// ============================================

export type ArtifactPanelSnippetProps = {
	/**
	 * Whether any artifact is open
	 */
	open: boolean;

	/**
	 * ID of the currently open artifact
	 */
	openId: string | null;

	/**
	 * The title snippet from the open artifact
	 */
	title: Snippet | null;

	/**
	 * The content snippet from the open artifact
	 */
	content: Snippet | null;

	/**
	 * Function to close the current artifact
	 */
	close: () => void;
};

export type ArtifactPanelPropsWithoutHTML = WithChild<{ [key: string]: unknown }, ArtifactPanelSnippetProps>;

export type ArtifactPanelProps = ArtifactPanelPropsWithoutHTML &
	Without<HPDPrimitiveDivAttributes, ArtifactPanelPropsWithoutHTML>;

// ============================================
// Title Component Props (Render Target)
// ============================================

export type ArtifactTitlePropsWithoutHTML = WithChild<{ [key: string]: unknown }>;

export type ArtifactTitleProps = ArtifactTitlePropsWithoutHTML &
	Without<HPDPrimitiveDivAttributes, ArtifactTitlePropsWithoutHTML>;

// ============================================
// Content Component Props (Render Target)
// ============================================

export type ArtifactContentPropsWithoutHTML = WithChild<{ [key: string]: unknown }>;

export type ArtifactContentProps = ArtifactContentPropsWithoutHTML &
	Without<HPDPrimitiveDivAttributes, ArtifactContentPropsWithoutHTML>;

// ============================================
// Close Component Props
// ============================================

export type ArtifactCloseSnippetProps = {
	/**
	 * Whether any artifact is open
	 */
	open: boolean;
};

export type ArtifactClosePropsWithoutHTML = WithChild<
	{
		/**
		 * Override disabled state
		 */
		disabled?: boolean;

		/**
		 * Rest props support
		 */
		[key: string]: unknown;
	},
	ArtifactCloseSnippetProps
>;

export type ArtifactCloseProps = ArtifactClosePropsWithoutHTML &
	Without<HPDPrimitiveButtonAttributes, ArtifactClosePropsWithoutHTML>;

// ============================================
// Component-level flat interfaces (used directly in .svelte $props())
// ============================================

export interface ArtifactProviderComponentProps {
	onOpenChange?: (open: boolean, id: string | null) => void;
	child?: Snippet<[{ props: Record<string, unknown> }]>;
	children?: Snippet;
	ref?: HTMLElement | null;
	[key: string]: unknown;
}

export interface ArtifactRootComponentProps {
	id: string;
	defaultOpen?: boolean;
	onOpenChange?: OnChangeFn<boolean>;
	child?: Snippet<[ArtifactRootSnippetProps & { props: Record<string, unknown> }]>;
	children?: Snippet<[ArtifactRootSnippetProps]>;
	ref?: HTMLElement | null;
	[key: string]: unknown;
}

export interface ArtifactTriggerComponentProps {
	disabled?: boolean;
	child?: Snippet<[ArtifactTriggerSnippetProps & { props: Record<string, unknown> }]>;
	children?: Snippet<[ArtifactTriggerSnippetProps]>;
	ref?: HTMLButtonElement | null;
	[key: string]: unknown;
}

export interface ArtifactPanelComponentProps {
	child?: Snippet<[ArtifactPanelSnippetProps & { props: Record<string, unknown> }]>;
	children?: Snippet<[ArtifactPanelSnippetProps]>;
	ref?: HTMLElement | null;
	[key: string]: unknown;
}

export interface ArtifactTitleComponentProps {
	child?: Snippet<[{ props: Record<string, unknown> }]>;
	children?: Snippet;
	ref?: HTMLElement | null;
	[key: string]: unknown;
}

export interface ArtifactContentComponentProps {
	child?: Snippet<[{ props: Record<string, unknown> }]>;
	children?: Snippet;
	ref?: HTMLElement | null;
	[key: string]: unknown;
}

export interface ArtifactCloseComponentProps {
	disabled?: boolean;
	child?: Snippet<[ArtifactCloseSnippetProps & { props: Record<string, unknown> }]>;
	children?: Snippet<[ArtifactCloseSnippetProps]>;
	ref?: HTMLButtonElement | null;
	[key: string]: unknown;
}
