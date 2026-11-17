<script lang="ts">
  import type { Snippet } from 'svelte';

  interface Props {
    eyebrow?: string;
    title: string;
    description?: string;
    actions?: Snippet;
  }

  let { eyebrow, title, description, actions }: Props = $props();
</script>

<header class="studio-workspace-header">
  <div class="min-w-0">
    {#if eyebrow}<p class="studio-label">{eyebrow}</p>{/if}
    <h1>{title}</h1>
    {#if description}<p class="studio-text-safe text-body-sm text-studio-muted">{description}</p>{/if}
  </div>
  {#if actions}<div class="flex flex-wrap gap-2">{@render actions()}</div>{/if}
</header>
