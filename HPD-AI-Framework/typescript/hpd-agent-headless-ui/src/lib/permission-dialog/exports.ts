/**
 * PermissionDialog Exports
 *
 * Clean public API surface for the PermissionDialog component.
 */

export { default as Root } from './components/permission-dialog.svelte';
export { default as Content } from './components/permission-dialog-content.svelte';
export { default as Overlay } from './components/permission-dialog-overlay.svelte';
export { default as Header } from './components/permission-dialog-header.svelte';
export { default as Description } from './components/permission-dialog-description.svelte';
export { default as Actions } from './components/permission-dialog-actions.svelte';
export { default as Approve } from './components/permission-dialog-approve.svelte';
export { default as Deny } from './components/permission-dialog-deny.svelte';

export type {
	PermissionDialogRootProps as RootProps,
	PermissionDialogContentProps as ContentProps,
	PermissionDialogContentSnippetProps as ContentSnippetProps,
	PermissionDialogOverlayProps as OverlayProps,
	PermissionDialogHeaderProps as HeaderProps,
	PermissionDialogHeaderSnippetProps as HeaderSnippetProps,
	PermissionDialogDescriptionProps as DescriptionProps,
	PermissionDialogDescriptionSnippetProps as DescriptionSnippetProps,
	PermissionDialogActionsProps as ActionsProps,
	PermissionDialogActionsSnippetProps as ActionsSnippetProps,
	PermissionDialogApproveProps as ApproveProps,
	PermissionDialogDenyProps as DenyProps
} from './types.ts';
