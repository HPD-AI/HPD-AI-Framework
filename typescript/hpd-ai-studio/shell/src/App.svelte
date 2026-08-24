<script lang="ts">
  import { onMount } from 'svelte';
  import { studioModuleDescriptor as baseStudioModule } from '@hpd-research/hpd-base-studio';
  import type { StudioPageProps } from '@hpd-research/hpd-studio-core';
  import type { StudioShellRuntime, StudioShellState } from './studio/shell-runtime.ts';
  import StudioShell from './studio/shell/StudioShell.svelte';
  import StudioUnavailable from './studio/shell/StudioUnavailable.svelte';

  let { runtime }: { runtime: StudioShellRuntime } = $props();
  // svelte-ignore state_referenced_locally
  let state: StudioShellState = $state(runtime.current);
  let Page = $derived(state.route?.route.page.moduleId === 'base'
    ? baseStudioModule.pageComponents[state.route.route.page.pageId]?.component
    : state.route?.component);
  let focusedPageId = '';
  let pageProps = $derived.by((): StudioPageProps | null => state.route ? Object.freeze({
    page: state.route.route.page,
    route: state.route.route.match, resource: state.route.runtime.resource,
    observation: state.route.runtime.snapshot(), navigation: state.route.runtime.navigation, commands: state.route.runtime.commands
  }) : null);

  onMount(() => {
    const unsubscribe = runtime.subscribe(value => state = value);
    return () => { unsubscribe(); void runtime.dispose(); };
  });
  $effect(() => { const pageId = state.route?.route.page.pageId ?? ''; if (!pageId || pageId === focusedPageId) return;
    focusedPageId = pageId; queueMicrotask(() => document.getElementById('studio-main')?.focus()); });
</script>

<StudioShell {runtime} {state}>
  {#snippet main()}
    {#if state.kind === 'authenticationRequired'}
      <main class="grid min-h-screen place-items-center p-6">
        <section class="studio-panel grid max-w-lg gap-4 p-8" aria-labelledby="sign-in-title">
          <p class="studio-label">HPD BASE Studio</p><h1 id="sign-in-title" class="text-2xl font-extrabold">Sign in to continue</h1>
          <p class="text-sm text-studio-muted">Authentication is managed by this application. Studio never receives or stores your credential.</p>
          <button class="studio-button-primary" type="button" onclick={() => runtime.authentication.beginSignIn(location.pathname + location.search)}>Sign in</button>
        </section>
      </main>
    {:else if state.kind === 'loading'}
      <main class="grid min-h-screen place-items-center p-6" aria-live="polite"><p>Loading authorized Studio workspace…</p></main>
    {:else if state.kind === 'failed'}
      <main class="grid min-h-screen place-items-center p-6"><StudioUnavailable /><p class="studio-text-safe text-xs">{state.failure}</p></main>
    {:else if Page && pageProps}
      {#key state.route?.route.page.pageId}
        <Page {...pageProps} />
      {/key}
    {:else}
      <StudioUnavailable />
    {/if}
  {/snippet}
</StudioShell>
