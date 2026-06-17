<!--
	Basic SplitPanel Example

	Demonstrates a simple two-pane layout with a resizable handle.
-->
<script lang="ts">
	import * as SplitPanel from '../index.js';

	// Example content components
	function LeftPane() {
		return 'Left Pane Content';
	}

	function RightPane() {
		return 'Right Pane Content';
	}
</script>

<div class="example-container">
	<h2>Basic Two-Pane Layout</h2>

	<SplitPanel.Root id="basic-example" undoable={true}>
		{#snippet children({ layoutState, canUndo, canRedo })}
			<!-- Toolbar with undo/redo -->
			<div class="toolbar">
				<button onclick={() => layoutState.undo()} disabled={!canUndo}> Undo </button>
				<button onclick={() => layoutState.redo()} disabled={!canRedo}> Redo </button>
			</div>

			<!-- Layout content -->
			<div class="layout-content">
				<SplitPanel.Pane id="left" minSize={200} priority="normal">
					<div class="pane-content">
						<h3>Left Pane</h3>
						<p>This is the left pane content. Try resizing using the handle!</p>
					</div>
				</SplitPanel.Pane>

				<SplitPanel.Handle axis="column" keyboardStep={10} keyboardStepLarge={50}>
					<div class="handle-visual">
						<div class="handle-grip"></div>
					</div>
				</SplitPanel.Handle>

				<SplitPanel.Pane id="right" minSize={200} priority="normal">
					<div class="pane-content">
						<h3>Right Pane</h3>
						<p>This is the right pane content. Use arrow keys when focused on the handle!</p>
					</div>
				</SplitPanel.Pane>
			</div>
		{/snippet}
	</SplitPanel.Root>
</div>

<style>
	.example-container {
		width: 100%;
		height: 500px;
		border: 1px solid #ccc;
		border-radius: 8px;
		padding: 16px;
		display: flex;
		flex-direction: column;
		gap: 16px;
	}

	.toolbar {
		display: flex;
		gap: 8px;
		padding: 8px;
		background: #f5f5f5;
		border-radius: 4px;
	}

	.toolbar button {
		padding: 4px 12px;
		border: 1px solid #ccc;
		border-radius: 4px;
		background: white;
		cursor: pointer;
	}

	.toolbar button:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}

	.layout-content {
		flex: 1;
		display: flex;
		gap: 0;
		overflow: hidden;
	}

	.pane-content {
		padding: 16px;
		height: 100%;
		overflow: auto;
		background: white;
		border: 1px solid #e0e0e0;
		border-radius: 4px;
	}

	.handle-visual {
		width: 100%;
		height: 100%;
		display: flex;
		align-items: center;
		justify-content: center;
		background: #e0e0e0;
		cursor: col-resize;
		transition: background 0.2s;
	}

	.handle-visual:hover {
		background: #d0d0d0;
	}

	.handle-grip {
		width: 4px;
		height: 32px;
		background: #999;
		border-radius: 2px;
	}

	:global([data-state='dragging']) .handle-visual {
		background: #b0b0b0;
	}

	h2 {
		margin: 0;
		font-size: 20px;
		font-weight: 600;
	}

	h3 {
		margin: 0 0 12px 0;
		font-size: 16px;
		font-weight: 600;
	}

	p {
		margin: 0;
		color: #666;
	}
</style>
