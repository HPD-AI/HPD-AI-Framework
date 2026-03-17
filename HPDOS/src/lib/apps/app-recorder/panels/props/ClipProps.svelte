<script lang="ts">
    import type { AppRecorderState } from '../../AppRecorderState.svelte';

    let { editor }: { editor: AppRecorderState } = $props();

    const clip = $derived(editor.selectedClip);
    const meta = $derived(clip ? editor.clipMetadata.get(clip.id) ?? null : null);

    function formatMs(ms: number): string {
        const s = ms / 1000;
        const m = Math.floor(s / 60);
        return `${m}:${String(Math.floor(s % 60)).padStart(2, '0')}.${String(Math.floor((s % 1) * 10))}`;
    }
</script>

{#if clip}
    <div class="clip-props">
        <!-- Filename -->
        <div class="prop-section">
            <div class="prop-row">
                <span class="prop-label">File</span>
                <span class="prop-value" title={clip.path}>{clip.path.split('/').pop()}</span>
            </div>
        </div>

        <!-- Timeline position -->
        <div class="prop-section">
            <div class="section-title">Timeline</div>
            <div class="prop-row">
                <span class="prop-label">Position</span>
                <span class="prop-value mono">{formatMs(clip.position)}</span>
            </div>
            <div class="prop-row">
                <span class="prop-label">Duration</span>
                <span class="prop-value mono">{formatMs(clip.end - clip.start)}</span>
            </div>
        </div>

        <!-- Source trim -->
        <div class="prop-section">
            <div class="section-title">Trim</div>
            <div class="prop-row">
                <span class="prop-label">In</span>
                <span class="prop-value mono">{formatMs(clip.start)}</span>
            </div>
            <div class="prop-row">
                <span class="prop-label">Out</span>
                <span class="prop-value mono">{formatMs(clip.end)}</span>
            </div>
        </div>

        <!-- Media info -->
        {#if meta}
            <div class="prop-section">
                <div class="section-title">Source</div>
                <div class="prop-row">
                    <span class="prop-label">Size</span>
                    <span class="prop-value mono">{meta.width}×{meta.height}</span>
                </div>
                <div class="prop-row">
                    <span class="prop-label">FPS</span>
                    <span class="prop-value mono">{meta.fps}</span>
                </div>
            </div>
        {/if}

        <!-- Delete -->
        <div class="prop-section">
            <button
                class="delete-btn"
                onclick={() => editor.removeClip(clip!.id)}
            >Remove Clip</button>
        </div>
    </div>
{/if}

<style>
    .clip-props {
        padding: 0.5rem 0;
        display: flex;
        flex-direction: column;
        gap: 0;
    }

    .prop-section {
        padding: 0.35rem 0.75rem;
        border-bottom: 1px solid rgb(var(--color-border-default) / 0.3);
    }

    .section-title {
        font-size: 0.6rem;
        font-weight: 600;
        text-transform: uppercase;
        letter-spacing: 0.06em;
        color: rgb(var(--color-text-tertiary));
        margin-bottom: 0.3rem;
    }

    .prop-row {
        display: flex;
        align-items: center;
        justify-content: space-between;
        min-height: 22px;
    }

    .prop-label {
        font-size: 0.72rem;
        color: rgb(var(--color-text-secondary));
        flex-shrink: 0;
    }

    .prop-value {
        font-size: 0.72rem;
        color: rgb(var(--color-text-primary));
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
        max-width: 65%;
        text-align: right;
    }

    .prop-value.mono {
        font-variant-numeric: tabular-nums;
        font-size: 0.68rem;
    }

    .delete-btn {
        width: 100%;
        padding: 0.35rem 0;
        background: rgb(239 68 68 / 0.08);
        border: 1px solid rgb(239 68 68 / 0.3);
        border-radius: var(--radius-sm);
        color: rgb(239 68 68);
        font-size: 0.72rem;
        cursor: pointer;
        transition: background var(--duration-fast);
        margin-top: 0.25rem;
    }

    .delete-btn:hover {
        background: rgb(239 68 68 / 0.18);
    }
</style>
