import type { Snippet } from 'svelte';
import type { ContentReference } from '@hpd/hpd-agent-client';
export type { ContentReference };
import type { FileAttachmentState } from './file-attachment.svelte.js';

// ============================================
// Supporting types
// ============================================

export type AttachmentStatus = 'uploading' | 'done' | 'error';

export interface PendingAttachment {
	localId: string;
	file: File;
	status: AttachmentStatus;
	content?: ContentReference;
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
	/** Pre-constructed state (preferred when resolvedContent is needed outside snippet) */
	state?: FileAttachmentState;
	/** AgentClient — used when state is not provided */
	client?: { uploadContent(sessionId: string, branchId: string, file: File | Blob, name?: string): Promise<ContentReference> };
	/** Active session ID — used when state is not provided */
	sessionId?: string | null;
	/** Active branch ID — used when state is not provided */
	branchId?: string | null;
	disabled?: boolean;
	child?: Snippet<[FileAttachmentSnippetProps & { props: FileAttachmentHTMLProps }]>;
	children?: Snippet<[FileAttachmentSnippetProps]>;
	[key: string]: unknown;
}
