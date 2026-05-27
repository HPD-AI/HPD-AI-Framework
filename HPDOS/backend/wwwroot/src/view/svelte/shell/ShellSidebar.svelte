<script lang="ts">
  import type { ShellController, ShellRoute } from "./controller";

  type Props = {
    shell: ShellController;
  };

  type RouteItem = {
    route: ShellRoute;
    label: string;
    icon: "chat" | "automation" | "settings";
    placement: "primary" | "secondary";
  };

  let { shell }: Props = $props();

  const shellState = $derived(shell.state);
  const routeItems: RouteItem[] = [
    { route: "chat", label: "Chat", icon: "chat", placement: "primary" },
    { route: "automations", label: "Automations", icon: "automation", placement: "primary" },
    { route: "settings", label: "Settings", icon: "settings", placement: "secondary" }
  ];

  const primaryItems = $derived(routeItems.filter((item) => item.placement === "primary"));
  const secondaryItems = $derived(routeItems.filter((item) => item.placement === "secondary"));
</script>

<aside class="hpd-shell-sidebar" id="hpd-shell-sidebar" aria-label="Sidebar">
  <nav class="hpd-shell-sidebar-nav" aria-label="Primary">
    {#each primaryItems as item}
      <button
        class="hpd-shell-sidebar-item"
        type="button"
        aria-current={$shellState.activeRoute === item.route ? "page" : undefined}
        title={item.label}
        onclick={() => shell.setRoute(item.route)}
      >
        {@render ShellSidebarIcon(item.icon)}
        <span>{item.label}</span>
      </button>
    {/each}
  </nav>

  <nav class="hpd-shell-sidebar-nav hpd-shell-sidebar-nav-secondary" aria-label="Secondary">
    {#each secondaryItems as item}
      <button
        class="hpd-shell-sidebar-item"
        type="button"
        aria-current={$shellState.activeRoute === item.route ? "page" : undefined}
        title={item.label}
        onclick={() => shell.setRoute(item.route)}
      >
        {@render ShellSidebarIcon(item.icon)}
        <span>{item.label}</span>
      </button>
    {/each}
  </nav>
</aside>

{#snippet ShellSidebarIcon(icon: RouteItem["icon"])}
  <svg class="hpd-shell-sidebar-icon" aria-hidden="true" viewBox="0 0 24 24" fill="none">
    {#if icon === "chat"}
      <path d="M5 6.5A3.5 3.5 0 0 1 8.5 3h7A3.5 3.5 0 0 1 19 6.5v4A3.5 3.5 0 0 1 15.5 14H11l-4 4v-4.25A3.5 3.5 0 0 1 5 10.5v-4Z" />
    {:else if icon === "automation"}
      <path d="M6 7h12" />
      <path d="M8 7a4 4 0 1 1 8 0" />
      <path d="M7 13h10" />
      <path d="M9 13a3 3 0 1 0 6 0" />
      <path d="M12 16v5" />
    {:else}
      <circle cx="12" cy="12" r="3" />
      <path d="M19 12a7.5 7.5 0 0 0-.1-1.2l2-1.5-2-3.5-2.4 1a7.3 7.3 0 0 0-2-1.2L14 3h-4l-.5 2.6a7.3 7.3 0 0 0-2 1.2l-2.4-1-2 3.5 2 1.5A7.5 7.5 0 0 0 5 12c0 .4 0 .8.1 1.2l-2 1.5 2 3.5 2.4-1a7.3 7.3 0 0 0 2 1.2L10 21h4l.5-2.6a7.3 7.3 0 0 0 2-1.2l2.4 1 2-3.5-2-1.5c.1-.4.1-.8.1-1.2Z" />
    {/if}
  </svg>
{/snippet}
