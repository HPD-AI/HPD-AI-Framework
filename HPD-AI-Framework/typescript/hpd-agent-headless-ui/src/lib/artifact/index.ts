/**
 * Artifact Component
 *
 * Headless artifact system for rendering AI-generated content in a side panel.
 * Content defined in one place (Artifact.Slot) renders elsewhere (Artifact.Panel).
 *
 * @example
 * ```svelte
 * <script>
 *   import { Artifact } from '@hpd/hpd-agent-headless-ui';
 * </script>
 *
 * <Artifact.Provider>
 *   <!-- Chat area -->
 *   <div class="chat">
 *     {#each messages as message}
 *       <div class="message">
 *         {message.text}
 *
 *         {#if message.artifact}
 *           <Artifact.Root id={message.id}>
 *             <Artifact.Trigger>
 *               Open Preview
 *             </Artifact.Trigger>
 *
 *             <Artifact.Slot>
 *               {#snippet title()}
 *                 {message.artifact.title}
 *               {/snippet}
 *               {#snippet content()}
 *                 <CodePreview code={message.artifact.code} />
 *               {/snippet}
 *             </Artifact.Slot>
 *           </Artifact.Root>
 *         {/if}
 *       </div>
 *     {/each}
 *   </div>
 *
 *   <!-- Artifact panel (can be anywhere in the tree) -->
 *   <aside class="artifact-sidebar">
 *     <Artifact.Panel>
 *       {#snippet children({ open, close })}
 *         {#if open}
 *           <header>
 *             <Artifact.Title />
 *             <Artifact.Close>Close</Artifact.Close>
 *           </header>
 *           <Artifact.Content />
 *         {/if}
 *       {/snippet}
 *     </Artifact.Panel>
 *   </aside>
 * </Artifact.Provider>
 * ```
 */

export * from './exports.ts';

export {
	ArtifactProviderState,
	ArtifactRootState,
	ArtifactSlotState,
	ArtifactPanelState,
	ArtifactTriggerState,
	ArtifactCloseState,
	artifactAttrs
} from './artifact.svelte.ts';

export type {
	ArtifactProviderProps,
	ArtifactRootProps,
	ArtifactSlotProps,
	ArtifactTriggerProps,
	ArtifactPanelProps,
	ArtifactTitleProps,
	ArtifactContentProps,
	ArtifactCloseProps,
	ArtifactRootSnippetProps,
	ArtifactTriggerSnippetProps,
	ArtifactPanelSnippetProps,
	ArtifactCloseSnippetProps,
	ArtifactSlotData
} from './types.ts';
