<script lang="ts">
  import type { StudioDisplayColumn, StudioDisplayRow } from './types.ts';
  import { nextStudioGridFocusIndex } from './grid-focus.ts';
  let { caption, columns, rows, selectedId = null, onselect = () => {} }: { caption: string; columns: readonly StudioDisplayColumn[];
    rows: readonly StudioDisplayRow[]; selectedId?: string | null; onselect?: (id: string) => void } = $props();
  let focused = $state(0);
  function move(event: KeyboardEvent, index: number): void {
    if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) return;
    event.preventDefault(); focused = nextStudioGridFocusIndex(index, rows.length, event.key);
    queueMicrotask(() => document.querySelector<HTMLElement>(`[data-studio-row-index="${focused}"]`)?.focus()); }
</script>

<div class="studio-grid-frame" role="region" aria-label={caption}>
  <table class="studio-grid-table">
    <caption class="sr-only">{caption}. {rows.length} disclosed rows.</caption>
    <thead><tr>{#each columns as column (column.id)}<th scope="col" class:studio-column-wide={column.width === 'wide'}>{column.label}</th>{/each}</tr></thead>
    <tbody>
      {#each rows as row, index (row.id)}
        <tr class:studio-row-selected={row.id === selectedId} tabindex={focused === index ? 0 : -1} data-studio-row-index={index}
          aria-selected={row.id === selectedId} onkeydown={event => move(event, index)} onclick={() => onselect(row.id)}>
          {#each columns as column (column.id)}<td>{row.cells[column.id] ?? 'Unavailable'}</td>{/each}
        </tr>
      {:else}<tr><td colspan={Math.max(1, columns.length)}><div class="studio-empty"><strong>No disclosed items</strong><span>This finite view returned no rows.</span></div></td></tr>{/each}
    </tbody>
  </table>
</div>
