<script lang="ts">
  import type { Snippet } from 'svelte';

  interface Props {
    title: string;
    description?: string;
    children?: Snippet;
  }

  let { title, description, children }: Props = $props();
</script>

<div class="studio-empty" role="status">
  <strong>{title}</strong>
  {#if description}<span class="studio-text-safe">{description}</span>{/if}
  {@render children?.()}
</div>
