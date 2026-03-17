/**
 * HPDOS HybridWebView Bridge
 * Low-level JS ↔ C# communication via MAUI HybridWebView
 */

declare global {
    interface Window {
        HybridWebView?: {
            InvokeDotNet: <T = unknown>(method: string, params?: unknown[]) => Promise<T>;
            SendRawMessage: (message: string) => void;
        };
        __HPDOS_API_BASE?: string;
    }
}

export const isHybridWebView = () =>
    typeof window !== 'undefined' && !!window.HybridWebView;

export async function invoke<T = unknown>(method: string, ...params: unknown[]): Promise<T> {
    if (!window.HybridWebView) {
        throw new Error('Not running in HybridWebView context');
    }
    return window.HybridWebView.InvokeDotNet<T>(method, params.length ? params : undefined);
}

export function sendMessage(message: string): void {
    window.HybridWebView?.SendRawMessage(message);
}

/**
 * Subscribe to C#→JS events emitted via HybridEventEmitter.
 * Returns an unsubscribe function.
 */
export function onEvent<T = unknown>(
    callback: (event: string, data: T) => void
): () => void {
    const handler = (e: CustomEvent<{ message: string }>) => {
        try {
            const parsed = JSON.parse(e.detail.message) as { event: string; data: T };
            callback(parsed.event, parsed.data);
        } catch {
            /* not a structured event */
        }
    };
    window.addEventListener('HybridWebViewMessageReceived', handler as EventListener);
    return () => window.removeEventListener('HybridWebViewMessageReceived', handler as EventListener);
}
