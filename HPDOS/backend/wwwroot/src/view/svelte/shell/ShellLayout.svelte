<script lang="ts">
  import type { ShellLayoutController } from "./controller";
  import { attachShellResize } from "./resize.svelte";
  import { shellMode } from "./layout";

  type Props = {
    shell: ShellLayoutController;
  };

  let { shell }: Props = $props();

  const shellState = $derived(shell.state);
  const mode = $derived(shellMode($shellState.sidebarCollapsed));
</script>

<div class="hpd-view" id="view">
  <section
    class="hpd-shell"
    id="chatShell"
    data-layout-mode={mode}
    data-hydrated={$shellState.hydrated ? "true" : "false"}
  >
    {#if !$shellState.sidebarCollapsed}
      <section class="hpd-edge-pane hpd-edge-pane-left" id="hpd-shell-sidebar" aria-label="Sidebar">
        <section class="hpd-edge-pane-body"></section>
      </section>
    {/if}
    <section class="hpd-workspace-pane" id="mainFrame" aria-label="Workspace">
      <div class="hpd-workspace-strip" aria-label="Workspace tabs"></div>
      <section class="hpd-workspace-blank" aria-label="Workspace surface"></section>
    </section>
    <section
      class="hpd-app-slot"
      aria-label="App section"
    >
      <!-- svelte-ignore a11y_no_noninteractive_tabindex - focusable separator is the ARIA splitter pattern. -->
      <div
        {@attach attachShellResize(shell)}
        class="hpd-app-resize-handle"
        role="separator"
        tabindex="0"
        aria-label="Resize app section"
        aria-orientation="vertical"
        aria-controls="mainFrame hpd-app-host"
      ></div>
      <section class="hpd-app-pane" id="hpd-app-host" aria-label="App host">
        <div class="hpd-app-pane-strip"></div>
        <section class="hpd-app-pane-body">
          <div class="hpd-app-pane-scroll">
            <div class="hpd-app-pane-content"></div>
          </div>
        </section>
      </section>
    </section>
  </section>
</div>
