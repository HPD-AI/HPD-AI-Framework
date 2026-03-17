/**
 * MessageEdit - Compound headless component for inline message editing
 *
 * Manages a draft textarea bound to a message index. On save, calls
 * `workspace.editMessage(index, draft)` which forks the branch and
 * re-runs the model. On cancel, discards the draft with no side effects.
 *
 * @example
 * ```svelte
 * <script>
 *   import * as MessageEdit from '@hpd/hpd-agent-headless-ui/message-edit';
 *   let editingIndex = $state<number | null>(null);
 * </script>
 *
 * {#if editingIndex === i}
 *   <MessageEdit.Root
 *     {workspace}
 *     messageIndex={i}
 *     initialValue={message.content}
 *     onSave={() => (editingIndex = null)}
 *     onCancel={() => (editingIndex = null)}
 *   >
 *     <MessageEdit.Textarea placeholder="Edit message…" />
 *     <div class="flex gap-1 justify-end mt-1">
 *       <MessageEdit.CancelButton>Cancel</MessageEdit.CancelButton>
 *       <MessageEdit.SaveButton>Save & Send</MessageEdit.SaveButton>
 *     </div>
 *   </MessageEdit.Root>
 * {/if}
 * ```
 */

export * from './exports.ts';

export {
	MessageEditRootState,
	MessageEditTextareaState,
	MessageEditSaveButtonState,
	MessageEditCancelButtonState,
	messageEditAttrs,
} from './message-edit.svelte.js';

export type * from './types.js';
