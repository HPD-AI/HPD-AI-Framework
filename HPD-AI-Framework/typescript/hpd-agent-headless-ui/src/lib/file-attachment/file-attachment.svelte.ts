import { type ReadableBox } from 'svelte-toolbelt';
import { boolToEmptyStrOrUndef } from '$lib/internal/attrs.js';
import { createId } from '$lib/internal/create-id.js';
import type { ContentReference } from '@hpd/hpd-agent-client';
export type { ContentReference };
import type {
	FileAttachmentHTMLProps,
	FileAttachmentSnippetProps,
	PendingAttachment,
	AttachmentStatus,
} from './types.js';

export type { PendingAttachment, AttachmentStatus };

// ============================================
// FileAttachmentState
// ============================================

interface FileAttachmentStateOpts {
	uploadFn: ReadableBox<(sessionId: string, branchId: string, file: File) => Promise<ContentReference>>;
	sessionId: ReadableBox<string | null>;
	branchId: ReadableBox<string | null>;
	disabled: ReadableBox<boolean>;
}

export class FileAttachmentState {
	readonly #opts: FileAttachmentStateOpts;
	#attachments = $state<PendingAttachment[]>([]);

	constructor(opts: FileAttachmentStateOpts) {
		this.#opts = opts;
	}

	get attachments() { return this.#attachments; }
	get hasAttachments() { return this.#attachments.length > 0; }
	get disabled() { return this.#opts.disabled.current; }
	get isUploading() {
		return this.#attachments.some((a) => a.status === 'uploading');
	}
	get resolvedContent(): ContentReference[] {
		return this.#attachments
			.filter((a) => a.status === 'done')
			.map((a) => a.content!);
	}
	get canSubmit() {
		return (
			!this.disabled &&
			!this.isUploading &&
			this.#attachments.every((a) => a.status !== 'error')
		);
	}

	async add(files: FileList | File[]): Promise<void> {
		const sessionId = this.#opts.sessionId.current;
		const branchId = this.#opts.branchId.current;
		if (!sessionId || !branchId) return;
		const upload = this.#opts.uploadFn.current;
		const list = Array.from(files);
		const entries: PendingAttachment[] = list.map((file) => ({
			localId: createId(),
			file,
			status: 'uploading',
		}));
		this.#attachments = [...this.#attachments, ...entries];
		await Promise.all(
			entries.map(async (entry) => {
				try {
					const content = await upload(sessionId, branchId, entry.file);
					this.#patch(entry.localId, { status: 'done', content });
				} catch (err) {
					this.#patch(entry.localId, {
						status: 'error',
						error: err instanceof Error ? err.message : String(err),
					});
				}
			})
		);
	}

	remove(localId: string) {
		this.#attachments = this.#attachments.filter((a) => a.localId !== localId);
	}

	async retry(localId: string) {
		const entry = this.#attachments.find((a) => a.localId === localId);
		if (!entry || entry.status !== 'error') return;
		this.#patch(localId, { status: 'uploading', error: undefined, content: undefined });
		const sessionId = this.#opts.sessionId.current;
		const branchId = this.#opts.branchId.current;
		if (!sessionId || !branchId) return;
		try {
			const content = await this.#opts.uploadFn.current(sessionId, branchId, entry.file);
			this.#patch(localId, { status: 'done', content });
		} catch (err) {
			this.#patch(localId, {
				status: 'error',
				error: err instanceof Error ? err.message : String(err),
			});
		}
	}

	clear() { this.#attachments = []; }

	#patch(localId: string, patch: Partial<PendingAttachment>) {
		this.#attachments = this.#attachments.map((a) =>
			a.localId === localId ? { ...a, ...patch } : a
		);
	}

	get props(): FileAttachmentHTMLProps {
		return {
			'data-file-attachment-root': '',
			'data-disabled': boolToEmptyStrOrUndef(this.disabled),
			'data-uploading': boolToEmptyStrOrUndef(this.isUploading),
		};
	}

	get snippetProps(): FileAttachmentSnippetProps {
		return {
			attachments: this.attachments,
			hasAttachments: this.hasAttachments,
			isUploading: this.isUploading,
			canSubmit: this.canSubmit,
			add: this.add.bind(this),
			remove: this.remove.bind(this),
			retry: this.retry.bind(this),
			clear: this.clear.bind(this),
		};
	}
}
