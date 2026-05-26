import {
  appPaneWidthBounds,
  clampAppPaneWidth,
  defaultAppPaneWidth,
  shellMode,
  type ShellLayoutMode
} from "./layout";
import {
  createDesktopShellLayoutStorage,
  defaultShellLayoutSnapshot,
  type ShellLayoutSnapshot,
  type ShellLayoutStorage
} from "./storage";
import { writable, type Readable } from "svelte/store";

export type ShellPaneLayout = {
  mode: ShellLayoutMode;
  resizableWidth: number;
  workspacePaneWidth: number;
  appPaneWidth: number;
  minAppPaneWidth: number;
  maxAppPaneWidth: number;
  appPaneShare: number;
};

export type ShellLayoutControllerOptions = {
  storage?: ShellLayoutStorage | null;
  initialSnapshot?: Partial<ShellLayoutSnapshot>;
};

export type ShellLayoutState = ShellLayoutSnapshot & {
  hydrated: boolean;
};

const keyboardStep = 24;
const largeKeyboardStep = keyboardStep * 4;
const minimumResizableWidth = 160;

export function appPaneWidthForKeyboardResize(
  key: string,
  mode: ShellLayoutMode,
  currentAppPaneWidth: number,
  resizableWidth: number,
  shiftKey = false
): number | null {
  const { min, max } = appPaneWidthBounds(mode, resizableWidth);
  const step = shiftKey ? largeKeyboardStep : keyboardStep;

  switch (key) {
    case "ArrowLeft":
      return currentAppPaneWidth + step;
    case "ArrowRight":
      return currentAppPaneWidth - step;
    case "Home":
      return min;
    case "End":
      return max;
    case "Enter":
      return defaultAppPaneWidth(mode, resizableWidth);
    default:
      return null;
  }
}

export class ShellLayoutController {
  #sidebarCollapsed: boolean;
  #resizableWidth = 0;
  #expandedAppPaneWidth: number | null = null;
  #collapsedAppPaneWidth: number | null = null;
  #hydrated: boolean;
  #storage: ShellLayoutStorage | null;
  #stateStore;

  public constructor(options: ShellLayoutControllerOptions = {}) {
    this.#storage = options.storage ?? null;
    const snapshot = {
      ...defaultShellLayoutSnapshot(),
      ...this.#storage?.load(),
      ...options.initialSnapshot
    };

    this.#sidebarCollapsed = snapshot.sidebarCollapsed;
    this.#expandedAppPaneWidth = snapshot.expandedAppPaneWidth;
    this.#collapsedAppPaneWidth = snapshot.collapsedAppPaneWidth;
    this.#hydrated = this.#storage?.hydrate === undefined;
    this.#stateStore = writable(this.stateSnapshot);
  }

  public get state(): Readable<ShellLayoutState> {
    return this.#stateStore;
  }

  public get snapshot(): ShellLayoutSnapshot {
    return {
      sidebarCollapsed: this.#sidebarCollapsed,
      expandedAppPaneWidth: this.#expandedAppPaneWidth,
      collapsedAppPaneWidth: this.#collapsedAppPaneWidth
    };
  }

  public get stateSnapshot(): ShellLayoutState {
    return {
      ...this.snapshot,
      hydrated: this.#hydrated
    };
  }

  public get hydrated(): boolean {
    return this.#hydrated;
  }

  public get sidebarCollapsed(): boolean {
    return this.#sidebarCollapsed;
  }

  public get mode(): ShellLayoutMode {
    return shellMode(this.#sidebarCollapsed);
  }

  public toggleSidebar(): ShellPaneLayout | null {
    return this.setSidebarCollapsed(!this.#sidebarCollapsed);
  }

  public setSidebarCollapsed(sidebarCollapsed: boolean, commit = true): ShellPaneLayout | null {
    this.#sidebarCollapsed = sidebarCollapsed;
    this.#publish();
    if (commit) this.commit();

    return this.currentLayout();
  }

  public measure(resizableWidth: number): ShellPaneLayout | null {
    this.#resizableWidth = Math.max(1, resizableWidth);
    return this.currentLayout();
  }

  public currentLayout(): ShellPaneLayout | null {
    if (this.#resizableWidth < minimumResizableWidth) return null;

    return this.#layoutFor(this.mode, this.#appPaneWidthForMode(this.mode), this.#resizableWidth);
  }

  public resizeAppPane(
    appPaneWidth: number,
    mode = this.mode,
    resizableWidth = this.#resizableWidth,
    commit = false
  ): ShellPaneLayout | null {
    if (resizableWidth < minimumResizableWidth) return null;

    const layout = this.#layoutFor(mode, appPaneWidth, resizableWidth);
    this.#setAppPaneWidthForMode(mode, layout.appPaneWidth, commit);
    if (commit) this.commit();

    return layout;
  }

  public resizeFromClientX(
    shellRight: number,
    clientX: number,
    mode = this.mode,
    resizableWidth = this.#resizableWidth,
    commit = false
  ): ShellPaneLayout | null {
    return this.resizeAppPane(shellRight - clientX, mode, resizableWidth, commit);
  }

  public keyboardResize(key: string, shiftKey = false): ShellPaneLayout | null {
    if (this.#resizableWidth < minimumResizableWidth) return null;

    const mode = this.mode;
    const currentAppPaneWidth = this.#appPaneWidthForMode(mode);
    const nextAppPaneWidth = appPaneWidthForKeyboardResize(
      key,
      mode,
      currentAppPaneWidth,
      this.#resizableWidth,
      shiftKey
    );

    if (nextAppPaneWidth === null) return null;

    return this.resizeAppPane(nextAppPaneWidth, mode, this.#resizableWidth, true);
  }

  public restore(snapshot: ShellLayoutSnapshot, commit = false): ShellPaneLayout | null {
    this.#sidebarCollapsed = snapshot.sidebarCollapsed;
    this.#expandedAppPaneWidth = snapshot.expandedAppPaneWidth;
    this.#collapsedAppPaneWidth = snapshot.collapsedAppPaneWidth;
    this.#publish();
    if (commit) this.commit();

    return this.currentLayout();
  }

  public async hydrate(): Promise<ShellPaneLayout | null> {
    try {
      const snapshot = await this.#storage?.hydrate?.();
      if (snapshot !== undefined && snapshot !== null) {
        this.restore(snapshot);
      }
    } finally {
      this.#hydrated = true;
      this.#publish();
    }

    return this.currentLayout();
  }

  public commit(): void {
    this.#storage?.save(this.snapshot);
    this.#publish();
  }

  #appPaneWidthForMode(mode: ShellLayoutMode): number {
    return (mode === "collapsed" ? this.#collapsedAppPaneWidth : this.#expandedAppPaneWidth)
      ?? defaultAppPaneWidth(mode, this.#resizableWidth);
  }

  #setAppPaneWidthForMode(mode: ShellLayoutMode, appPaneWidth: number, publish = false): void {
    if (mode === "collapsed") {
      this.#collapsedAppPaneWidth = appPaneWidth;
      if (publish) this.#publish();
      return;
    }

    this.#expandedAppPaneWidth = appPaneWidth;
    if (publish) this.#publish();
  }

  #layoutFor(mode: ShellLayoutMode, requestedAppPaneWidth: number, resizableWidth: number): ShellPaneLayout {
    const appPaneWidth = clampAppPaneWidth(mode, requestedAppPaneWidth, resizableWidth);
    const workspacePaneWidth = Math.max(0, resizableWidth - appPaneWidth);
    const { min, max } = appPaneWidthBounds(mode, resizableWidth);

    return {
      mode,
      resizableWidth,
      workspacePaneWidth,
      appPaneWidth,
      minAppPaneWidth: min,
      maxAppPaneWidth: max,
      appPaneShare: appPaneWidth / resizableWidth
    };
  }

  #publish(): void {
    this.#stateStore.set(this.stateSnapshot);
  }
}

export function createShellLayoutController(
  options: ShellLayoutControllerOptions = {}
): ShellLayoutController {
  const controller = new ShellLayoutController({
    storage: createDesktopShellLayoutStorage(),
    ...options
  });
  void controller.hydrate().catch(() => undefined);

  return controller;
}
