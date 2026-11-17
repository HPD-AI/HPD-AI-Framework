<script lang="ts">
  import type { Snippet } from 'svelte';
  import type { HTMLButtonAttributes } from 'svelte/elements';

  type Variant = 'default' | 'primary' | 'danger';
  type Size = 'default' | 'small';
  interface Props extends HTMLButtonAttributes {
    variant?: Variant;
    size?: Size;
    loading?: boolean;
    children?: Snippet;
  }

  let {
    variant = 'default',
    size = 'default',
    loading = false,
    disabled = false,
    class: className = '',
    children,
    ...rest
  }: Props = $props();

  const variantClass: Readonly<Record<Variant, string>> = Object.freeze({
    default: '',
    primary: 'studio-button-primary',
    danger: 'studio-button-danger'
  });
  const classes = $derived([
    'studio-button',
    variantClass[variant],
    size === 'small' ? 'studio-button-sm' : '',
    className
  ].filter(Boolean).join(' '));
</script>

<button {...rest} class={classes} disabled={disabled || loading} aria-busy={loading || undefined}>
  {#if loading}
    <span class="size-3 animate-spin rounded-full border-2 border-current border-r-transparent" aria-hidden="true"></span>
    <span class="sr-only">Working</span>
  {/if}
  {@render children?.()}
</button>
