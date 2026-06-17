/**
 * FileAttachment headless component
 *
 * Handles file selection, immediate upload to POST /sessions/{sid}/threads/{bid}/content,
 * and accumulates ContentReference[] for passing to workspace.send().
 *
 * @example
 * ```svelte
 * <script>
 *   import { FileAttachment } from '@hpd-research/hpd-agent-svelte-headless-ui';
 *
 *   const attachments = new FileAttachment.State({...});
 * </script>
 *
 * <FileAttachment.Root state={attachments}>
 *   {#snippet children(fa)}
 *     <button onclick={() => fileInput.click()}>Attach</button>
 *     {#each fa.attachments as a (a.localId)}
 *       <span>{a.file.name} — {a.status}</span>
 *     {/each}
 *   {/snippet}
 * </FileAttachment.Root>
 * ```
 */

export * from './exports.ts';
export { FileAttachmentState as State, FileAttachmentState } from './file-attachment.svelte.ts';

export type {
	FileAttachmentProps,
	FileAttachmentHTMLProps,
	FileAttachmentSnippetProps,
	PendingAttachment,
	AttachmentStatus,
} from './types.ts';
