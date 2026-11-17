<script lang="ts">
  import type { Snippet } from 'svelte';

  type Tone = 'info' | 'success' | 'warning' | 'danger';
  interface Props {
    tone?: Tone;
    title?: string;
    children?: Snippet;
  }

  let { tone = 'info', title, children }: Props = $props();
  const toneClass: Readonly<Record<Tone, string>> = Object.freeze({
    info: 'studio-status-info',
    success: 'studio-status-good',
    warning: 'studio-status-warning',
    danger: 'studio-status-danger'
  });
  const classes = $derived(`studio-status ${toneClass[tone]}`);
  const role = $derived(tone === 'danger' || tone === 'warning' ? 'alert' : 'status');
</script>

<div class={classes} {role}>
  {#if title}<strong class="block">{title}</strong>{/if}
  <div class="text-body-sm">{@render children?.()}</div>
</div>
