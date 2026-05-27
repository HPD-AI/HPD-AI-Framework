import {
  createDesktopShellStorage,
  defaultShellSnapshot,
  type ShellSnapshot,
  type ShellStorage
} from "./shellStorage";
import { writable, type Readable } from "svelte/store";

export type ShellRoute = "chat" | "automations" | "settings";

export type ShellState = ShellSnapshot & {
  hydrated: boolean;
};

export type ShellControllerOptions = {
  storage?: ShellStorage | null;
  initialSnapshot?: Partial<ShellSnapshot>;
};

export class ShellController {
  #activeRoute: ShellRoute;
  #sidebarCollapsed: boolean;
  #hydrated: boolean;
  #storage: ShellStorage | null;
  #stateStore;

  public constructor(options: ShellControllerOptions = {}) {
    this.#storage = options.storage ?? null;
    const snapshot = {
      ...defaultShellSnapshot(),
      ...this.#storage?.load(),
      ...options.initialSnapshot
    };

    this.#activeRoute = snapshot.activeRoute;
    this.#sidebarCollapsed = snapshot.sidebarCollapsed;
    this.#hydrated = this.#storage?.hydrate === undefined;
    this.#stateStore = writable(this.stateSnapshot);
  }

  public get state(): Readable<ShellState> {
    return this.#stateStore;
  }

  public get snapshot(): ShellSnapshot {
    return {
      activeRoute: this.#activeRoute,
      sidebarCollapsed: this.#sidebarCollapsed
    };
  }

  public get stateSnapshot(): ShellState {
    return {
      ...this.snapshot,
      hydrated: this.#hydrated
    };
  }

  public get hydrated(): boolean {
    return this.#hydrated;
  }

  public get activeRoute(): ShellRoute {
    return this.#activeRoute;
  }

  public get sidebarCollapsed(): boolean {
    return this.#sidebarCollapsed;
  }

  public setRoute(route: ShellRoute, commit = true): void {
    if (this.#activeRoute === route) return;

    this.#activeRoute = route;
    this.#publish();
    if (commit) this.commit();
  }

  public toggleSidebar(): void {
    this.setSidebarCollapsed(!this.#sidebarCollapsed);
  }

  public setSidebarCollapsed(sidebarCollapsed: boolean, commit = true): void {
    if (this.#sidebarCollapsed === sidebarCollapsed) return;

    this.#sidebarCollapsed = sidebarCollapsed;
    this.#publish();
    if (commit) this.commit();
  }

  public restore(snapshot: ShellSnapshot, commit = false): void {
    this.#activeRoute = snapshot.activeRoute;
    this.#sidebarCollapsed = snapshot.sidebarCollapsed;
    this.#publish();
    if (commit) this.commit();
  }

  public async hydrate(): Promise<void> {
    try {
      const snapshot = await this.#storage?.hydrate?.();
      if (snapshot !== undefined && snapshot !== null) {
        this.restore(snapshot);
      }
    } finally {
      this.#hydrated = true;
      this.#publish();
    }
  }

  public commit(): void {
    this.#storage?.save(this.snapshot);
    this.#publish();
  }

  #publish(): void {
    this.#stateStore.set(this.stateSnapshot);
  }
}

export function createShellController(options: ShellControllerOptions = {}): ShellController {
  const controller = new ShellController({
    storage: createDesktopShellStorage(),
    ...options
  });
  void controller.hydrate().catch(() => undefined);

  return controller;
}
