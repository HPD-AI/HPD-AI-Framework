<script lang="ts">
  import { onMount, tick } from 'svelte';
  import { studioModuleDescriptor as baseStudioModule } from '@hpd-research/hpd-base-studio';
  import { StudioButton } from '@hpd-research/hpd-studio-design';
  import type { StudioPageProps } from '@hpd-research/hpd-studio-core';
  import type { StudioShellRuntime, StudioShellState } from './studio/shell-runtime.ts';
  import Sidebar from './studio/shell/Sidebar.svelte';
  import StudioMountedPage from './studio/shell/StudioMountedPage.svelte';
  import StudioUnavailable from './studio/shell/StudioUnavailable.svelte';

  let { runtime }: { runtime: StudioShellRuntime } = $props();
  let revision = $state(0);
  let shellState = $derived.by<StudioShellState>(() => { revision; return runtime.current; });
  let focusedPageId = '';
  let currentRoute = $derived(shellState.route);
  let currentPage = $derived(currentRoute?.route.page);
  let currentModuleId = $derived(currentPage?.moduleId ?? '');
  let documentTitle = $derived((({
    agents: 'HPD Agent Studio',
    base: 'HPD Base Studio',
    graph: 'HPD Graph Studio',
    gateway: 'HPD Gateway Studio'
  } as Readonly<Record<string, string>>)[currentModuleId] ?? 'HPD Studio'));
  let pageProps = $derived.by((): StudioPageProps | null => currentRoute ? Object.freeze({
    page: currentRoute.route.page,
    route: currentRoute.route.match, resource: currentRoute.runtime.resource,
    observation: currentRoute.runtime.snapshot(), navigation: currentRoute.runtime.navigation, commands: currentRoute.runtime.commands
  }) : null);
  let observationState = $derived((pageProps?.observation as { state?: string } | undefined)?.state ?? '');
  let observationCode = $derived((pageProps?.observation as { code?: string } | undefined)?.code ?? '');

  onMount(() => {
    const unsubscribe = runtime.subscribe(value => {
      document.documentElement.dataset.studioModule = value.route?.route.page.moduleId ?? value.kind;
      revision++;
      document.documentElement.dataset.studioAssigned = value.route?.route.page.moduleId ?? value.kind;
      void tick();
    });
    return () => { unsubscribe(); void runtime.dispose(); };
  });
  $effect(() => { const pageId = shellState.route?.route.page.pageId ?? ''; if (!pageId || pageId === focusedPageId) return;
    focusedPageId = pageId; queueMicrotask(() => document.getElementById('studio-main')?.focus()); });
  $effect(() => { document.title = documentTitle; });
</script>

{#key revision}
<div class="studio-shell-layout bg-studio-bg text-studio-ink">
  <a class="studio-skip-link" href="#studio-main">Skip to workspace</a>
  {#if shellState.kind === 'ready'}<Sidebar {runtime} state={shellState} />{/if}
  <section id="studio-main" tabindex="-1" class="studio-shell-main"
    data-module-id={currentModuleId}
    data-page-id={currentPage?.pageId ?? ''}
    data-resource-kind={pageProps?.resource?.kind ?? ''}
    data-observation-state={observationState}
    data-observation-code={observationCode}>
    {#if shellState.kind === 'authenticationRequired'}
      <main class="grid min-h-screen place-items-center p-6">
        <section class="studio-panel grid max-w-lg gap-4 p-8" aria-labelledby="sign-in-title">
          <p class="studio-label">HPD Studio</p><h1 id="sign-in-title" class="text-2xl font-extrabold">Sign in to continue</h1>
          <p class="text-sm text-studio-muted">Authentication is managed by this application. Studio never receives or stores your credential.</p>
          <StudioButton variant="primary" type="button"
            onclick={() => runtime.authentication.beginSignIn(location.pathname + location.search)}>Sign in</StudioButton>
        </section>
      </main>
    {:else if shellState.kind === 'loading'}
      <main class="grid min-h-screen place-items-center p-6" aria-live="polite"><p>Loading authorized Studio workspace…</p></main>
    {:else if shellState.kind === 'failed'}
      <main class="grid min-h-screen place-items-center p-6"><StudioUnavailable /><p class="studio-text-safe text-xs">{shellState.failure}</p></main>
    {:else if pageProps && currentModuleId === 'base'}
      {@const BasePage = baseStudioModule.pageComponents[currentPage!.pageId]!.component}
      {#key `${currentModuleId}:${currentPage?.pageId}`}
        <BasePage {...pageProps} />
      {/key}
    {:else if pageProps && currentRoute?.mounter}
      {#key `${currentModuleId}:${currentPage?.pageId}`}
        <StudioMountedPage mounter={currentRoute.mounter} props={pageProps} />
      {/key}
    {:else if pageProps && currentRoute?.component}
      {@const ModulePage = currentRoute.component}
      {#key `${currentModuleId}:${currentPage?.pageId}`}
        <ModulePage {...pageProps} />
      {/key}
    {:else}
      <StudioUnavailable />
    {/if}
  </section>
</div>
{/key}

<style>
  .studio-shell-layout {
    display: grid;
    grid-template-columns: minmax(10rem, 12.5rem) minmax(0, 1fr);
    grid-template-rows: minmax(0, 1fr);
    min-block-size: 100dvh;
  }

  .studio-shell-main {
    display: grid;
    min-block-size: 100dvh;
    min-inline-size: 0;
  }

  @media (min-width: 64rem) {
    .studio-shell-layout {
      grid-template-columns: var(--spacing-studio-sidebar) minmax(0, 1fr);
      grid-template-rows: minmax(0, 1fr);
    }
  }
</style>
