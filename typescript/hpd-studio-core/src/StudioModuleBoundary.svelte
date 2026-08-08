<script lang="ts">
  import type { Snippet } from 'svelte';
  import type { StudioModuleContextHandle } from './contracts.ts';
  import { resolveModuleContext } from './context-internal.ts';
  import { provideStudioModuleContext } from './module-context.svelte.ts';

  let { context, children }: { context: StudioModuleContextHandle; children?: Snippet } = $props();
  // svelte-ignore state_referenced_locally
  provideStudioModuleContext(resolveModuleContext(context));
</script>

{@render children?.()}
