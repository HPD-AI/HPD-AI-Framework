import type { AppManifest, AppCategory } from './types';
import { isHybridWebView } from '../ipc/bridge';
import { listApps } from '../ipc/client';

/**
 * AppRegistry — centralized singleton for app discovery and component loading.
 * Mirrors HPDOSPlatform on the C# side.
 */
class AppRegistry {
    private apps = new Map<string, AppManifest>();
    private loadedComponents = new Map<string, any>();

    // ── Registration ───────────────────────────────────────────────────────────

    register(manifest: AppManifest): void {
        if (this.apps.has(manifest.id)) {
            console.warn(`[AppRegistry] Already registered: ${manifest.id}`);
            return;
        }
        this.apps.set(manifest.id, manifest);
        console.log(`[AppRegistry] Registered: ${manifest.name} (${manifest.id})`);
        this.validateManifest(manifest).catch((err) =>
            console.warn(`[AppRegistry] Validation warning for ${manifest.id}:`, err)
        );
    }

    registerAll(manifests: AppManifest[]): void {
        for (const m of manifests) this.register(m);
    }

    unregister(appId: string): void {
        this.apps.delete(appId);
        this.loadedComponents.delete(appId);
    }

    // ── Querying ───────────────────────────────────────────────────────────────

    get(id: string): AppManifest | undefined { return this.apps.get(id); }
    list(): AppManifest[]                    { return Array.from(this.apps.values()); }
    has(appId: string): boolean              { return this.apps.has(appId); }
    get count(): number                      { return this.apps.size; }

    getByCategory(category: AppCategory): AppManifest[] {
        return this.list().filter((a) => a.category === category);
    }

    search(query: string): AppManifest[] {
        const q = query.toLowerCase();
        return this.list().filter(
            (a) =>
                a.name.toLowerCase().includes(q) ||
                a.description?.toLowerCase().includes(q) ||
                a.keywords?.some((kw) => kw.toLowerCase().includes(q))
        );
    }

    hasBackend(appId: string): boolean {
        return !!this.get(appId)?.backendAppId;
    }

    // ── Component Loading ──────────────────────────────────────────────────────

    async loadComponent(appId: string): Promise<any> {
        const manifest = this.get(appId);
        if (!manifest) throw new Error(`[AppRegistry] App not found: ${appId}`);

        if (this.loadedComponents.has(appId)) return this.loadedComponents.get(appId);

        const mod = await manifest.component();
        if (!mod.default) throw new Error(`Component for ${appId} has no default export`);

        this.loadedComponents.set(appId, mod.default);
        return mod.default;
    }

    clearComponentCache(appId?: string): void {
        if (appId) this.loadedComponents.delete(appId);
        else this.loadedComponents.clear();
    }

    // ── Validation ─────────────────────────────────────────────────────────────

    private async validateManifest(manifest: AppManifest): Promise<void> {
        if (!manifest.id || !manifest.name || !manifest.version || !manifest.icon)
            throw new Error('Manifest missing required fields: id, name, version, icon');

        if (!/^[a-z0-9-]+$/.test(manifest.id))
            throw new Error(`Invalid app ID format: ${manifest.id} (must be kebab-case)`);

        if (!/^\d+\.\d+\.\d+/.test(manifest.version))
            throw new Error(`Invalid version: ${manifest.version} (must be semver X.Y.Z)`);

        if (typeof manifest.component !== 'function')
            throw new Error(`App ${manifest.id}: component must be a lazy-load function`);

        if (manifest.backendAppId && isHybridWebView()) {
            const backendApps = await listApps();
            if (!backendApps.includes(manifest.backendAppId)) {
                console.warn(
                    `[AppRegistry] Backend app not found: ${manifest.backendAppId} ` +
                    `(app: ${manifest.id}). Available: ${backendApps.join(', ')}`
                );
            }
        }
    }
}

export const appRegistry = new AppRegistry();
export { AppRegistry };
