<!--
	Layout Tree Example - Using renderLayoutNode Snippet

	Demonstrates polymorphic rendering of the layout tree structure.
	This shows how to render an existing layout tree from state directly.
-->
<script lang="ts">
	import { SplitPanelRootState } from '../components/split-panel-root-state.svelte.js';
	import { SplitPanelRootContext } from '../components/split-panel-context.js';
	import { splitPanelAttrs } from '../components/split-panel-attrs.js';
	import { registerPanel, getPanelElement } from '../actions/register-panel.js';
	import {
		getActiveChildren,
		computeGridTemplate,
		encodeResizeHandle,
		decodeResizeHandle,
		findPanelInDirection,
		collectPanelIds
	} from '../utils/index.js';
	import type { LayoutNode, BranchNode } from '../types/index.js';

	// Create root state
	let containerElement = $state<HTMLDivElement | null>(null);
	const getContainerWidth = () => containerElement?.clientWidth ?? 800;
	const getContainerHeight = () => containerElement?.clientHeight ?? 600;

	const rootState = new SplitPanelRootState({
		id: 'layout-tree-example',
		undoable: true,
		containerWidth: getContainerWidth,
		containerHeight: getContainerHeight
	});

	// Set context
	SplitPanelRootContext.set(rootState);

	// Initialize a sample layout tree programmatically
	$effect(() => {
		// Create a simple three-pane layout: [sidebar | main | panel]
		rootState.layoutState.root = {
			type: 'branch',
			axis: 'row',
			children: [
				{ type: 'leaf', id: 'sidebar', size: 250, priority: 'normal' as const, maximized: false },
				{ type: 'leaf', id: 'main', size: 400, priority: 'normal' as const, maximized: false },
				{ type: 'leaf', id: 'panel', size: 250, priority: 'normal' as const, maximized: false }
			],
			flexes: new Float32Array([250, 400, 250])
		};
	});

	/**
	 * Polymorphic snippet that renders any LayoutNode (Branch or Leaf).
	 * Recursively renders the tree structure using CSS Grid.
	 */
	function renderLayoutNode(
		node: LayoutNode,
		parentAxis: 'row' | 'column',
		parentPath: number[] = []
	): any {
		if (node.type === 'leaf') {
			// Leaf node: render panel with content
			return {
				type: 'leaf',
				id: node.id,
				props: {
					[splitPanelAttrs.pane]: '',
					'data-panel-id': node.id,
					'data-collapsed': node.size === 0 ? '' : undefined,
					'data-maximized': node.maximized ? '' : undefined,
					role: 'region',
					'aria-label': `Panel ${node.id}`,
					tabindex: 0,
					style: 'min-width: 0; min-height: 0; overflow: auto;'
				}
			};
		} else {
			// Branch node: render nested grid
			const active = getActiveChildren(node);
			return {
				type: 'branch',
				axis: node.axis,
				children: active,
				gridTemplate:
					node.axis === 'row' ? computeGridTemplate(node, 'row', rootState.layoutState) : null,
				gridTemplateRows:
					node.axis === 'column' ? computeGridTemplate(node, 'column', rootState.layoutState) : null
			};
		}
	}

	/**
	 * Handle pointer down on resize handle.
	 */
	function handleResizePointerDown(event: PointerEvent) {
		const handle = event.currentTarget as HTMLElement;
		const encoded = handle.dataset.resizeHandle;
		if (!encoded) return;

		const { parentPath, dividerIndex } = decodeResizeHandle(encoded);
		const axis = handle.dataset.parentAxis as 'row' | 'column';

		let lastClientPos = axis === 'row' ? event.clientX : event.clientY;

		const onPointerMove = (moveEvent: PointerEvent) => {
			const currentPos = axis === 'row' ? moveEvent.clientX : moveEvent.clientY;
			const delta = currentPos - lastClientPos;
			lastClientPos = currentPos;

			rootState.layoutState.resizeDivider(parentPath, dividerIndex, delta);
		};

		const onPointerUp = () => {
			document.removeEventListener('pointermove', onPointerMove);
			document.removeEventListener('pointerup', onPointerUp);
			document.removeEventListener('pointercancel', onPointerUp);
		};

		document.addEventListener('pointermove', onPointerMove);
		document.addEventListener('pointerup', onPointerUp);
		document.addEventListener('pointercancel', onPointerUp);

		event.preventDefault();
	}

	/**
	 * Handle keyboard on resize handle.
	 */
	function handleResizeKeyDown(event: KeyboardEvent) {
		const handle = event.currentTarget as HTMLElement;
		const encoded = handle.dataset.resizeHandle;
		if (!encoded) return;

		const { parentPath, dividerIndex } = decodeResizeHandle(encoded);
		const axis = handle.dataset.parentAxis as 'row' | 'column';

		let delta = 0;
		const step = 10;

		if (axis === 'row' && event.key === 'ArrowRight') {
			delta = step;
		} else if (axis === 'row' && event.key === 'ArrowLeft') {
			delta = -step;
		} else if (axis === 'column' && event.key === 'ArrowDown') {
			delta = step;
		} else if (axis === 'column' && event.key === 'ArrowUp') {
			delta = -step;
		} else {
			return;
		}

		rootState.layoutState.resizeDivider(parentPath, dividerIndex, delta);
		event.preventDefault();
	}

	/**
	 * Handle keyboard navigation on panels.
	 * Uses geometry-based navigation for arrow keys.
	 */
	function handlePanelKeyDown(event: KeyboardEvent) {
		const target = event.currentTarget as HTMLElement;
		const panelId = target.getAttribute('data-panel-id');
		if (!panelId) return;

		// Arrow key navigation
		const arrowKeys = ['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'];
		if (arrowKeys.includes(event.key)) {
			const direction = {
				ArrowUp: 'up',
				ArrowDown: 'down',
				ArrowLeft: 'left',
				ArrowRight: 'right'
			}[event.key] as 'up' | 'down' | 'left' | 'right';

			// Get all panel IDs from the layout tree
			const allPanelIds = collectPanelIds(rootState.layoutState.root);

			// Find next panel in direction
			const nextPanelId = findPanelInDirection(panelId, direction, allPanelIds);

			if (nextPanelId) {
				// Focus the next panel element
				const nextElement = getPanelElement(nextPanelId);
				if (nextElement) {
					nextElement.focus();
					event.preventDefault();
				}
			}
		}

		// Toggle with Space/Enter
		if (event.key === ' ' || event.key === 'Enter') {
			rootState.togglePane(panelId);
			event.preventDefault();
		}
	}
</script>

{#snippet renderNode(node: LayoutNode, parentAxis: 'row' | 'column', parentPath: number[] = [])}
	{#if node.type === 'leaf'}
		<!-- Leaf node: render panel -->
		<div
			use:registerPanel={{ layoutState: rootState.layoutState, panelId: node.id }}
			data-split-panel-pane=""
			data-panel-id={node.id}
			data-collapsed={node.size === 0 ? '' : undefined}
			data-maximized={node.maximized ? '' : undefined}
			role="group"
			aria-label="Panel {node.id}"
			tabindex={0}
			class="panel"
			onkeydown={handlePanelKeyDown}
		>
			<div class="panel-header">
				<h3>{node.id}</h3>
				<button onclick={() => rootState.togglePane(node.id)}>Toggle</button>
			</div>
			<div class="panel-content">
				<p>Panel content for <strong>{node.id}</strong></p>
				<p>Size: {node.size}px</p>
				<p>Collapsed: {node.size === 0}</p>
			</div>
		</div>
	{:else}
		<!-- Branch node: render nested grid -->
		<div
			class="branch"
			style:display="grid"
			style:grid-template-columns={node.axis === 'row'
				? computeGridTemplate(node, 'row', rootState.layoutState)
				: '1fr'}
			style:grid-template-rows={node.axis === 'column'
				? computeGridTemplate(node, 'column', rootState.layoutState)
				: '1fr'}
			style:width="100%"
			style:height="100%"
		>
			{#each getActiveChildren(node) as { child, activeIndex, totalActive, childIndex }}
				{@render renderNode(child, node.axis, [...parentPath, childIndex])}

				<!-- Render handle between active children -->
				{#if activeIndex < totalActive - 1}
					<div
						data-split-panel-handle=""
						class="resize-handle"
						data-parent-axis={node.axis}
						data-resize-handle={encodeResizeHandle(parentPath, activeIndex)}
						role="separator"
						aria-orientation={node.axis === 'row' ? 'vertical' : 'horizontal'}
						aria-label="Resize between panels"
						aria-valuenow={50}
						aria-valuemin={0}
						aria-valuemax={100}
						tabindex={0}
						onpointerdown={handleResizePointerDown}
						onkeydown={handleResizeKeyDown}
					>
						<div class="handle-grip"></div>
					</div>
				{/if}
			{/each}
		</div>
	{/if}
{/snippet}

<div class="example-container">
	<h2>Layout Tree Example (renderLayoutNode)</h2>

	<div class="toolbar">
		<button onclick={() => rootState.undo()} disabled={!rootState.canUndo}> Undo </button>
		<button onclick={() => rootState.redo()} disabled={!rootState.canRedo}> Redo </button>
		<button onclick={() => rootState.resetLayout()}> Reset </button>
	</div>

	<div bind:this={containerElement} class="layout-root" data-split-panel-root="">
		{@render renderNode(rootState.layoutState.root, 'column', [])}
	</div>
</div>

<style>
	.example-container {
		width: 100%;
		height: 600px;
		border: 1px solid #ccc;
		border-radius: 8px;
		padding: 16px;
		display: flex;
		flex-direction: column;
		gap: 16px;
		background: #f9f9f9;
	}

	h2 {
		margin: 0;
		font-size: 20px;
		font-weight: 600;
	}

	.toolbar {
		display: flex;
		gap: 8px;
		padding: 8px;
		background: white;
		border-radius: 4px;
		border: 1px solid #e0e0e0;
	}

	.toolbar button {
		padding: 6px 16px;
		border: 1px solid #ccc;
		border-radius: 4px;
		background: white;
		cursor: pointer;
		font-size: 14px;
		transition: all 0.2s;
	}

	.toolbar button:hover:not(:disabled) {
		background: #f0f0f0;
		border-color: #999;
	}

	.toolbar button:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.layout-root {
		flex: 1;
		background: white;
		border: 1px solid #e0e0e0;
		border-radius: 4px;
		overflow: hidden;
	}

	.branch {
		min-width: 0;
		min-height: 0;
	}

	.panel {
		min-width: 0;
		min-height: 0;
		overflow: auto;
		background: white;
		border: 1px solid #e0e0e0;
		display: flex;
		flex-direction: column;
	}

	.panel[data-collapsed] {
		opacity: 0.5;
	}

	.panel-header {
		padding: 12px 16px;
		background: #f5f5f5;
		border-bottom: 1px solid #e0e0e0;
		display: flex;
		justify-content: space-between;
		align-items: center;
	}

	.panel-header h3 {
		margin: 0;
		font-size: 16px;
		font-weight: 600;
	}

	.panel-header button {
		padding: 4px 12px;
		border: 1px solid #ccc;
		border-radius: 4px;
		background: white;
		cursor: pointer;
		font-size: 12px;
	}

	.panel-header button:hover {
		background: #e0e0e0;
	}

	.panel-content {
		padding: 16px;
		flex: 1;
		overflow: auto;
	}

	.panel-content p {
		margin: 0 0 8px 0;
		color: #666;
	}

	.resize-handle {
		background: #e0e0e0;
		cursor: col-resize;
		display: flex;
		align-items: center;
		justify-content: center;
		transition: background 0.2s;
		user-select: none;
	}

	.resize-handle:hover {
		background: #d0d0d0;
	}

	.resize-handle:focus {
		outline: 2px solid #4a90e2;
		outline-offset: -1px;
	}

	.resize-handle[data-parent-axis='column'] {
		cursor: row-resize;
		width: 100%;
		height: 4px;
	}

	.resize-handle[data-parent-axis='row'] {
		cursor: col-resize;
		width: 4px;
		height: 100%;
	}

	.handle-grip {
		background: #999;
		border-radius: 2px;
	}

	.resize-handle[data-parent-axis='row'] .handle-grip {
		width: 4px;
		height: 32px;
	}

	.resize-handle[data-parent-axis='column'] .handle-grip {
		width: 32px;
		height: 4px;
	}
</style>
