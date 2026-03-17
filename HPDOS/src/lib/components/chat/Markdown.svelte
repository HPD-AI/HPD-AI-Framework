<script lang="ts">
	import { renderMarkdown } from './markdown.js';

	interface Props {
		content: string;
		streaming: boolean;
	}

	let { content, streaming }: Props = $props();

	let rendered = $state('');
	let prevContent = '';

	$effect(() => {
		if (streaming) return;
		if (content === prevContent) return;
		prevContent = content;
		if (!content) { rendered = ''; return; }
		renderMarkdown(content).then((html) => { rendered = html; });
	});
</script>

{#if streaming}
	<span class="md-streaming">{content}<span class="md-cursor"></span></span>
{:else}
	<!-- svelte-ignore a11y_no_static_element_interactions -->
	<div class="md-body">{@html rendered}</div>
{/if}

<style>
	/* Streaming plain-text view */
	.md-streaming {
		display: block;
		white-space: pre-wrap;
		word-break: break-word;
		font-size: 0.875rem;
		line-height: 1.6;
		color: rgb(var(--color-text-primary));
	}

	.md-cursor {
		display: inline-block;
		width: 2px;
		height: 1em;
		background: rgb(var(--color-accent-light, var(--color-accent-primary)));
		animation: md-blink 1s step-end infinite;
		vertical-align: text-bottom;
		margin-left: 1px;
	}
	@keyframes md-blink {
		0%, 100% { opacity: 1; }
		50% { opacity: 0; }
	}

	/* ===== Rendered markdown ===== */
	.md-body {
		font-size: 0.875rem;
		line-height: 1.6;
		color: rgb(var(--color-text-primary));
		word-break: break-word;
	}

	.md-body :global(p) {
		margin: 0 0 0.75em;
	}
	.md-body :global(p:last-child) { margin-bottom: 0; }

	.md-body :global(h1),
	.md-body :global(h2),
	.md-body :global(h3),
	.md-body :global(h4),
	.md-body :global(h5),
	.md-body :global(h6) {
		margin: 1em 0 0.4em;
		font-weight: 600;
		line-height: 1.3;
		color: rgb(var(--color-text-primary));
	}
	.md-body :global(h1) { font-size: 1.35em; }
	.md-body :global(h2) { font-size: 1.2em; }
	.md-body :global(h3) { font-size: 1.05em; }
	.md-body :global(h4),
	.md-body :global(h5),
	.md-body :global(h6) { font-size: 0.95em; }

	.md-body :global(ul),
	.md-body :global(ol) {
		margin: 0 0 0.75em;
		padding-left: 1.4em;
	}
	.md-body :global(li) { margin-bottom: 0.2em; }
	.md-body :global(li > p) { margin: 0; }

	.md-body :global(blockquote) {
		margin: 0 0 0.75em;
		padding: 0.25em 0.75em;
		border-left: 3px solid rgb(var(--color-accent-primary) / 0.5);
		color: rgb(var(--color-text-secondary));
		font-style: italic;
	}

	.md-body :global(a) {
		color: rgb(var(--color-accent-primary));
		text-decoration: underline;
		text-underline-offset: 2px;
	}
	.md-body :global(a:hover) { opacity: 0.8; }

	.md-body :global(code) {
		font-family: ui-monospace, 'Cascadia Code', 'Fira Code', monospace;
		font-size: 0.85em;
		background: rgb(255 255 255 / 0.08);
		border-radius: 4px;
		padding: 0.1em 0.35em;
	}

	/* Shiki code blocks — override the pre/code shiki wraps */
	.md-body :global(.shiki) {
		margin: 0.5em 0 0.75em;
		border-radius: 8px;
		overflow: hidden;
		font-size: 0.82em;
	}
	.md-body :global(.shiki code) {
		background: none;
		padding: 0;
		border-radius: 0;
		font-size: inherit;
		display: block;
		padding: 1rem;
		overflow-x: auto;
	}

	.md-body :global(table) {
		width: 100%;
		border-collapse: collapse;
		margin: 0.5em 0 0.75em;
		font-size: 0.9em;
	}
	.md-body :global(th),
	.md-body :global(td) {
		border: 1px solid rgb(255 255 255 / 0.1);
		padding: 0.4em 0.6em;
		text-align: left;
	}
	.md-body :global(th) {
		background: rgb(255 255 255 / 0.06);
		font-weight: 600;
	}
	.md-body :global(tr:nth-child(even) td) {
		background: rgb(255 255 255 / 0.02);
	}

	.md-body :global(hr) {
		border: none;
		border-top: 1px solid rgb(255 255 255 / 0.1);
		margin: 1em 0;
	}

	.md-body :global(img) {
		max-width: 100%;
		border-radius: 6px;
	}
</style>
