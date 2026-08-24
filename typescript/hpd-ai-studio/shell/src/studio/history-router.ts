import { matchStudioRoute, type StudioRouteMatch, type StudioVisiblePage } from '@hpd-research/hpd-studio-core';

export interface StudioHistoryRoute {
  readonly page: StudioVisiblePage;
  readonly match: StudioRouteMatch;
}

/** Typed browser-history router backed exclusively by principal-disclosed route definitions. */
export class StudioHistoryRouter {
  readonly #pages: readonly StudioVisiblePage[];
  readonly #listeners = new Set<(route: StudioHistoryRoute | null) => void>();
  readonly #pop = () => this.#publish(this.match(this.#currentUrl()));
  #current: StudioHistoryRoute | null;
  #disposed = false;
  constructor(pages: readonly StudioVisiblePage[]) {
    this.#pages = Object.freeze([...pages]); this.#current = this.match(this.#currentUrl());
    globalThis.addEventListener('popstate', this.#pop);
  }
  get current(): StudioHistoryRoute | null { return this.#current; }
  match(url: string): StudioHistoryRoute | null {
    for (const page of this.#pages) {
      const match = matchStudioRoute(page.route, url);
      if (match) return Object.freeze({ page, match });
    }
    return null;
  }
  navigate(url: string, replace = false): StudioHistoryRoute | null {
    if (this.#disposed) return null;
    const route = this.match(url); if (!route) return null;
    const target = new URL(url.slice(1), document.baseURI);
    if (replace) globalThis.history.replaceState(null, '', target); else globalThis.history.pushState(null, '', target);
    this.#publish(route); return route;
  }
  subscribe(listener: (route: StudioHistoryRoute | null) => void): () => void {
    this.#listeners.add(listener); try { listener(this.#current); } catch { /* initial observers are isolated */ }
    return () => this.#listeners.delete(listener);
  }
  dispose(): void {
    if (this.#disposed) return; this.#disposed = true; this.#listeners.clear(); globalThis.removeEventListener('popstate', this.#pop);
  }
  #currentUrl(): string {
    const base = new URL(document.baseURI); const path = globalThis.location.pathname;
    if (!path.startsWith(base.pathname)) return '/';
    const suffix = path.slice(base.pathname.length); return `/${suffix}${globalThis.location.search}`;
  }
  #publish(route: StudioHistoryRoute | null): void {
    this.#current = route; for (const listener of this.#listeners) try { listener(route); } catch { /* route observers are isolated */ }
  }
}
