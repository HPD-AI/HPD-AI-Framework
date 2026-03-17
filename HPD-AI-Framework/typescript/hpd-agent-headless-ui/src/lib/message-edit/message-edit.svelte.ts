/**
 * MessageEdit State Management
 *
 * Compound headless component for inline message editing.
 *
 * MessageEdit.Root wraps a message bubble and manages the toggle between
 * display mode and edit mode. It supports both controlled and uncontrolled
 * editing state:
 *
 * - **Uncontrolled** (default): omit the `editing` prop — Root manages the
 *   open/closed state internally. `startEdit()` enters edit mode,
 *   saving/cancelling exits it.
 *
 * - **Controlled**: pass `editing={editingIndex === i}` plus `onStartEdit`,
 *   `onSave`, and `onCancel` callbacks. Root reads `editing` reactively and
 *   delegates state changes to the callbacks.
 *
 * Submitting calls `workspace.editMessage(messageIndex, draft)`, which forks
 * the branch at that index and re-runs the model.
 *
 * Parts: Root, Textarea, SaveButton, CancelButton
 *
 * @example Uncontrolled
 * ```svelte
 * <MessageEdit.Root {workspace} messageIndex={i} initialValue={message.content}>
 *   {#snippet children({ editing, startEdit, draft, pending, save, cancel })}
 *     {#if editing}
 *       <MessageEdit.Textarea />
 *       <MessageEdit.SaveButton>Save & Send</MessageEdit.SaveButton>
 *       <MessageEdit.CancelButton>Cancel</MessageEdit.CancelButton>
 *     {:else}
 *       <p>{message.content}</p>
 *       <button onclick={startEdit}>Edit</button>
 *     {/if}
 *   {/snippet}
 * </MessageEdit.Root>
 * ```
 *
 * @example Controlled
 * ```svelte
 * <MessageEdit.Root
 *   {workspace}
 *   messageIndex={i}
 *   initialValue={message.content}
 *   editing={editingIndex === i}
 *   onStartEdit={() => (editingIndex = i)}
 *   onSave={() => (editingIndex = null)}
 *   onCancel={() => (editingIndex = null)}
 * >
 *   {#snippet children({ editing, startEdit })}
 *     {#if editing}
 *       <MessageEdit.Textarea />
 *       <MessageEdit.SaveButton>Save & Send</MessageEdit.SaveButton>
 *       <MessageEdit.CancelButton>Cancel</MessageEdit.CancelButton>
 *     {:else}
 *       <p>{message.content}</p>
 *       <button onclick={startEdit}>Edit</button>
 *     {/if}
 *   {/snippet}
 * </MessageEdit.Root>
 * ```
 */

import { Context } from 'runed';
import { type ReadableBox } from 'svelte-toolbelt';
import { createHPDAttrs, boolToEmptyStrOrUndef } from '$lib/internal/attrs.js';
import { kbd } from '$lib/internal/kbd.js';
import type { Workspace } from '../workspace/types.ts';
import type {
	MessageEditRootHTMLProps,
	MessageEditRootSnippetProps,
	MessageEditTextareaHTMLProps,
	MessageEditTextareaSnippetProps,
	MessageEditSaveButtonHTMLProps,
	MessageEditSaveButtonSnippetProps,
	MessageEditCancelButtonHTMLProps,
	MessageEditCancelButtonSnippetProps,
} from './types.js';

// ============================================
// Data Attributes
// ============================================

export const messageEditAttrs = createHPDAttrs({
	component: 'message-edit',
	parts: ['root', 'textarea', 'save', 'cancel'] as const,
});

// ============================================
// Root Context
// ============================================

const MessageEditRootContext = new Context<MessageEditRootState>('MessageEdit.Root');

// ============================================
// Root State
// ============================================

interface MessageEditRootStateOpts {
	workspace: ReadableBox<Workspace>;
	messageIndex: ReadableBox<number>;
	initialValue: ReadableBox<string>;
	/** Controlled editing state — omit for uncontrolled */
	editing: ReadableBox<boolean | undefined>;
	onStartEdit: ReadableBox<(() => void) | undefined>;
	onSave: ReadableBox<(() => void) | undefined>;
	onCancel: ReadableBox<(() => void) | undefined>;
	onError: ReadableBox<((err: unknown) => void) | undefined>;
}

export class MessageEditRootState {
	readonly #opts: MessageEditRootStateOpts;

	draft = $state('');
	#pending = $state(false);
	#internalEditing = $state(false);

	constructor(opts: MessageEditRootStateOpts) {
		this.#opts = opts;
	}

	static create(opts: MessageEditRootStateOpts): MessageEditRootState {
		return MessageEditRootContext.set(new MessageEditRootState(opts));
	}

	// ============================================
	// Derived
	// ============================================

	readonly workspace = $derived.by(() => this.#opts.workspace.current);
	readonly messageIndex = $derived.by(() => this.#opts.messageIndex.current);
	readonly pending = $derived.by(() => this.#pending);
	readonly isEmpty = $derived.by(() => this.draft.trim() === '');
	readonly canSave = $derived.by(() => !this.#pending && !this.isEmpty);

	/** Whether controlled mode is active (editing prop was explicitly provided) */
	readonly #isControlled = $derived.by(() => this.#opts.editing.current !== undefined);

	/** Current editing state — controlled or internal */
	readonly editing = $derived.by(() =>
		this.#isControlled ? (this.#opts.editing.current ?? false) : this.#internalEditing
	);

	// ============================================
	// Actions
	// ============================================

	readonly startEdit = (): void => {
		// Always reset draft to latest initialValue when entering edit mode
		this.draft = this.#opts.initialValue.current;
		if (this.#isControlled) {
			this.#opts.onStartEdit.current?.();
		} else {
			this.#internalEditing = true;
		}
	};

	readonly save = async (): Promise<void> => {
		if (!this.canSave) return;
		this.#pending = true;
		try {
			await this.workspace.editMessage(this.messageIndex, this.draft.trim());
			if (!this.#isControlled) {
				this.#internalEditing = false;
			}
			this.#opts.onSave.current?.();
		} catch (err) {
			this.#opts.onError.current?.(err);
		} finally {
			this.#pending = false;
		}
	};

	readonly cancel = (): void => {
		if (!this.#isControlled) {
			this.#internalEditing = false;
		}
		this.#opts.onCancel.current?.();
	};

	readonly updateDraft = (value: string): void => {
		this.draft = value;
	};

	// ============================================
	// Props
	// ============================================

	get props(): MessageEditRootHTMLProps {
		return {
			'data-message-edit-root': '',
			'data-editing': boolToEmptyStrOrUndef(this.editing),
			'data-pending': boolToEmptyStrOrUndef(this.#pending),
		};
	}

	get snippetProps(): MessageEditRootSnippetProps {
		return {
			editing: this.editing,
			draft: this.draft,
			pending: this.pending,
			canSave: this.canSave,
			startEdit: this.startEdit,
			save: this.save,
			cancel: this.cancel,
		};
	}
}

// ============================================
// Textarea State
// ============================================

interface MessageEditTextareaStateOpts {
	placeholder: ReadableBox<string>;
	ariaLabel: ReadableBox<string>;
}

export class MessageEditTextareaState {
	readonly #root: MessageEditRootState;
	readonly #opts: MessageEditTextareaStateOpts;

	constructor(root: MessageEditRootState, opts: MessageEditTextareaStateOpts) {
		this.#root = root;
		this.#opts = opts;
	}

	static create(opts: MessageEditTextareaStateOpts): MessageEditTextareaState {
		return new MessageEditTextareaState(MessageEditRootContext.get(), opts);
	}

	readonly handleKeyDown = (event: KeyboardEvent): void => {
		if (event.key === kbd.ESCAPE) {
			event.preventDefault();
			this.#root.cancel();
		} else if (event.key === kbd.ENTER && !event.shiftKey && !event.isComposing) {
			event.preventDefault();
			void this.#root.save();
		}
	};

	readonly handleChange = (value: string): void => {
		this.#root.updateDraft(value);
	};

	get props(): MessageEditTextareaHTMLProps {
		return {
			'data-message-edit-textarea': '',
			'aria-label': this.#opts.ariaLabel.current,
			'aria-multiline': 'true',
			disabled: this.#root.pending,
		};
	}

	get snippetProps(): MessageEditTextareaSnippetProps {
		return {
			value: this.#root.draft,
			pending: this.#root.pending,
			placeholder: this.#opts.placeholder.current,
			handleKeyDown: this.handleKeyDown,
			handleChange: this.handleChange,
		};
	}
}

// ============================================
// SaveButton State
// ============================================

interface MessageEditSaveButtonStateOpts {
	ariaLabel: ReadableBox<string>;
}

export class MessageEditSaveButtonState {
	readonly #root: MessageEditRootState;
	readonly #opts: MessageEditSaveButtonStateOpts;

	constructor(root: MessageEditRootState, opts: MessageEditSaveButtonStateOpts) {
		this.#root = root;
		this.#opts = opts;
	}

	static create(opts: MessageEditSaveButtonStateOpts): MessageEditSaveButtonState {
		return new MessageEditSaveButtonState(MessageEditRootContext.get(), opts);
	}

	readonly save = () => this.#root.save();

	get props(): MessageEditSaveButtonHTMLProps {
		return {
			'data-message-edit-save': '',
			'data-disabled': boolToEmptyStrOrUndef(!this.#root.canSave),
			type: 'button',
			disabled: !this.#root.canSave,
			'aria-label': this.#opts.ariaLabel.current,
		};
	}

	get snippetProps(): MessageEditSaveButtonSnippetProps {
		return {
			pending: this.#root.pending,
			disabled: !this.#root.canSave,
			save: this.#root.save,
		};
	}
}

// ============================================
// CancelButton State
// ============================================

interface MessageEditCancelButtonStateOpts {
	ariaLabel: ReadableBox<string>;
}

export class MessageEditCancelButtonState {
	readonly #root: MessageEditRootState;
	readonly #opts: MessageEditCancelButtonStateOpts;

	constructor(root: MessageEditRootState, opts: MessageEditCancelButtonStateOpts) {
		this.#root = root;
		this.#opts = opts;
	}

	static create(opts: MessageEditCancelButtonStateOpts): MessageEditCancelButtonState {
		return new MessageEditCancelButtonState(MessageEditRootContext.get(), opts);
	}

	readonly cancel = () => this.#root.cancel();

	get props(): MessageEditCancelButtonHTMLProps {
		return {
			'data-message-edit-cancel': '',
			'data-disabled': boolToEmptyStrOrUndef(this.#root.pending),
			type: 'button',
			disabled: this.#root.pending,
			'aria-label': this.#opts.ariaLabel.current,
		};
	}

	get snippetProps(): MessageEditCancelButtonSnippetProps {
		return {
			pending: this.#root.pending,
			cancel: this.#root.cancel,
		};
	}
}
