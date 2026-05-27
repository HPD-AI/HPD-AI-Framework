export type ChatLayoutMode = "expanded" | "collapsed";

export type ChatSplitPolicy = {
  defaultAppPaneShare: number;
  minAppPaneWidth: number;
  minWorkspacePaneWidth: number;
};

export const chatSplitPolicies: Record<ChatLayoutMode, ChatSplitPolicy> = {
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

export function chatLayoutMode(sidebarCollapsed: boolean): ChatLayoutMode {
  return sidebarCollapsed ? "collapsed" : "expanded";
}

export function defaultAppPaneWidth(mode: ChatLayoutMode, resizableWidth: number): number {
  return clampAppPaneWidth(mode, resizableWidth * chatSplitPolicies[mode].defaultAppPaneShare, resizableWidth);
}

export function appPaneWidthBounds(
  mode: ChatLayoutMode,
  resizableWidth: number
): { min: number; max: number } {
  const policy = chatSplitPolicies[mode];
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
  mode: ChatLayoutMode,
  appPaneWidth: number,
  resizableWidth: number
): number {
  const requestedWidth = Math.max(0, appPaneWidth);
  const { min, max } = appPaneWidthBounds(mode, resizableWidth);

  return Math.min(Math.max(requestedWidth, min), max);
}
