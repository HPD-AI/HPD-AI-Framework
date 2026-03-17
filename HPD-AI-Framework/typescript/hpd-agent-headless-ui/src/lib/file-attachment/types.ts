import type { Snippet } from 'svelte';
import type { AssetReference } from '@hpd/hpd-agent-client';
export type { AssetReference };
import type { FileAttachmentState } from './file-attachment.svelte.js';

// ============================================
// Supporting types
// ============================================

export type AttachmentStatus = 'uploading' | 'done' | 'error';

export interface PendingAttachment {
	localId: string;
	file: File;
	status: AttachmentStatus;
	asset?: AssetReference;
	error?: string;
}

// ============================================
// FileAttachment Component Types
// ============================================

export interface FileAttachmentHTMLProps {
	'data-file-attachment-root': '';
	'data-disabled'?: '';
	'data-uploading'?: '';
	class?: string | undefined;
	[key: string]: unknown;
}

export interface FileAttachmentSnippetProps {
	attachments: PendingAttachment[];
	hasAttachments: boolean;
	isUploading: boolean;
	canSubmit: boolean;
	add: (files: FileList | File[]) => Promise<void>;
	remove: (localId: string) => void;
	retry: (localId: string) => Promise<void>;
	clear: () => void;
}

export interface FileAttachmentProps {
	/** Pre-constructed state (preferred when resolvedAssets is needed outside snippet) */
	state?: FileAttachmentState;
	/** AgentClient — used when state is not provided */
	client?: { uploadAsset(sessionId: string, file: File | Blob, name?: string): Promise<AssetReference> };
	/** Active session ID — used when state is not provided */
	sessionId?: string | null;
	disabled?: boolean;
	child?: Snippet<[FileAttachmentSnippetProps & { props: FileAttachmentHTMLProps }]>;
	children?: Snippet<[FileAttachmentSnippetProps]>;
	[key: string]: unknown;
}
