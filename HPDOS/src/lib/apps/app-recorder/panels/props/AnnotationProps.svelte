<script lang="ts">
	import type { AppRecorderState } from '../../AppRecorderState.svelte';

	let { editor }: { editor: AppRecorderState } = $props();
</script>

<div class="annotation-props">
	{#if !editor.selectedAnnotation}
		<p class="hint">Select an annotation on the canvas or timeline to edit.</p>
	{:else}
		{@const ann = editor.selectedAnnotation}

		<!-- Text-specific controls -->
		{#if ann.kind === 'text'}
			<div class="prop-card">
				<p class="prop-label">Text</p>
				<textarea
					class="text-input"
					rows="3"
					value={ann.text ?? ''}
					oninput={(e) => editor.updateAnnotation(ann.id, { text: (e.currentTarget as HTMLTextAreaElement).value })}
					placeholder="Enter text..."
				></textarea>
			</div>

			{#if ann.textStyle}
				{@const ts = ann.textStyle}
				<div class="prop-card">
					<p class="prop-label">Style</p>
					<div class="style-row">
						<label class="style-label">Font size</label>
						<input
							type="number" min="8" max="120"
							value={ts.fontSize}
							oninput={(e) => editor.updateAnnotation(ann.id, { textStyle: { ...ts, fontSize: Number((e.currentTarget as HTMLInputElement).value) } })}
							class="num-input"
						/>
					</div>
					<div class="style-row">
						<label class="style-label">Color</label>
						<input
							type="color"
							value={ts.color}
							oninput={(e) => editor.updateAnnotation(ann.id, { textStyle: { ...ts, color: (e.currentTarget as HTMLInputElement).value } })}
							class="color-input"
						/>
					</div>
					<div class="style-row">
						<label class="style-label">Weight</label>
						<button
							class="style-toggle"
							class:active={ts.fontWeight === 'bold'}
							onclick={() => editor.updateAnnotation(ann.id, { textStyle: { ...ts, fontWeight: ts.fontWeight === 'bold' ? 'normal' : 'bold' } })}
						><strong>B</strong></button>
						<button
							class="style-toggle"
							class:active={ts.fontStyle === 'italic'}
							onclick={() => editor.updateAnnotation(ann.id, { textStyle: { ...ts, fontStyle: ts.fontStyle === 'italic' ? 'normal' : 'italic' } })}
						><em>I</em></button>
						<button
							class="style-toggle"
							class:active={ts.textDecoration === 'underline'}
							onclick={() => editor.updateAnnotation(ann.id, { textStyle: { ...ts, textDecoration: ts.textDecoration === 'underline' ? 'none' : 'underline' } })}
						><u>U</u></button>
					</div>
					<div class="style-row">
						<label class="style-label">Align</label>
						{#each (['left', 'center', 'right'] as const) as align}
							<button
								class="style-toggle"
								class:active={ts.textAlign === align}
								onclick={() => editor.updateAnnotation(ann.id, { textStyle: { ...ts, textAlign: align } })}
							>{align.charAt(0).toUpperCase()}</button>
						{/each}
					</div>
					<button class="font-btn" onclick={() => editor.openFontPicker()}>
						{ts.fontFamily} ▾
					</button>
				</div>
			{/if}
		{/if}

		<!-- Arrow-specific controls -->
		{#if ann.kind === 'arrow' && ann.figureData}
			{@const fd = ann.figureData}
			<div class="prop-card">
				<p class="prop-label">Arrow</p>
				<div class="style-row">
					<label class="style-label">Color</label>
					<input
						type="color"
						value={fd.color}
						oninput={(e) => editor.updateAnnotation(ann.id, { figureData: { ...fd, color: (e.currentTarget as HTMLInputElement).value } })}
						class="color-input"
					/>
				</div>
				<div class="style-row">
					<label class="style-label">Width</label>
					<input
						type="range" min="1" max="16"
						value={fd.strokeWidth}
						oninput={(e) => editor.updateAnnotation(ann.id, { figureData: { ...fd, strokeWidth: Number((e.currentTarget as HTMLInputElement).value) } })}
						class="slider"
					/>
					<span class="slider-val">{fd.strokeWidth}px</span>
				</div>
			</div>
		{/if}

		<!-- Image-specific controls -->
		{#if ann.kind === 'image'}
			<div class="prop-card">
				<button class="upload-btn">Replace Image</button>
			</div>
		{/if}

		<!-- Shared controls -->
		<div class="prop-card">
			<p class="prop-label">Position</p>
			<div class="style-row">
				<label class="style-label">Opacity</label>
				<input
					type="range" min="0" max="1" step="0.01"
					value={ann.opacity}
					oninput={(e) => editor.updateAnnotation(ann.id, { opacity: Number((e.currentTarget as HTMLInputElement).value) })}
					class="slider"
				/>
				<span class="slider-val">{Math.round(ann.opacity * 100)}%</span>
			</div>
			<div class="z-btns">
				<button class="z-btn" onclick={() => editor.bringAnnotationToFront(ann.id)}>Bring to Front</button>
				<button class="z-btn" onclick={() => editor.sendAnnotationToBack(ann.id)}>Send to Back</button>
			</div>
		</div>

		<button class="delete-btn" onclick={() => editor.removeAnnotation(ann.id)}>
			Delete Annotation
		</button>
	{/if}
</div>

<style>
	.annotation-props { display: flex; flex-direction: column; gap: 0.5rem; padding: 0.75rem; }
	.hint { font-size: 0.75rem; color: rgb(var(--color-text-tertiary)); font-style: italic; text-align: center; padding: 1rem 0; margin: 0; }

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
		margin: 0;
	}

	.text-input {
		width: 100%;
		background: rgb(var(--color-bg-secondary));
		border: 1px solid rgb(var(--color-border-default));
		border-radius: var(--radius-sm);
		color: rgb(var(--color-text-primary));
		font-size: 0.75rem;
		padding: 0.4rem;
		resize: vertical;
		min-height: 60px;
		box-sizing: border-box;
	}

	.style-row { display: flex; align-items: center; gap: 0.4rem; }
	.style-label { font-size: 0.7rem; color: rgb(var(--color-text-secondary)); min-width: 52px; }
	.num-input {
		width: 56px;
		background: rgb(var(--color-bg-secondary));
		border: 1px solid rgb(var(--color-border-default));
		border-radius: var(--radius-sm);
		color: rgb(var(--color-text-primary));
		font-size: 0.75rem;
		padding: 0.2rem 0.4rem;
	}
	.color-input {
		width: 32px; height: 24px;
		padding: 0;
		border: 1px solid rgb(var(--color-border-default));
		border-radius: var(--radius-sm);
		cursor: pointer;
		background: none;
	}
	.style-toggle {
		width: 26px; height: 26px;
		background: rgb(var(--color-bg-secondary));
		border: 1px solid rgb(var(--color-border-default));
		border-radius: var(--radius-sm);
		color: rgb(var(--color-text-secondary));
		font-size: 0.7rem;
		cursor: pointer;
		transition: all var(--duration-fast);
	}
	.style-toggle.active {
		background: rgb(var(--color-accent-primary) / 0.15);
		border-color: rgb(var(--color-accent-primary));
		color: rgb(var(--color-accent-primary));
	}
	.font-btn {
		width: 100%;
		padding: 0.35rem;
		background: rgb(var(--color-bg-secondary));
		border: 1px solid rgb(var(--color-border-default));
		border-radius: var(--radius-sm);
		color: rgb(var(--color-text-primary));
		font-size: 0.75rem;
		cursor: pointer;
		text-align: left;
	}
	.font-btn:hover { border-color: rgb(var(--color-accent-primary) / 0.5); }
	.upload-btn {
		width: 100%;
		padding: 0.4rem;
		background: rgb(var(--color-bg-secondary));
		border: 1px dashed rgb(var(--color-border-default));
		border-radius: var(--radius-sm);
		color: rgb(var(--color-text-secondary));
		font-size: 0.75rem;
		cursor: pointer;
	}
	.slider { flex: 1; accent-color: rgb(var(--color-accent-primary)); cursor: pointer; }
	.slider-val { font-size: 0.7rem; color: rgb(var(--color-text-tertiary)); min-width: 30px; text-align: right; font-family: monospace; }
	.z-btns { display: flex; gap: 0.4rem; }
	.z-btn {
		flex: 1;
		padding: 0.3rem;
		background: rgb(var(--color-bg-secondary));
		border: 1px solid rgb(var(--color-border-default));
		border-radius: var(--radius-sm);
		color: rgb(var(--color-text-secondary));
		font-size: 0.7rem;
		cursor: pointer;
	}
	.z-btn:hover { color: rgb(var(--color-text-primary)); }
	.delete-btn {
		width: 100%;
		padding: 0.5rem;
		background: rgb(var(--color-error) / 0.1);
		border: 1px solid rgb(var(--color-error) / 0.3);
		border-radius: var(--radius-sm);
		color: rgb(var(--color-error));
		font-size: 0.75rem;
		cursor: pointer;
	}
	.delete-btn:hover { background: rgb(var(--color-error) / 0.2); }
</style>
