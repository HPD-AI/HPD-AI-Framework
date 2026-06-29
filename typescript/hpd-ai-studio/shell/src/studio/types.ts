import type { Component } from 'svelte';

export type StudioModuleStatus = 'active' | 'planned' | string;
export type StudioMode = 'development' | 'read-only' | string;

export interface StudioModuleConfig {
  id: string;
  label: string;
  title: string;
  status?: StudioModuleStatus;
}

export interface StudioRuntimeConfig {
  apiBasePath: string;
  routePrefix: string;
  productTitle: string;
  mode: StudioMode;
  capabilities: string[];
  studioModules: StudioModuleConfig[];
}

export interface StudioRoute {
  path: string;
  component: Component;
  title: string;
  eyebrow?: string;
  summary: string;
}

export interface StudioNavItem {
  path: string;
  label: string;
  summary?: string;
}

export interface StudioModule {
  id: string;
  label: string;
  title: string;
  description?: string;
  status?: StudioModuleStatus;
  capabilities?: string[];
  navItems: StudioNavItem[];
  routes: StudioRoute[];
}

export interface StudioModuleCatalogItem extends Omit<Partial<StudioModule>, 'id' | 'label' | 'title' | 'status'> {
  id: string;
  label: string;
  title: string;
  status: StudioModuleStatus;
  isActive: boolean;
  isLive: boolean;
}

export interface StudioSelection {
  agentId: string;
  sessionId: string;
  threadId: string;
  runId: string;
  eventId: string;
}

export interface StudioStateModel {
  config: StudioRuntimeConfig;
  modules: StudioModule[];
  activeRoute: string;
  apiStatus: string;
  selection: StudioSelection;
}

export interface StudioRouteWithModule extends StudioRoute {
  module: StudioModule;
}

export interface StudioNavItemWithModule extends StudioNavItem {
  module: StudioModule;
}

export interface StudioController {
  state: StudioStateModel;
  routes: StudioRouteWithModule[];
  moduleCatalog: StudioModuleCatalogItem[];
  activeModule: StudioModule;
  navItems: StudioNavItemWithModule[];
  currentRoute: StudioRouteWithModule;
  defaultRoute: StudioRoute;
  navigate(path: string): void;
  selectModule(moduleId: string): void;
  syncRouteFromLocation(): void;
  selectAgent(agentId: string | null | undefined): void;
  selectSession(sessionId: string | null | undefined): void;
  selectThread(threadId: string | null | undefined): void;
  can(capability: string): boolean;
  isReadOnly(): boolean;
}
