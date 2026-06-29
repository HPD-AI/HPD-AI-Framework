<script lang="ts">
  import type { Snippet } from 'svelte';
  import Sidebar from './Sidebar.svelte';
  import { setStudioContext } from '../state/studioContext';
  import type { StudioController } from '../types';

  let { studio, main }: { studio: StudioController; main?: Snippet } = $props();

  setStudioContext(() => studio);
</script>

<div class="grid min-h-screen grid-cols-[var(--spacing-studio-sidebar)_minmax(0,1fr)] bg-studio-bg text-studio-ink">
  <Sidebar {studio} />

  <section class="grid min-h-screen min-w-0">
    {@render main?.()}
  </section>
</div>
