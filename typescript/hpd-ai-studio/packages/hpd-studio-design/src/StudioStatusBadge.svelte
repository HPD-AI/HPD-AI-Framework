<script lang="ts">
  import type { Snippet } from 'svelte';

  type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';
  interface Props {
    tone?: Tone;
    children?: Snippet;
  }

  let { tone = 'neutral', children }: Props = $props();
  const toneClass: Readonly<Record<Tone, string>> = Object.freeze({
    neutral: '',
    info: 'studio-badge-info',
    success: 'studio-badge-good',
    warning: 'studio-badge-warning',
    danger: 'studio-badge-danger'
  });
  const classes = $derived(`studio-badge ${toneClass[tone]}`.trim());
</script>

<span class={classes}>{@render children?.()}</span>
