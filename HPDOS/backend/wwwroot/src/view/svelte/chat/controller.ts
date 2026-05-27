import {
  appPaneWidthBounds,
  clampAppPaneWidth,
  defaultAppPaneWidth,
  type ChatLayoutMode
} from "./layout";
import {
  createDesktopChatLayoutStorage,
  defaultChatLayoutSnapshot,
  type ChatLayoutSnapshot,
  type ChatLayoutStorage
} from "./storage";
import { writable, type Readable } from "svelte/store";

export type ChatPaneLayout = {
  mode: ChatLayoutMode;
  resizableWidth: number;
  workspacePaneWidth: number;
  appPaneWidth: number;
  minAppPaneWidth: number;
  maxAppPaneWidth: number;
  appPaneShare: number;
};

export type ChatLayoutControllerOptions = {
  storage?: ChatLayoutStorage | null;
  initialSnapshot?: Partial<ChatLayoutSnapshot>;
};

const keyboardStep = 24;
const largeKeyboardStep = keyboardStep * 4;
const minimumResizableWidth = 160;

export function appPaneWidthForKeyboardResize(
  key: string,
  mode: ChatLayoutMode,
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

export class ChatLayoutController {
  #resizableWidth = 0;
  #expandedAppPaneWidth: number | null = null;
  #collapsedAppPaneWidth: number | null = null;
  #storage: ChatLayoutStorage | null;
  #stateStore;

  public constructor(options: ChatLayoutControllerOptions = {}) {
    this.#storage = options.storage ?? null;
    const snapshot = {
      ...defaultChatLayoutSnapshot(),
      ...this.#storage?.load(),
      ...options.initialSnapshot
    };

    this.#expandedAppPaneWidth = snapshot.expandedAppPaneWidth;
    this.#collapsedAppPaneWidth = snapshot.collapsedAppPaneWidth;
    this.#stateStore = writable(this.snapshot);
  }

  public get state(): Readable<ChatLayoutSnapshot> {
    return this.#stateStore;
  }

  public get snapshot(): ChatLayoutSnapshot {
    return {
      expandedAppPaneWidth: this.#expandedAppPaneWidth,
      collapsedAppPaneWidth: this.#collapsedAppPaneWidth
    };
  }

  public measure(resizableWidth: number, mode: ChatLayoutMode): ChatPaneLayout | null {
    this.#resizableWidth = Math.max(1, resizableWidth);
    return this.currentLayout(mode);
  }

  public currentLayout(mode: ChatLayoutMode): ChatPaneLayout | null {
    if (this.#resizableWidth < minimumResizableWidth) return null;

    return this.#layoutFor(mode, this.#appPaneWidthForMode(mode), this.#resizableWidth);
  }

  public resizeAppPane(
    appPaneWidth: number,
    mode: ChatLayoutMode,
    resizableWidth = this.#resizableWidth,
    commit = false
  ): ChatPaneLayout | null {
    if (resizableWidth < minimumResizableWidth) return null;

    const layout = this.#layoutFor(mode, appPaneWidth, resizableWidth);
    this.#setAppPaneWidthForMode(mode, layout.appPaneWidth, commit);
    if (commit) this.commit();

    return layout;
  }

  public resizeFromClientX(
    routeRight: number,
    clientX: number,
    mode: ChatLayoutMode,
    resizableWidth = this.#resizableWidth,
    commit = false
  ): ChatPaneLayout | null {
    return this.resizeAppPane(routeRight - clientX, mode, resizableWidth, commit);
  }

  public keyboardResize(key: string, mode: ChatLayoutMode, shiftKey = false): ChatPaneLayout | null {
    if (this.#resizableWidth < minimumResizableWidth) return null;

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

  public restore(snapshot: ChatLayoutSnapshot, commit = false): void {
    this.#expandedAppPaneWidth = snapshot.expandedAppPaneWidth;
    this.#collapsedAppPaneWidth = snapshot.collapsedAppPaneWidth;
    this.#publish();
    if (commit) this.commit();
  }

  public async hydrate(): Promise<void> {
    const snapshot = await this.#storage?.hydrate?.();
    if (snapshot !== undefined && snapshot !== null) {
      this.restore(snapshot);
    }
  }

  public commit(): void {
    this.#storage?.save(this.snapshot);
    this.#publish();
  }

  #appPaneWidthForMode(mode: ChatLayoutMode): number {
    return (mode === "collapsed" ? this.#collapsedAppPaneWidth : this.#expandedAppPaneWidth)
      ?? defaultAppPaneWidth(mode, this.#resizableWidth);
  }

  #setAppPaneWidthForMode(mode: ChatLayoutMode, appPaneWidth: number, publish = false): void {
    if (mode === "collapsed") {
      this.#collapsedAppPaneWidth = appPaneWidth;
      if (publish) this.#publish();
      return;
    }

    this.#expandedAppPaneWidth = appPaneWidth;
    if (publish) this.#publish();
  }

  #layoutFor(mode: ChatLayoutMode, requestedAppPaneWidth: number, resizableWidth: number): ChatPaneLayout {
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
    this.#stateStore.set(this.snapshot);
  }
}

export function createChatLayoutController(
  options: ChatLayoutControllerOptions = {}
): ChatLayoutController {
  const controller = new ChatLayoutController({
    storage: createDesktopChatLayoutStorage(),
    ...options
  });
  void controller.hydrate().catch(() => undefined);

  return controller;
}
