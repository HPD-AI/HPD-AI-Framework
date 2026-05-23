<script lang="ts">
  import Composer from "./Composer.svelte";
  import Sidebar from "./Sidebar.svelte";
  import WorkspaceSurface from "./WorkspaceSurface.svelte";
  import type { AppShellProps } from "./types.js";

  let {
    appState,
    actions,
    draft,
    artifactViews,
    sidebarView,
    setSidebarView
  }: AppShellProps = $props();
</script>

<main class="hpd-app">
  <div class="hpd-view" id="view">
    <section class="hpd-shell" id="chatShell">
      <Sidebar {appState} {actions} {artifactViews} {sidebarView} {setSidebarView} />
      <section class="hpd-panel hpd-main-frame" id="mainFrame">
        <WorkspaceSurface {appState} {actions} {artifactViews} showDashboard={sidebarView === "workspaceSessions"} />
        <Composer {appState} {actions} {draft} />
      </section>
    </section>
  </div>
  <div class="hpd-toast" data-visible={String(Boolean(appState.error))} id="toast">
    {appState.error || ""}
  </div>
</main>
