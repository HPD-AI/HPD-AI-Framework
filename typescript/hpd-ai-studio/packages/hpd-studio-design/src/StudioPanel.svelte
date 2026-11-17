<script lang="ts">
  import type { Snippet } from 'svelte';

  interface Props {
    title?: string;
    description?: string;
    labelledBy?: string;
    class?: string;
    children?: Snippet;
  }

  let { title, description, labelledBy, class: className = '', children }: Props = $props();
</script>

<section class={`studio-panel grid gap-3 p-4 ${className}`.trim()} aria-labelledby={labelledBy}>
  {#if title || description}
    <header class="grid gap-1">
      {#if title}<h2 class="text-heading">{title}</h2>{/if}
      {#if description}<p class="studio-text-safe text-body-sm text-studio-muted">{description}</p>{/if}
    </header>
  {/if}
  {@render children?.()}
</section>
