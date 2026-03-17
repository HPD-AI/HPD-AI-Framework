/**
 * Artifact Component Exports
 */

export { default as Provider } from './components/artifact-provider.svelte';
export { default as Root } from './components/artifact-root.svelte';
export { default as Slot } from './components/artifact-slot.svelte';
export { default as Trigger } from './components/artifact-trigger.svelte';
export { default as Panel } from './components/artifact-panel.svelte';
export { default as Title } from './components/artifact-title.svelte';
export { default as Content } from './components/artifact-content.svelte';
export { default as Close } from './components/artifact-close.svelte';

export type {
	ArtifactProviderProps as ProviderProps,
	ArtifactRootProps as RootProps,
	ArtifactSlotProps as SlotProps,
	ArtifactTriggerProps as TriggerProps,
	ArtifactPanelProps as PanelProps,
	ArtifactTitleProps as TitleProps,
	ArtifactContentProps as ContentProps,
	ArtifactCloseProps as CloseProps,
	ArtifactRootSnippetProps as RootSnippetProps,
	ArtifactTriggerSnippetProps as TriggerSnippetProps,
	ArtifactPanelSnippetProps as PanelSnippetProps,
	ArtifactCloseSnippetProps as CloseSnippetProps,
	ArtifactSlotData as SlotData
} from './types.ts';
