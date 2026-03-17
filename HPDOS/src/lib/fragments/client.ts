/**
 * Web Fragments Client Initialization
 *
 * Initializes the web-fragments custom element (<web-fragment>) once per page.
 * Call initFragments() before any <web-fragment> elements are created.
 */

let initialized = false;

export async function initFragments(): Promise<void> {
    if (typeof window === 'undefined') return;
    if (initialized) return;

    const { initializeWebFragments } = await import('web-fragments');
    initializeWebFragments();

    initialized = true;
    console.log('[WebFragments] Initialized');
}

export function isFragmentsInitialized(): boolean {
    return initialized;
}
