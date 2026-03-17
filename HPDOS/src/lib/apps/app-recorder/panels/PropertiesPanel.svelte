<script lang="ts">
	import type { AppRecorderState } from '../AppRecorderState.svelte';
	import NoSelectionProps from './props/NoSelectionProps.svelte';
	import ZoomProps from './props/ZoomProps.svelte';
	import TrimProps from './props/TrimProps.svelte';
	import SpeedProps from './props/SpeedProps.svelte';
	import AnnotationProps from './props/AnnotationProps.svelte';
	import TransitionProps from './props/TransitionProps.svelte';
	import ClipProps from './props/ClipProps.svelte';

	let {
		editor,
		isCollapsed = false,
		toggle = () => {}
	}: { editor: AppRecorderState; isCollapsed?: boolean; toggle?: () => void } = $props();

	// Selection priority (highest first):
	// clip > annotation > zoom > trim > speed > transition > none
	const activePanel = $derived.by(() => {
		if (editor.selectedClip)        return 'clip';
		if (editor.selectedAnnotation)  return 'annotation';
		if (editor.selectedZoom)        return 'zoom';
		if (editor.selectedTrim)        return 'trim';
		if (editor.selectedSpeed)       return 'speed';
		if (editor.selectedTransition)  return 'transition';
		return 'none';
	});

	const PANEL_LABELS: Record<string, string> = {
		clip:       'Clip',
		annotation: 'Annotation',
		zoom:       'Zoom Region',
		trim:       'Trim Region',
		speed:      'Speed Region',
		transition: 'Transition',
		none:       'Properties',
	};
</script>

<div class="properties-panel">

	<!-- ── Header ── -->
	<div class="panel-header">
		<button class="collapse-btn" onclick={toggle} title={isCollapsed ? 'Show properties' : 'Hide properties'}>
			<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5">
				<polyline points={isCollapsed ? '6,4 10,8 6,12' : '10,4 6,8 10,12'} />
			</svg>
		</button>
		<span class="panel-title">{PANEL_LABELS[activePanel]}</span>
		{#if activePanel !== 'none'}
			<button class="deselect-btn" onclick={() => editor.clearSelection()} title="Clear selection">✕</button>
		{/if}
	</div>

	<!-- ── Content (scrollable) ── -->
	<div class="panel-content">
		{#if activePanel === 'clip'}
			<ClipProps {editor} />
		{:else if activePanel === 'annotation'}
			<AnnotationProps {editor} />
		{:else if activePanel === 'zoom'}
			<ZoomProps {editor} />
		{:else if activePanel === 'trim'}
			<TrimProps {editor} />
		{:else if activePanel === 'speed'}
			<SpeedProps {editor} />
		{:else if activePanel === 'transition'}
			<TransitionProps {editor} />
		{:else}
			<NoSelectionProps {editor} />
		{/if}
	</div>

</div>

<style>
	.properties-panel {
		display: flex;
		flex-direction: column;
		height: 100%;
		width: 100%;
		background: rgb(var(--color-bg-secondary));
		border-left: 1px solid rgb(var(--color-border-default));
		overflow: hidden;
	}

	.panel-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 0 0.75rem;
		height: 36px;
		border-bottom: 1px solid rgb(var(--color-border-default));
		flex-shrink: 0;
	}
	.panel-title {
		font-size: 0.75rem;
		font-weight: 600;
		color: rgb(var(--color-text-secondary));
		text-transform: uppercase;
		letter-spacing: 0.06em;
	}
	.collapse-btn {
		background: none;
		border: none;
		color: rgb(var(--color-text-tertiary));
		cursor: pointer;
		padding: 0.2rem;
		display: flex;
		align-items: center;
		flex-shrink: 0;
	}
	.collapse-btn:hover { color: rgb(var(--color-text-primary)); }
	.collapse-btn svg { width: 14px; height: 14px; }

	.deselect-btn {
		background: none;
		border: none;
		color: rgb(var(--color-text-tertiary));
		font-size: 0.7rem;
		cursor: pointer;
		padding: 0.2rem;
		line-height: 1;
	}
	.deselect-btn:hover { color: rgb(var(--color-text-primary)); }

	.panel-content {
		flex: 1;
		overflow-y: auto;
		min-height: 0;
	}
</style>
