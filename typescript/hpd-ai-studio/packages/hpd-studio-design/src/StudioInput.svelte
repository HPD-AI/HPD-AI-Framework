<script lang="ts">
  import type { HTMLInputAttributes } from 'svelte/elements';

  interface Props extends HTMLInputAttributes {
    class?: string;
  }

  let { class: className = '', ...rest }: Props = $props();
  const classes = $derived(`studio-input ${className}`.trim());
</script>

<input {...rest} class={classes} />
