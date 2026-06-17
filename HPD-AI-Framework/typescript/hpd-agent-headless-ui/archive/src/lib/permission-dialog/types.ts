/**
 * PermissionDialog Types
 *
 * Type definitions for the PermissionDialog component.
 */

import type { Workspace } from '$lib/workspace/types.js';
import type { PermissionChoice, PermissionRequest } from '$lib/agent/types.js';
import type { OnChangeFn, WithChild } from '$lib/internal/types.js';
import type { HPDPrimitiveDivAttributes } from '$lib/shared/types.js';
import type { Without } from 'svelte-toolbelt';

// ============================================
// Permission Dialog Status
// ============================================

/**
 * Permission dialog execution status
 * - "inProgress": Waiting for permission request to arrive
 * - "executing": Permission request active, waiting for user response
 * - "complete": User has responded, processing response
 */
export type PermissionDialogStatus = 'inProgress' | 'executing' | 'complete';

// ============================================
// Render Function Props
// ============================================

/**
 * Props passed to the render function  
 */
export type PermissionDialogRenderProps = {
	/**
	 * Current permission request (null if no pending request)
	 */
	request: PermissionRequest | null;

	/**
	 * Current execution status
	 */
	status: PermissionDialogStatus;

	/**
	 * Approve the permission with a choice
	 */
	approve: (choice?: PermissionChoice) => Promise<void>;

	/**
	 * Deny the permission with an optional reason
	 */
	deny: (reason?: string) => Promise<void>;
};

/**
 * Render snippet type for custom UI
 * This is a Svelte snippet that receives PermissionDialogRenderProps
 */
export type PermissionDialogRenderFunction = import('svelte').Snippet<
	[PermissionDialogRenderProps]
>;

// ============================================
// Root Component Props
// ============================================

export type PermissionDialogRootPropsWithoutHTML = {
	/**
	 * The agent instance to watch for permission requests
	 */
	agent: Workspace;

	/**
	 * Callback when dialog open state changes (after animation completes)
	 */
	onOpenChangeComplete?: OnChangeFn<boolean>;

	/**
	 * Optional render function for complete UI customization  
	 * When provided, overrides default compound component rendering
	 */
	render?: PermissionDialogRenderFunction;

	/**
	 * Children components (compound component pattern)
	 * Used when render function is not provided
	 */
	children?: import('svelte').Snippet;
};

export type PermissionDialogRootProps = PermissionDialogRootPropsWithoutHTML &
	Without<HPDPrimitiveDivAttributes, PermissionDialogRootPropsWithoutHTML>;

// ============================================
// Content Component Props
// ============================================

export type PermissionDialogContentSnippetProps = {
	/**
	 * The current permission request (if any)
	 */
	request: PermissionRequest | null;

	/**
	 * Current execution status
	 */
	status: PermissionDialogStatus;

	/**
	 * Whether the dialog is open
	 */
	isOpen: boolean;

	/**
	 * Approve the permission with a choice
	 */
	approve: (choice?: PermissionChoice) => Promise<void>;

	/**
	 * Deny the permission with an optional reason
	 */
	deny: (reason?: string) => Promise<void>;
};

export type PermissionDialogContentPropsWithoutHTML = WithChild<
	{
		/**
		 * Force mount the content (for animation testing)
		 */
		forceMount?: boolean;

		/**
		 * Callback when escape key is pressed
		 */
		onEscapeKeydown?: (event: KeyboardEvent) => void;

		/**
		 * Callback when user clicks outside the dialog
		 */
		onInteractOutside?: (event: MouseEvent | TouchEvent) => void;

		/**
		 * Callback when focus moves into the dialog
		 */
		onOpenAutoFocus?: (event: Event) => void;

		/**
		 * Callback when focus moves out of the dialog
		 */
		onCloseAutoFocus?: (event: Event) => void;
	},
	PermissionDialogContentSnippetProps
>;

export type PermissionDialogContentProps = PermissionDialogContentPropsWithoutHTML &
	Without<HPDPrimitiveDivAttributes, PermissionDialogContentPropsWithoutHTML>;

// ============================================
// Overlay Component Props
// ============================================

export type PermissionDialogOverlayPropsWithoutHTML = {
	/**
	 * Force mount the overlay (for animation testing)
	 */
	forceMount?: boolean;

	/**
	 * Component ref
	 */
	ref?: HTMLElement | null;

	/**
	 * Child snippet (full override)
	 */
	child?: import('svelte').Snippet<[{ props: Record<string, unknown> }]>;

	/**
	 * Children snippet
	 */
	children?: import('svelte').Snippet;
};

export type PermissionDialogOverlayProps = PermissionDialogOverlayPropsWithoutHTML &
	Without<HPDPrimitiveDivAttributes, PermissionDialogOverlayPropsWithoutHTML>;

// ============================================
// Header Component Props
// ============================================

export type PermissionDialogHeaderSnippetProps = {
	/**
	 * The function name requesting permission
	 */
	functionName: string | undefined;

	/**
	 * The source name (tool group) requesting permission
	 */
	sourceName: string | undefined;
};

export type PermissionDialogHeaderPropsWithoutHTML = WithChild<
	{
		/**
		 * Heading level (default: 2)
		 */
		level?: 1 | 2 | 3 | 4 | 5 | 6;
	},
	PermissionDialogHeaderSnippetProps
>;

export type PermissionDialogHeaderProps = PermissionDialogHeaderPropsWithoutHTML &
	Without<HPDPrimitiveDivAttributes, PermissionDialogHeaderPropsWithoutHTML>;

// ============================================
// Description Component Props
// ============================================

export type PermissionDialogDescriptionSnippetProps = {
	/**
	 * The tool description (if provided)
	 */
	description: string | undefined;

	/**
	 * The tool arguments (if provided)
	 */
	arguments: Record<string, unknown> | undefined;

	/**
	 * The function name
	 */
	functionName: string | undefined;

	/**
	 * Current execution status
	 */
	status: PermissionDialogStatus;
};

export type PermissionDialogDescriptionPropsWithoutHTML = {
	/**
	 * Component ref
	 */
	ref?: HTMLElement | null;

	/**
	 * Child snippet (full override)
	 */
	child?: import('svelte').Snippet<
		[PermissionDialogDescriptionSnippetProps & { props: Record<string, unknown> }]
	>;

	/**
	 * Children snippet
	 */
	children?: import('svelte').Snippet<[PermissionDialogDescriptionSnippetProps]>;
};

export type PermissionDialogDescriptionProps = PermissionDialogDescriptionPropsWithoutHTML &
	Without<HPDPrimitiveDivAttributes, PermissionDialogDescriptionPropsWithoutHTML>;

// ============================================
// Actions Component Props
// ============================================

export type PermissionDialogActionsSnippetProps = {
	/**
	 * Approve with a specific choice
	 */
	approve: (choice?: PermissionChoice) => Promise<void>;

	/**
	 * Deny with an optional reason
	 */
	deny: (reason?: string) => Promise<void>;
};

export type PermissionDialogActionsPropsWithoutHTML = {
	/**
	 * Component ref
	 */
	ref?: HTMLElement | null;

	/**
	 * Child snippet (full override)
	 */
	child?: import('svelte').Snippet<
		[PermissionDialogActionsSnippetProps & { props: Record<string, unknown> }]
	>;

	/**
	 * Children snippet
	 */
	children?: import('svelte').Snippet<[PermissionDialogActionsSnippetProps]>;
};

export type PermissionDialogActionsProps = PermissionDialogActionsPropsWithoutHTML &
	Without<HPDPrimitiveDivAttributes, PermissionDialogActionsPropsWithoutHTML>;

// ============================================
// Approve Button Component Props
// ============================================

export type PermissionDialogApprovePropsWithoutHTML = {
	/**
	 * The permission choice to send when approved
	 */
	choice?: PermissionChoice;

	/**
	 * Whether the button is disabled
	 */
	disabled?: boolean;

	/**
	 * Component ref
	 */
	ref?: HTMLButtonElement | null;

	/**
	 * Child snippet (full override)
	 */
	child?: import('svelte').Snippet<[{ props: Record<string, unknown> }]>;

	/**
	 * Children snippet
	 */
	children?: import('svelte').Snippet;
};

export type PermissionDialogApproveProps = PermissionDialogApprovePropsWithoutHTML &
	Without<import('$lib/shared/types.js').HPDPrimitiveButtonAttributes, PermissionDialogApprovePropsWithoutHTML>;

// ============================================
// Deny Button Component Props
// ============================================

export type PermissionDialogDenyPropsWithoutHTML = {
	/**
	 * The reason to send when denied
	 */
	reason?: string;

	/**
	 * Whether the button is disabled
	 */
	disabled?: boolean;

	/**
	 * Component ref
	 */
	ref?: HTMLButtonElement | null;

	/**
	 * Child snippet (full override)
	 */
	child?: import('svelte').Snippet<[{ props: Record<string, unknown> }]>;

	/**
	 * Children snippet
	 */
	children?: import('svelte').Snippet;
};

export type PermissionDialogDenyProps = PermissionDialogDenyPropsWithoutHTML &
	Without<import('$lib/shared/types.js').HPDPrimitiveButtonAttributes, PermissionDialogDenyPropsWithoutHTML>;
