import type { Component } from 'svelte';

export type StudioMode = 'development' | 'read-only';
export type StudioModuleRequirement = 'required' | 'optional';
export type StudioJson = null | boolean | number | string | readonly StudioJson[] | { readonly [key: string]: StudioJson };

export interface StudioAuthenticationSnapshot {
  readonly isAuthenticated: boolean;
  readonly displayName?: string;
  readonly subjectHint?: string;
}

export interface StudioAuthenticationService {
  snapshot(): StudioAuthenticationSnapshot;
  subscribe(listener: (snapshot: StudioAuthenticationSnapshot) => void): () => void;
  beginSignIn?(): void | Promise<void>;
  beginSignOut?(): void | Promise<void>;
}

export interface StudioRoute {
  readonly path: string;
  readonly component: Component;
  readonly title: string;
  readonly eyebrow?: string;
  readonly summary: string;
}

export interface StudioNavItem {
  readonly path: string;
  readonly label: string;
  readonly summary?: string;
}

export interface StudioModuleContextReader {
  get<T>(name: string): T | undefined;
}

export interface StudioModuleContextWriter extends StudioModuleContextReader {
  set<T>(name: string, value: T): void;
  delete(name: string): void;
}

export interface StudioLifecycle {
  readonly signal: AbortSignal;
  defer(dispose: () => void | Promise<void>): void;
  trackAbortController(controller?: AbortController): AbortController;
  setInterval(callback: () => void, milliseconds: number): number;
  listen(target: Pick<EventTarget, 'addEventListener' | 'removeEventListener'>, type: string, listener: EventListener): void;
}

export interface StudioModuleInitialization {
  readonly moduleId: string;
  readonly mode: StudioMode;
  readonly configuration: Readonly<Record<string, StudioJson>>;
  readonly authentication: StudioAuthenticationService;
  readonly contexts: StudioModuleContextWriter;
  readonly lifecycle: StudioLifecycle;
}

export interface StudioModuleActivation {
  dispose?(): void | Promise<void>;
}

export interface StudioModule {
  readonly id: string;
  readonly label: string;
  readonly title: string;
  readonly description?: string;
  readonly routes: readonly StudioRoute[];
  readonly navItems?: readonly StudioNavItem[];
  initialize?(context: StudioModuleInitialization): void | StudioModuleActivation | Promise<void | StudioModuleActivation>;
}

export interface StudioModuleRegistration {
  readonly module: StudioModule;
  readonly requirement: StudioModuleRequirement;
  readonly configuration?: Readonly<Record<string, StudioJson>>;
}

export interface StudioShellConfiguration {
  readonly productTitle: string;
  readonly apiBasePath?: string;
  readonly routePrefix?: string;
  readonly assetContractVersion?: '1';
  readonly assetIdentity?: string;
  readonly mode: StudioMode;
}

export interface StudioModuleContextHandle {
  readonly moduleId: string;
}

export interface StudioRuntimeRoute extends StudioRoute {
  readonly moduleId: string;
  readonly context: StudioModuleContextHandle;
}

export interface StudioRuntimeModule {
  readonly id: string;
  readonly label: string;
  readonly title: string;
  readonly description?: string;
  readonly requirement: StudioModuleRequirement;
  readonly routes: readonly StudioRuntimeRoute[];
  readonly navItems: readonly StudioNavItem[];
}

export interface StudioQuarantinedModule {
  readonly id: string;
  readonly code: 'studio.module.initializationFailed';
}

export interface StudioRouteObservation {
  readonly route: StudioRuntimeRoute | null;
  readonly requestedPath: string;
  readonly isFallback: boolean;
}

export interface StudioRuntime {
  readonly configuration: StudioShellConfiguration;
  readonly authentication: StudioAuthenticationService;
  readonly modules: readonly StudioRuntimeModule[];
  readonly quarantinedModules: readonly StudioQuarantinedModule[];
  readonly routes: readonly StudioRuntimeRoute[];
  readonly current: StudioRouteObservation;
  navigate(path: string): StudioRouteObservation;
  subscribe(listener: (observation: StudioRouteObservation) => void): () => void;
  dispose(): Promise<void>;
}

export interface StudioShellServices {
  readonly configuration: StudioShellConfiguration;
  readonly authentication: StudioAuthenticationService;
  currentModuleId(): string | null;
  navigate(path: string): void;
}

export interface ComposeStudioOptions {
  readonly configuration: StudioShellConfiguration;
  readonly authentication: StudioAuthenticationService;
  readonly modules: Iterable<StudioModuleRegistration>;
}
