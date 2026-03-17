<script lang="ts">
	import { SplitPanel } from '@hpd/hpd-agent-headless-ui';
	import type { AppRecorderState } from '../AppRecorderState.svelte';
	import MediaPanel from '../panels/MediaPanel.svelte';
	import PropertiesPanel from '../panels/PropertiesPanel.svelte';
	import VideoCanvas from '../canvas/VideoCanvas.svelte';
	import Timeline from '../timeline/Timeline.svelte';

	let { editor, tabId: _tabId = 'default' }: { editor: AppRecorderState; tabId?: string } = $props();

	let propsCollapsed = $state(true);
	let propsToggle = $state<() => void>(() => {});

	function wirePropsToggle(_node: HTMLElement, toggle: () => void) {
		propsToggle = toggle;
		return {
			update(t: () => void) { propsToggle = t; },
			destroy() {}
		};
	}
</script>

<!--
	Edit Page layout:
	┌──────────────┬────────────────────────┬──────────────┐
	│              │   VideoCanvas          │  Properties  │
	│  Media       │                        │  Panel       │
	│  Panel       ├────────────────────────┴──────────────┤
	│              │   Timeline (full width)               │
	└──────────────┴───────────────────────────────────────┘

	Tree:
	Split(horizontal)
	├── Pane(media)
	└── Split(vertical)
	    ├── Split(horizontal)   ← canvas + properties
	    │   ├── Pane(canvas)
	    │   └── Pane(properties)
	    └── Pane(timeline)      ← full width
-->
<div class="edit-page">
	<SplitPanel.Root id="edit-layout">
		<SplitPanel.Split axis="horizontal">

			<!-- ── Left: Media Panel ── -->
			<SplitPanel.Pane id="edit-media" minSize={180} initialSize={240} initialSizeUnit="pixels" priority="low">
				<div class="pane-fill">
					<MediaPanel {editor} />
				</div>
			</SplitPanel.Pane>

			<SplitPanel.Handle>
				<div class="resize-handle resize-handle-vertical"></div>
			</SplitPanel.Handle>

			<!-- ── Right side: vertical split (top = canvas+props, bottom = timeline) ── -->
			<SplitPanel.Split axis="vertical">

				<!-- ── Top row: canvas + properties ── -->
				<SplitPanel.Split axis="horizontal">

					<SplitPanel.Pane id="edit-canvas" priority="high" minSize={200} initialSize={70} initialSizeUnit="percent">
						<div class="pane-fill canvas-pane">
							<VideoCanvas {editor} />
							<button
								class="props-toggle-btn"
								onclick={() => propsToggle()}
								title={propsCollapsed ? 'Show properties' : 'Hide properties'}
							>
								<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5">
									<polyline points={propsCollapsed ? '6,4 10,8 6,12' : '10,4 6,8 10,12'} />
								</svg>
							</button>
						</div>
					</SplitPanel.Pane>

					<SplitPanel.Handle>
						<div class="resize-handle resize-handle-vertical"></div>
					</SplitPanel.Handle>

					<SplitPanel.Pane id="edit-props" minSize={220} initialSize={300} initialSizeUnit="pixels" priority="low" bind:collapsed={propsCollapsed}>
						{#snippet children({ isCollapsed, toggle })}
							<div class="pane-fill" use:wirePropsToggle={toggle}>
								<PropertiesPanel {editor} {isCollapsed} {toggle} />
							</div>
						{/snippet}
					</SplitPanel.Pane>

				</SplitPanel.Split>

				<SplitPanel.Handle>
					<div class="resize-handle resize-handle-horizontal"></div>
				</SplitPanel.Handle>

				<!-- ── Bottom: Timeline (full width) ── -->
				<SplitPanel.Pane id="edit-timeline" minSize={120} initialSize={300} initialSizeUnit="pixels" priority="low">
					<div class="pane-fill timeline-pane">
						<Timeline {editor} />
					</div>
				</SplitPanel.Pane>

			</SplitPanel.Split>

		</SplitPanel.Split>
	</SplitPanel.Root>
</div>

<style>
	.edit-page {
		flex: 1;
		min-height: 0;
		display: flex;
		flex-direction: column;
		height: 100%;
		width: 100%;
		background: rgb(var(--color-bg-primary));
	}

	.pane-fill {
		height: 100%;
		width: 100%;
		display: flex;
		flex-direction: column;
		min-height: 0;
		overflow: hidden;
	}


	.canvas-pane   { background: rgb(var(--color-bg-primary)); position: relative; }
	.timeline-pane {
		background: rgb(var(--color-bg-tertiary));
		border-top: 1px solid rgb(var(--color-border-default));
	}

	.resize-handle {
		background: rgb(var(--color-border-default));
		transition: background var(--duration-fast);
		flex-shrink: 0;
	}
	.resize-handle:hover      { background: rgb(var(--color-accent-primary)); }
	.resize-handle-vertical   { width: 4px;  height: 100%; cursor: col-resize; position: relative; display: flex; align-items: center; justify-content: center; }
	.resize-handle-horizontal { height: 4px; width: 100%;  cursor: row-resize; }

	.props-toggle-btn {
		position: absolute;
		top: 8px;
		right: 8px;
		background: rgb(var(--color-surface-2));
		border: 1px solid rgb(var(--color-border-default));
		border-radius: var(--radius-sm);
		color: rgb(var(--color-text-secondary));
		cursor: pointer;
		width: 26px;
		height: 26px;
		display: flex;
		align-items: center;
		justify-content: center;
		padding: 0;
		transition: all var(--duration-fast);
		z-index: 1;
	}
	.props-toggle-btn:hover {
		background: rgb(var(--color-surface-3));
		color: rgb(var(--color-text-primary));
		border-color: rgb(var(--color-accent-primary) / 0.4);
	}
	.props-toggle-btn svg { width: 13px; height: 13px; }
</style>
