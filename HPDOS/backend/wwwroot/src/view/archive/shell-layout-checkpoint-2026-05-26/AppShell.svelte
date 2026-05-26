<script lang="ts">
  import WorkspaceSurface from "./WorkspaceSurface.svelte";

  let sidebarCollapsed = false;
  let shellElement: HTMLElement;
  let expandedSplitRatio: number | null = null;
  let collapsedSplitRatio: number | null = null;

  $: activeSplitRatio = (sidebarCollapsed ? collapsedSplitRatio : expandedSplitRatio) ?? (sidebarCollapsed ? 0.65 : 0.45);
  $: shellStyle = `--hpd-middle-pane-share: ${1 - activeSplitRatio}fr; --hpd-right-pane-share: ${activeSplitRatio}fr;`;

  function startAppPaneResize(event: PointerEvent) {
    if (!shellElement) return;

    event.preventDefault();
    document.body.dataset.hpdPaneResizing = "true";

    const updateAppPaneWidth = (clientX: number) => {
      const shellRect = shellElement.getBoundingClientRect();
      const sidebarWidth = sidebarCollapsed
        ? 0
        : shellElement.querySelector(".hpd-shell-pane-left")?.getBoundingClientRect().width ?? 0;
      const shellStyles = getComputedStyle(shellElement);
      const gap = Number.parseFloat(shellStyles.columnGap) || 0;
      const activeGaps = sidebarCollapsed ? 1 : 2;
      const resizableWidth = Math.max(1, shellRect.width - sidebarWidth - gap * activeGaps);
      const minAppPaneWidth = 384;
      const minMiddlePaneWidth = 320;
      const hardMinAppShare = Math.min(0.9, minAppPaneWidth / resizableWidth);
      const hardMaxAppShare = Math.max(hardMinAppShare, 1 - minMiddlePaneWidth / resizableWidth);
      const semanticMinAppShare = sidebarCollapsed ? 0.5 : 0.3;
      const semanticMaxAppShare = sidebarCollapsed ? 0.75 : 0.55;
      const minAppShare = Math.max(hardMinAppShare, semanticMinAppShare);
      const maxAppShare = Math.min(hardMaxAppShare, semanticMaxAppShare);
      const nextShare = (shellRect.right - clientX) / resizableWidth;
      const nextSplitRatio = Math.min(Math.max(nextShare, minAppShare), maxAppShare);

      if (sidebarCollapsed) {
        collapsedSplitRatio = nextSplitRatio;
      } else {
        expandedSplitRatio = nextSplitRatio;
      }
    };

    const handlePointerMove = (moveEvent: PointerEvent) => {
      updateAppPaneWidth(moveEvent.clientX);
    };

    const stopResize = () => {
      delete document.body.dataset.hpdPaneResizing;
      window.removeEventListener("pointermove", handlePointerMove);
      window.removeEventListener("pointerup", stopResize);
      window.removeEventListener("pointercancel", stopResize);
    };

    updateAppPaneWidth(event.clientX);
    window.addEventListener("pointermove", handlePointerMove);
    window.addEventListener("pointerup", stopResize);
    window.addEventListener("pointercancel", stopResize);
  }
</script>

<main class="hpd-app">
  <div class="hpd-window-chrome" aria-label="Window controls">
    <button
      class="hpd-window-sidebar-button"
      type="button"
      aria-label="Toggle sidebar"
      aria-pressed={sidebarCollapsed}
      title="Sidebar"
      onclick={() => {
        sidebarCollapsed = !sidebarCollapsed;
      }}
    >
      <svg class="hpd-window-sidebar-icon" aria-hidden="true" viewBox="0 0 24 24" fill="none">
        <rect x="4" y="5" width="16" height="14" rx="2" />
        <path d="M9 5V19" />
      </svg>
    </button>
  </div>
  <div class="hpd-view" id="view">
    <section
      bind:this={shellElement}
      class="hpd-shell"
      id="chatShell"
      data-sidebar-collapsed={sidebarCollapsed}
      style={shellStyle}
    >
      <section class="hpd-shell-pane hpd-shell-pane-left" aria-label="Sidebar">
        <section class="hpd-shell-pane-body"></section>
      </section>
      <WorkspaceSurface />
      <section class="hpd-shell-pane hpd-shell-pane-right" aria-label="Inspector">
        <button
          class="hpd-pane-resize-handle"
          type="button"
          aria-label="Resize app section"
          onpointerdown={startAppPaneResize}
        ></button>
        <div class="hpd-shell-pane-strip"></div>
        <section class="hpd-shell-pane-body hpd-app-pane-scroll">
          <section class="hpd-app-pane-surface"></section>
        </section>
      </section>
    </section>
  </div>
</main>
