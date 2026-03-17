<script lang="ts">
	/**
	 * AnnotationToolbar — vertical icon-button strip for the Annotate page.
	 * Active tool is highlighted. Undo/Redo are present but disabled (future undo stack).
	 */
	import type { AppRecorderState, AnnotationTool } from '../AppRecorderState.svelte';

	let { editor }: { editor: AppRecorderState } = $props();

	interface ToolDef {
		tool: AnnotationTool;
		label: string;
		key: string;
		icon: string; // SVG path or symbol
	}

	const TOOLS: ToolDef[] = [
		{ tool: 'select',     label: 'Select',     key: 'V', icon: 'select'     },
		{ tool: 'text',       label: 'Text',        key: 'T', icon: 'text'       },
		{ tool: 'arrow',      label: 'Arrow',       key: 'A', icon: 'arrow'      },
		{ tool: 'image',      label: 'Image',       key: 'I', icon: 'image'      },
		{ tool: 'zoom-point', label: 'Zoom Point',  key: 'Z', icon: 'zoom-point' },
		{ tool: 'crop',       label: 'Crop',        key: 'C', icon: 'crop'       },
	];
</script>

<div class="annotation-toolbar" role="toolbar" aria-label="Annotation tools">

	{#each TOOLS as t}
		<button
			class="tool-btn {editor.annotationTool === t.tool ? 'active' : ''}"
			onclick={() => editor.setAnnotationTool(t.tool)}
			title="{t.label} ({t.key})"
			aria-label={t.label}
			aria-pressed={editor.annotationTool === t.tool}
		>
			{#if t.icon === 'select'}
				<!-- Cursor arrow -->
				<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linejoin="round">
					<path d="M3 2l10 6-5 1-3 5z" fill="currentColor" stroke="none" opacity="0.9"/>
				</svg>
			{:else if t.icon === 'text'}
				<!-- T -->
				<svg viewBox="0 0 16 16" fill="currentColor">
					<path d="M3 3h10v2H9.5v8h-3V5H3V3z"/>
				</svg>
			{:else if t.icon === 'arrow'}
				<!-- Arrow right -->
				<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
					<line x1="2" y1="8" x2="13" y2="8"/>
					<polyline points="9,4 13,8 9,12"/>
				</svg>
			{:else if t.icon === 'image'}
				<!-- Image frame + mountain -->
				<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4">
					<rect x="2" y="3" width="12" height="10" rx="1"/>
					<circle cx="5.5" cy="6.5" r="1" fill="currentColor" stroke="none"/>
					<path d="M2 11l3.5-3.5 3 3 2-2 3 3" stroke-linecap="round" stroke-linejoin="round"/>
				</svg>
			{:else if t.icon === 'zoom-point'}
				<!-- Target / crosshair -->
				<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4">
					<circle cx="8" cy="8" r="4"/>
					<line x1="8" y1="2" x2="8" y2="5"/>
					<line x1="8" y1="11" x2="8" y2="14"/>
					<line x1="2" y1="8" x2="5" y2="8"/>
					<line x1="11" y1="8" x2="14" y2="8"/>
				</svg>
			{:else if t.icon === 'crop'}
				<!-- Crop handles -->
				<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round">
					<path d="M4 2v8h8"/>
					<path d="M2 4h8v8"/>
				</svg>
			{/if}
		</button>
	{/each}

	<!-- Separator -->
	<div class="separator"></div>

	<!-- Undo / Redo (disabled — future undo stack) -->
	<button class="tool-btn" disabled title="Undo (Ctrl+Z)" aria-label="Undo">
		<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round">
			<path d="M3 7a5 5 0 1 0 1-3"/>
			<polyline points="3,3 3,7 7,7"/>
		</svg>
	</button>

	<button class="tool-btn" disabled title="Redo (Ctrl+Shift+Z)" aria-label="Redo">
		<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round">
			<path d="M13 7a5 5 0 1 1-1-3"/>
			<polyline points="13,3 13,7 9,7"/>
		</svg>
	</button>

</div>

<style>
	.annotation-toolbar {
		width: 48px;
		flex-shrink: 0;
		height: 100%;
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 2px;
		padding: 8px 4px;
		background: rgb(var(--color-bg-secondary));
		border-right: 1px solid rgb(var(--color-border-default));
		overflow-y: auto;
	}

	.tool-btn {
		width: 36px;
		height: 36px;
		border-radius: var(--radius-sm);
		background: transparent;
		border: 1px solid transparent;
		color: rgb(var(--color-text-secondary));
		cursor: pointer;
		display: flex;
		align-items: center;
		justify-content: center;
		padding: 0;
		flex-shrink: 0;
		transition: all var(--duration-fast);
	}

	.tool-btn svg {
		width: 16px;
		height: 16px;
	}

	.tool-btn:hover:not(:disabled) {
		background: rgb(var(--color-surface-2));
		color: rgb(var(--color-text-primary));
		border-color: rgb(var(--color-border-default));
	}

	.tool-btn.active {
		background: rgb(var(--color-accent-primary) / 0.15);
		border-color: rgb(var(--color-accent-primary) / 0.4);
		color: rgb(var(--color-accent-primary));
	}

	.tool-btn:disabled {
		opacity: 0.28;
		cursor: not-allowed;
	}

	.separator {
		width: 24px;
		height: 1px;
		background: rgb(var(--color-border-default));
		margin: 4px 0;
		flex-shrink: 0;
	}
</style>
