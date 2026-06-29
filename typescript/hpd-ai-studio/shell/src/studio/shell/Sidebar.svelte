<script lang="ts">
  import type { StudioController } from '../types';

  let { studio }: { studio: StudioController } = $props();
</script>

<aside class="flex flex-col gap-6 bg-studio-nav px-5 py-5 text-studio-nav-ink">
  <a
    class="flex items-center gap-3 text-inherit no-underline"
    href={`#${studio.defaultRoute.path}`}
    aria-label={studio.state.config.productTitle}
  >
    <span class="grid size-9 place-items-center rounded-studio-sm bg-studio-brand text-studio-nav font-extrabold">
      H
    </span>
    <span class="min-w-0">
      <strong class="studio-truncate block text-base font-extrabold">{studio.state.config.productTitle}</strong>
      <small class="studio-truncate mt-0.5 block text-xs text-studio-nav-muted">{studio.state.config.apiBasePath}</small>
    </span>
  </a>

  <label class="grid gap-2">
    <span class="studio-label-on-nav">Module</span>
    <select
      class="studio-nav-control"
      bind:value={() => studio.activeModule.id, (moduleId) => studio.selectModule(moduleId)}
      aria-label="Studio module"
    >
      {#each studio.moduleCatalog as module (module.id)}
        <option value={module.id} disabled={!module.isLive}>
          {module.label}{module.isLive ? '' : ' - planned'}
        </option>
      {/each}
    </select>
  </label>

  {#if studio.navItems.length > 0}
    <nav
      class="studio-nav-divider grid gap-1 pt-4"
      aria-label={`${studio.activeModule.title} navigation`}
    >
      <span class="studio-label-on-nav mb-1">{studio.activeModule.title}</span>
      {#each studio.navItems as item}
        <a
          class="studio-nav-item"
          href={`#${item.path}`}
          title={item.summary}
          aria-current={studio.state.activeRoute === item.path ? 'page' : undefined}
        >
          {item.label}
        </a>
      {/each}
    </nav>
  {/if}
</aside>
