<script lang="ts">
  import type { Snippet } from 'svelte';

  interface Props {
    label: string;
    for?: string;
    hint?: string;
    error?: string;
    required?: boolean;
    children?: Snippet;
  }

  let { label, for: htmlFor, hint, error, required = false, children }: Props = $props();
</script>

<div class="grid gap-1 text-body">
  <label class="font-semibold" for={htmlFor}>
    {label}{#if required}<span aria-hidden="true"> *</span>{/if}
  </label>
  {@render children?.()}
  {#if hint}<p class="studio-text-safe text-body-sm text-studio-muted" id={htmlFor ? `${htmlFor}-hint` : undefined}>{hint}</p>{/if}
  {#if error}<p class="studio-text-safe text-body-sm text-studio-danger" role="alert" id={htmlFor ? `${htmlFor}-error` : undefined}>{error}</p>{/if}
</div>
