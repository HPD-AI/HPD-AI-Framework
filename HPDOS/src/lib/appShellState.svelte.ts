/**
 * appShellState — reactive singleton that drives the OS shell from outside Svelte components.
 *
 * The workspace's onClientToolInvoke handler calls openApp/closeApp here.
 * OS.svelte reads selectedAppId and panelOpen reactively.
 */
class AppShellState {
    selectedAppId = $state<string | null>(null);

    // Wired by OS.svelte after mount — calls SplitPanel imperatively
    #expandPanel: (() => void) | null = null;
    #collapsePanel: (() => void) | null = null;

    registerPanelControls(expand: () => void, collapse: () => void) {
        this.#expandPanel = expand;
        this.#collapsePanel = collapse;
    }

    openApp(appId: string) {
        this.selectedAppId = appId;
        this.#expandPanel?.();
    }

    closeApp() {
        this.#collapsePanel?.();
    }
}

export const appShellState = new AppShellState();
