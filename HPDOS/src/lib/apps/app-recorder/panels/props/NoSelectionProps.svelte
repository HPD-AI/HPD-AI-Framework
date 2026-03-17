<script lang="ts">
	import type { AppRecorderState } from '../../AppRecorderState.svelte';
	import type { BackgroundKind } from '../../AppRecorderState.svelte';

	let { editor }: { editor: AppRecorderState } = $props();

	let bgTab = $state<BackgroundKind>('solid');

	// Preset background colors
	const BG_PRESETS = [
		'#1a1a2e', '#0f3460', '#16213e',
		'#2d1b69', '#11998e', '#1b2838',
		'#2c3e50', '#8e44ad', '#2980b9',
	];
</script>

<div class="no-selection-props">

	<!-- ── Background card ── -->
	<div class="prop-card">
		<p class="prop-label">Background</p>
		<div class="bg-tabs">
			{#each (['solid', 'gradient', 'image'] as const) as kind}
				<button
					class="bg-tab"
					class:active={bgTab === kind}
					onclick={() => bgTab = kind}
				>
					{kind.charAt(0).toUpperCase() + kind.slice(1)}
				</button>
			{/each}
		</div>

		{#if bgTab === 'solid'}
			<div class="preset-grid">
				{#each BG_PRESETS as color}
					<button
						class="preset-swatch"
						style="background: {color}"
						class:selected={editor.background.color === color}
						onclick={() => editor.setBackground({ kind: 'solid', color })}
						title={color}
					></button>
				{/each}
			</div>
			<div class="color-row">
				<input
					type="color"
					value={editor.background.color ?? '#1a1a2e'}
					oninput={(e) => editor.setBackground({ kind: 'solid', color: (e.currentTarget as HTMLInputElement).value })}
					class="color-input"
				/>
				<span class="hex-label">{editor.background.color ?? '#1a1a2e'}</span>
			</div>

		{:else if bgTab === 'gradient'}
			<div class="gradient-row">
				<input
					type="color"
					value="#1a1a2e"
					class="color-input"
					title="Start color"
				/>
				<span class="gradient-arrow">→</span>
				<input
					type="color"
					value="#8e44ad"
					class="color-input"
					title="End color"
				/>
			</div>
			<div class="slider-row">
				<label class="slider-label">Angle</label>
				<input type="range" min="0" max="360" value="135" class="slider" />
				<span class="slider-val">135°</span>
			</div>

		{:else}
			<button class="upload-btn">Upload Image</button>
		{/if}
	</div>

	<!-- ── Visual options card ── -->
	<div class="prop-card">
		<p class="prop-label">Visual Options</p>

		<div class="toggle-row">
			<label class="toggle-label">Motion Blur</label>
			<button
				class="toggle-btn"
				class:on={editor.visual.motionBlur}
				onclick={() => editor.setVisual({ motionBlur: !editor.visual.motionBlur })}
				role="switch"
				aria-checked={editor.visual.motionBlur}
			>
				<span class="toggle-thumb"></span>
			</button>
		</div>

		<div class="toggle-row">
			<label class="toggle-label">Background Blur</label>
			<button
				class="toggle-btn"
				class:on={editor.visual.backgroundBlur}
				onclick={() => editor.setVisual({ backgroundBlur: !editor.visual.backgroundBlur })}
				role="switch"
				aria-checked={editor.visual.backgroundBlur}
			>
				<span class="toggle-thumb"></span>
			</button>
		</div>

		<div class="toggle-row">
			<label class="toggle-label">Drop Shadow</label>
			<button
				class="toggle-btn"
				class:on={editor.visual.dropShadow}
				onclick={() => editor.setVisual({ dropShadow: !editor.visual.dropShadow })}
				role="switch"
				aria-checked={editor.visual.dropShadow}
			>
				<span class="toggle-thumb"></span>
			</button>
		</div>

		<div class="slider-row">
			<label class="slider-label">Roundness</label>
			<input
				type="range"
				min="0" max="50"
				value={editor.visual.borderRadius}
				oninput={(e) => editor.setVisual({ borderRadius: Number((e.currentTarget as HTMLInputElement).value) })}
				class="slider"
			/>
			<span class="slider-val">{editor.visual.borderRadius}</span>
		</div>

		<div class="slider-row">
			<label class="slider-label">Padding</label>
			<input
				type="range"
				min="0" max="100"
				value={editor.visual.padding}
				oninput={(e) => editor.setVisual({ padding: Number((e.currentTarget as HTMLInputElement).value) })}
				class="slider"
			/>
			<span class="slider-val">{editor.visual.padding}</span>
		</div>
	</div>

	<!-- ── Crop button ── -->
	<div class="prop-card">
		<button
			class="crop-btn"
			onclick={() => {
				editor.setAnnotationTool('crop');
				editor.setActivePage('annotate');
			}}
		>
			✂ Crop Video
		</button>
	</div>

</div>

<style>
	.no-selection-props {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
		padding: 0.75rem;
	}

	.prop-card {
		background: rgb(var(--color-surface-1));
		border: 1px solid rgb(var(--color-border-subtle));
		border-radius: var(--radius-md);
		padding: 0.75rem;
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}

	.prop-label {
		font-size: 0.7rem;
		font-weight: 600;
		text-transform: uppercase;
		letter-spacing: 0.07em;
		color: rgb(var(--color-text-secondary));
		margin: 0 0 0.25rem;
	}

	/* ── Background tabs ── */
	.bg-tabs { display: flex; gap: 0.25rem; }
	.bg-tab {
		flex: 1;
		padding: 0.3rem 0;
		background: rgb(var(--color-bg-secondary));
		border: 1px solid rgb(var(--color-border-default));
		border-radius: var(--radius-sm);
		color: rgb(var(--color-text-secondary));
		font-size: 0.7rem;
		cursor: pointer;
	}
	.bg-tab.active {
		background: rgb(var(--color-accent-primary) / 0.15);
		border-color: rgb(var(--color-accent-primary) / 0.5);
		color: rgb(var(--color-accent-primary));
	}

	/* ── Preset swatches ── */
	.preset-grid {
		display: grid;
		grid-template-columns: repeat(9, 1fr);
		gap: 0.25rem;
	}
	.preset-swatch {
		aspect-ratio: 1;
		border-radius: 3px;
		border: 2px solid transparent;
		cursor: pointer;
		transition: border-color var(--duration-fast), transform var(--duration-fast);
	}
	.preset-swatch:hover    { transform: scale(1.1); }
	.preset-swatch.selected { border-color: rgb(var(--color-accent-primary)); }

	/* ── Color row ── */
	.color-row {
		display: flex;
		align-items: center;
		gap: 0.5rem;
	}
	.color-input {
		width: 32px;
		height: 24px;
		padding: 0;
		border: 1px solid rgb(var(--color-border-default));
		border-radius: var(--radius-sm);
		cursor: pointer;
		background: none;
	}
	.hex-label { font-size: 0.7rem; color: rgb(var(--color-text-secondary)); font-family: monospace; }

	/* ── Gradient row ── */
	.gradient-row { display: flex; align-items: center; gap: 0.5rem; }
	.gradient-arrow { color: rgb(var(--color-text-tertiary)); font-size: 0.8rem; }

	/* ── Upload btn ── */
	.upload-btn {
		width: 100%;
		padding: 0.5rem;
		background: rgb(var(--color-bg-secondary));
		border: 1px dashed rgb(var(--color-border-default));
		border-radius: var(--radius-sm);
		color: rgb(var(--color-text-secondary));
		font-size: 0.75rem;
		cursor: pointer;
	}
	.upload-btn:hover { border-color: rgb(var(--color-accent-primary)); color: rgb(var(--color-text-primary)); }

	/* ── Toggle rows ── */
	.toggle-row {
		display: flex;
		align-items: center;
		justify-content: space-between;
	}
	.toggle-label { font-size: 0.75rem; color: rgb(var(--color-text-primary)); }
	.toggle-btn {
		position: relative;
		width: 32px;
		height: 18px;
		border-radius: 9px;
		background: rgb(var(--color-border-default));
		border: none;
		cursor: pointer;
		transition: background var(--duration-fast);
		flex-shrink: 0;
	}
	.toggle-btn.on { background: rgb(var(--color-accent-primary)); }
	.toggle-thumb {
		position: absolute;
		top: 2px;
		left: 2px;
		width: 14px;
		height: 14px;
		border-radius: 50%;
		background: #fff;
		transition: left var(--duration-fast);
		display: block;
	}
	.toggle-btn.on .toggle-thumb { left: 16px; }

	/* ── Sliders ── */
	.slider-row {
		display: flex;
		align-items: center;
		gap: 0.5rem;
	}
	.slider-label { font-size: 0.7rem; color: rgb(var(--color-text-secondary)); min-width: 60px; }
	.slider { flex: 1; accent-color: rgb(var(--color-accent-primary)); cursor: pointer; }
	.slider-val { font-size: 0.7rem; color: rgb(var(--color-text-tertiary)); min-width: 24px; text-align: right; font-family: monospace; }

	/* ── Crop btn ── */
	.crop-btn {
		width: 100%;
		padding: 0.5rem;
		background: rgb(var(--color-bg-secondary));
		border: 1px solid rgb(var(--color-border-default));
		border-radius: var(--radius-sm);
		color: rgb(var(--color-text-primary));
		font-size: 0.75rem;
		cursor: pointer;
		text-align: center;
	}
	.crop-btn:hover {
		background: rgb(var(--color-surface-1));
		border-color: rgb(var(--color-accent-primary) / 0.5);
	}
</style>
