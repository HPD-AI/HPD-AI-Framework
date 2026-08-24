<script lang="ts">
  import type { Snippet } from 'svelte';
  import type { StudioShellRuntime, StudioShellState } from '../shell-runtime.ts';
  import Sidebar from './Sidebar.svelte';
  let { runtime, state, main }: { runtime: StudioShellRuntime; state: StudioShellState; main?: Snippet } = $props();
</script>

<div class="studio-shell-layout bg-studio-bg text-studio-ink">
  <a class="studio-skip-link" href="#studio-main">Skip to workspace</a>
  {#if state.kind === 'ready'}<Sidebar {runtime} {state} />{/if}
  <section id="studio-main" tabindex="-1" class="studio-shell-main">{@render main?.()}</section>
</div>

<style>
  .studio-shell-layout {
    display: grid;
    grid-template-columns: minmax(10rem, 12.5rem) minmax(0, 1fr);
    grid-template-rows: minmax(0, 1fr);
    min-block-size: 100dvh;
  }

  .studio-shell-main {
    display: grid;
    min-block-size: 100dvh;
    min-inline-size: 0;
  }

  @media (min-width: 64rem) {
    .studio-shell-layout {
      grid-template-columns: var(--spacing-studio-sidebar) minmax(0, 1fr);
      grid-template-rows: minmax(0, 1fr);
    }
  }
</style>
