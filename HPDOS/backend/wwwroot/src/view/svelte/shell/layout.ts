export type ShellLayoutMode = "expanded" | "collapsed";

export type ShellSplitPolicy = {
  defaultAppPaneShare: number;
  minAppPaneWidth: number;
  minWorkspacePaneWidth: number;
};

export const shellSplitPolicies: Record<ShellLayoutMode, ShellSplitPolicy> = {
  expanded: {
    defaultAppPaneShare: 0.45,
    minAppPaneWidth: 288,
    minWorkspacePaneWidth: 416
  },
  collapsed: {
    defaultAppPaneShare: 0.65,
    minAppPaneWidth: 384,
    minWorkspacePaneWidth: 320
  }
};

export function shellMode(sidebarCollapsed: boolean): ShellLayoutMode {
  return sidebarCollapsed ? "collapsed" : "expanded";
}

export function defaultAppPaneWidth(mode: ShellLayoutMode, resizableWidth: number): number {
  return clampAppPaneWidth(mode, resizableWidth * shellSplitPolicies[mode].defaultAppPaneShare, resizableWidth);
}

export function appPaneWidthBounds(
  mode: ShellLayoutMode,
  resizableWidth: number
): { min: number; max: number } {
  const policy = shellSplitPolicies[mode];
  const minimumTotalWidth = policy.minAppPaneWidth + policy.minWorkspacePaneWidth;

  if (resizableWidth <= minimumTotalWidth) {
    const degradedWidth = resizableWidth * (policy.minAppPaneWidth / minimumTotalWidth);
    return { min: degradedWidth, max: degradedWidth };
  }

  return {
    min: policy.minAppPaneWidth,
    max: resizableWidth - policy.minWorkspacePaneWidth
  };
}

export function clampAppPaneWidth(
  mode: ShellLayoutMode,
  appPaneWidth: number,
  resizableWidth: number
): number {
  const requestedWidth = Math.max(0, appPaneWidth);
  const { min, max } = appPaneWidthBounds(mode, resizableWidth);

  return Math.min(Math.max(requestedWidth, min), max);
}
