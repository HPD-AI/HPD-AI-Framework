import type { Component } from 'svelte';

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
  status?: 'active' | 'planned' | string;
  capabilities?: string[];
  navItems: StudioNavItem[];
  routes: StudioRoute[];
}

export interface AgentStudioApiConfig {
  apiBasePath: string;
}

export type JsonBody = Record<string, unknown> | null;
