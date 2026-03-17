/**
 * HPDOS IPC Client
 * High-level API for communicating with the HPDOS platform backend.
 */

import { invoke, isHybridWebView } from './bridge';

export type JsonValue =
    | string
    | number
    | boolean
    | null
    | JsonValue[]
    | { [key: string]: JsonValue };

export interface AppInfo {
    id: string;
    name: string;
    version: string;
}

export interface ResourceStats {
    totalFiles: number;
    totalResources: number;
}

/** Route a command to a backend IApplication */
export async function invokeAppCommand<T = JsonValue>(
    appId: string,
    command: string,
    payload: JsonValue = {}
): Promise<T> {
    if (!isHybridWebView()) {
        console.warn('[IPC] Not in HybridWebView context');
        return { success: false, error: 'Not in native context' } as T;
    }
    return invoke<T>('RouteCommand', appId, command, payload);
}

/** List all registered backend app IDs */
export async function listApps(): Promise<string[]> {
    if (!isHybridWebView()) return [];
    return invoke<string[]>('ListApps');
}

/** Get info for all registered apps */
export async function getAllAppInfo(): Promise<AppInfo[]> {
    if (!isHybridWebView()) return [];
    return invoke<AppInfo[]>('GetAllAppInfo');
}

/** Get info for a specific app */
export async function getAppInfo(appId: string): Promise<AppInfo | null> {
    if (!isHybridWebView()) return null;
    return invoke<AppInfo | null>('GetAppInfo', appId);
}

/** Check if an app is registered */
export async function hasApp(appId: string): Promise<boolean> {
    if (!isHybridWebView()) return false;
    return invoke<boolean>('HasApp', appId);
}

/** Get resource statistics */
export async function getResourceStats(): Promise<ResourceStats> {
    if (!isHybridWebView()) return { totalFiles: 0, totalResources: 0 };
    return invoke<ResourceStats>('GetResourceStats');
}
