import type { Attachment } from 'svelte/attachments';
import {
  contentReferenceToUriContent,
  type AIContent,
  type ContentReference,
} from '@hpd-research/hpd-agent-client';
import {
  createFileAttachmentElementProps,
  createFileAttachmentSnapshot,
} from './props.js';
import type {
  FileAttachmentActions,
  FileAttachmentApi,
  FileAttachmentElementProps,
  FileAttachmentStateOptions,
  FileAttachmentUpload,
  PendingFileAttachment,
} from './types.js';

export class FileAttachmentState {
  private readonly upload: FileAttachmentUpload;
  private readonly sessionId: string;
  private readonly threadId: string;
  private disabledValue = $state(false);
  private input: HTMLInputElement | null = $state(null);
  private items = $state<PendingFileAttachment[]>([]);

  constructor(options: FileAttachmentStateOptions) {
    this.sessionId = options.sessionId;
    this.threadId = options.threadId;
    this.disabledValue = options.disabled ?? false;
    this.upload = options.upload ?? (async ({ file, sessionId, threadId }) => {
      if (!options.client) {
        throw new Error('FileAttachmentState requires client or upload.');
      }
      return options.client.uploadContent(sessionId, threadId, file, file.name);
    });
  }

  get attachments(): PendingFileAttachment[] {
    return this.items;
  }

  get disabled(): boolean {
    return this.disabledValue;
  }

  set disabled(value: boolean) {
    this.disabledValue = value;
  }

  get hasAttachments(): boolean {
    return this.items.length > 0;
  }

  get isUploading(): boolean {
    return this.items.some((item) => item.status === 'uploading');
  }

  get hasError(): boolean {
    return this.items.some((item) => item.status === 'error');
  }

  get canSubmit(): boolean {
    return !this.disabled && !this.isUploading && !this.hasError;
  }

  get readyContents(): AIContent[] {
    return this.items
      .filter((item): item is PendingFileAttachment & { content: ContentReference } =>
        item.status === 'ready' && item.content !== undefined)
      .map((item) => contentReferenceToUriContent(item.content));
  }

  add = async (files: FileList | File[]): Promise<void> => {
    if (this.disabled) return;
    const existingKeys = new Set(this.items.map((item) => createFileKey(item.file)));
    const entries = Array.from(files)
      .filter((file) => {
        const key = createFileKey(file);
        if (existingKeys.has(key)) return false;
        existingKeys.add(key);
        return true;
      })
      .map((file): PendingFileAttachment => ({
        id: createAttachmentId(),
        file,
        status: 'uploading',
      }));
    if (entries.length === 0) return;

    this.items = [...this.items, ...entries];
    await Promise.all(entries.map((entry) => this.uploadEntry(entry)));
  };

  remove = (id: string): void => {
    this.items = this.items.filter((item) => item.id !== id);
  };

  retry = async (id: string): Promise<void> => {
    const entry = this.items.find((item) => item.id === id);
    if (!entry || entry.status !== 'error') return;
    this.patch(id, { status: 'uploading', error: undefined, content: undefined });
    await this.uploadEntry(entry);
  };

  clear = (): void => {
    this.items = [];
    if (this.input) this.input.value = '';
  };

  open = (): void => {
    if (!this.disabled) this.input?.click();
  };

  inputAttachment: Attachment<HTMLInputElement> = (node) => {
    this.input = node;
    return () => {
      if (this.input === node) this.input = null;
    };
  };

  createApi(props: FileAttachmentElementProps): FileAttachmentApi {
    return {
      actions: this.actions,
      props,
      state: this.snapshot,
    };
  }

  get snapshot(): FileAttachmentApi['state'] {
    return createFileAttachmentSnapshot({
      attachments: this.attachments,
      canSubmit: this.canSubmit,
      disabled: this.disabled,
      inputRef: this.input,
      readyContents: this.readyContents,
    });
  }

  get actions(): FileAttachmentActions {
    return {
      add: this.add,
      clear: this.clear,
      open: this.open,
      remove: this.remove,
      retry: this.retry,
    };
  }

  createElementProps(input: {
    accept?: string;
    multiple: boolean;
    onInputChange: (event: Event) => void;
    rootProps: Record<string, unknown>;
    triggerLabel: string;
  }): FileAttachmentElementProps {
    return createFileAttachmentElementProps({
      ...input,
      actions: this.actions,
      attachments: this.attachments,
      disabled: this.disabled,
      inputAttachment: this.inputAttachment,
    });
  }

  private async uploadEntry(entry: PendingFileAttachment): Promise<void> {
    try {
      const content = await this.upload({
        file: entry.file,
        sessionId: this.sessionId,
        threadId: this.threadId,
      });
      this.patch(entry.id, { status: 'ready', content, error: undefined });
    } catch (error) {
      this.patch(entry.id, {
        status: 'error',
        error: error instanceof Error ? error.message : String(error),
      });
    }
  }

  private patch(id: string, patch: Partial<PendingFileAttachment>): void {
    this.items = this.items.map((item) => item.id === id ? { ...item, ...patch } : item);
  }
}

export function createFileAttachmentState(options: FileAttachmentStateOptions): FileAttachmentState {
  return new FileAttachmentState(options);
}

function createAttachmentId(): string {
  return `attachment:${Date.now()}:${Math.random().toString(36).slice(2)}`;
}

function createFileKey(file: File): string {
  return `${file.name}:${file.type}:${file.size}:${file.lastModified}`;
}
